// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Exact raw-byte ranges linked to typed IEC-101 interpretation. Invoked once
/// when a frame is selected, never in the receive/polling hot path.
/// Offsets are raw FT1.2 offsets; object offsets from decoder are ASDU-relative.
/// Invalid/truncated byte groups are never silently presented as healthy.
/// </summary>
public static class Iec101FrameByteInspector
{
    public sealed record Field(string Key, string Title, string Hex, string Meaning, int Offset, int Count);

    public static IReadOnlyList<Field> Explain(Iec103MasterEvidenceEvent evidence)
    {
        if (evidence.ProtocolMode != Iec60870ProtocolMode.Iec101 ||
            evidence.Frame is not { RawBytes.Count: > 0 } frame)
            return Array.Empty<Field>();

        var raw = frame.RawBytes;
        var fields = new List<Field>(14);
        void Add(string key, string title, int start, int count, string meaning)
        {
            if (count <= 0 || start < 0 || start > raw.Count || count > raw.Count - start) return;
            var hex = string.Join(" ", raw.Skip(start).Take(count).Select(x => x.ToString("X2", CultureInfo.InvariantCulture)));
            fields.Add(new Field(key, title, hex, meaning, start, count));
        }

        if (frame.Format == Ft12FrameFormat.SingleCharacter && raw.Count == 1)
        {
            Add("envelope", "Short response", 0, 1, raw[0] == 0xE5
                ? "E5 · Link ACK; match the request. Not proof of a command operation."
                : $"0x{raw[0]:X2} · Check device-specific reply meaning.");
            return fields;
        }

        if (frame.Format == Ft12FrameFormat.FixedLength && raw.Count >= 4)
        {
            Add("envelope", "Start", 0, 1, "10 · Fixed-length link frame");
            Add("control", "Control", 1, 1, DescribeLink(frame.LinkControl));
            var addressSize = Math.Clamp(frame.LinkAddressSize, 0, 2);
            if (addressSize > 0)
                Add("address", "Link address", 2, addressSize, $"RTU {frame.LinkAddress?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
            Add("check", "Integrity", raw.Count - 2, 2, DescribeIntegrity(frame));
            return fields;
        }

        if (frame.Format != Ft12FrameFormat.VariableLength || raw.Count < 8)
        {
            Add("raw", "Unrecognized frame", 0, raw.Count,
                "Frame structure is incomplete or invalid; check raw bytes.");
            return fields;
        }

        var linkSize = Math.Clamp(frame.LinkAddressSize, 0, 2);
        var asduStart = 5 + linkSize;
        Add("envelope", "FT1.2 header", 0, 4,
            $"Start 68 · L={frame.DeclaredLength?.ToString(CultureInfo.InvariantCulture) ?? "?"} bytes" +
            (frame.IsLengthValid ? "" : " · LENGTH INVALID"));
        Add("control", "Link control", 4, 1, DescribeLink(frame.LinkControl));
        if (linkSize > 0)
            Add("address", "Link address", 5, linkSize,
                $"RTU {frame.LinkAddress?.ToString(CultureInfo.InvariantCulture) ?? "?"}");

        var asdu = evidence.AsduDetails;
        if (asdu is { Issues.Count: 0 } && frame.IsChecksumValid && frame.IsLengthValid &&
            frame.AsduBytes.SequenceEqual(asdu.RawBytes) &&
            asdu.CotWidth is >= 1 and <= 2 &&
            asdu.CommonAddressWidth is >= 1 and <= 2 &&
            asdu.InformationObjectAddressWidth is >= 1 and <= 3 &&
            asduStart + 2 + asdu.CotWidth + asdu.CommonAddressWidth <= raw.Count - 2)
        {
            Add("asdu-type", "Type ID", asduStart, 1, $"{asdu.TypeId} · {asdu.TypeName}");
            Add("vsq", "VSQ", asduStart + 1, 1,
                $"Count {asdu.ObjectCount} · SQ={(asdu.IsSequence ? 1 : 0)}");
            // Source-configured profile, not an inferred first-object offset.
            // An SQ=1 second object deliberately carries no physical IOA.
            var cotSize = asdu.CotWidth;
            var caSize = asdu.CommonAddressWidth;
            Add("cot", "Cause (COT)", asduStart + 2, cotSize,
                $"{asdu.CauseOfTransmission} · {asdu.CauseName}" +
                (asdu.IsNegativeConfirm ? " · NEGATIVE" : "") +
                (asdu.IsTest ? " · TEST" : "") +
                (cotSize == 2 ? $" · Origin {asdu.OriginatorAddress}" : ""));
            if (caSize > 0)
                Add("ca", "Common address", asduStart + 2 + cotSize, caSize,
                    $"CA {asdu.CommonAddress}");

            foreach (var obj in asdu.Objects)
            {
                var key = $"object-{obj.Index}";
                if (obj.InformationObjectOffset >= 0 &&
                    obj.InformationElementOffset > obj.InformationObjectOffset)
                    Add(key, $"IOA #{obj.Index}", asduStart + obj.InformationObjectOffset,
                        obj.InformationElementOffset - obj.InformationObjectOffset,
                        $"IOA {obj.InformationObjectAddress}");
                var meaning = $"{(asdu.TypeId is 1 or 2 or 3 or 4 or 30 or 31 ? "Digital" : "Value")} " +
                    $"IOA {obj.InformationObjectAddress} = {obj.ShortValue}";
                if (obj.QualityDetails is { } quality)
                    meaning += $" · {(quality.Invalid ? "INVALID" : quality.DisplayText)}";
                else if (!string.IsNullOrWhiteSpace(obj.QualityText))
                    meaning += $" · {obj.QualityText}";
                if (!string.IsNullOrWhiteSpace(obj.TimestampText))
                    meaning += $" · {obj.TimestampText}";
                if (obj.Issues.Count > 0)
                    meaning += " · Decode warning";
                Add($"value-{obj.Index}", $"Value #{obj.Index}",
                    asduStart + obj.InformationElementOffset, obj.RawBytes.Count, meaning);
            }

            if (asdu.Objects.Count != asdu.ObjectCount || asdu.Issues.Count > 0)
                fields.Add(new Field("issue", "Decode warning", "-", string.Join("; ", asdu.Issues), -1, 0));
        }
        else if (raw.Count > asduStart + 2)
        {
            Add("asdu", "ASDU bytes", asduStart, raw.Count - asduStart - 2,
                "ASDU not decoded; verify Type/COT/CA/IOA sizes.");
        }

        Add("check", "Integrity", raw.Count - 2, 2, DescribeIntegrity(frame));
        return fields;
    }

    private static string DescribeLink(LinkControlInfo? link)
    {
        if (link is null) return "Control not decoded";
        if (link.Prm)
            return $"Master · FC={link.FunctionCode} ({link.FunctionName}) · PRM=1 FCB={Bit(link.Fcb)} FCV={Bit(link.Fcv)}";
        var result = $"RTU · FC={link.FunctionCode} ({link.FunctionName}) · PRM=0 ACD={Bit(link.Acd)} DFC={Bit(link.Dfc)}";
        if (link.FunctionCode == 9) result += " · No requested data";
        if (link.Acd == true) result += " · Class 1 pending";
        if (link.Dfc == true) result += " · Data flow blocked";
        return result;
    }

    private static string DescribeIntegrity(Ft12FrameDecode frame)
    {
        var actual = frame.Checksum.HasValue ? $"CS 0x{frame.Checksum.Value:X2}" : "CS ?";
        var calculated = frame.CalculatedChecksum.HasValue ? $" (expected 0x{frame.CalculatedChecksum.Value:X2})" : "";
        return $"{actual}{calculated} · checksum {(frame.IsChecksumValid ? "OK" : "BAD")} · " +
            $"length {(frame.IsLengthValid ? "OK" : "BAD")} · end 16";
    }

    private static string Bit(bool? bit) => bit.HasValue ? bit.Value ? "1" : "0" : "?";
}
