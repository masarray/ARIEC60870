// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// ASE-style, one-physical-frame / one-block IEC-101 trace presentation.
/// Uses the shared typed FT1.2/ASDU decoder, never infers object fields from a
/// string or rereads HEX. No UI controls, I/O, global mutable state or timers.
/// </summary>
public static class Iec101TraceNarrator
{
    public static (string Title, string Meaning) Describe(Iec103MasterEvidenceEvent item)
    {
        var arrow = item.Direction switch
        {
            FrameDirection.MasterToSlave => "⇧",
            FrameDirection.SlaveToMaster => "⇩",
            _ => "•"
        };
        var prefix = $"{arrow} [{item.TimestampUtc.ToLocalTime():HH:mm:ss}] ";
        if (!string.IsNullOrWhiteSpace(item.PhysicalSource))
            prefix += $"[{item.PhysicalSource}] ";

        var frame = item.Frame;
        var link = frame?.LinkControl;
        var address = item.LinkAddress ?? frame?.LinkAddress;
        var addressText = address.HasValue ? address.Value.ToString(CultureInfo.InvariantCulture) : "?";

        if (frame?.IsSingleCharacterAck == true)
            return (prefix + "Single-character response (E5)",
                "Data Link: E5 short response (request-dependent ACK / no-data); " +
                "no link address encoded. Match preceding request before interpreting. " +
                "Not proof of physical command execution.");
        if (frame?.IsSingleCharacterNack == true)
            return (prefix + "Single-character response (A2)",
                "Data Link: A2 single-character negative response; verify equipment " +
                "profile and raw frame before interpreting as IEC-101.");
        if (frame is { IsChecksumValid: false } || frame is { IsLengthValid: false, Format: Ft12FrameFormat.Malformed })
            return (prefix + "Invalid FT1.2 frame",
                "Data Link: Frame malformed or checksum/length invalid. " +
                "Decoded fields are not trusted; inspect original HEX.");

        if (link is null)
            return (prefix + "IEC-101 undecoded frame",
                "Data Link: Function and flags unavailable. Inspect original HEX and configured link-address width.");

        var primary = $"[PRM:1,FCV:{Bit(link.Fcv)}] [FCB:{Bit(link.Fcb)}]";
        var secondary = $"[PRM:0,ACD:{Bit(link.Acd)},DFC:{Bit(link.Dfc)}]";
        var declaredLength = frame?.DeclaredLength is int len ? $" Length {len}" : "";
        var addressSuffix = $" Data Link Addr {addressText}";

        if (link.Prm && link.FunctionCode is 10 or 11)
        {
            var which = link.FunctionCode == 10 ? "Class 1" : "Class 2";
            return (prefix + $"{which} request",
                $"Data Link: {which} (Respond) {primary}{addressSuffix}");
        }

        if (!link.Prm && link.FunctionCode == 9)
        {
            var which = GetRequestedClass(item.DataClass);
            var title = which + " Data Not Available";
            title += link.Acd switch
            {
                true => ", Class 1 Data Available!",
                false => ", No Class 1 Data Indicated",
                _ => ", Class 1 Availability Unknown"
            };
            var flow = link.Dfc == true ? " [DFC busy]" : "";
            return (prefix + title,
                $"Data Link: Nack (Data Not Available) {secondary}{flow}{addressSuffix}");
        }

        if (!link.Prm && link.FunctionCode == 8)
        {
            var asdu = item.AsduDetails;
            var title = asdu?.TypeId switch
            {
                30 => "Single Point with Date/Time response",
                31 => "Double Point with Date/Time response",
                1 => "Single Point response",
                3 => "Double Point response",
                100 => "Interrogation response",
                _ => asdu is not null ? $"{GetTypeLabel(asdu)} response" : "User Data response"
            };
            var builder = new StringBuilder(220);
            builder.Append("Data Link: User Data ").Append(secondary)
                .Append(declaredLength).Append(addressSuffix);
            if (asdu is not null)
                AppendAsdu(builder, asdu);
            else
                builder.Append('\n').Append("ASDU: Not decoded; check COT, CA, IOA and link-width settings.");
            return (prefix + title, builder.ToString());
        }

        string meaning;
        string label;
        if (!link.Prm && link.FunctionCode == 0)
        {
            label = "Link ACK response";
            meaning = $"Data Link: ACK {secondary}{addressSuffix} " +
                "(link confirmation only; physical command operation not proven)";
        }
        else if (!link.Prm && link.FunctionCode == 1)
        {
            label = "Link NACK response";
            meaning = $"Data Link: NACK {secondary}{addressSuffix}";
        }
        else if (link.Prm && link.FunctionCode == 0)
        {
            label = "Reset remote link";
            meaning = $"Data Link: Reset Remote Link {primary}{addressSuffix}";
        }
        else if (link.Prm && link.FunctionCode == 9)
        {
            label = "Link status request";
            meaning = $"Data Link: Link Status Request {primary}{addressSuffix}";
        }
        else if (!link.Prm && link.FunctionCode == 11)
        {
            label = "Link status response";
            meaning = $"Data Link: Link Status {secondary}{addressSuffix}";
        }
        else
        {
            label = link.FunctionName;
            meaning = $"Data Link: {link.FunctionName} " +
                (link.Prm ? primary : secondary) + declaredLength + addressSuffix;
        }

        if (item.AsduDetails is { } extra)
        {
            var builder = new StringBuilder(meaning);
            AppendAsdu(builder, extra);
            meaning = builder.ToString();
        }
        return (prefix + label, meaning);
    }

    private static void AppendAsdu(StringBuilder builder, Iec10xAsduDecode asdu)
    {
        builder.Append('\n').Append("ASDU: Type ID ").Append(asdu.TypeId)
            .Append(" <").Append(GetTypeLabel(asdu)).Append("> Count ").Append(asdu.ObjectCount);
        builder.Append('\n').Append("Cause of transmission: ").Append(asdu.CauseOfTransmission)
            .Append(" (").Append(asdu.CauseName).Append(") | Origin: ")
            .Append(asdu.CauseDetails?.OriginatorAddress is int origin ? origin.ToString(CultureInfo.InvariantCulture)
                : asdu.CauseDetails is null ? asdu.OriginatorAddress.ToString(CultureInfo.InvariantCulture) : "N/A");
        if (asdu.IsNegativeConfirm) builder.Append(" | P/N:1 (Negative)");
        if (asdu.IsTest) builder.Append(" | TEST:1");
        builder.Append('\n').Append("Comm Addr ").Append(asdu.CommonAddress);
        foreach (var obj in asdu.Objects)
            AppendObject(builder, asdu.TypeId, obj);
        if (asdu.Objects.Count != asdu.ObjectCount)
            builder.Append('\n').Append("Decoded ").Append(asdu.Objects.Count)
                .Append('/').Append(asdu.ObjectCount).Append(" objects; inspect decoder issues.");
        if (asdu.Issues.Count > 0)
            builder.Append('\n').Append("Decoder issues: ").Append(string.Join("; ", asdu.Issues));
    }

    private static void AppendObject(StringBuilder builder, int type, Iec10xInformationObject obj)
    {
        var digital = type is 1 or 2 or 3 or 4 or 30 or 31;
        var label = type is 1 or 2 or 30 ? "SI" :
            type is 3 or 4 or 31 ? "DI" : "IOA";
        string value;
        if (type is 1 or 2 or 30 && obj.RawBytes.Count > 0)
            value = (obj.RawBytes[0] & 1).ToString(CultureInfo.InvariantCulture);
        else if (type is 3 or 4 or 31 && obj.RawBytes.Count > 0)
            value = (obj.RawBytes[0] & 3).ToString(CultureInfo.InvariantCulture);
        else
            value = obj.ShortValue;
        if (string.IsNullOrWhiteSpace(value)) value = "(not decoded)";

        builder.Append('\n').Append(label).Append(' ').Append(obj.InformationObjectAddress)
            .Append(' ').Append(value);
        if (obj.QualityDetails is { } q)
            builder.Append(" [").Append(q.Invalid ? "Invalid" : q.DisplayText == "Good" ? "Valid" : "Valid, " + q.DisplayText)
                .Append(']');
        else if (digital)
            builder.Append(" [Quality unavailable]");
        else if (!string.IsNullOrWhiteSpace(obj.QualityText))
            builder.Append(" [").Append(obj.QualityText).Append(']');

        if (!string.IsNullOrWhiteSpace(obj.TimestampText))
        {
            var source = obj.TimestampText;
            var invalid = source.Contains("invalid", StringComparison.OrdinalIgnoreCase);
            if (DateTime.TryParseExact(source.Replace(" invalid-time", "", StringComparison.OrdinalIgnoreCase),
                    "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                builder.Append(' ').Append(date.ToString("MM/dd/yy HH:mm:ss.fff", CultureInfo.InvariantCulture));
            else
                builder.Append(' ').Append(source);

            if (HasCp56Time(type, obj.RawBytes))
            {
                var timeByteOffset = type switch
                {
                    30 or 31 or 58 or 59 or 60 => 1,
                    32 => 2,
                    34 or 35 or 61 or 62 => 3,
                    36 or 37 or 33 or 63 => 5,
                    64 => 4,
                    103 => 0,
                    _ => -1
                };
                if (timeByteOffset >= 0 && obj.RawBytes.Count >= timeByteOffset + 7)
                {
                    var minuteFlags = obj.RawBytes[timeByteOffset + 2];
                    var hourFlags = obj.RawBytes[timeByteOffset + 3];
                    invalid |= (minuteFlags & 0x80) != 0;
                    builder.Append(invalid ? " [Invalid time" : " [Valid time");
                    builder.Append((hourFlags & 0x80) != 0 ? ",Summer time]" : ",Standard time]");
                }
            }
            else if (invalid) builder.Append(" [Invalid time]");
        }

        if (obj.Issues.Count > 0)
            builder.Append(" [Decode issue: ").Append(string.Join("; ", obj.Issues)).Append(']');
    }

    private static bool HasCp56Time(int type, IReadOnlyList<byte> data) =>
        type is >= 30 and <= 37 or >= 58 and <= 64 or 103 && data.Count >= 7;

    private static string GetTypeLabel(Iec10xAsduDecode asdu) => asdu.TypeId switch
    {
        1 => "Single-point",
        3 => "Double-point",
        30 => "Single-point with date/time",
        31 => "Double-point with date/time",
        100 => "Interrogation command",
        _ => asdu.TypeName
    };

    private static string GetRequestedClass(string? dataClass)
    {
        if (dataClass?.Contains("Class 1", StringComparison.OrdinalIgnoreCase) == true) return "Class 1";
        if (dataClass?.Contains("Class 2", StringComparison.OrdinalIgnoreCase) == true) return "Class 2";
        return "Requested";
    }

    private static string Bit(bool? bit) => bit.HasValue ? bit.Value ? "1" : "0" : "?";
}
