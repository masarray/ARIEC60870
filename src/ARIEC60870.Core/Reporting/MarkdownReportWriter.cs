// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;

namespace ARIEC60870.Core.Reporting;

public sealed class MarkdownReportWriter
{
    public string Write(AnalysisReport report, int maxTrafficRows = 120)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# ARIEC60870 Analysis Report");
        sb.AppendLine();
        sb.AppendLine($"**Source:** `{report.SourceFile}`");
        sb.AppendLine($"**Generated:** {report.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();

        WriteSummary(sb, report.Summary);
        WriteFindings(sb, report.Findings);
        WriteAsduTypes(sb, report.Summary);
        WriteTraffic(sb, report.Records, maxTrafficRows);
        return sb.ToString();
    }

    private static void WriteSummary(StringBuilder sb, TrafficSummary summary)
    {
        sb.AppendLine("## Traffic Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|---|---:|");
        sb.AppendLine($"| Total frames | {summary.TotalFrames.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Fixed frames | {summary.FixedFrames.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Variable frames | {summary.VariableFrames.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Malformed frames | {summary.MalformedFrames.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Checksum errors | {summary.ChecksumErrors.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Class 1 requests | {summary.Class1Requests.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Class 2 requests | {summary.Class2Requests.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| NO DATA responses | {summary.NoDataResponses.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Reset FCB commands | {summary.ResetFcbCommands.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| NO DATA ratio | {summary.NoDataRatio:P1} |");
        sb.AppendLine();
    }

    private static void WriteFindings(StringBuilder sb, IReadOnlyList<Finding> findings)
    {
        sb.AppendLine("## Engineering Findings");
        sb.AppendLine();
        if (findings.Count == 0)
        {
            sb.AppendLine("No semantic finding was raised by the current rule set.");
            sb.AppendLine();
            return;
        }

        foreach (var finding in findings)
        {
            sb.AppendLine($"### {finding.Severity}: {finding.Title}");
            sb.AppendLine();
            sb.AppendLine($"**Rule ID:** `{finding.Id}`");
            sb.AppendLine();
            sb.AppendLine(finding.Summary);
            sb.AppendLine();
            sb.AppendLine($"**Impact:** {finding.Impact}");
            sb.AppendLine();
            sb.AppendLine($"**Recommendation:** {finding.Recommendation}");
            sb.AppendLine();

            if (finding.Evidence.Count > 0)
            {
                sb.AppendLine("Evidence:");
                sb.AppendLine();
                sb.AppendLine("| Line | Time | Direction | Label | Meaning | Hex |");
                sb.AppendLine("|---:|---|---|---|---|---|");
                foreach (var evidence in finding.Evidence)
                {
                    sb.AppendLine($"| {evidence.LineNumber} | {Escape(evidence.Timestamp)} | {Escape(evidence.Direction)} | {Escape(evidence.Label)} | {Escape(evidence.Meaning)} | `{Escape(evidence.Hex)}` |");
                }
                sb.AppendLine();
            }
        }
    }

    private static void WriteAsduTypes(StringBuilder sb, TrafficSummary summary)
    {
        if (summary.AsduTypeCounts.Count == 0) return;

        sb.AppendLine("## ASDU Type Distribution");
        sb.AppendLine();
        sb.AppendLine("| Type | Name | Count |");
        sb.AppendLine("|---:|---|---:|");
        foreach (var kv in summary.AsduTypeCounts.OrderBy(x => x.Key))
        {
            sb.AppendLine($"| {kv.Key} | {Escape(AsduDecoder.TypeName(kv.Key))} | {kv.Value} |");
        }
        sb.AppendLine();
    }

    private static void WriteTraffic(StringBuilder sb, IReadOnlyList<DecodedTraceRecord> records, int maxRows)
    {
        sb.AppendLine("## Decoded Traffic Preview");
        sb.AppendLine();
        sb.AppendLine($"Showing first {Math.Min(maxRows, records.Count)} of {records.Count} decoded frame(s).");
        sb.AppendLine();
        sb.AppendLine("| Line | Time | Direction | Addr | Control | ASDU | COT | FUN | INF | Semantic | Meaning | Hex |");
        sb.AppendLine("|---:|---|---|---:|---|---|---:|---:|---:|---|---|---|");

        foreach (var record in records.Take(maxRows))
        {
            var frame = record.Frame;
            var asdu = frame.Asdu;
            sb.AppendLine(
                $"| {record.Entry.LineNumber} | {Escape(record.Entry.TimestampText)} | {Escape(record.DirectionText)} | {DisplayByte(frame.LinkAddress)} | {DisplayByte(frame.Control)} | {Escape(asdu?.TypeName ?? "-")} | {DisplayInt(asdu?.CauseOfTransmission)} | {DisplayInt(asdu?.FunctionType)} | {DisplayInt(asdu?.InformationNumber)} | {Escape(asdu?.SemanticLabel ?? "-")} | {Escape(frame.ShortMeaning)} | `{Escape(frame.Hex)}` |");
        }

        sb.AppendLine();
    }

    private static string DisplayByte(byte? value) => value.HasValue ? $"0x{value.Value:X2}" : "-";
    private static string DisplayByte(int? value) => value.HasValue ? $"0x{value.Value:X}" : "-";
    private static string DisplayInt(int? value) => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "-";

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }
}
