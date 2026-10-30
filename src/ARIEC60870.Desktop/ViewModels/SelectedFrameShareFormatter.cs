// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// User-selected frames only. A portable, plain-text companion to verified frames.jsonl;
/// it never substitutes interpretations for the original bytes.
/// </summary>
public static class SelectedFrameShareFormatter
{
    /// <summary>Exactly the operator-facing Trace lines; not the forensic archive report.</summary>
    public static string FormatForClipboard(IReadOnlyList<EvidenceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var sb = new StringBuilder(Math.Max(256, rows.Count * 160));
        foreach (var row in rows.OrderBy(x => x.Sequence))
        {
            if (sb.Length > 0) sb.AppendLine().AppendLine();
            var stamp = row.Source.TimestampUtc.Year >= 2000
                ? row.Source.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd ", CultureInfo.InvariantCulture) + row.Time
                : row.Time;
            var arrow = row.Direction == "TX" ? "↑ TX" : row.Direction == "RX" ? "↓ RX" : row.Direction;
            sb.Append(stamp).Append("  ").Append(arrow).Append("  ")
                .AppendLine(row.ProtocolTraceHeader);
            if (!string.IsNullOrWhiteSpace(row.ProtocolTraceCompactMeaning))
                sb.AppendLine(row.ProtocolTraceCompactMeaning.Trim());
            sb.Append("Hex Frame: ").Append(row.RawHex);
        }
        return sb.ToString();
    }

    public static string Format(IReadOnlyList<EvidenceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var sb = new StringBuilder(Math.Max(1024, rows.Count * 384));
        sb.AppendLine("# ARIEC60870 — Selected Protocol Frames");
        sb.AppendLine();
        sb.AppendLine("This is a user-selected EXCERPT of the visible protocol trace, not a continuous full-session capture.");
        sb.AppendLine("Source of truth: original raw HEX in each frame. Decoded fields are interpretation,");
        sb.AppendLine("and may depend on the IEC field-width profile. PC times are host receive estimates,");
        sb.AppendLine("not calibrated on-wire timestamps or RTU processing durations.");
        sb.AppendLine("Unknown/absent quality does not mean Good. Link ACK and ACTCON do not prove physical action.");
        sb.AppendLine();
        sb.AppendLine("Selected frames: " + rows.Count.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine();
        foreach (var row in rows.OrderBy(x => x.Sequence))
        {
            sb.AppendLine("## Frame " + row.Sequence.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
            Field(sb, "Protocol", row.ProtocolName);
            Field(sb, "Direction", row.Direction);
            Field(sb, "Host time", row.Time);
            Field(sb, "Link / source", row.Source.PhysicalSource);
            Field(sb, "Stream ID", row.Source.PhysicalStreamId?.ToString(CultureInfo.InvariantCulture));
            Field(sb, "Source byte offset", row.Source.RawStreamOffset?.ToString(CultureInfo.InvariantCulture));
            Field(sb, "Link address", row.LinkAddress);
            Field(sb, "ASDU Type ID", row.TypeIdName);
            Field(sb, "VSQ", row.Vsq);
            Field(sb, "COT", row.CotDisplay);
            Field(sb, "CA", row.CommonAddress);
            Field(sb, "IOA", row.IoAddress);
            Field(sb, "Quality", row.Quality);
            Field(sb, "IED / RTU time", row.RelayTime);
            Field(sb, "Integrity", row.Source.Frame?.IsChecksumValid == false
                ? "Invalid checksum" : row.Source.Frame is null
                    ? "Unverified / no parsed frame" : "Frame decoder checksum valid or not applicable");
            if (row.Source.CauseDetails is { } cause) Field(sb, "COT bits", cause.Detail);
            if (row.Source.CommandDetails is { } cmd) Field(sb, "Command / RTU reply", cmd.Detail);
            if (row.Source.QualityDetails is { } quality) Field(sb, "Quality bits", quality.BitDetails);
            if (row.Source.AsduDetails is { } asdu)
            {
                foreach (var obj in asdu.Objects)
                {
                    sb.Append("- Object #").Append(obj.Index).Append(": IOA ")
                        .Append(obj.InformationObjectAddress).Append("; value=")
                        .Append(Plain(obj.ValueText)).Append("; quality=")
                        .Append(Plain(obj.QualityDetails?.BitDetails ?? "N/A"))
                        .Append("; RTU time=").AppendLine(Plain(obj.TimestampText));
                }
            }
            sb.AppendLine();
            sb.AppendLine("Decoded Trace:");
            sb.AppendLine("~~~text");
            sb.AppendLine(Plain(row.ProtocolTraceTitle));
            sb.AppendLine(TraceText(row.ProtocolTraceMeaning));
            sb.AppendLine("~~~");
            sb.AppendLine();
            sb.AppendLine("Original raw HEX:");
            sb.AppendLine("~~~text");
            var bytes = row.Source.Frame?.RawBytes;
            sb.AppendLine(bytes is { Count: > 0 }
                ? string.Join(" ", bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)))
                : Plain(row.RawHex));
            sb.AppendLine("~~~");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static void Field(StringBuilder sb, string name, string? value)
        => sb.Append("- ").Append(name).Append(": ").AppendLine(Plain(value));

    private static string TraceText(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" :
            value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("~~~", "~ ~ ~", StringComparison.Ordinal).Trim();

    private static string Plain(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" :
            value.Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal).Trim();
}
