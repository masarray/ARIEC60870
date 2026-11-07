// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using ARIEC60870.Core.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Lazy, read-only three-column forensic presentation of an existing typed frame.
/// Never parses text/HEX to derive protocol semantics and never runs in RX callbacks.
/// The original byte ranges remain owned by IecForensicByteInspector.
/// </summary>
public sealed record ForensicTraceColumns(
    string LinkLayer, string AsduHeader, string InformationObjects,
    string Validation, string ActiveFlags);

public static class ForensicTraceLayout
{
    public static ForensicTraceColumns Create(EvidenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var frame = row.Source.Frame;
        var asdu = row.Source.AsduDetails;
        var link = new StringBuilder(192);
        var header = new StringBuilder(192);
        var objects = new StringBuilder(256);
        var validation = new StringBuilder(128);
        var flags = new List<string>();

        if (row.ProtocolMode == "104")
        {
            Add(link, "Format", "IEC-104 APDU");
            Add(link, "APCI", row.ApciFormat);
            Add(link, "N(S)", row.SendSequence);
            Add(link, "N(R)", row.ReceiveSequence);
            if (row.UFormatName != "-") Add(link, "U function", row.UFormatName);
            Add(validation, "Integrity", "Consult original APDU and decoder issues");
        }
        else if (frame is null)
        {
            Add(link, "Link", "No decoded FT1.2 metadata");
            Add(validation, "Integrity", "Not verified; original raw evidence retained");
        }
        else
        {
            Add(link, "Format", frame.Format.ToString());
            if (frame.RawBytes.Count > 0)
                Add(link, "Start byte", $"0x{frame.RawBytes[0]:X2}");
            if (frame.Format == Ft12FrameFormat.VariableLength && frame.RawBytes.Count >= 4)
                Add(link, "Length bytes", $"0x{frame.RawBytes[1]:X2} / 0x{frame.RawBytes[2]:X2}");
            Add(link, "Frame bytes", frame.RawBytes.Count.ToString(CultureInfo.InvariantCulture));
            if (frame.DeclaredLength.HasValue)
                Add(link, "Declared L", frame.DeclaredLength.Value.ToString(CultureInfo.InvariantCulture));
            Add(link, "Control", frame.Control.HasValue
                ? $"0x{frame.Control.Value:X2} ({Convert.ToString((int)frame.Control.Value, 2).PadLeft(8, '0')})" : "N/A");
            if (frame.LinkControl is { } control)
            {
                Add(link, "PRM", control.Prm ? "1 (primary)" : "0 (secondary)");
                if (control.Prm)
                {
                    Add(link, "FCB", Bit(control.Fcb));
                    Add(link, "FCV", Bit(control.Fcv));
                }
                else
                {
                    Add(link, "ACD", Bit(control.Acd));
                    Add(link, "DFC", Bit(control.Dfc));
                    if (control.Acd == true) flags.Add("ACD=1");
                    if (control.Dfc == true) flags.Add("DFC=1");
                }
                Add(link, "Function", $"{control.FunctionCode} · {control.FunctionName}");
            }
            Add(link, "Link addr", frame.LinkAddress?.ToString(CultureInfo.InvariantCulture) ?? "N/A");
            Add(link, "Addr width", frame.LinkAddressSize.ToString(CultureInfo.InvariantCulture));

            if (frame.Format == Ft12FrameFormat.SingleCharacter)
            {
                Add(validation, "Single byte", frame.SingleCharacterName);
                Add(validation, "Checksum", "N/A (single-character frame)");
            }
            else
            {
                Add(validation, "Length", frame.IsLengthValid ? "PASS" : "FAIL");
                Add(validation, "Checksum", frame.Checksum.HasValue ? $"0x{frame.Checksum.Value:X2}" : "Unavailable");
                Add(validation, "Calculated", frame.CalculatedChecksum.HasValue
                    ? $"0x{frame.CalculatedChecksum.Value:X2}" : "Unavailable");
                Add(validation, "Checksum test", frame.IsChecksumValid ? "PASS" : "FAIL");
                Add(validation, "End marker", frame.RawBytes.Count > 0
                    ? $"0x{frame.RawBytes[frame.RawBytes.Count - 1]:X2}" : "Unavailable");
                if (!frame.IsChecksumValid || !frame.IsLengthValid) flags.Add("FRAME INVALID");
            }
            if (frame.Issues.Count > 0)
                Add(validation, "Frame issues", string.Join("; ", frame.Issues));
        }

        // Physical IEC-101 fields are trustworthy only when the typed ASDU is
        // identical to the parser's accepted bytes with valid FT1.2 integrity.
        // IEC-104 uses its existing APDU/ASDU typed pipeline, not FT1.2 rules.
        var trustedAsdu = IsTrustedAsdu(row);
        if (trustedAsdu && asdu is not null)
        {
            Add(header, "Type ID", $"{asdu.TypeId} · {asdu.TypeName}");
            Add(header, "VSQ", $"0x{asdu.VariableStructureQualifier:X2}");
            Add(header, "SQ", asdu.IsSequence ? "1" : "0");
            Add(header, "Objects", $"{asdu.Objects.Count} / {asdu.ObjectCount}");
            Add(header, "COT", $"{asdu.CauseOfTransmission} · {asdu.CauseName}");
            if (asdu.CauseDetails is { } cause)
                Add(header, "COT raw", $"0x{cause.Raw:X2}");
            Add(header, "NEG", asdu.IsNegativeConfirm ? "1" : "0");
            Add(header, "TEST", asdu.IsTest ? "1" : "0");
            Add(header, "Origin", asdu.CotWidth == 2
                ? asdu.OriginatorAddress.ToString(CultureInfo.InvariantCulture)
                : "N/A (1-byte COT)");
            Add(header, "CA", asdu.CommonAddress.ToString(CultureInfo.InvariantCulture));
            if (asdu.IsNegativeConfirm) flags.Add("NEG=1");
            if (asdu.IsTest) flags.Add("TEST=1");
            if (asdu.Objects.Count == 0)
                Add(objects, "Objects", "None decoded");
            foreach (var obj in asdu.Objects)
                AppendObject(objects, flags, obj, asdu);
            if (asdu.Issues.Count > 0)
                Add(validation, "ASDU issues", string.Join("; ", asdu.Issues));
        }
        else if (row.ProtocolMode == "103" && frame is { IsChecksumValid: true, IsLengthValid: true, Asdu: not null })
        {
            // IEC-103 has FUN/INF, not IEC-101 IOA. Retain its native identity.
            Add(header, "IEC-103 Type", frame.Asdu.TypeName);
            Add(header, "CA", frame.Asdu.CommonAddress.ToString(CultureInfo.InvariantCulture));
            Add(objects, "FUN", row.Fun);
            Add(objects, "INF", row.Inf);
            Add(objects, "Value", row.SemanticState);
        }
        else
        {
            Add(header, "ASDU", asdu is null ? "Not present / not decoded" : "Unverified; inspect raw bytes");
            Add(objects, "Objects", "Not decoded as trustworthy data");
        }
        return new ForensicTraceColumns(link.ToString().TrimEnd(),
            header.ToString().TrimEnd(), objects.ToString().TrimEnd(),
            validation.ToString().TrimEnd(), string.Join(" · ", flags));
    }

    private static void AppendObject(StringBuilder sb, List<string> flags,
        Iec10xInformationObject obj, Iec10xAsduDecode asdu)
    {
        if (sb.Length > 0) sb.AppendLine().AppendLine();
        sb.Append("OBJECT #").Append(obj.Index).AppendLine();
        Add(sb, "IOA", obj.InformationObjectAddress.ToString(CultureInfo.InvariantCulture) +
            (asdu.IsSequence && obj.Index > 1 ? " (SQ=1 implicit)" : ""));

        // Original IOA octets at decoder-proven byte offsets, never reconstructed
        // from the decimal address. SQ=1 later objects have no physical IOA.
        if (asdu.IsSequence && obj.Index > 1)
            Add(sb, "IOA raw", "N/A (SQ=1 implicit)");
        else if (obj.InformationObjectOffset >= 0 &&
                 obj.InformationElementOffset > obj.InformationObjectOffset &&
                 obj.InformationElementOffset <= asdu.RawBytes.Count)
            Add(sb, "IOA raw", Hex(asdu.RawBytes.Skip(obj.InformationObjectOffset)
                .Take(obj.InformationElementOffset - obj.InformationObjectOffset).ToArray()));
        Add(sb, "Value", string.IsNullOrWhiteSpace(obj.ShortValue) ? "Not decoded" : obj.ShortValue);
        if (obj.RawBytes.Count > 0)
            Add(sb, "Raw element", Hex(obj.RawBytes));
        if (obj.QualityDetails is { } q)
        {
            Add(sb, q.Kind.ToString().ToUpperInvariant(), $"0x{q.Raw:X2} ({Convert.ToString(q.Raw, 2).PadLeft(8, '0')})");
            AddFlag(sb, flags, obj.Index, "OV", q.Overflow);
            AddFlag(sb, flags, obj.Index, "BL", q.Blocked);
            AddFlag(sb, flags, obj.Index, "SB", q.Substituted);
            AddFlag(sb, flags, obj.Index, "NT", q.NotTopical);
            AddFlag(sb, flags, obj.Index, "IV", q.Invalid);
            if (q.Carry.HasValue) AddFlag(sb, flags, obj.Index, "CY", q.Carry);
            if (q.Adjusted.HasValue) AddFlag(sb, flags, obj.Index, "CA", q.Adjusted);
            if (q.CounterSequence.HasValue)
                Add(sb, "Counter SQ", q.CounterSequence.Value.ToString(CultureInfo.InvariantCulture));
            Add(sb, "Quality", q.DisplayText);
        }
        else
        {
            var state = ProtocolFlagPresentation.QualityState(null, null, true, asdu.TypeId);
            Add(sb, "Quality", state.Chips.Count > 0 && state.Chips[0].Text == "—"
                ? "N/A (not encoded for this type)" : "Unknown (not decoded)");
        }
        if (!string.IsNullOrWhiteSpace(obj.TimestampText))
        {
            Add(sb, "Device time", obj.TimestampText);
            if (obj.RawBytes.Count >= 7)
                Add(sb, "Time raw", Hex(obj.RawBytes.Skip(obj.RawBytes.Count - 7).ToArray()));
        }
        if (obj.Issues.Count > 0)
            Add(sb, "Decode issues", string.Join("; ", obj.Issues));
    }

    private static void AddFlag(StringBuilder sb, List<string> flags, int objectNumber,
        string name, bool? value)
    {
        if (!value.HasValue) return; // N/A is NOT 0.
        Add(sb, name, value.Value ? "1" : "0");
        if (value.Value) flags.Add($"#{objectNumber} {name}=1");
    }

    private static void Add(StringBuilder sb, string key, string value)
        => sb.Append(key.PadRight(13)).Append(": ").AppendLine(value);

    private static string Bit(bool? value) =>
        value.HasValue ? (value.Value ? "1" : "0") : "Unknown";

    private static string Hex(IReadOnlyList<byte> bytes)
        => string.Join(" ", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));


    private static bool IsTrustedAsdu(EvidenceRow row)
    {
        var asdu = row.Source.AsduDetails;
        if (asdu is null || asdu.Issues.Count > 0) return false;
        if (row.ProtocolMode == "104") return true; // accepted canonical APDU path
        var frame = row.Source.Frame;
        return row.ProtocolMode == "101" &&
               frame is { IsChecksumValid: true, IsLengthValid: true, Format: Ft12FrameFormat.VariableLength } &&
               frame.AsduBytes.SequenceEqual(asdu.RawBytes);
    }

    /// <summary>
    /// Exact bit predicates over ALL typed objects. No English-text/HEX search
    /// for flags, no second protocol decoder and no first-object-only shortcut.
    /// Null means the query is not a recognized structured bit predicate.
    /// </summary>
    public static bool? MatchFlag(EvidenceRow row, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var parts = query.Trim().ToUpperInvariant().Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || (parts[1] != "0" && parts[1] != "1"))
            return null;
        bool expected = parts[1] == "1";
        var asdu = IsTrustedAsdu(row) ? row.Source.AsduDetails : null;
        var frame = row.Source.Frame;
        var link = frame is { IsChecksumValid: true, IsLengthValid: true }
            ? frame.LinkControl : null;
        bool? BitFromObjects(Func<Iec10xQualityDetails, bool?> projection)
        {
            if (asdu is null) return false;
            return asdu.Objects.Any(obj =>
                obj.QualityDetails is { } q && projection(q) == expected);
        }
        return parts[0] switch
        {
            "OV" => BitFromObjects(q => q.Overflow),
            "IV" => BitFromObjects(q => q.Invalid),
            "BL" => BitFromObjects(q => q.Blocked),
            "SB" => BitFromObjects(q => q.Substituted),
            "NT" => BitFromObjects(q => q.NotTopical),
            "NEG" or "NEGATIVE" => asdu is not null &&
                asdu.IsNegativeConfirm == expected,
            "TEST" => asdu is not null && asdu.IsTest == expected,
            "ACD" => link?.Acd == expected,
            "DFC" => link?.Dfc == expected,
            "FCB" => link?.Fcb == expected,
            "FCV" => link?.Fcv == expected,
            _ => null
        };
    }
}
