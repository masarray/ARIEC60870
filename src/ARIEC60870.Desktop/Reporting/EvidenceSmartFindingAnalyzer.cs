// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.Reporting;

/// <summary>
/// Rule based IEC 60870 evidence diagnostics for the report.
///
/// The analyzer is intentionally concise: every finding must explain the actual
/// protocol symptom, why it matters, and the next field action. Generic warnings
/// are filtered out unless they carry evidence that helps a beginner engineer.
/// </summary>
public static class EvidenceSmartFindingAnalyzer
{
    public static IReadOnlyList<EvidenceSmartFinding> Analyze(
        IReadOnlyList<EvidenceRow> rows,
        IReadOnlyList<FindingRow> existingFindings,
        IReadOnlyList<KeyValuePair<string, string>> communicationSetup)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<EvidenceSmartFinding>();
        }

        var ordered = rows.OrderBy(row => row.Sequence).ToArray();
        var findings = new List<EvidenceSmartFinding>();
        var configuredCa = ReadSetupInt(communicationSetup, "Common address");
        var protocolMode = InferProtocolMode(ordered, communicationSetup);
        var autoConfig = Iec10xAutoConfigCorrector.Analyze(ordered, communicationSetup, protocolMode);

        AddConnectionContextFindings(ordered, existingFindings, findings);
        AddCommonAddressMismatch(ordered, configuredCa, findings);
        AddUnknownAddressFindings(ordered, findings);
        AddProfileSizeMismatch(ordered, autoConfig, findings);
        AddGiIncomplete(ordered, findings);
        AddCommandNoFeedback(ordered, findings);
        AddCommandDelayedByClassOneTraffic(ordered, findings);
        AddQualityFindings(ordered, findings);
        AddLinkAliveApplicationSilent(ordered, findings);
        AddHighValueExistingFindings(existingFindings, findings);

        return findings
            .GroupBy(finding => finding.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(f => SeverityRank(f.Severity)).ThenBy(f => f.Sequence).First())
            .OrderByDescending(finding => SeverityRank(finding.Severity))
            .ThenBy(finding => finding.Sequence)
            .Take(8)
            .ToArray();
    }

    private static void AddCommonAddressMismatch(IReadOnlyList<EvidenceRow> rows, int? configuredCa, List<EvidenceSmartFinding> findings)
    {
        var txCandidates = rows
            .Where(row => IsTx(row) && (IsGi(row) || IsControlCommand(row) || IsReadRequest(row)))
            .Select(row => (Row: row, Ca: ParsePositiveInt(row.CommonAddress)))
            .Where(item => item.Ca.HasValue)
            .ToArray();

        var rxCandidates = rows
            .Where(IsRx)
            .Select(row => (Row: row, Ca: ParsePositiveInt(row.CommonAddress)))
            .Where(item => item.Ca.HasValue)
            .ToArray();

        var txCa = configuredCa ?? Dominant(txCandidates.Select(item => item.Ca!.Value));
        var rxCa = Dominant(rxCandidates.Select(item => item.Ca!.Value));

        if (!txCa.HasValue || !rxCa.HasValue || txCa.Value == rxCa.Value)
        {
            return;
        }

        var txCount = txCandidates.Count(item => item.Ca == txCa.Value);
        var rxCount = rxCandidates.Count(item => item.Ca == rxCa.Value);
        if (txCount == 0 || rxCount == 0)
        {
            return;
        }

        var first = txCandidates.First(item => item.Ca == txCa.Value).Row;
        findings.Add(new EvidenceSmartFinding(
            EvidenceSmartFindingSeverity.Error,
            "ARIEC-SMART-CA-MISMATCH",
            $"Common address mismatch: master uses CA={txCa.Value}, device traffic shows CA={rxCa.Value}.",
            "IEC-101/104 has link/TCP connectivity and application CA as separate layers. A wrong CA can make GI, read, and command look like timeout even when the link is alive.",
            $"TX rows use CA={txCa.Value}; RX ASDUs use CA={rxCa.Value}. First TX evidence #{first.Sequence}.",
            $"Set Common address to {rxCa.Value}. Keep link address/serial settings unchanged unless link ACK is also missing. Retest GI, then verify ACTCON and feedback.",
            "High",
            first.Sequence));
    }

    private static void AddUnknownAddressFindings(IReadOnlyList<EvidenceRow> rows, List<EvidenceSmartFinding> findings)
    {
        var unknownCa = rows.FirstOrDefault(row => ContainsAny(SearchText(row), "unknown common address", "unknown ca", "unknown_cot_45", "unknown_ca"));
        if (unknownCa is not null)
        {
            findings.Add(new EvidenceSmartFinding(
                EvidenceSmartFindingSeverity.Error,
                "ARIEC-SMART-UNKNOWN-CA",
                "Device rejected the application common address.",
                "The slave recognized the frame enough to answer, but it did not accept the CA used by the master.",
                $"Unknown CA symptom at row #{unknownCa.Sequence}: {Short(unknownCa.ProtocolTraceMeaning, 120)}",
                "Use the CA observed in valid RX ASDUs or the RTU configuration. After changing CA, repeat GI and command ACTCON test.",
                "High",
                unknownCa.Sequence));
        }

        var unknownIoa = rows.FirstOrDefault(row => ContainsAny(SearchText(row), "unknown information object", "unknown ioa", "unknown_ioa"));
        if (unknownIoa is not null)
        {
            findings.Add(new EvidenceSmartFinding(
                EvidenceSmartFindingSeverity.Warning,
                "ARIEC-SMART-UNKNOWN-IOA",
                "IOA is not accepted by the device.",
                "The command/read target is not in the slave point table, or the command IOA is different from the feedback IOA.",
                $"Unknown IOA symptom at row #{unknownIoa.Sequence}, CA={Clean(unknownIoa.CommonAddress)}, IOA={Clean(unknownIoa.IoAddress)}.",
                "Run GI, locate the real feedback point, then verify command IOA and feedback IOA as a pair. Update the mapping profile before retesting.",
                "High",
                unknownIoa.Sequence));
        }
    }

    private static void AddProfileSizeMismatch(IReadOnlyList<EvidenceRow> rows, Iec10xAutoConfigCorrector.Iec10xAutoConfigSuggestion autoConfig, List<EvidenceSmartFinding> findings)
    {
        var hit = rows.FirstOrDefault(row => ContainsAny(SearchText(row),
            "profile size", "cot size", "ioa size", "ca size", "parse", "unknown asdu", "unknown type", "invalid vsq"));

        var relevantChanges = autoConfig.Changes
            .Where(change => change.Key is "LinkAddressSize" or "CotSize" or "CaSize" or "IoaSize")
            .ToArray();

        // Do not create a generic profile warning just because a trace row contains
        // parser text. A Smart Finding should remain active only when the correction
        // engine can point to a concrete setup field, or when the protocol engine has
        // already produced a high-value runtime finding that is imported below.
        if (relevantChanges.Length == 0)
        {
            return;
        }

        var fix = relevantChanges.Length == 0
            ? "Try the documented profile first. If unknown, test COT 1/2, CA 1/2, IOA 1/2/3 and keep the profile that yields stable TypeID, COT, CA, and plausible IOA range."
            : "Apply the profile that matches the raw evidence: " + string.Join(", ", relevantChanges.Select(change => $"{change.Label}={change.ProposedValue}")) + ". Reconnect or re-run the session after saving the corrected setup.";
        var proof = hit is null
            ? Short(autoConfig.Summary, 160)
            : $"Parser/profile symptom at row #{hit.Sequence}: {Short(hit.ProtocolTraceMeaning, 120)}";
        var sequence = hit?.Sequence ?? rows.FirstOrDefault()?.Sequence ?? 0;

        findings.Add(new EvidenceSmartFinding(
            EvidenceSmartFindingSeverity.Warning,
            "ARIEC-SMART-PROFILE-SIZE",
            relevantChanges.Length == 0 ? "Application profile size may not match the slave." : "Application profile size can be corrected from the captured evidence.",
            "Wrong COT/CA/IOA byte size shifts the ASDU fields. The frame can be present, but TypeID, COT, CA, or IOA are decoded incorrectly.",
            proof,
            fix,
            relevantChanges.Length >= 2 ? "High" : "Medium",
            sequence));
    }

    private static void AddGiIncomplete(IReadOnlyList<EvidenceRow> rows, List<EvidenceSmartFinding> findings)
    {
        var giTx = rows.Where(row => IsTx(row) && IsGi(row)).OrderByDescending(row => row.Sequence).FirstOrDefault();
        if (giTx is null)
        {
            return;
        }

        var afterGi = rows.Where(row => row.Sequence >= giTx.Sequence).OrderBy(row => row.Sequence).ToArray();
        var hasActCon = afterGi.Any(row => IsRx(row) && IsActivationConfirmation(row));
        var hasActTerm = afterGi.Any(row => IsRx(row) && IsActivationTermination(row));
        var hasApplicationData = afterGi.Any(row => IsRx(row) && IsGiApplicationData(row));

        // Do not keep a transient GI warning alive once the evidence has proven that
        // the device answered the interrogation. Many field devices stream values
        // before ACTTERM, and some captures do not include the final termination row.
        if ((hasActCon && hasApplicationData) || hasActTerm)
        {
            return;
        }

        // Avoid flickering a false warning while the session is still collecting the
        // first few frames after GI. A missing ACTCON is only meaningful after several
        // follow-up frames have arrived.
        if (!hasActCon && afterGi.Length < 6)
        {
            return;
        }

        findings.Add(new EvidenceSmartFinding(
            EvidenceSmartFindingSeverity.Warning,
            "ARIEC-SMART-GI-INCOMPLETE",
            hasActCon ? "GI started but no data or termination is visible yet." : "GI request has no activation confirmation.",
            "A complete GI needs request, activation confirmation, data, and normally activation termination. Missing ACTCON plus no data means the baseline point scan is not proven.",
            $"GI request row #{giTx.Sequence}; ACTCON={(hasActCon ? "present" : "missing")}; data={(hasApplicationData ? "present" : "missing")}; ACTTERM={(hasActTerm ? "present" : "missing")}. Used latest GI request to avoid stale findings.",
            "Check CA, interrogation group, scan table, and device support for station interrogation. Repeat GI after link reset and compare point count.",
            hasActCon ? "Medium" : "High",
            giTx.Sequence));
    }

    private static void AddCommandNoFeedback(IReadOnlyList<EvidenceRow> rows, List<EvidenceSmartFinding> findings)
    {
        var commandTx = rows.FirstOrDefault(row => IsTx(row) && IsControlCommand(row));
        if (commandTx is null)
        {
            return;
        }

        var window = rows.Where(row => row.Sequence >= commandTx.Sequence).Take(80).ToArray();
        var hasActCon = window.Any(row => IsRx(row) && IsActivationConfirmation(row));
        var hasActTerm = window.Any(row => IsRx(row) && IsActivationTermination(row));
        var hasFeedback = window.Any(row => IsLikelyControlFeedback(row, commandTx));
        var hasNegative = window.Any(row => IsNegative(row));

        if ((hasActCon || hasActTerm || hasFeedback) && !hasNegative)
        {
            return;
        }

        findings.Add(new EvidenceSmartFinding(
            hasNegative ? EvidenceSmartFindingSeverity.Error : EvidenceSmartFindingSeverity.Warning,
            "ARIEC-SMART-COMMAND-FEEDBACK",
            hasNegative ? "Command was rejected or negatively confirmed." : "Command feedback is missing from the evidence window.",
            "A command is not proven by TX alone. The report must show confirmation and a status/feedback change from the slave.",
            $"Command row #{commandTx.Sequence}, CA={Clean(commandTx.CommonAddress)}, IOA={Clean(commandTx.IoAddress)}; ACTCON={(hasActCon ? "yes" : "no")}; ACTTERM={(hasActTerm ? "yes" : "no")}; feedback={(hasFeedback ? "yes" : "no")}.",
            "Verify command IOA, select/operate mode, cause of transmission, and feedback IOA mapping. Retest and keep the feedback row in the report scope.",
            hasNegative ? "High" : "Medium",
            commandTx.Sequence));
    }

    private static void AddCommandDelayedByClassOneTraffic(IReadOnlyList<EvidenceRow> rows, List<EvidenceSmartFinding> findings)
    {
        var commandTx = rows.FirstOrDefault(row => IsTx(row) && IsControlCommand(row));
        if (commandTx is null)
        {
            return;
        }

        var afterCommand = rows.Where(row => row.Sequence > commandTx.Sequence).Take(120).ToArray();
        if (afterCommand.Length == 0)
        {
            return;
        }

        var analogClassOne = afterCommand.Where(row => IsClassOne(row) && IsAnalogMeasuredValue(row)).Take(80).ToArray();
        var spontaneousAnalog = afterCommand.Where(row => IsSpontaneous(row) && IsAnalogMeasuredValue(row)).Take(80).ToArray();
        var latencyMs = afterCommand.Select(ParseResponseTimeMs).Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty(0).Max();
        var hasCommandConfirmation = afterCommand.Any(row => IsRx(row) && IsActivationConfirmation(row));
        var hasCommandFeedback = afterCommand.Any(row => IsRx(row) && IsLikelyControlFeedback(row, commandTx));

        var dominant = analogClassOne.Length >= spontaneousAnalog.Length ? analogClassOne : spontaneousAnalog;
        if (dominant.Length == 0)
        {
            return;
        }

        // This finding is only valid for real operate/select commands. A General
        // Interrogation burst after redundancy switchover is expected behaviour and
        // can contain many Class 1 values with a very small response time. Do not
        // call that congestion.
        var delayedEnough = latencyMs >= 1500;
        var noisyEnough = dominant.Length >= 8;
        var severeQueueWithoutProof = dominant.Length >= 24 && !hasCommandConfirmation && !hasCommandFeedback;
        if ((!delayedEnough || !noisyEnough) && !severeQueueWithoutProof)
        {
            return;
        }

        findings.Add(new EvidenceSmartFinding(
            EvidenceSmartFindingSeverity.Warning,
            "ARIEC-SMART-CLASS1-CONGESTION",
            "Control command evidence may be delayed by noisy Class 1 / spontaneous analog traffic.",
            "Class 1 should carry urgent events and command evidence. Cyclic analog values in Class 1 can delay confirmation and make an operate command feel unresponsive.",
            $"After control command row #{commandTx.Sequence}, detected {dominant.Length} analog/Class 1 rows; max response time {latencyMs} ms; ACTCON={(hasCommandConfirmation ? "present" : "missing")}; feedback={(hasCommandFeedback ? "present" : "missing")}.",
            "Move cyclic analog/measured values to Class 2 or background scan. Keep Class 1 for SOE, protection events, control command confirmation, and status changes, then retest command latency.",
            dominant.Length >= 24 || latencyMs >= 3000 ? "High" : "Medium",
            commandTx.Sequence));
    }

    private static void AddQualityFindings(IReadOnlyList<EvidenceRow> rows, List<EvidenceSmartFinding> findings)
    {
        var bad = rows.Where(row => HasBadQuality(row)).Take(20).ToArray();
        if (bad.Length == 0)
        {
            return;
        }

        var first = bad[0];
        findings.Add(new EvidenceSmartFinding(
            EvidenceSmartFindingSeverity.Warning,
            "ARIEC-SMART-QUALITY",
            "Some values are present but not acceptable as clean evidence.",
            "IEC quality flags can mark a value invalid, non-topical, substituted, blocked, or overflowed. Presence of data does not mean the point is valid for FAT/SAT.",
            $"{bad.Length} quality-related rows. First row #{first.Sequence}, quality={Clean(first.Quality)}, IOA={Clean(first.IoAddress)}.",
            "Fix the source acquisition/RTU point state first, then repeat GI or SOE test. Do not accept the point until quality is Good or the exception is approved.",
            bad.Length >= 5 ? "High" : "Medium",
            first.Sequence));
    }

    private static void AddLinkAliveApplicationSilent(IReadOnlyList<EvidenceRow> rows, List<EvidenceSmartFinding> findings)
    {
        var hasTxApplication = rows.Any(row => IsTx(row) && (IsGi(row) || IsControlCommand(row) || IsReadRequest(row)));
        var noDataRows = rows.Where(row => ContainsAny(SearchText(row), "no data", "ack/no-data", "no user data")).Take(20).ToArray();
        var rxAsduRows = rows.Count(row => IsRx(row) && ParsePositiveInt(row.TypeId).HasValue);

        if (!hasTxApplication || noDataRows.Length < 3 || rxAsduRows > 0)
        {
            return;
        }

        findings.Add(new EvidenceSmartFinding(
            EvidenceSmartFindingSeverity.Warning,
            "ARIEC-SMART-LINK-ALIVE-APP-SILENT",
            "Link layer answers, but application data is silent.",
            "Repeated no-data responses mean serial/link communication may be alive. The next suspect is usually CA, COT/CA/IOA size, interrogation group, or point table configuration.",
            $"{noDataRows.Length} no-data rows after application requests; no valid RX ASDU decoded in the selected evidence scope.",
            "Do not change baud/parity first if link ACK exists. Verify CA and profile sizes, then run GI and check for ACTCON/ACTTERM.",
            "Medium",
            noDataRows[0].Sequence));
    }

    private static void AddConnectionContextFindings(IReadOnlyList<EvidenceRow> rows, IReadOnlyList<FindingRow> existingFindings, List<EvidenceSmartFinding> findings)
    {
        var evidenceText = string.Join(" ", rows.Select(SearchText));
        var findingText = string.Join(" ", existingFindings.Select(finding => string.Join(" ", finding.Id, finding.Title, finding.Evidence, finding.Impact, finding.Recommendation)));
        var text = string.Join(" ", evidenceText, findingText);

        if (string.IsNullOrWhiteSpace(text) || IsOperatorStopContext(text))
        {
            return;
        }

        var latestTx = rows.Where(IsTx).OrderByDescending(row => row.Sequence).FirstOrDefault();
        var latestRx = rows.Where(IsRx).OrderByDescending(row => row.Sequence).FirstOrDefault();
        var rowsAfterLatestTx = latestTx is null ? 0 : rows.Count(row => row.Sequence > latestTx.Sequence);
        var hasRecentTxWithoutRx = latestTx is not null && (latestRx is null || latestTx.Sequence > latestRx.Sequence + 2) && rowsAfterLatestTx >= 4;
        var hasTimeoutEvidence = ContainsAny(text, "timeout", "timed out", "no response", "no data received", "TESTFR confirmation was not received", "STARTDT confirmation was not received");
        var hasFaultEvidence = ContainsAny(text, "session faulted", "transport", "socket", "serial", "connection", "ioexception", "unauthorized", "access to the port", "port not", "does not exist", "forcibly closed", "connection reset", "broken pipe", "host unreachable", "network unreachable", "connection refused");

        // Do not create a live flicker merely because a TX row is currently the
        // newest frame. Wait for an actual timeout/fault diagnostic or several
        // subsequent rows after the unanswered request.
        if (!hasFaultEvidence && !hasTimeoutEvidence && !hasRecentTxWithoutRx)
        {
            return;
        }

        var classification = ClassifyConnectionContext(text, hasRecentTxWithoutRx, rows);
        if (classification is null)
        {
            return;
        }

        findings.Add(new EvidenceSmartFinding(
            classification.Severity,
            classification.Code,
            classification.Problem,
            classification.Why,
            classification.Proof,
            classification.Fix,
            classification.Confidence,
            classification.Sequence));
    }

    private static ConnectionContextFinding? ClassifyConnectionContext(string text, bool hasRecentTxWithoutRx, IReadOnlyList<EvidenceRow> rows)
    {
        var lastSequence = rows.Count == 0 ? 0 : rows.Max(row => row.Sequence);

        if (ContainsAny(text, "actively refused", "connection refused", "no connection could be made"))
        {
            return new ConnectionContextFinding(
                EvidenceSmartFindingSeverity.Error,
                "ARIEC-SMART-CONNECTION-REFUSED",
                "IEC-104 TCP connection was refused by the target endpoint.",
                "The network path reached a host, but the remote TCP service rejected port access. This is different from a manual Disconnect.",
                "Fault text indicates connection refused / no listener response.",
                "Verify IP address, TCP port 2404, server service status, firewall rule, and active-client limit. Check whether another master is already connected.",
                "High",
                lastSequence);
        }

        if (ContainsAny(text, "forcibly closed", "connection reset", "reset by peer", "broken pipe", "connection abort", "software caused connection abort"))
        {
            return new ConnectionContextFinding(
                EvidenceSmartFindingSeverity.Error,
                "ARIEC-SMART-REMOTE-CLOSED",
                "Remote endpoint closed or reset the IEC-104 connection.",
                "A TCP reset/abort normally means the peer, firewall, or network stack closed an active connection. This is not the same as the operator pressing Disconnect.",
                "Fault text contains reset/abort/forcibly-closed evidence.",
                "Check server logs, duplicate master/client sessions, IEC-104 STARTDT policy, keepalive/test-frame timers, and network equipment that may reset idle TCP sessions.",
                "High",
                lastSequence);
        }

        if (ContainsAny(text, "host unreachable", "network unreachable", "no route", "destination unreachable"))
        {
            return new ConnectionContextFinding(
                EvidenceSmartFindingSeverity.Error,
                "ARIEC-SMART-NETWORK-PATH-DOWN",
                "Network path to the IEC-104 endpoint is unreachable.",
                "The client cannot reach the host/network. This points to cable, switch, routing, VLAN, IP addressing, or device power rather than an application CA/COT/IOA issue.",
                "Fault text indicates unreachable host/network/no route.",
                "Check link LEDs, switch port, VLAN, IP/subnet/gateway, firewall, and device power before changing IEC application profile fields.",
                "High",
                lastSequence);
        }

        if (ContainsAny(text, "access to the port", "unauthorizedaccess", "access is denied"))
        {
            return new ConnectionContextFinding(
                EvidenceSmartFindingSeverity.Error,
                "ARIEC-SMART-SERIAL-PORT-BUSY",
                "Serial COM port is not available to the analyzer.",
                "The local PC/driver rejected access before the protocol could prove slave health. This is a local port ownership problem, not a slave CA/profile problem.",
                "Fault text indicates access denied / port busy.",
                "Close the other serial client, release the USB/RS-485 adapter, verify driver permission, then reconnect. Do not change CA/COT/IOA for this symptom.",
                "High",
                lastSequence);
        }

        if (ContainsAny(text, "port does not exist", "does not exist", "port not found", "could not find", "file not found"))
        {
            return new ConnectionContextFinding(
                EvidenceSmartFindingSeverity.Error,
                "ARIEC-SMART-SERIAL-PORT-MISSING",
                "Configured serial COM port is missing.",
                "The PC cannot open the configured serial adapter. This usually means wrong COM selection, unplugged USB adapter, disabled driver, or OS port renumbering.",
                "Fault text indicates port missing/not found.",
                "Refresh ports, select the correct COM port, verify USB/RS-485 adapter power/driver, then reconnect.",
                "High",
                lastSequence);
        }

        if (ContainsAny(text, "TESTFR confirmation was not received", "testfr"))
        {
            return new ConnectionContextFinding(
                EvidenceSmartFindingSeverity.Warning,
                "ARIEC-SMART-IEC104-SUPERVISION-TIMEOUT",
                "IEC-104 idle supervision is not confirmed.",
                "IEC-104 uses TCP plus APCI supervision. Missing TESTFR confirmation means the TCP session may be half-open, filtered, overloaded, or the server does not answer the test frame policy.",
                "TESTFR confirmation timeout was recorded.",
                "Check t1/t2/t3 timers, server TESTFR support, firewall/NAT idle timeout, duplicate sessions, and whether I-format traffic resumes after the timeout.",
                "Medium",
                lastSequence);
        }

        if (ContainsAny(text, "STARTDT confirmation was not received", "startdt"))
        {
            return new ConnectionContextFinding(
                EvidenceSmartFindingSeverity.Error,
                "ARIEC-SMART-IEC104-STARTDT-BLOCKED",
                "IEC-104 connection opened, but STARTDT was not confirmed.",
                "TCP may be connected, but the IEC-104 data-transfer state did not open. This usually points to server policy, duplicate clients, access control, or a non-IEC-104 service on the port.",
                "STARTDT confirmation timeout was recorded.",
                "Verify server connection limit, allowed client IP, port 2404, STARTDT policy, firewall/NAT path, and server application state.",
                "High",
                lastSequence);
        }

        if (ContainsAny(text, "timed out", "timeout", "no response") || hasRecentTxWithoutRx)
        {
            var hasAnyRx = rows.Any(IsRx);
            var hasLinkAck = rows.Any(row => IsRx(row) && ContainsAny(SearchText(row), "link status", "access demand", "acd=", "dfc=", "ack"));
            var problem = hasAnyRx
                ? "Communication became silent after previously valid responses."
                : "No response was received from the slave/device.";
            var why = hasAnyRx
                ? "The line had valid RX evidence earlier, so the later silence is more consistent with slave hang, device restart, cable disturbance, or serial/network interruption than a static CA/profile error."
                : "Protocol evidence alone cannot prove whether the cable is unplugged, the device is powered off, or the device application is hung; it can prove that TX was sent and no slave response arrived.";
            var proof = hasLinkAck
                ? "Earlier link-layer response was seen, then timeout/no-response evidence appeared."
                : "Timeout/no-response evidence exists without sufficient RX proof in the selected scope.";
            return new ConnectionContextFinding(
                EvidenceSmartFindingSeverity.Warning,
                hasAnyRx ? "ARIEC-SMART-LINK-DROPPED-OR-HUNG" : "ARIEC-SMART-NO-SLAVE-RESPONSE",
                problem,
                why,
                proof,
                "First separate operator Disconnect from fault evidence. Then check physical cable/link LEDs, device power/boot state, serial polarity/termination, COM adapter, and only after link replies exist check CA/COT/IOA/application profile.",
                hasAnyRx ? "Medium" : "High",
                lastSequence);
        }

        return null;
    }

    private static bool IsOperatorStopContext(string text)
        => ContainsAny(text, "stop requested by user", "session stopped by user", "operator disconnected", "operator disconnect", "disconnect button", "manual disconnect");

    private sealed record ConnectionContextFinding(
        EvidenceSmartFindingSeverity Severity,
        string Code,
        string Problem,
        string Why,
        string Proof,
        string Fix,
        string Confidence,
        long Sequence);

    private static void AddHighValueExistingFindings(IReadOnlyList<FindingRow> existingFindings, List<EvidenceSmartFinding> findings)
    {
        foreach (var finding in existingFindings.Take(6))
        {
            var text = string.Join(" ", finding.Id, finding.Title, finding.Evidence, finding.Impact, finding.Recommendation);
            if (ShouldSkipImportedExistingFinding(text)
                || !ContainsAny(text, "timeout", "negative", "unknown", "ca", "ioa", "quality", "class 1", "gi", "command", "profile"))
            {
                continue;
            }

            var code = "ARIEC-SMART-" + SafeCode(finding.Id);
            if (findings.Any(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            findings.Add(new EvidenceSmartFinding(
                ParseSeverity(finding.Severity),
                code,
                Clean(finding.Title),
                Short(Clean(finding.Impact), 150),
                Short(Clean(finding.Evidence), 160),
                Short(Clean(finding.Recommendation), 180),
                "Medium",
                0));
        }
    }

    private static bool ShouldSkipImportedExistingFinding(string text)
    {
        if (ContainsAny(text, "no failover", "session completed without failover", "healthy during the run", "auto failback is enabled"))
        {
            return true;
        }

        if (IsOperatorStopContext(text) || ContainsAny(text, "stopped by cancellation", "operation canceled"))
        {
            return true;
        }

        return text.Contains("class", StringComparison.OrdinalIgnoreCase)
               && text.Contains("congestion", StringComparison.OrdinalIgnoreCase)
               && (text.Contains("general interrogation", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("C_IC", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("interrogation command", StringComparison.OrdinalIgnoreCase));
    }

    private static Iec60870ProtocolMode InferProtocolMode(IReadOnlyList<EvidenceRow> rows, IReadOnlyList<KeyValuePair<string, string>> setup)
    {
        var setupProtocol = setup.FirstOrDefault(pair => pair.Key.Equals("Protocol", StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
        if (setupProtocol.Contains("104", StringComparison.OrdinalIgnoreCase) || rows.Any(row => row.ProtocolMode == "104"))
        {
            return Iec60870ProtocolMode.Iec104;
        }

        if (setupProtocol.Contains("101", StringComparison.OrdinalIgnoreCase) || rows.Any(row => row.ProtocolMode == "101"))
        {
            return Iec60870ProtocolMode.Iec101;
        }

        return Iec60870ProtocolMode.Iec103;
    }

    private static bool IsTx(EvidenceRow row) => row.Direction.Equals("TX", StringComparison.OrdinalIgnoreCase);
    private static bool IsRx(EvidenceRow row) => row.Direction.Equals("RX", StringComparison.OrdinalIgnoreCase);
    private static bool IsGi(EvidenceRow row) => IsTypeId(row, 100) || ContainsAny(SearchText(row), "general interrogation", "c_ic_na_1", "c_ic", "interrogation qualifier", "qoi");
    private static bool IsReadRequest(EvidenceRow row) => IsTypeId(row, 102) || ContainsAny(SearchText(row), "c_rd_na_1", "read command", "read request");

    private static bool IsControlCommand(EvidenceRow row)
    {
        var typeId = ParsePositiveInt(row.TypeId);
        if (typeId.HasValue)
        {
            // IEC-101/104 operate command ASDUs. Service commands such as GI (100),
            // counter interrogation (101), read (102), clock sync (103), test/reset,
            // and delay acquisition are intentionally excluded.
            return typeId.Value is 45 or 46 or 47 or 48 or 49 or 50 or 51
                or 58 or 59 or 60 or 61 or 62 or 63 or 64;
        }

        var text = SearchText(row);
        if (ContainsAny(text,
            "general interrogation", "interrogation command", "c_ic_na_1", "c_ci_na_1",
            "c_rd_na_1", "read command", "c_cs_na_1", "clock sync", "clock synchronization",
            "c_ts_na_1", "test command", "c_rp_na_1", "reset process", "c_cd_na_1", "delay acquisition"))
        {
            return false;
        }

        return ContainsAny(text,
            "c_sc_na_1", "c_dc_na_1", "c_rc_na_1", "c_se_na_1", "c_se_nb_1", "c_se_nc_1", "c_bo_na_1",
            "c_sc_ta_1", "c_dc_ta_1", "c_rc_ta_1", "c_se_ta_1", "c_se_tb_1", "c_se_tc_1", "c_bo_ta_1",
            "select", "operate", "control command", "breaker open", "breaker close");
    }

    private static bool IsLikelyControlFeedback(EvidenceRow row, EvidenceRow commandTx)
    {
        if (!IsRx(row))
        {
            return false;
        }

        if (IsActivationConfirmation(row) || IsActivationTermination(row))
        {
            return true;
        }

        var typeId = ParsePositiveInt(row.TypeId);
        if (typeId.HasValue && typeId.Value is 1 or 2 or 3 or 4 or 30 or 31)
        {
            return true;
        }

        var commandIoa = ParsePositiveInt(commandTx.IoAddress);
        var feedbackIoa = ParsePositiveInt(row.IoAddress);
        if (commandIoa.HasValue && feedbackIoa.HasValue && commandIoa.Value == feedbackIoa.Value)
        {
            return true;
        }

        return ContainsAny(SearchText(row), "feedback", "position", "status", "closed", "opened", "single-point", "double-point");
    }

    private static bool IsActivationConfirmation(EvidenceRow row) => IsCotCode(row, 7) || ContainsAny(SearchText(row), "activation confirmation", "actcon", "act_con");
    private static bool IsActivationTermination(EvidenceRow row) => IsCotCode(row, 10) || ContainsAny(SearchText(row), "activation termination", "actterm", "act_term");
    private static bool IsNegative(EvidenceRow row) => ContainsAny(SearchText(row), "negative", "nack", "failed", "reject", "not accepted");
    private static bool IsCotCode(EvidenceRow row, int expected) => ParsePositiveInt(row.CotCode) == expected;
    private static bool IsTypeId(EvidenceRow row, int expected) => ParsePositiveInt(row.TypeId) == expected;
    private static bool IsClassOne(EvidenceRow row) => ContainsAny(SearchText(row), "class 1", "class1", "data class 1", "class=1");
    private static bool IsSpontaneous(EvidenceRow row) => ContainsAny(SearchText(row), "spontaneous", "spont");
    private static bool IsGiApplicationData(EvidenceRow row)
    {
        var typeId = ParsePositiveInt(row.TypeId);
        if (!typeId.HasValue)
        {
            return false;
        }

        // C_IC_NA_1 confirmation/termination rows prove GI control flow, but they are
        // not the application image. Values/events after the GI request are the proof
        // that interrogation is progressing.
        if (typeId.Value == 100)
        {
            return false;
        }

        return !IsActivationConfirmation(row) && !IsActivationTermination(row);
    }

    private static bool IsAnalogMeasuredValue(EvidenceRow row)
    {
        var typeId = ParsePositiveInt(row.TypeId);
        if (typeId.HasValue && (typeId.Value is 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 21 or 34 or 35 or 36))
        {
            return true;
        }

        return ContainsAny(SearchText(row), "measured", "analog", "m_me", "normalized", "scaled", "short floating", "float");
    }

    private static bool HasBadQuality(EvidenceRow row)
    {
        var quality = row.Quality ?? string.Empty;
        if (string.IsNullOrWhiteSpace(quality) || quality.Trim() == "-")
        {
            return false;
        }

        return ContainsAny(quality, "invalid", "not topical", "non topical", "substituted", "blocked", "overflow", "elapsed", "questionable", "bad")
            || (!quality.Contains("good", StringComparison.OrdinalIgnoreCase) && !quality.Equals("0", StringComparison.OrdinalIgnoreCase));
    }

    private static string SearchText(EvidenceRow row)
        => string.Join(" ", row.Category, row.State, row.DataClass, row.ProtocolService, row.ProtocolTraceTitle, row.ProtocolTraceMeaning, row.Detail, row.Summary, row.CotDisplay, row.Cot, row.Quality, row.RawHex, row.SignalOrAddress, row.SemanticState);

    private static int? Dominant(IEnumerable<int> values)
        => values.GroupBy(value => value).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).Select(group => (int?)group.Key).FirstOrDefault();

    private static int? ReadSetupInt(IReadOnlyList<KeyValuePair<string, string>> setup, string key)
    {
        var item = setup.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return ParsePositiveInt(item.Value);
    }

    private static int? ParsePositiveInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 0 || !int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            return null;
        }

        return parsed;
    }

    private static int? ParseResponseTimeMs(EvidenceRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ResponseTime) || row.ResponseTime == "-")
        {
            return null;
        }

        var digits = new string(row.ResponseTime.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static bool ContainsAny(string text, params string[] needles)
        => needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string Clean(string? value)
    {
        var clean = (value ?? string.Empty).ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(clean) ? "-" : clean;
    }

    private static string Short(string? value, int max)
    {
        var clean = Clean(value);
        if (clean.Length <= max || max < 4)
        {
            return clean;
        }

        return clean[..(max - 3)] + "...";
    }

    private static int SeverityRank(EvidenceSmartFindingSeverity severity)
        => severity switch
        {
            EvidenceSmartFindingSeverity.Error => 3,
            EvidenceSmartFindingSeverity.Warning => 2,
            _ => 1
        };

    private static EvidenceSmartFindingSeverity ParseSeverity(string severity)
        => severity.Equals("Error", StringComparison.OrdinalIgnoreCase) ? EvidenceSmartFindingSeverity.Error
            : severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? EvidenceSmartFindingSeverity.Warning
            : EvidenceSmartFindingSeverity.Info;

    private static string SafeCode(string value)
    {
        var clean = new string((value ?? string.Empty).Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(clean) ? "EXISTING-FINDING" : clean;
    }
}
