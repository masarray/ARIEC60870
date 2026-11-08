// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>Short IEC-101/104 names for engineers. Raw Type ID remains the wire authority.</summary>
public static class Iec10xTypeIdPresentation
{
    public static string Label(int? typeId) => typeId is null ? "-" : $"TI-{typeId.Value}";

    public static string Name(int? typeId) => typeId switch
    {
        1 or 30 => "Single point" + Timestamp(typeId),
        2 => "Single point with short timestamp",
        3 or 31 => "Double point" + Timestamp(typeId),
        4 => "Double point with short timestamp",
        5 or 32 => "Step position" + Timestamp(typeId),
        6 => "Step position with short timestamp",
        7 or 33 => "32-bit status" + Timestamp(typeId),
        8 => "32-bit status with short timestamp",
        9 or 34 => "Normalized measurement" + Timestamp(typeId),
        21 => "Normalized measurement without quality descriptor",
        10 => "Normalized measurement with short timestamp",
        11 or 35 => "Scaled measurement" + Timestamp(typeId),
        12 => "Scaled measurement with short timestamp",
        13 or 36 => "Floating-point measurement" + Timestamp(typeId),
        14 => "Floating-point measurement with short timestamp",
        15 or 37 => "Counter" + Timestamp(typeId),
        16 => "Counter with short timestamp",
        45 => "Single command",
        46 => "Double command",
        47 => "Step command",
        70 => "End of initialization",
        100 => "General interrogation",
        101 => "Counter interrogation",
        106 => "Delay acquisition",
        _ => CanonicalName(typeId)
    };

    // The decoder's TypeName is the single source for every additional
    // supported Type ID. A presentation-only table must not silently label
    // newly decoded standard elements as unsupported.
    private static string CanonicalName(int? typeId)
    {
        if (typeId is null || typeId.Value is < 0 or > 255)
            return "Unknown or reserved type";
        var canonical = Iec10xAsduDecoder.TypeName((byte)typeId.Value);
        if (canonical.StartsWith("Type ", StringComparison.Ordinal))
            return "Unknown or reserved type";
        var descriptionOffset = canonical.IndexOf(' ');
        var description = descriptionOffset >= 0 ? canonical[(descriptionOffset + 1)..] : canonical;
        // Brief UI labels communicate timestamp presence, while the selected
        // inspector retains exact CP24/CP56 fields and underlying source bytes.
        return description.Replace("with CP56Time2a", "with timestamp", StringComparison.Ordinal)
            .Replace("with CP24Time2a", "with short timestamp", StringComparison.Ordinal);
    }

    private static string Timestamp(int? typeId)
        => typeId is >= 30 and <= 37 ? " with timestamp" : "";

    public static string Tooltip(int? typeId)
        => typeId is null ? "Type unavailable" : $"{Label(typeId)} — {Name(typeId)}";

    // IEC-103 has a different Type ID space; never label its ASDUs using
    // IEC-101/104 process meanings just because the numeric Type ID matches.
    public static string Tooltip(int? typeId, string protocol) =>
        protocol is "101" or "104" ? Tooltip(typeId) :
        typeId is null ? "Type unavailable" : $"{Label(typeId)} — IEC-103 ASDU";

    public static string Compare(int? mapped, int? received)
        => $"Signal List: {Tooltip(mapped)}\nReceived: {Tooltip(received)}";
}
