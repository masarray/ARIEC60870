// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Compact, copyable IEC-101/104 frame interpretation. One source-typed
/// projection is shared by WPF, Clipboard and .txt export. This never
/// reparses HEX, creates signal names, or substitutes host time for RTU time.
/// </summary>
public static class SelectedFrameProtocolText
{
    public static string Format(EvidenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var b = new StringBuilder(350);
        var source = row.Source;
        var frame = source.Frame;
        var link = frame?.LinkControl;
        var asdu = source.AsduDetails;

        b.Append('#').Append(row.Sequence).Append("  ")
            .Append(row.Time).Append("  ").Append(row.Direction)
            .Append("  ").AppendLine(row.ProtocolName);
        // Some capture projections carry the decoded physical FT1.2 address
        // only on the frame; never replace it with the unrelated ASDU CA.
        var linkAddress = source.LinkAddress ?? frame?.LinkAddress;
        var address = linkAddress?.ToString(CultureInfo.InvariantCulture) ?? "-";
        if (link is not null)
        {
            var control = "Address=" + address +
                "  PRM=" + Bit(link.Prm) +
                "  FC=" + link.FunctionCode.ToString(CultureInfo.InvariantCulture);
            if (link.Prm)
                control += "  FCB=" + NullableBit(link.Fcb) + "  FCV=" + NullableBit(link.Fcv);
            else
                control += "  ACD=" + NullableBit(link.Acd) + "  DFC=" + NullableBit(link.Dfc);
            Line(b, "LINK", control);
            Line(b, "Function", link.FunctionName);
        }
        else if (linkAddress.HasValue)
            Line(b, "LINK", "Address=" + address);

        if (asdu is not null)
        {
            Line(b, "ASDU", "TI=" + asdu.TypeId.ToString(CultureInfo.InvariantCulture) +
                " (" + Iec101TraceNarrator.HumanTypeName(asdu) + ")" +
                "  VSQ=" + asdu.VariableStructureQualifier.ToString("X2", CultureInfo.InvariantCulture) +
                "  SQ=" + Bit(asdu.IsSequence) +
                "  Objects=" + asdu.Objects.Count.ToString(CultureInfo.InvariantCulture) +
                "/" + asdu.ObjectCount.ToString(CultureInfo.InvariantCulture));
            Line(b, "COT", asdu.CauseOfTransmission.ToString(CultureInfo.InvariantCulture) +
                (string.IsNullOrWhiteSpace(asdu.CauseName) ? "" : " (" + asdu.CauseName + ")") +
                "  NEG=" + Bit(asdu.IsNegativeConfirm) +
                "  TEST=" + Bit(asdu.IsTest) +
                (asdu.CotWidth == 2 ? "  OA=" + asdu.OriginatorAddress.ToString(CultureInfo.InvariantCulture) : ""));
            Line(b, "CA", asdu.CommonAddress.ToString(CultureInfo.InvariantCulture));
            foreach (var obj in asdu.Objects)
            {
                var q = obj.QualityDetails;
                var value = obj.Issues.Count > 0 ? "Decode issue" :
                    !string.IsNullOrWhiteSpace(obj.ShortValue) ? obj.ShortValue : "Not decoded";
                var quality = q is null ? "Not encoded" : q.BitDetails;
                var time = string.IsNullOrWhiteSpace(obj.TimestampText) ? "Not encoded" : obj.TimestampText;
                b.Append("IOA ").Append(obj.InformationObjectAddress.ToString(CultureInfo.InvariantCulture))
                    .Append("  Value=").Append(SingleLine(value))
                    .Append("  Quality=").Append(quality)
                    .Append("  RTU Time=").Append(SingleLine(time));
                if (obj.Cp56TimeDetails is { } clock)
                    b.Append("  TimeIV=").Append(Bit(clock.InvalidFlag))
                        .Append("  GEN=").Append(Bit(clock.SubstitutedFlag))
                        .Append("  Calendar=").Append(clock.CalendarInRange ? "In range" : "Invalid");
                b.AppendLine();
                if (obj.Issues.Count > 0)
                    Line(b, "Decode issue", string.Join("; ", obj.Issues));
            }
            if (asdu.Issues.Count > 0)
                Line(b, "ASDU issues", string.Join("; ", asdu.Issues));
        }
        else
            b.AppendLine("ASDU  Not decoded / not encoded in this frame");

        if (frame is not null)
        {
            Line(b, "FT1.2", "Checksum=" + (frame.IsChecksumValid ? "OK" : "INVALID") +
                "  Length=" + (frame.IsLengthValid ? "OK" : "INVALID"));
            if (frame.Issues.Count > 0)
                Line(b, "LINK issues", string.Join("; ", frame.Issues));
        }
        if (row.HasTimelineAlerts)
            Line(b, "FLAGS", row.TimelineAlerts);

        // In the scroll-limited inspector the engineer must see Address, TI,
        // COT and CA first, not a long HEX paragraph. Original source bytes
        // still appear verbatim in this exact clipboard/export text at the end.
        if (!string.IsNullOrWhiteSpace(source.PhysicalSource))
            Line(b, "Source", source.PhysicalSource);
        if (source.PhysicalStreamId.HasValue || source.RawStreamOffset.HasValue)
            Line(b, "Stream", (source.PhysicalStreamId?.ToString(CultureInfo.InvariantCulture) ?? "?") +
                "  Offset=" + (source.RawStreamOffset?.ToString(CultureInfo.InvariantCulture) ?? "?"));
        b.Append("HEX  ").AppendLine(row.RawHex);
        return b.ToString().TrimEnd();
    }

    private static void Line(StringBuilder builder, string field, string? value)
        => builder.Append(field).Append("  ").AppendLine(SingleLine(value));
    private static string SingleLine(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" :
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    private static string Bit(bool value) => value ? "1" : "0";
    private static string NullableBit(bool? value) => value.HasValue ? Bit(value.Value) : "-";
}
