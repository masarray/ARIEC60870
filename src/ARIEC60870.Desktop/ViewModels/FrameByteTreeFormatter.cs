// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using ARIEC60870.Core.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// A selection-only physical byte map for a floating WPF inspector.
/// It does not decode a protocol. Every connector comes from the existing
/// verified IecForensicByteInspector physical offset/count and original bytes.
/// A missing or malformed field is never invented from prose or guessed sizes.
/// </summary>
public static class FrameByteTreeFormatter
{
    private sealed record Annotation(int Offset, int Count, string Label);

    public static string Format(EvidenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!TrySourceBytes(row.RawHex, out var bytes))
            return "Raw frame has no verified byte sequence.\nOriginal: " + row.RawHex;
        var sb = new StringBuilder(bytes.Length * 5 + 512);
        sb.Append("Frame #").Append(row.Sequence).Append(" · ")
            .Append(row.Direction).Append(" · Host ").Append(row.Time)
            .Append(" · ").AppendLine(row.ProtocolName);

        var frame = row.Source.Frame;
        var mismatch = frame is { RawBytes.Count: > 0 } &&
            !frame.RawBytes.SequenceEqual(bytes);
        var fields = mismatch
            ? Array.Empty<Iec101FrameByteInspector.Field>()
            : IecForensicByteInspector.Explain(row.Source).ToArray();

        var annotations = new List<Annotation>();
        var notes = new List<string>();
        foreach (var field in fields)
        {
            if (field.Offset < 0 || field.Count <= 0)
            {
                if (!string.IsNullOrWhiteSpace(field.Meaning))
                    notes.Add(field.Title + ": " + field.Meaning);
                continue;
            }
            if (field.Offset > bytes.Length || field.Count > bytes.Length - field.Offset)
                continue;
            var original = Hex(bytes.AsSpan(field.Offset, field.Count));
            if (!string.Equals(original, field.Hex, StringComparison.OrdinalIgnoreCase))
            {
                notes.Add("Offset " + field.Offset.ToString(CultureInfo.InvariantCulture) +
                    ": byte-map mismatch; interpretation withheld");
                continue;
            }
            if (field.Key == "envelope" &&
                frame?.Format == Ft12FrameFormat.VariableLength && field.Count == 4)
                continue; // Physical header octets are individually mapped below.
            if (field.Key == "check" &&
                frame is { Format: Ft12FrameFormat.VariableLength or Ft12FrameFormat.FixedLength } &&
                field.Count == 2)
                continue; // Checksum and stop marker are separately mapped below.
            annotations.Add(new Annotation(field.Offset, field.Count,
                field.Title + ": " + field.Meaning));
        }

        if (!mismatch && frame is { Format: Ft12FrameFormat.VariableLength, RawBytes.Count: >= 8 })
        {
            Add(0, 1, "Start #1 (0x68)");
            Add(1, 1, "Length #1: " + bytes[1].ToString(CultureInfo.InvariantCulture) + " bytes");
            Add(2, 1, "Length #2: " + bytes[2].ToString(CultureInfo.InvariantCulture) + " bytes");
            Add(3, 1, "Start #2 (0x68)");
        }
        if (!mismatch && frame is { Format: Ft12FrameFormat.FixedLength, RawBytes.Count: >= 4 })
            Add(0, 1, "Start (0x10)");
        if (!mismatch && frame is { Format: Ft12FrameFormat.VariableLength or Ft12FrameFormat.FixedLength, RawBytes.Count: >= 4 })
        {
            Add(bytes.Length - 2, 1,
                "Checksum: 0x" + bytes[^2].ToString("X2", CultureInfo.InvariantCulture) +
                (frame.IsChecksumValid ? " · OK" : " · INVALID"));
            Add(bytes.Length - 1, 1,
                "End character: 0x" + bytes[^1].ToString("X2", CultureInfo.InvariantCulture));
        }

        // CP56Time2a is seven final element octets for the canonical time-tagged
        // IEC-101/104 object types. Anchor the six spans to the physical
        // existing Value field, and use ONLY canonical time properties.
        var asdu = row.Source.AsduDetails;
        if (!mismatch && asdu is { Issues.Count: 0 } &&
            asdu.TypeId is 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37 or
                58 or 59 or 60 or 61 or 62 or 63 or 64 or 103)
        {
            foreach (var obj in asdu.Objects)
            {
                if (obj.Cp56TimeDetails is not { } t) continue;
                var physicalValue = fields.FirstOrDefault(f =>
                    f.Key == "value-" + obj.Index.ToString(CultureInfo.InvariantCulture) &&
                    f.Count >= 7 && f.Offset >= 0 &&
                    f.Offset <= bytes.Length - f.Count);
                if (physicalValue is null) continue;
                var offset = physicalValue.Offset + physicalValue.Count - 7;
                var prefix = "IOA " + obj.InformationObjectAddress.ToString(CultureInfo.InvariantCulture) + " · ";
                Add(offset, 2, prefix + "CP56 milliseconds in minute: " +
                    t.MillisecondsInMinute.ToString(CultureInfo.InvariantCulture));
                Add(offset + 2, 1, prefix + "CP56 minute: " + t.Minute +
                    (t.InvalidFlag ? " · Time IV=1" : " · Time IV=0") +
                    (t.SubstitutedFlag ? " · GEN=1 Substituted" : " · GEN=0 Genuine"));
                Add(offset + 3, 1, prefix + "CP56 hour: " + t.Hour +
                    (t.SummerTimeFlag ? " · Summer time" : " · Standard time"));
                Add(offset + 4, 1, prefix + "CP56 day: " + t.Day);
                Add(offset + 5, 1, prefix + "CP56 month: " + t.Month);
                Add(offset + 6, 1, prefix + "CP56 year: " + t.Year);
                var quality = obj.QualityDetails;
                if (quality is not null && asdu.TypeId is 30 or 31)
                    Add(physicalValue.Offset, 1, prefix +
                        (asdu.TypeId == 30 ? "SIQ: " : "DIQ: ") + quality.BitDetails);
            }
        }

        void Add(int offset, int count, string label)
        {
            if (offset >= 0 && count > 0 &&
                offset <= bytes.Length - count)
                annotations.Add(new Annotation(offset, count, label));
        }

        if (mismatch)
            sb.AppendLine("WARNING: source frame bytes disagree with displayed raw HEX; typed byte ranges withheld.");
        else if (frame is { IsLengthValid: false } or { IsChecksumValid: false })
            sb.AppendLine("WARNING: invalid FT1.2 frame; decoded values must not be treated as verified.");

        const int perLine = 28;
        for (var block = 0; block < bytes.Length; block += perLine)
        {
            var count = Math.Min(perLine, bytes.Length - block);
            sb.AppendLine().Append("Offset ")
                .Append(block.ToString("D3", CultureInfo.InvariantCulture))
                .Append("–").Append((block + count - 1).ToString("D3", CultureInfo.InvariantCulture))
                .AppendLine();
            sb.Append("HEX  ").AppendLine(Hex(bytes.AsSpan(block, count)));
            foreach (var a in annotations.Where(a => a.Offset >= block && a.Offset < block + count)
                         .OrderByDescending(a => a.Offset).ThenBy(a => a.Count))
            {
                sb.Append("     ");
                for (var i = 0; i < count; i++)
                {
                    var at = block + i;
                    sb.Append(at < a.Offset ? "│  " :
                        at == a.Offset ? "└─ " :
                        at < a.Offset + a.Count ? "┴─ " : "   ");
                }
                sb.Append("  ").Append(a.Label).Append(" [")
                    .Append(a.Offset).Append("..").Append(a.Offset + a.Count - 1)
                    .AppendLine("]");
            }
        }

        foreach (var note in notes)
            sb.Append("Note: ").AppendLine(note);
        if (annotations.Count == 0 && notes.Count == 0)
            sb.AppendLine("No verified field offsets available; original bytes only.");
        return sb.ToString().TrimEnd();
    }

    private static string Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(bytes).Chunk(2)
            .Select(chunk => new string(chunk)).Aggregate(new StringBuilder(),
                (b, octet) => b.Length == 0 ? b.Append(octet) : b.Append(' ').Append(octet)).ToString();

    private static bool TrySourceBytes(string? hex, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(hex) || hex.Length > 1100) return false;
        var compact = new string(hex.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length is < 2 or > 510 || compact.Length % 2 != 0 ||
            !compact.All(Uri.IsHexDigit)) return false;
        bytes = Convert.FromHexString(compact);
        return bytes.Length is > 0 and <= 255;
    }
}
