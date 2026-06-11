// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Master.Reporting;

public sealed class MasterMarkdownReportWriter
{
    public string Write(Iec103MasterRunResult result, int maxEvents = 300)
    {
        var sb = new StringBuilder();
        var c = result.Counters;

        sb.AppendLine("# ARIEC60870 Master Evidence Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Mode: {result.ProductMode}");
        sb.AppendLine($"Started UTC: {result.StartedUtc:O}");
        sb.AppendLine($"Finished UTC: {result.FinishedUtc:O}");
        sb.AppendLine($"Duration: {result.Duration}");
        sb.AppendLine($"Completion: {(result.CompletedNormally ? "Normal" : "Faulted")} - {result.CompletionReason}");
        sb.AppendLine();

        sb.AppendLine("## Connection");
        sb.AppendLine();
        sb.AppendLine($"- Serial: `{result.Settings.SerialSummary}`");
        sb.AppendLine($"- Mapping profile file: `{(string.IsNullOrWhiteSpace(result.Settings.MappingProfilePath) ? "not loaded" : result.Settings.MappingProfilePath)}`");
        sb.AppendLine($"- Timeout: `{result.Settings.ResponseTimeoutMs} ms`");
        sb.AppendLine($"- Class 2 interval: `{result.Settings.Class2PollIntervalMs} ms`");
        sb.AppendLine($"- Max Class 1 drain frames: `{result.Settings.MaxClass1DrainFrames}`");
        sb.AppendLine($"- Reset FCB on connect: `{result.Settings.ResetFcbOnConnect}`");
        sb.AppendLine($"- GI on connect: `{result.Settings.SendGeneralInterrogationOnConnect}`");
        sb.AppendLine($"- Clock sync on connect: `{result.Settings.SendClockSyncOnConnect}`");
        sb.AppendLine();

        sb.AppendLine("## Session counters");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|---|---:|");
        sb.AppendLine($"| TX frames | {c.TxFrames} |");
        sb.AppendLine($"| RX frames | {c.RxFrames} |");
        sb.AppendLine($"| Class 1 requests | {c.Class1Requests} |");
        sb.AppendLine($"| Class 2 requests | {c.Class2Requests} |");
        sb.AppendLine($"| NO DATA responses | {c.NoDataResponses} |");
        sb.AppendLine($"| User data responses | {c.UserDataResponses} |");
        sb.AppendLine($"| ACK / NACK | {c.AckResponses} / {c.NackResponses} |");
        sb.AppendLine($"| Reset remote link / Reset FCB | {c.ResetRemoteLinkCommands} / {c.ResetFcbCommands} |");
        sb.AppendLine($"| GI commands / GI END | {c.GiCommands} / {c.GiEndResponses} |");
        sb.AppendLine($"| Clock sync commands | {c.ClockSyncCommands} |");
        sb.AppendLine($"| DPI events | {c.DpiEvents} |");
        sb.AppendLine($"| Unknown ASDU responses | {c.UnknownAsduResponses} |");
        sb.AppendLine($"| Timeouts / timeout recoveries | {c.Timeouts} / {c.TimeoutRecoveries} |");
        sb.AppendLine($"| Transport exceptions captured | {c.TransportExceptions} |");
        sb.AppendLine($"| Busy responses | {c.BusyResponses} |");
        sb.AppendLine($"| Checksum errors / malformed | {c.ChecksumErrors} / {c.MalformedFrames} |");
        sb.AppendLine($"| Avg / Max response time | {c.AverageResponseTimeMs:F1} ms / {c.MaxResponseTimeMs} ms |");
        sb.AppendLine($"| Value points | {result.ValuePoints.Count} |");
        sb.AppendLine($"| Relay event log entries retained | {result.EventLog.Count} |");
        sb.AppendLine($"| Evidence events retained | {result.Events.Count} |");
        sb.AppendLine($"| Evidence events dropped from memory buffer | {c.EvidenceEventsDroppedFromMemory} |");
        sb.AppendLine($"| Relay events dropped from memory buffer | {c.RelayEventsDroppedFromMemory} |");
        sb.AppendLine($"| Findings dropped from memory buffer | {c.FindingsDroppedFromMemory} |");
        sb.AppendLine();

        sb.AppendLine("## AutoTest assessment");
        sb.AppendLine();
        sb.AppendLine($"Overall: **{result.Assessment.OverallStatus}** — score **{result.Assessment.Score}/100**");
        sb.AppendLine();
        sb.AppendLine(Escape(result.Assessment.Summary));
        sb.AppendLine();
        sb.AppendLine("| Area | Status | Check | Evidence | Recommendation |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var item in result.Assessment.Items)
        {
            sb.AppendLine($"| {Escape(item.Area)} | {item.Status} | {Escape(item.Title)} | {Escape(item.Evidence)} | {Escape(item.Recommendation)} |");
        }
        if (result.Assessment.Items.Count == 0)
        {
            sb.AppendLine("| - | Info | No assessment item | - | - |");
        }
        sb.AppendLine();

        sb.AppendLine("## Findings");
        sb.AppendLine();
        if (result.Findings.Count == 0)
        {
            sb.AppendLine("No master finding was raised by the current rule set.");
        }
        else
        {
            foreach (var finding in result.Findings)
            {
                sb.AppendLine($"### [{finding.Severity}] {finding.Id} - {finding.Title}");
                sb.AppendLine();
                sb.AppendLine($"**Evidence:** {finding.Evidence}");
                sb.AppendLine();
                sb.AppendLine($"**Impact:** {finding.Impact}");
                sb.AppendLine();
                sb.AppendLine($"**Recommendation:** {finding.Recommendation}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Value viewer snapshot");
        sb.AppendLine();
        sb.AppendLine("This table is the current relay value snapshot. Signal names are shown only when a user mapping profile is loaded. Otherwise ARIEC60870 keeps raw FUN/INF evidence visible.");
        sb.AppendLine();
        sb.AppendLine("| Signal | Value | Group | FUN | INF | Type | COT | Relay time | Mapped | Raw |");
        sb.AppendLine("|---|---|---|---:|---:|---|---|---|---|---|");
        foreach (var value in result.ValuePoints.Take(maxEvents))
        {
            sb.AppendLine($"| {Escape(value.SignalName)} | {Escape(value.DisplayValue)} | {Escape(value.SignalGroup)} | {DisplayInt(value.FunctionType)} | {DisplayInt(value.InformationNumber)} | {Escape(value.SignalType)} | {Escape(value.CauseOfTransmission)} | {Escape(value.RelayTimeText)} | {(value.IsMapped ? "yes" : "no")} | `{value.RawHex}` |");
        }
        if (result.ValuePoints.Count == 0)
        {
            sb.AppendLine("| - | - | - | - | - | - | - | - | - | - |");
        }
        sb.AppendLine();

        sb.AppendLine("## Relay event log");
        sb.AppendLine();
        sb.AppendLine("Event log time uses the relay timestamp from IEC-103 ASDU time fields. PC arrival time is not used as the event time.");
        sb.AppendLine();
        sb.AppendLine("| # | Relay time | Signal | Previous | New state/value | Edge reason | FUN | INF | Type | COT | Mapped | Raw |");
        sb.AppendLine("|---:|---|---|---|---|---|---:|---:|---|---|---|---|");
        foreach (var ev in result.EventLog.Take(maxEvents))
        {
            sb.AppendLine($"| {ev.EvidenceSequenceNumber} | {Escape(ev.RelayTimeText)} | {Escape(ev.SignalName)} | {Escape(ev.PreviousValue)} | {Escape(ev.NewValue)} | {Escape(ev.EdgeReason)} | {DisplayInt(ev.FunctionType)} | {DisplayInt(ev.InformationNumber)} | {Escape(ev.SignalType)} | {Escape(ev.CauseOfTransmission)} | {(ev.IsMapped ? "yes" : "no")} | `{ev.RawHex}` |");
        }
        if (result.EventLog.Count == 0)
        {
            sb.AppendLine("| - | No relay edge/state-change event recorded | - | - | - | - | - | - | - | - | - | - |");
        }
        sb.AppendLine();

        sb.AppendLine("## Diagnostics appendix");
        sb.AppendLine();
        sb.AppendLine("Runtime diagnostics are captured as evidence rows instead of being allowed to crash the WPF workflow. Select/copy rows from the Diagnostics tab in the desktop app for escalation.");
        sb.AppendLine();
        sb.AppendLine("| # | Time | State | Category | Summary | Detail | Recommendation | Exception |");
        sb.AppendLine("|---:|---|---|---|---|---|---|---|");
        var diagnosticRows = result.Events
            .Where(x => !string.IsNullOrWhiteSpace(x.ExceptionType)
                        || x.Category.Contains("Error", StringComparison.OrdinalIgnoreCase)
                        || x.Category.Contains("Warning", StringComparison.OrdinalIgnoreCase)
                        || x.Category.Contains("Diagnostic", StringComparison.OrdinalIgnoreCase)
                        || x.Summary.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            .Take(maxEvents)
            .ToArray();
        foreach (var item in diagnosticRows)
        {
            var recommendation = string.IsNullOrWhiteSpace(item.OperatorAction) ? item.PollingReason : item.OperatorAction;
            var exceptionType = string.IsNullOrWhiteSpace(item.ExceptionType) ? "-" : item.ExceptionType;
            sb.AppendLine($"| {item.SequenceNumber} | {item.TimestampUtc:HH:mm:ss.fff} | {Escape(item.State.ToString())} | {Escape(item.Category)} | {Escape(item.Summary)} | {Escape(item.Detail)} | {Escape(recommendation)} | {Escape(exceptionType)} |");
        }
        if (diagnosticRows.Length == 0)
        {
            sb.AppendLine("| - | - | - | - | No runtime diagnostic captured | - | - | - |");
        }
        sb.AppendLine();

        sb.AppendLine("## Evidence trace");
        sb.AppendLine();
        sb.AppendLine("This is the advanced evidence layer. Operator meaning is shown first; raw hex remains available for protocol transparency.");
        sb.AppendLine();
        sb.AppendLine("| # | PC time | State | Dir | Class | Operator meaning | Action | Signal | Value | Relay time | Protocol detail | Rt | Hex |");
        sb.AppendLine("|---:|---|---|---|---|---|---|---|---|---|---|---:|---|");
        foreach (var item in result.Events.Take(maxEvents))
        {
            var rt = item.ResponseTimeMs.HasValue ? item.ResponseTimeMs.Value.ToString() : "";
            var signal = string.IsNullOrWhiteSpace(item.SignalName) ? string.Empty : item.SignalName;
            var value = string.IsNullOrWhiteSpace(item.SignalDisplayValue) ? string.Empty : item.SignalDisplayValue;
            var operatorMeaning = string.IsNullOrWhiteSpace(item.OperatorMessage) ? item.Summary : item.OperatorMessage;
            var action = string.IsNullOrWhiteSpace(item.OperatorAction) ? item.PollingReason : item.OperatorAction;
            var protocolDetail = string.IsNullOrWhiteSpace(item.ProtocolMeaning) ? item.Detail : item.ProtocolMeaning;
            sb.AppendLine($"| {item.SequenceNumber} | {item.TimestampUtc:HH:mm:ss.fff} | {Escape(item.State.ToString())} | {item.DirectionText} | {Escape(item.DataClass)} | {Escape(operatorMeaning)} | {Escape(action)} | {Escape(signal)} | {Escape(value)} | {Escape(item.RelayTimestampText)} | {Escape(protocolDetail)} | {rt} | `{item.RawHex}` |");
        }

        if (result.Events.Count > maxEvents)
        {
            sb.AppendLine();
            sb.AppendLine($"Trace table limited to {maxEvents} retained events. Long sessions are memory-bounded by the configured retention policy; counters above show if old retained events were dropped.");
        }

        sb.AppendLine();
        sb.AppendLine("## Engineering note");
        sb.AppendLine();
        sb.AppendLine("ARIEC60870 uses a SCADA-style polling policy: Class 2/background polling is the normal cycle; Class 1 is drained only after ACD=1 or inside a bounded GI follow-up window. Normal event drain can stop when ACD clears, but GI follow-up drain is stricter: it continues until ACTTERM/GI END, NO DATA, cancellation, DFC busy, or configured drain limit. This avoids losing multi-frame GI data while still preventing continuous Class 1 bombardment during idle operation.");
        sb.AppendLine();
        sb.AppendLine("Signal names in Value Viewer and Event Log come from a user-supplied mapping profile. ARIEC60870 does not include built-in vendor signal mapping; raw FUN/INF remains the source of truth.");
        sb.AppendLine();
        sb.AppendLine("For high-volume polling, ARIEC60870 keeps a bounded retained evidence buffer, a value snapshot, and an edge-only relay event log. This keeps long-running sessions lighter in memory and avoids rendering every frame as a user-facing event.");
        return sb.ToString();
    }

    private static string DisplayInt(int? value) => value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    }
}
