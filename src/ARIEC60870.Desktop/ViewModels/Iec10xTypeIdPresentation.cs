// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

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
        100 => "General interrogation",
        101 => "Counter interrogation",
        _ => "Unknown or reserved type"
    };

    private static string Timestamp(int? typeId)
        => typeId is >= 30 and <= 37 ? " with timestamp" : "";

    public static string Tooltip(int? typeId)
        => typeId is null ? "Type unavailable" : $"{Label(typeId)} — {Name(typeId)}";

    public static string Compare(int? mapped, int? received)
        => $"Signal List: {Tooltip(mapped)}\nReceived: {Tooltip(received)}";
}
