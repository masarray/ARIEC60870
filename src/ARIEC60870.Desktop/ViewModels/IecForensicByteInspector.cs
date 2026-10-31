// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Field = ARIEC60870.Desktop.ViewModels.Iec101FrameByteInspector.Field;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Selection-only physical-byte inspection for IEC-101/103/104. Fields always
/// refer to offsets in the ORIGINAL FT1.2 frame or IEC-104 APDU, not text,
/// indices within an ASDU, or a reconstructed telegram.
/// No additional receive decoder, buffer ownership, or WPF work on RX.
/// </summary>
public static class IecForensicByteInspector
{
    public static IReadOnlyList<Field> Explain(Iec103MasterEvidenceEvent evidence)
        => evidence.ProtocolMode switch
        {
            Iec60870ProtocolMode.Iec101 => Iec101FrameByteInspector.Explain(evidence),
            Iec60870ProtocolMode.Iec103 => Explain103(evidence),
            Iec60870ProtocolMode.Iec104 => Explain104(evidence),
            _ => Array.Empty<Field>()
        };

    private static IReadOnlyList<Field> Explain103(Iec103MasterEvidenceEvent evidence)
    {
        var frame = evidence.Frame;
        if (frame is not { RawBytes.Count: > 0 })
            return Array.Empty<Field>();

        var raw = frame.RawBytes;
        var fields = new List<Field>();
        void Add(string key, string title, int offset, int count, string meaning)
            => AddPhysical(fields, raw, key, title, offset, count, meaning);

        if (!frame.IsChecksumValid || !frame.IsLengthValid ||
            frame.Format == Ft12FrameFormat.Malformed)
        {
            Add("raw", "Unverified FT1.2", 0, raw.Count,
                "Link checksum, end marker, or declared length failed. Do not trust typed protection fields.");
            return fields;
        }

        if (frame.Format == Ft12FrameFormat.SingleCharacter)
        {
            Add("envelope", "Link reply", 0, 1,
                frame.IsSingleCharacterAck ? "E5 · Link ACK only; not physical command execution" :
                frame.IsSingleCharacterNack ? "A2 · Link NACK only" : "Unknown short response");
            return fields;
        }

        var linkWidth = frame.LinkAddressSize;
        if (linkWidth is < 0 or > 2) return fields;
        if (frame.Format == Ft12FrameFormat.FixedLength)
        {
            Add("envelope", "FT1.2 start", 0, 1, "10 · Fixed link frame");
            Add("control", "Link control", 1, 1, frame.LinkControl?.FunctionName ?? "Link control byte");
            if (linkWidth > 0)
                Add("address", "Link address", 2, linkWidth,
                    $"Link {frame.LinkAddress?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
            Add("check", "Checksum / end", raw.Count - 2, 2, "Verified FT1.2 link checksum and end 16");
            return fields;
        }
        if (frame.Format != Ft12FrameFormat.VariableLength)
        {
            Add("raw", "Unrecognized frame", 0, raw.Count, "No verified FT1.2 layout");
            return fields;
        }

        var asduStart = 5 + linkWidth;
        Add("envelope", "FT1.2 header", 0, 4, $"68 · Declared L={frame.DeclaredLength}");
        Add("control", "Link control", 4, 1, frame.LinkControl?.FunctionName ?? "Link control byte");
        if (linkWidth > 0)
            Add("address", "Link address", 5, linkWidth,
                $"Link {frame.LinkAddress?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");

        var asdu = frame.Asdu;
        var trustworthyPayload = asdu is not null && frame.AsduBytes.Count >= 4 &&
            asduStart + frame.AsduBytes.Count == raw.Count - 2 &&
            frame.AsduBytes.SequenceEqual(raw.Skip(asduStart).Take(frame.AsduBytes.Count));
        if (trustworthyPayload && asdu is not null)
        {
            Add("asdu-type", "Type ID", asduStart, 1,
                $"IEC-103 Type {asdu!.TypeId} · {asdu.TypeName}");
            Add("vsq", "VSQ", asduStart + 1, 1,
                $"Declared objects={asdu.ObjectCount} · SQ={(asdu.Sequence ? 1 : 0)}");
            Add("cot", "Cause", asduStart + 2, 1,
                $"COT {asdu.CauseOfTransmission} · {asdu.CauseName}");
            Add("ca", "ASDU address", asduStart + 3, 1,
                $"IEC-103 common address {asdu.CommonAddress}");
            if (frame.AsduBytes.Count >= 6 && asdu.FunctionType.HasValue && asdu.InformationNumber.HasValue)
            {
                Add("fun", "FUN", asduStart + 4, 1,
                    $"Function type {asdu.FunctionType.Value}; no IEC-101 IOA implied");
                Add("inf", "INF", asduStart + 5, 1,
                    $"Information number {asdu.InformationNumber.Value}");
                if (frame.AsduBytes.Count > 6)
                    Add("payload", "Information elements", asduStart + 6,
                        frame.AsduBytes.Count - 6,
                        "Original payload bytes. IEC-103 multi-object/private layouts are NOT inferred from this first FUN/INF.");
            }
            else if (frame.AsduBytes.Count > 4)
                Add("payload", "Undecoded information", asduStart + 4,
                    frame.AsduBytes.Count - 4, "Incomplete FUN/INF: raw only");
        }
        else if (frame.AsduBytes.Count > 0)
            Add("asdu", "Raw ASDU", asduStart, frame.AsduBytes.Count,
                "No trustworthy IEC-103 ASDU layout; raw original preserved");

        Add("check", "Checksum / end", raw.Count - 2, 2,
            "Verified FT1.2 link checksum and end marker 16");
        return fields;
    }

    private static IReadOnlyList<Field> Explain104(Iec103MasterEvidenceEvent evidence)
    {
        if (!TryReadRawHex(evidence.RawHex, out var raw))
            return Array.Empty<Field>();
        var fields = new List<Field>();
        void Add(string key, string title, int offset, int count, string meaning)
            => AddPhysical(fields, raw, key, title, offset, count, meaning);

        // An IEC-104 APDU has 6-byte APCI and at most 253 declared APDU
        // octets. Malformed frames must not yield convincing typed rows.
        if (raw.Length < 6 || raw.Length > 255 || raw[0] != 0x68 ||
            raw[1] != raw.Length - 2 || raw[1] > 253)
        {
            Add("raw", "Unverified APDU", 0, raw.Length,
                "Malformed IEC-104 length/start; keep raw evidence, do not interpret fields");
            return fields;
        }

        // Authoritative protocol parser, invoked only on inspector selection.
        var asdu = evidence.AsduDetails;
        var parser = new Iec104ApduParser(
            asdu?.CotWidth is >= 1 and <= 2 ? asdu.CotWidth : 2,
            asdu?.CommonAddressWidth is >= 1 and <= 2 ? asdu.CommonAddressWidth : 2,
            asdu?.InformationObjectAddressWidth is >= 1 and <= 3 ? asdu.InformationObjectAddressWidth : 3);
        var apdu = parser.Decode(raw);
        if (!apdu.IsValid || apdu.Format is not ("I" or "S" or "U") ||
            (apdu.Format is "S" or "U" && raw.Length != 6))
        {
            Add("raw", "Unverified APCI", 0, raw.Length,
                "Malformed APCI or unsupported length; raw bytes retained without typed claim");
            return fields;
        }

        Add("envelope", "APDU header", 0, 2, $"IEC-104 68 · length={raw[1]}");
        var apciText = apdu.Format switch
        {
            "I" => $"I-format N(S)={apdu.SendSequence}, N(R)={apdu.ReceiveSequence}; not a remote execution acknowledgement",
            "S" => $"S-format acknowledgement N(R)={apdu.ReceiveSequence}; no ASDU",
            _ => $"U-format {apdu.UFormatName}; session control, not a process value"
        };
        Add("apci", "APCI control", 2, 4, apciText);
        if (apdu.Format != "I") return fields;

        var asduLength = raw.Length - 6;
        // Compare original ASDU octets to the same typed parser result
        // produced by the active Master/replay path. Do not reinterpret a
        // capture's user-editable display text as protocol evidence.
        var trusted = asdu is { Issues.Count: 0 } &&
            asdu.CotWidth is >= 1 and <= 2 &&
            asdu.CommonAddressWidth is >= 1 and <= 2 &&
            asdu.InformationObjectAddressWidth is >= 1 and <= 3 &&
            asdu.RawBytes.Count == asduLength &&
            asdu.RawBytes.SequenceEqual(raw.Skip(6));
        if (!trusted || asdu is null)
        {
            if (asduLength > 0)
                Add("asdu", "Unverified ASDU", 6, asduLength,
                    "No matching validated ASDU/profile; no Type/COT/CA/IOA assumed");
            return fields;
        }

        Add("asdu-type", "Type ID", 6, 1, $"{asdu!.TypeId} · {asdu.TypeName}");
        Add("vsq", "VSQ", 7, 1, $"Count {asdu.ObjectCount} · SQ={(asdu.IsSequence ? 1 : 0)}");
        Add("cot", "Cause (COT)", 8, asdu.CotWidth,
            $"COT {asdu.CauseOfTransmission} ({asdu.CauseName})" +
            (asdu.IsNegativeConfirm ? " · NEG" : "") + (asdu.IsTest ? " · TEST" : "") +
            (asdu.CotWidth == 2 ? $" · OA {asdu.OriginatorAddress}" : ""));
        Add("ca", "Common address", 8 + asdu.CotWidth, asdu.CommonAddressWidth,
            $"CA {asdu.CommonAddress}");

        foreach (var obj in asdu.Objects)
        {
            if (obj.InformationObjectOffset >= 0)
                Add($"object-{obj.Index}", $"IOA #{obj.Index}",
                    6 + obj.InformationObjectOffset, asdu.InformationObjectAddressWidth,
                    $"IOA {obj.InformationObjectAddress}");
            else if (asdu.IsSequence && obj.Index > 1)
                fields.Add(new Field($"implicit-ioa-{obj.Index}", $"Implied IOA #{obj.Index}",
                    "-", $"SQ=1 derives IOA {obj.InformationObjectAddress} from the first transmitted IOA; NO address octets at this position", -1, 0));

            Add($"value-{obj.Index}", $"Value #{obj.Index}",
                6 + obj.InformationElementOffset, obj.RawBytes.Count,
                $"IOA {obj.InformationObjectAddress}: {obj.ShortValue}" +
                (obj.QualityDetails is { } q ? $" · Quality {q.DisplayText}" : " · No quality field") +
                (string.IsNullOrWhiteSpace(obj.TimestampText) ? "" : $" · {obj.TimestampText}"));
        }

        if (asdu.Objects.Count != asdu.ObjectCount)
            fields.Add(new Field("issue", "Object count mismatch", "-",
                $"Decoded {asdu.Objects.Count} / declared {asdu.ObjectCount}; do not infer absent elements", -1, 0));
        return fields;
    }

    private static void AddPhysical(List<Field> fields, IReadOnlyList<byte> raw,
        string key, string title, int offset, int count, string meaning)
    {
        if (offset < 0 || count <= 0 || offset > raw.Count || count > raw.Count - offset)
            return;
        var hex = string.Join(" ", raw.Skip(offset).Take(count)
            .Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        fields.Add(new Field(key, title, hex, meaning, offset, count));
    }

    private static bool TryReadRawHex(string? hex, out byte[] raw)
    {
        raw = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(hex) || hex.Length > 1_100)
            return false;
        var tokens = hex.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1 && tokens[0].Length % 2 == 0 &&
            tokens[0].Length <= 510 && tokens[0].All(Uri.IsHexDigit))
        {
            raw = Convert.FromHexString(tokens[0]);
            return true;
        }
        if (tokens.Length is < 1 or > 255)
            return false;
        var result = new byte[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Length != 2 || !byte.TryParse(tokens[i],
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result[i]))
                return false;
        }
        raw = result;
        return true;
    }
}
