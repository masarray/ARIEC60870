// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Compact typed process-object label for the virtualized IEC-101/104 trace.
/// The IOA octets are low-to-high little endian address components, not three
/// unrelated addresses. SQ=1 subsequent addresses are derived, not transmitted.
/// No decoder, signal-state inference or quality validation occurs here.
/// </summary>
public static class Iec10xSignalTraceFormatter
{
    public static string Format(Iec10xAsduDecode asdu, Iec10xInformationObject obj,
        string? authoritativeName = null)
    {
        ArgumentNullException.ThrowIfNull(asdu);
        ArgumentNullException.ThrowIfNull(obj);
        var address = obj.InformationObjectAddress;
        var width = asdu.InformationObjectAddressWidth;
        var ioaInRange = width is >= 1 and <= 3 && address >= 0 &&
            address <= (1 << (8 * width)) - 1;
        // Display the decimal/unstructured IOA together with its structured
        // high-to-low components. On the wire IOA1 (least significant) comes
        // FIRST. In the usual human numeric representation IOA1 is at RIGHT.
        var parts = width is < 1 or > 3 ? "IOA width unknown" :
            !ioaInRange ? "IOA out of configured range · inspect original HEX" :
            StructuredIoa(address, width);
        var implied = asdu.IsSequence && obj.Index > 1
            ? " · SQ=1 implied (no IOA octets repeated)" : "";
        if (ioaInRange && asdu.RawBytes.Count > 0 && width is >= 1 and <= 3 &&
            obj.InformationObjectOffset >= 0 &&
            obj.InformationObjectOffset + width <= asdu.RawBytes.Count &&
            !(asdu.IsSequence && obj.Index > 1))
        {
            var fromWire = 0;
            for (var i = 0; i < width; i++)
                fromWire |= asdu.RawBytes[obj.InformationObjectOffset + i] << (8 * i);
            if (fromWire != address)
                parts = "IOA byte mismatch · inspect original HEX";
        }

        var name = !string.IsNullOrWhiteSpace(authoritativeName)
            ? authoritativeName.Trim() + " · " : "";
        var signalKind = Kind(asdu.TypeId);
        // Unknown decoder output cannot be silently displayed as a real state.
        var value = !ioaInRange || obj.Issues.Count > 0 ? "Decode warning" :
            string.IsNullOrWhiteSpace(obj.ShortValue) ? "Not decoded" :
            TrimKnownTypedPrefix(obj.ShortValue, signalKind);

        var flags = QualityFlags(obj.QualityDetails);
        var label = name + "IOA " + address.ToString(CultureInfo.InvariantCulture) +
            " (" + parts + ")" + implied +
            "  Value: " + value + " (" + signalKind + ")";
        return flags.Length == 0 ? label : label + " · " + flags;
    }

    // A single typed scan field set backs both legacy scans and Frame Trace.
    // This is presentation only: Core remains the source of raw/decoded truth.
    public static string ScanValue(Iec10xAsduDecode asdu, Iec10xInformationObject obj,
        string? authoritativeName = null)
    {
        var f = BuildScanFields(asdu, obj, authoritativeName);
        return f.Identity + " · Value: " + f.Value + " (" + f.Kind + ")" +
            " · TI-" + asdu.TypeId.ToString(CultureInfo.InvariantCulture) + f.Suffix;
    }

    /// <summary>Each IOA's own timestamp and quality stay adjacent in SQ0/SQ1 previews.</summary>
    public static string TraceObject(Iec10xAsduDecode asdu, Iec10xInformationObject obj,
        string? authoritativeName = null)
    {
        var f = BuildScanFields(asdu, obj, authoritativeName, traceDisplay: true);
        var ti = "TI-" + asdu.TypeId.ToString(CultureInfo.InvariantCulture);
        var cp56 = obj.Cp56TimeDetails;
        var stamp = cp56 is not null
            ? " · " + cp56.Day.ToString("00", CultureInfo.InvariantCulture) + "-" +
              cp56.Month.ToString("00", CultureInfo.InvariantCulture) + "-" +
              cp56.Year.ToString("0000", CultureInfo.InvariantCulture) + " " +
              cp56.Hour.ToString("00", CultureInfo.InvariantCulture) + ":" +
              cp56.Minute.ToString("00", CultureInfo.InvariantCulture) + ":" +
              (cp56.MillisecondsInMinute / 1000).ToString("00", CultureInfo.InvariantCulture) + "." +
              (cp56.MillisecondsInMinute % 1000).ToString("000", CultureInfo.InvariantCulture)
            : !string.IsNullOrWhiteSpace(obj.TimestampText)
                ? " · RTU partial time: " + obj.TimestampText : "";
        var quality = obj.Issues.Count > 0 ? "Decode warning" :
            obj.QualityDetails?.DisplayText ?? "Not provided";
        // A missing optional device time is a state, not a property label.
        // Never call CP24/partial text valid without canonical CP56 proof.
        var timeStatus = cp56 is not null
            ? (cp56.IsValid ? "Timestamp Valid" : "Timestamp Invalid") +
              (cp56.SubstitutedFlag ? " · Substituted (GEN=1)" : " · Genuine (GEN=0)") +
              (cp56.SummerTimeFlag ? " · DST ON" : " · DST OFF") +
              (cp56.UnexpectedReservedBits ? " · Reserved bits SET" : "") +
              (cp56.DayOfWeekMatchesCalendar == false ? " · Day mismatch" : "") +
              (cp56.DayOfWeek > 0 ? " · Day: " + cp56.DayOfWeekText : "")
            : string.IsNullOrWhiteSpace(obj.TimestampText)
                ? "No Timestamp" : "Timestamp Partial (CP56 validity / GEN / DST unknown)";
        return f.Identity + " · " + ti + " · Value: " + f.Value + stamp + f.Suffix +
            Environment.NewLine + "  " + ti + " " + f.Kind + " Signal · Quality: " +
            quality + " · " + timeStatus;
    }

    private readonly record struct ScanFields(string Identity, string Value, string Kind, string Suffix);

    private static ScanFields BuildScanFields(Iec10xAsduDecode asdu, Iec10xInformationObject obj,
        string? authoritativeName, bool traceDisplay = false)
    {
        ArgumentNullException.ThrowIfNull(asdu);
        ArgumentNullException.ThrowIfNull(obj);
        var address = obj.InformationObjectAddress;
        var width = asdu.InformationObjectAddressWidth;
        var validAddress = width is >= 1 and <= 3 && address >= 0 &&
            address <= (1 << (8 * width)) - 1;
        var mismatch = false;
        if (validAddress && asdu.RawBytes.Count > 0 &&
            !(asdu.IsSequence && obj.Index > 1) &&
            obj.InformationObjectOffset >= 0 &&
            obj.InformationObjectOffset + width <= asdu.RawBytes.Count)
        {
            var wire = 0;
            for (var i = 0; i < width; i++)
                wire |= asdu.RawBytes[obj.InformationObjectOffset + i] << (8 * i);
            mismatch = wire != address;
        }
        var kind = Kind(asdu.TypeId);
        var value = !validAddress || mismatch || obj.Issues.Count > 0
            ? "Decode warning" : string.IsNullOrWhiteSpace(obj.ShortValue)
                ? "Not decoded" : TrimKnownTypedPrefix(obj.ShortValue, kind);
        // DPI bits are typed decoder evidence (DIQ), not parsed from strings.
        // 00/11 describe an indeterminate double-point STATE, not quality IV.
        // Preserve original Core decoder text for all non-Trace consumers.
        if (traceDisplay && validAddress && !mismatch && obj.Issues.Count == 0 &&
            asdu.TypeId is 3 or 4 or 31 &&
            obj.QualityDetails is { Kind: Iec10xQualityKind.Diq } diq)
            value = (diq.Raw & 0x03) switch
            {
                0 => "Invalid [00]",
                1 => "OFF [01]",
                2 => "ON [10]",
                _ => "Invalid [11]"
            };
        var name = string.IsNullOrWhiteSpace(authoritativeName)
            ? string.Empty : authoritativeName.Trim() + " · ";
        var byteStructure = validAddress && !mismatch
            ? traceDisplay
                ? " [ " + StructuredIoa(address, width).Replace(" - ", " · ", StringComparison.Ordinal) + " ]"
                : " (" + StructuredIoa(address, width) + ")"
            : "";
        return new ScanFields(name + "IOA " + address.ToString(CultureInfo.InvariantCulture) +
            byteStructure, value, kind,
            (asdu.IsSequence && obj.Index > 1 ? " · SQ=1 implied IOA" : "") +
            (mismatch ? " · IOA bytes mismatch" : ""));
    }

    // CP24/other timestamp does not establish CP56 GEN, DST or time validity.
    public static string ScanDeviceTime(Iec10xInformationObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj.Cp56TimeDetails is not { } time)
            return string.IsNullOrWhiteSpace(obj.TimestampText) ? string.Empty :
                "RTU " + obj.TimestampText + " · CP56 flags: N/A";

        // The canonical typed CP56 record contains the remote clock even when
        // a compact decoder summary is absent. Never backfill from host time.
        var clock = string.IsNullOrWhiteSpace(obj.TimestampText)
            ? time.TimestampText : obj.TimestampText;
        var result = new List<string>(10)
        {
            "RTU " + clock,
            "Valid: " + Bool(time.IsValid),
            "Genuine: " + Bool(!time.SubstitutedFlag),
            "Summer Time: " + Bool(time.SummerTimeFlag)
        };
        if (time.InvalidFlag) result.Add("Time invalid (IV): TRUE");
        if (!time.CalendarInRange) result.Add("Calendar valid: FALSE");
        if (time.DayOfWeek > 0) result.Add("Day: " + time.DayOfWeekText);
        if (time.DayOfWeekMatchesCalendar == false) result.Add("Day mismatch: TRUE");
        if (time.UnexpectedReservedBits) result.Add("Reserved bits: SET");
        return string.Join(" · ", result);
    }

    // Only applicable flags that are actually set. Missing flag ≠ False.
    public static string ScanActiveQuality(Iec10xQualityDetails? quality)
    {
        if (quality is null) return string.Empty;
        var result = new List<string>(7);
        if (quality.Overflow == true) result.Add("Overflow (OV): TRUE");
        if (quality.Invalid) result.Add("Invalid (IV): TRUE");
        if (quality.Blocked == true) result.Add("Blocked (BL): TRUE");
        if (quality.NotTopical == true) result.Add("Not topical (NT): TRUE");
        if (quality.Substituted == true) result.Add("Substituted (SB): TRUE");
        if (quality.Carry == true) result.Add("Carry (CY): TRUE");
        if (quality.Adjusted == true) result.Add("Adjusted (CA): TRUE");
        return string.Join(" · ", result);
    }

    private static string Bool(bool value) => value ? "TRUE" : "FALSE";

    public static string Kind(byte type) => type switch
    {
        1 or 2 or 30 => "Single Point",
        3 or 4 or 31 => "Double Point",
        13 or 14 or 36 => "Float",
        9 or 10 or 21 or 34 => "Normalized",
        11 or 12 or 35 => "Scaled",
        5 or 6 or 32 => "Step Position",
        7 or 8 or 33 => "Bitstring",
        15 or 16 or 37 => "Counter",
        45 or 46 or 47 or 58 or 59 or 60 => "Command",
        48 or 49 or 50 or 61 or 62 or 63 => "Setpoint Command",
        70 => "Initialization",
        100 => "GI Command",
        106 => "Delay Acquisition",
        _ => "Information Object"
    };

    /// <summary>
    /// High-to-low STRUCTURED notation of the numeric (unstructured) IOA.
    /// IOA1 is least-significant, so it is printed last. FT1.2/ASDU wire
    /// serialization is the reverse order: IOA1, then IOA2, then IOA3.
    /// For SQ=1 subsequent objects this is a logical address decomposition,
    /// never a claim that its IOA octets were transmitted.
    /// </summary>
    private static string StructuredIoa(int address, int width) => width switch
    {
        1 => "IOA1:" + Octet(address, 0),
        2 => "IOA2:" + Octet(address, 1) + " - IOA1:" + Octet(address, 0),
        3 => "IOA3:" + Octet(address, 2) + " - IOA2:" + Octet(address, 1) +
             " - IOA1:" + Octet(address, 0),
        _ => "IOA width unknown"
    };

    private static string Octet(int address, int shift) =>
        ((address >> (8 * shift)) & 0xFF).ToString(CultureInfo.InvariantCulture);

    private static string TrimKnownTypedPrefix(string value, string kind)
    {
        var separator = value.IndexOf('=');
        if (separator < 1) return value;
        var prefix = value[..separator].Trim();
        if ((kind == "Single Point" && prefix.Equals("SP", StringComparison.OrdinalIgnoreCase)) ||
            (kind == "Double Point" && prefix.Equals("DP", StringComparison.OrdinalIgnoreCase)) ||
            (kind == "Float" && (prefix.Equals("Float", StringComparison.OrdinalIgnoreCase) ||
                                  prefix.Equals("FLT", StringComparison.OrdinalIgnoreCase))))
            return value[(separator + 1)..].Trim();
        return value;
    }

    private static string QualityFlags(Iec10xQualityDetails? q)
    {
        if (q is null) return string.Empty;
        var flags = new List<string>(7);
        if (q.Overflow == true) flags.Add("OV=1 Overflow");
        if (q.Invalid) flags.Add("IV=1 Invalid");
        if (q.Blocked == true) flags.Add("BL=1 Blocked");
        if (q.NotTopical == true) flags.Add("NT=1 Not topical");
        if (q.Substituted == true) flags.Add("SB=1 Substituted");
        if (q.Carry == true) flags.Add("CY=1 Carry");
        if (q.Adjusted == true) flags.Add("CA=1 Adjusted");
        return string.Join(" · ", flags);
    }
}
