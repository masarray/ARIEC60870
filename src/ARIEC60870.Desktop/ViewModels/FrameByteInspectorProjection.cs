// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using ARIEC60870.Core.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Immutable selection-only evidence for an interactive byte/field inspector.
/// Uses the production physical-offset map; does not recalculate protocol fields
/// or infer their positions from rendered text. Invalid sources keep raw bytes
/// but suppress unverified typed field ranges.
/// </summary>
public sealed class FrameByteInspectorProjection
{
    public sealed record Field(string Key, string Group, string Name, int Offset, int Count,
        string Hex, string Meaning)
    {
        public int EndExclusive => Offset + Count;
        public bool Contains(int offset) => Offset >= 0 && offset >= Offset && offset < EndExclusive;
    }

    public IReadOnlyList<byte> Bytes { get; }
    public IReadOnlyList<Field> Fields { get; }
    public string Warning { get; }
    public string OriginalHex { get; }

    private FrameByteInspectorProjection(byte[] bytes, List<Field> fields, string originalHex, string warning)
    {
        Bytes = bytes;
        Fields = fields;
        OriginalHex = originalHex;
        Warning = warning;
    }

    public static FrameByteInspectorProjection Create(EvidenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!TryBytes(row.RawHex, out var bytes))
            return new(Array.Empty<byte>(), new(), row.RawHex,
                "No verified original byte sequence is available.");
        var frame = row.Source.Frame;
        if (frame is { RawBytes.Count: > 0 } && !frame.RawBytes.SequenceEqual(bytes))
            return new(bytes, new(), row.RawHex,
                "Original HEX differs from the decoded frame source. Field positions withheld.");

        var fields = new List<Field>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string key, string group, string name, int offset, int count, string meaning)
        {
            if (offset < 0 || count <= 0 || offset > bytes.Length - count) return;
            var hex = string.Join(" ", bytes.Skip(offset).Take(count)
                .Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
            if (seen.Add(key + "|" + offset + "|" + count))
                fields.Add(new Field(key, group, name, offset, count, hex, meaning));
        }

        // These framing bytes have verified physical positions by FT1.2
        // format, not by guessing values from a malformed dump.
        if (frame is { Format: Ft12FrameFormat.VariableLength } && bytes.Length >= 8)
        {
            Add("start-1", "Framing", "Start character", 0, 1, "FT1.2 start 68");
            Add("length-1", "Framing", "Length L1", 1, 1, "Declared length (octets)");
            Add("length-2", "Framing", "Length L2", 2, 1, "Repeated declared length");
            Add("start-2", "Framing", "Repeated start", 3, 1, "Repeated start 68");
        }
        else if (frame is { Format: Ft12FrameFormat.FixedLength } && bytes.Length >= 5)
            Add("start", "Framing", "Start character", 0, 1, "FT1.2 fixed start 10");

        foreach (var source in IecForensicByteInspector.Explain(row.Source))
        {
            if (source.Offset < 0 || source.Count <= 0)
            {
                if (!string.IsNullOrWhiteSpace(source.Meaning))
                    warnings.Add(source.Title + ": " + source.Meaning);
                continue;
            }
            if (source.Offset > bytes.Length - source.Count) continue;
            var original = string.Join(" ", bytes.Skip(source.Offset).Take(source.Count)
                .Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
            if (!string.Equals(original, source.Hex, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Byte range mismatch for " + source.Title + "; field withheld");
                continue;
            }
            Add(source.Key, Group(source.Key), source.Title, source.Offset, source.Count, source.Meaning);
        }

        // Reveal CP56Time2a as interactive byte fields, not text-art
        // connectors. These offsets are derived only from the validated
        // production value-element range for this exact information object.
        if (row.Source.AsduDetails is { Issues.Count: 0 } asdu &&
            asdu.TypeId is 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37 or
                58 or 59 or 60 or 61 or 62 or 63 or 64 or 103)
        {
            foreach (var obj in asdu.Objects)
            {
                if (obj.Cp56TimeDetails is not { } t) continue;
                var value = fields.FirstOrDefault(x =>
                    x.Key == "value-" + obj.Index.ToString(CultureInfo.InvariantCulture) &&
                    x.Count >= 7);
                if (value is null) continue;
                var offset = value.EndExclusive - 7;
                var prefix = "IOA " + obj.InformationObjectAddress.ToString(CultureInfo.InvariantCulture);
                Add("cp56-ms-" + obj.Index, "Device time", prefix + " · milliseconds", offset, 2,
                    "CP56 milliseconds in minute: " + t.MillisecondsInMinute);
                Add("cp56-minute-" + obj.Index, "Device time", prefix + " · minute", offset + 2, 1,
                    "Minute " + t.Minute + (t.InvalidFlag ? " · IV=1 Invalid" : " · IV=0") +
                    (t.SubstitutedFlag ? " · GEN=1 Substituted" : " · GEN=0 Genuine"));
                Add("cp56-hour-" + obj.Index, "Device time", prefix + " · hour", offset + 3, 1,
                    "Hour " + t.Hour + (t.SummerTimeFlag ? " · Summer time" : " · Standard time"));
                Add("cp56-day-" + obj.Index, "Device time", prefix + " · day", offset + 4, 1,
                    "Day " + t.Day);
                Add("cp56-month-" + obj.Index, "Device time", prefix + " · month", offset + 5, 1,
                    "Month " + t.Month);
                Add("cp56-year-" + obj.Index, "Device time", prefix + " · year", offset + 6, 1,
                    "Year " + t.Year);
                if (asdu.TypeId is 30 or 31 && obj.QualityDetails is { } quality &&
                    value.Offset >= 0)
                    Add("quality-" + obj.Index, "Information objects",
                        prefix + " · " + (asdu.TypeId == 30 ? "SIQ" : "DIQ"),
                        value.Offset, 1, quality.BitDetails);
            }
        }

        if (frame is { Format: Ft12FrameFormat.FixedLength or Ft12FrameFormat.VariableLength } &&
            bytes.Length >= 5)
        {
            Add("checksum-octet", "Integrity", "Checksum octet", bytes.Length - 2, 1,
                frame.IsChecksumValid ? "Checksum valid" : "Checksum INVALID");
            Add("end-octet", "Integrity", "End octet", bytes.Length - 1, 1,
                "FT1.2 terminator; verify against 16");
        }
        if (frame is { IsLengthValid: false } or { IsChecksumValid: false })
            warnings.Add("FT1.2 validation failed. Do not treat typed process values as verified.");
        if (fields.Count == 0 && warnings.Count == 0)
            warnings.Add("No verified field offsets; original HEX is still available.");

        return new(bytes, fields, row.RawHex, string.Join("  ", warnings));
    }

    private static string Group(string key) => key switch
    {
        "envelope" or "raw" => "Framing",
        "control" or "address" or "apci" => "Link",
        "asdu" or "asdu-type" or "vsq" or "cot" or "ca" => "ASDU",
        "check" => "Integrity",
        _ when key.StartsWith("value-", StringComparison.Ordinal) ||
               key.StartsWith("object-", StringComparison.Ordinal) ||
               key.StartsWith("implicit-ioa-", StringComparison.Ordinal) => "Information objects",
        _ => "Protocol"
    };

    public Field? BestFieldAt(int byteOffset) =>
        Fields.Where(f => f.Contains(byteOffset))
            .OrderBy(f => f.Count).ThenBy(f => f.Offset).FirstOrDefault();

    public string ExportSelectedField(Field? field) =>
        field is null ? OriginalHex :
        $"{field.Name} [{field.Offset}..{field.EndExclusive - 1}]  {field.Hex}{Environment.NewLine}{field.Meaning}";

    public string ExportAllFields()
    {
        var b = new StringBuilder();
        b.Append("HEX  ").AppendLine(OriginalHex);
        foreach (var f in Fields)
            b.Append(f.Group).Append(" | ").Append(f.Name)
                .Append(" [").Append(f.Offset).Append("..").Append(f.EndExclusive - 1)
                .Append("] | ").Append(f.Hex).Append(" | ").AppendLine(f.Meaning);
        if (!string.IsNullOrWhiteSpace(Warning))
            b.Append("NOTE  ").AppendLine(Warning);
        return b.ToString().TrimEnd();
    }

    private static bool TryBytes(string? hex, out byte[] bytes)
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
