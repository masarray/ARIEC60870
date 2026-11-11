// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using ARIEC60870.Core.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>Immutable, lazy presentation for one decoded physical frame.</summary>
public sealed record ForensicTraceColumns(
    string LinkLayer, string AsduHeader, string InformationObjects,
    string Validation, string ActiveFlags);

/// <summary>
/// Forensic text is a projection of canonical decoder facts, not another
/// receive decoder. Monospace, decimal-first, copied exactly from the viewport
/// or as a linear plain-text report without WPF controls.
/// </summary>
public static class ForensicTraceLayout
{
    public static ForensicTraceColumns Create(EvidenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var frame = row.Source.Frame;
        var asdu = row.Source.AsduDetails;
        var link = new StringBuilder(176);
        var header = new StringBuilder(160);
        var objects = new StringBuilder(256);
        var check = new StringBuilder(96);
        var flags = new List<string>(6);

        if (row.ProtocolMode == "104")
        {
            Add(link, "Format", "IEC-104 APDU");
            Add(link, "APCI", row.ApciFormat);
            if (row.SendSequence != "-") Add(link, "Send N(S)", row.SendSequence);
            if (row.ReceiveSequence != "-") Add(link, "Recv N(R)", row.ReceiveSequence);
            if (row.UFormatName != "-") Add(link, "U function", row.UFormatName);
            Add(check, "Integrity", "APDU (not FT1.2 checksum)");
        }
        else if (frame is null)
        {
            Add(link, "Link", "No verified FT1.2 record");
            Add(check, "Integrity", "Unknown · original raw frame retained");
        }
        else
        {
            var name = frame.Format switch
            {
                Ft12FrameFormat.FixedLength => "Fixed FT1.2",
                Ft12FrameFormat.VariableLength => "Variable FT1.2",
                Ft12FrameFormat.SingleCharacter => "Single-byte reply",
                _ => frame.Format.ToString()
            };
            Add(link, "Frame", $"{name} · {frame.RawBytes.Count} bytes");
            if (frame.LinkAddress.HasValue)
                Add(link, "Link address", frame.LinkAddress.Value.ToString(CultureInfo.InvariantCulture));
            if (frame.Control.HasValue)
            {
                Add(link, "Control", $"{frame.Control.Value} (hex {frame.Control.Value:X2})");
                if (frame.LinkControl is { } ctl && frame.IsChecksumValid && frame.IsLengthValid)
                {
                    if (ctl.Prm)
                    {
                        Child(link, "PRM", "1 · Master / primary");
                        Child(link, "FCB", Flag(ctl.Fcb));
                        Child(link, "FCV", Flag(ctl.Fcv));
                    }
                    else
                    {
                        Child(link, "PRM", "0 · RTU / secondary");
                        Child(link, "ACD", $"{Flag(ctl.Acd)} · {(ctl.Acd == true ? "Class 1 pending" : "No Class 1 indicated")}");
                        Child(link, "DFC", $"{Flag(ctl.Dfc)} · {(ctl.Dfc == true ? "Flow restricted" : "Ready")}");
                        if (ctl.Dfc == true) flags.Add("DFC=1");
                        // On a link-only empty response, ACD still demands a
                        // Class 1 drain. Keep it visible when Class 2 is empty.
                        if (ctl.Acd == true && asdu is null)
                            flags.Add("ACD=1 · Class 1 pending");
                    }
                    Child(link, "Function", $"{ctl.FunctionCode} · {ctl.FunctionName}", last: true);
                }
                else
                    Child(link, "Fields", "Unverified; check frame integrity", last: true);
            }
            if (frame.Format == Ft12FrameFormat.SingleCharacter)
                Add(check, "Response", $"{frame.SingleCharacterName} (one byte)");
            else
            {
                var length = frame.DeclaredLength.HasValue
                    ? $" ({frame.DeclaredLength.Value} bytes)" : string.Empty;
                Add(check, "Length", (frame.IsLengthValid ? "PASS" : "FAIL") + length);
                var received = frame.Checksum.HasValue
                    ? frame.Checksum.Value.ToString(CultureInfo.InvariantCulture) : "Unknown";
                var expected = frame.CalculatedChecksum.HasValue
                    ? frame.CalculatedChecksum.Value.ToString(CultureInfo.InvariantCulture) : "Unknown";
                Add(check, "Checksum", frame.IsChecksumValid
                    ? $"PASS · {received}"
                    : $"FAIL · RX {received}, calc {expected}");
                if (!frame.IsLengthValid || !frame.IsChecksumValid) flags.Add("FRAME INVALID");
                if (frame.RawBytes.Count > 0 && frame.RawBytes[^1] != 0x16)
                    Add(check, "End byte", $"{frame.RawBytes[^1]} · expected 22");
            }
            if (frame.Issues.Count > 0) Add(check, "Issues", string.Join("; ", frame.Issues));
        }

        if (IsTrustedAsdu(row) && asdu is not null)
        {
            var typeCode = asdu.TypeName.Split(' ', 2)[0];
            Add(header, "Type", $"{asdu.TypeId} · {typeCode}");
            Add(header, "Meaning", ShortTypeMeaning(asdu.TypeId));
            // Time belongs to the physical information element, not to the
            // host timestamp. For one object put it immediately after TI:
            // engineers must not dig through QDS to find their SOE time.
            if (asdu.Objects.Count == 1 && asdu.Objects[0].Cp56TimeDetails is not null)
                AppendCp56(header, flags, asdu.Objects[0]);
            else if (asdu.Objects.Count == 1 &&
                     !string.IsNullOrWhiteSpace(asdu.Objects[0].TimestampText))
                Add(header, "Signal time", asdu.Objects[0].TimestampText + " · partial CP24");
            else if (asdu.Objects.Count > 1 &&
                     asdu.Objects.Any(o => o.Cp56TimeDetails is not null ||
                                           !string.IsNullOrWhiteSpace(o.TimestampText)))
                Add(header, "Signal time", "Per object (right column)");
            else if (asdu.TypeId is >= 1 and <= 16 or >= 30 and <= 37)
                Add(header, "Signal time", $"Not encoded in TI {asdu.TypeId}");
            Add(header, "VSQ", $"{asdu.ObjectCount} object(s), SQ={(asdu.IsSequence ? 1 : 0)}");
            Add(header, "Cause", $"{asdu.CauseOfTransmission} · {HumanCause(asdu)}");
            Child(header, "NEG", asdu.IsNegativeConfirm ? "1 · Negative" : "0 · Normal");
            Child(header, "TEST", asdu.IsTest ? "1 · Test data" : "0 · Normal");
            Child(header, "Origin", asdu.CotWidth == 2
                ? asdu.OriginatorAddress.ToString(CultureInfo.InvariantCulture) : "N/A", last: true);
            Add(header, asdu.IsOpaqueVendorSpecific ? "Raw ASDU CA (vendor)" : "CA",
                asdu.IsOpaqueVendorSpecific
                    ? Iec10xVendorAsduPresentation.RawAddress(asdu)
                    : asdu.CommonAddress.ToString(CultureInfo.InvariantCulture));
            if (asdu.IsNegativeConfirm) flags.Add("NEG=1");
            if (asdu.IsTest) flags.Add("TEST=1");
            if (asdu.CauseOfTransmission is >= 44 and <= 47)
                flags.Add($"COT={asdu.CauseOfTransmission} · protocol error");
            if (asdu.IsOpaqueVendorSpecific)
                Add(objects, "Vendor payload", "Opaque · not decoded as process IOA");
            else
            {
                if (asdu.Objects.Count != asdu.ObjectCount)
                    Add(check, "Objects", $"{asdu.Objects.Count}/{asdu.ObjectCount} decoded");
                if (asdu.Objects.Count == 0) Add(objects, "Objects", "None decoded");
            }
            foreach (var obj in asdu.Objects)
                AppendObject(objects, flags, obj, asdu);
        }
        else if (row.ProtocolMode == "103" &&
                 frame is { IsChecksumValid: true, IsLengthValid: true, Asdu: not null })
        {
            Add(header, "IEC-103 type", frame.Asdu.TypeName);
            Add(header, "CA", frame.Asdu.CommonAddress.ToString(CultureInfo.InvariantCulture));
            Add(objects, "FUN", row.Fun);
            Add(objects, "INF", row.Inf);
            Add(objects, "Value", row.SemanticState);
        }
        else
        {
            Add(header, "ASDU", asdu is null ? "N/A · link-only / undecoded" : "Unverified; see raw bytes");
            if (asdu is not null) Add(check, "ASDU", "Not trusted · no projected values");
        }
        return new ForensicTraceColumns(link.ToString().TrimEnd(),
            header.ToString().TrimEnd(), objects.ToString().TrimEnd(),
            check.ToString().TrimEnd(), string.Join("  ·  ", flags));
    }

    private static void AppendObject(StringBuilder text, List<string> flags,
        Iec10xInformationObject obj, Iec10xAsduDecode asdu)
    {
        if (text.Length > 0) text.AppendLine().AppendLine();
        text.Append("OBJECT #").Append(obj.Index).AppendLine();
        Add(text, "IOA", obj.InformationObjectAddress.ToString(CultureInfo.InvariantCulture) +
            (asdu.IsSequence && obj.Index > 1 ? " · sequential" : ""));
        if (asdu.IsSequence && obj.Index > 1)
            Add(text, "IOA bytes", "N/A · SQ=1 implicit");
        else if (obj.InformationObjectOffset >= 0 &&
                 obj.InformationElementOffset > obj.InformationObjectOffset &&
                 obj.InformationElementOffset <= asdu.RawBytes.Count)
            Add(text, "IOA bytes", Hex(asdu.RawBytes.Skip(obj.InformationObjectOffset)
                .Take(obj.InformationElementOffset - obj.InformationObjectOffset).ToArray()));

        var quality = obj.QualityDetails;
        var humanValue = HumanValue(obj, asdu.TypeId);
        var digital = quality?.Kind is Iec10xQualityKind.Siq or Iec10xQualityKind.Diq;
        var step = obj.StepDetails is not null;
        if (!digital && !step)
            Add(text, "Value", humanValue);
        if (step)
        {
            Add(text, "Step (VTI)", humanValue);
            Child(text, "T", obj.StepDetails!.Transient ? "1 · Transient" : "0 · Stable", last: true);
        }

        if (quality is { } q)
        {
            Add(text, q.Kind.ToString().ToUpperInvariant(), $"{q.Raw} (hex {q.Raw:X2})");
            var bits = new List<(string Name, string Text)>();
            if (q.Kind == Iec10xQualityKind.Siq)
                bits.Add(("SPI", $"{q.Raw & 1} · {humanValue}"));
            if (q.Kind == Iec10xQualityKind.Diq)
                bits.Add(("DPI", $"{q.Raw & 3} · {humanValue}"));
            if (q.Overflow.HasValue)
                bits.Add(("OV", $"{Flag(q.Overflow)} · {(q.Overflow.Value ? "Overflow" : "No overflow")}"));
            if (q.Blocked.HasValue)
                bits.Add(("BL", $"{Flag(q.Blocked)} · {(q.Blocked.Value ? "Blocked" : "Not blocked")}"));
            if (q.Substituted.HasValue)
                bits.Add(("SB", $"{Flag(q.Substituted)} · {(q.Substituted.Value ? "Substituted" : "Original")}"));
            if (q.NotTopical.HasValue)
                bits.Add(("NT", $"{Flag(q.NotTopical)} · {(q.NotTopical.Value ? "Not current" : "Current")}"));
            if (q.Carry.HasValue)
                bits.Add(("CY", $"{Flag(q.Carry)} · {(q.Carry.Value ? "Carry" : "No carry")}"));
            if (q.Adjusted.HasValue)
                bits.Add(("CA", $"{Flag(q.Adjusted)} · {(q.Adjusted.Value ? "Adjusted" : "Normal")}"));
            if (q.CounterSequence.HasValue)
                bits.Add(("SQ", q.CounterSequence.Value.ToString(CultureInfo.InvariantCulture)));
            bits.Add(("IV", $"{Flag(q.Invalid)} · {(q.Invalid ? "Invalid" : "Valid")}"));
            for (var i = 0; i < bits.Count; i++)
                Child(text, bits[i].Name, bits[i].Text, last: i == bits.Count - 1);
            if (q.Invalid) flags.Add($"#{obj.Index} IV=1");
            if (q.Overflow == true) flags.Add($"#{obj.Index} OV=1");
            if (q.Blocked == true) flags.Add($"#{obj.Index} BL=1");
            if (q.Substituted == true) flags.Add($"#{obj.Index} SB=1");
            if (q.NotTopical == true) flags.Add($"#{obj.Index} NT=1");
            if (q.Carry == true) flags.Add($"#{obj.Index} CY=1");
            if (q.Adjusted == true) flags.Add($"#{obj.Index} BCR CA=1");
        }
        else
        {
            var state = ProtocolFlagPresentation.QualityState(null, null, true, asdu.TypeId);
            Add(text, "Quality", state.Chips.Count > 0 && state.Chips[0].Text == "—"
                ? "N/A · not encoded" : "Unknown · not decoded");
        }

        // A single object's readable timestamp and bits are shown directly
        // below its TI in the ASDU header; never repeat the whole tree here.
        // With SQ=1/VSQ>1, each object retains its independent timestamp.
        if (obj.Cp56TimeDetails is not null && asdu.Objects.Count > 1)
            AppendCp56(text, flags, obj);
        else if (obj.Cp56TimeDetails is null && asdu.Objects.Count > 1 &&
                 !string.IsNullOrWhiteSpace(obj.TimestampText))
            Add(text, "Signal time", obj.TimestampText + " · partial CP24");

        if (obj.Cp56TimeDetails is not null && obj.RawBytes.Count >= 7)
            Add(text, "Time bytes", Hex(obj.RawBytes.Skip(obj.RawBytes.Count - 7).ToArray()));

        if (obj.Issues.Count > 0) Add(text, "Decode issue", string.Join("; ", obj.Issues));
    }

    private static void AppendCp56(StringBuilder text, List<string> flags,
        Iec10xInformationObject obj)
    {
        var time = obj.Cp56TimeDetails!;
        Add(text, "Signal time", time.TimestampText);
        Child(text, "IV", time.InvalidFlag ? "1 · Invalid" :
            time.CalendarInRange ? "0 · Valid" : "0 · Bad time range");
        Child(text, "RES1", time.SubstitutedFlag ? "1 · Substituted" : "0 · Genuine");
        Child(text, "SU", time.SummerTimeFlag ? "1 · Summer" : "0 · Standard");
        Child(text, "DOW", $"{time.DayOfWeek} · {time.DayOfWeekText}", last: true);
        if (time.DayOfWeekMatchesCalendar == false)
        {
            Add(text, "Time warning", "Weekday/date mismatch");
            flags.Add($"#{obj.Index} TIME DOW mismatch");
        }
        if (time.UnexpectedReservedBits)
        {
            Add(text, "Reserved bits",
                $"RES2={time.Reserved2} RES3={time.Reserved3} RES4={time.Reserved4}");
            flags.Add($"#{obj.Index} TIME reserved bits");
        }
        if (!time.IsValid) flags.Add($"#{obj.Index} TIME IV/range");
        if (time.SubstitutedFlag) flags.Add($"#{obj.Index} RES1=1");
    }

    private static string ShortTypeMeaning(byte type) => type switch
    {
        1 or 30 => "Single-point" + (type == 30 ? " + time" : ""),
        3 or 31 => "Double-point" + (type == 31 ? " + time" : ""),
        5 or 32 => "Step position" + (type == 32 ? " + time" : ""),
        9 or 34 => "Normalized" + (type == 34 ? " + time" : ""),
        11 or 35 => "Scaled" + (type == 35 ? " + time" : ""),
        13 or 36 => "Float" + (type == 36 ? " + time" : ""),
        15 or 37 => "Counter" + (type == 37 ? " + time" : ""),
        100 => "Station GI request",
        101 => "Counter GI request",
        _ => Iec10xTypeIdPresentation.Name(type)
    };

    // UI only: remove the canonical decoder's already-known value prefix.
    // This does not parse raw bytes or duplicate electrical state decoding.
    private static string HumanValue(Iec10xInformationObject obj, byte type)
    {
        var original = obj.ShortValue;
        if (string.IsNullOrWhiteSpace(original)) return "Not decoded";
        var prefix = type switch
        {
            1 or 2 or 30 => "SP=",
            3 or 4 or 31 => "DP=",
            5 or 6 or 32 => "ST=",
            9 or 10 or 34 => "Normalized=",
            11 or 12 or 35 => "Scaled=",
            13 or 14 or 36 => "Float=",
            _ => string.Empty
        };
        return prefix.Length > 0 && original.StartsWith(prefix, StringComparison.Ordinal)
            ? original[prefix.Length..] : original;
    }

    private static string HumanCause(Iec10xAsduDecode asdu) => asdu.CauseOfTransmission switch
    {
        3 => "Spontaneous",
        6 => "Activation",
        7 => "Activation confirmation",
        10 => "Activation termination",
        20 => "Station GI",
        >= 21 and <= 36 => $"Group interrogation {asdu.CauseOfTransmission - 20}",
        _ => asdu.CauseName
    };

    private static void Add(StringBuilder sb, string label, string value)
        => sb.Append("  ").Append(label.PadRight(12)).Append(": ").AppendLine(value);

    private static void Child(StringBuilder sb, string label, string value, bool last = false)
        => sb.Append("    ").Append(last ? "└─ " : "├─ ")
            .Append(label.PadRight(8)).Append(": ").AppendLine(value);

    private static string Flag(bool? bit) =>
        bit.HasValue ? (bit.Value ? "1" : "0") : "Unknown";

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
            "TIV" => asdu is not null && asdu.Objects.Any(o =>
                o.Cp56TimeDetails is { } t && t.InvalidFlag == expected),
            "RES1" => asdu is not null && asdu.Objects.Any(o =>
                o.Cp56TimeDetails is { } t && t.SubstitutedFlag == expected),
            "SU" => asdu is not null && asdu.Objects.Any(o =>
                o.Cp56TimeDetails is { } t && t.SummerTimeFlag == expected),
            "BL" => BitFromObjects(q => q.Blocked),
            "SB" => BitFromObjects(q => q.Substituted),
            "NT" => BitFromObjects(q => q.NotTopical),
            "CY" or "CARRY" => BitFromObjects(q => q.Carry),
            // CA without a prefix is commonly understood as Common Address.
            // Use ADJ to avoid treating 'CA=1' as a BCR flag search.
            "ADJ" or "BCRCA" => BitFromObjects(q => q.Adjusted),
            "TIMEBAD" => asdu is not null && asdu.Objects.Any(o =>
                o.Cp56TimeDetails is { } t && !t.IsValid == expected),
            "TIMEWARN" => asdu is not null && asdu.Objects.Any(o =>
                o.Cp56TimeDetails is { } t &&
                (t.UnexpectedReservedBits || t.DayOfWeekMatchesCalendar == false) == expected),
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
