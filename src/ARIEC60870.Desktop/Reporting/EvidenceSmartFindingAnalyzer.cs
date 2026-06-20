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
            .Where(row => IsTx(row) && (IsGi(row) || IsCommand(row) || IsReadRequest(row)))
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
        var commandTx = rows.FirstOrDefault(row => IsTx(row) && IsCommand(row));
        if (commandTx is null)
        {
            return;
        }

        var window = rows.Where(row => row.Sequence >= commandTx.Sequence).Take(80).ToArray();
        var hasActCon = window.Any(row => IsRx(row) && IsActivationConfirmation(row));
        var hasActTerm = window.Any(row => IsRx(row) && IsActivationTermination(row));
        var hasFeedback = window.Any(row => IsRx(row) && ContainsAny(SearchText(row), "feedback", "single-point", "double-point", "position", "status", "soe", "spontaneous"));
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
        var commandTx = rows.FirstOrDefault(row => IsTx(row) && IsCommand(row));
        if (commandTx is null)
        {
            return;
        }

        var afterCommand = rows.Where(row => row.Sequence > commandTx.Sequence).Take(120).ToArray();
        var analogClassOne = afterCommand.Where(row => IsClassOne(row) && IsAnalogMeasuredValue(row)).Take(80).ToArray();
        var spontaneousAnalog = afterCommand.Where(row => IsSpontaneous(row) && IsAnalogMeasuredValue(row)).Take(80).ToArray();
        var latencyMs = afterCommand.Select(ParseResponseTimeMs).Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty(0).Max();

        if (analogClassOne.Length < 8 && spontaneousAnalog.Length < 8 && latencyMs < 1500)
        {
            return;
        }

        var dominant = analogClassOne.Length >= spontaneousAnalog.Length ? analogClassOne : spontaneousAnalog;
        if (dominant.Length == 0)
        {
            return;
        }

        findings.Add(new EvidenceSmartFinding(
            EvidenceSmartFindingSeverity.Warning,
            "ARIEC-SMART-CLASS1-CONGESTION",
            "Command response is competing with noisy Class 1 / spontaneous analog traffic.",
            "Class 1 should carry urgent events and command evidence. Cyclic analog values in Class 1 can delay confirmation and make the command feel unresponsive.",
            $"After command row #{commandTx.Sequence}, detected {dominant.Length} analog/Class 1 rows before the response window settled; max response time {latencyMs} ms.",
            "Move cyclic analog/measured values to Class 2 or background scan. Keep Class 1 for SOE, protection events, command confirmation, and status changes, then retest command latency.",
            dominant.Length >= 15 || latencyMs >= 3000 ? "High" : "Medium",
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
        var hasTxApplication = rows.Any(row => IsTx(row) && (IsGi(row) || IsCommand(row) || IsReadRequest(row)));
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

    private static void AddHighValueExistingFindings(IReadOnlyList<FindingRow> existingFindings, List<EvidenceSmartFinding> findings)
    {
        foreach (var finding in existingFindings.Take(6))
        {
            var text = string.Join(" ", finding.Id, finding.Title, finding.Evidence, finding.Impact, finding.Recommendation);
            if (!ContainsAny(text, "timeout", "negative", "unknown", "ca", "ioa", "quality", "class 1", "gi", "command", "profile"))
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
    private static bool IsGi(EvidenceRow row) => ContainsAny(SearchText(row), "general interrogation", "interrogation", "c_ic", "qoi");
    private static bool IsCommand(EvidenceRow row) => ContainsAny(SearchText(row), "command", "select", "operate", "c_sc", "c_dc", "c_se", "set-point", "setpoint");
    private static bool IsReadRequest(EvidenceRow row) => ContainsAny(SearchText(row), "read", "c_rd");
    private static bool IsActivationConfirmation(EvidenceRow row) => IsCotCode(row, 7) || ContainsAny(SearchText(row), "activation confirmation", "actcon", "act_con");
    private static bool IsActivationTermination(EvidenceRow row) => IsCotCode(row, 10) || ContainsAny(SearchText(row), "activation termination", "actterm", "act_term");
    private static bool IsNegative(EvidenceRow row) => ContainsAny(SearchText(row), "negative", "nack", "failed", "reject", "not accepted");
    private static bool IsCotCode(EvidenceRow row, int expected) => ParsePositiveInt(row.CotCode) == expected;
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
