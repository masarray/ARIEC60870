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
        var parts = width is < 1 or > 3 ? "IOA1:? - IOA2:? - IOA3:? (width unknown)" :
            !ioaInRange ? "IOA out of configured range · inspect original HEX" :
            "IOA1:" + Octet(address, 0) +
              " - IOA2:" + (width >= 2 ? Octet(address, 1) : "N/A") +
              " - IOA3:" + (width >= 3 ? Octet(address, 2) : "N/A");
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
