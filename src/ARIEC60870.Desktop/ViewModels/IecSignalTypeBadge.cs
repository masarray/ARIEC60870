// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Read-only visual shorthand for a known IEC-101/104 Type ID.
/// A chip describes the ASDU family, NOT value validity, command success,
/// contact position, or the meaning of a vendor-specific unknown payload.
/// </summary>
public sealed record IecSignalTypeBadge(string Label, string Family)
{
    // Immutable singleton chips: the UI never allocates a second dictionary
    // or parses signal names for each frame, point or grid refresh.
    private static readonly IReadOnlyDictionary<int, IecSignalTypeBadge> ByTypeId = Build();

    public static IecSignalTypeBadge? FromDecoded(string? protocol, int? typeId)
    {
        if (protocol is not ("101" or "104") || !typeId.HasValue)
            return null;
        return ByTypeId.TryGetValue(typeId.Value, out var chip) ? chip : null;
    }

    public static IecSignalTypeBadge? FromDecoded(string? protocol, string? typeId)
        => int.TryParse(typeId, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? FromDecoded(protocol, parsed) : null;

    private static IReadOnlyDictionary<int, IecSignalTypeBadge> Build()
    {
        var items = new Dictionary<int, IecSignalTypeBadge>();
        void Map(string label, string family, params int[] types)
        {
            var badge = new IecSignalTypeBadge(label, family);
            foreach (var type in types)
                items.Add(type, badge); // Fail loudly if a type is assigned twice.
        }

        // IEC process information: un-timed, CP24 and CP56 families all share
        // the same type chip, while the exact Type ID/time stays in evidence.
        Map("SPS", "Digital", 1, 2, 30);
        Map("DPS", "Digital", 3, 4, 31);
        Map("STP", "Digital", 5, 6, 32);
        Map("BSI", "Digital", 7, 8, 33);
        Map("NVA", "Measurement", 9, 10, 21, 34);
        Map("INT", "Measurement", 11, 12, 35); // Signed scaled measured value
        Map("FLT", "Measurement", 13, 14, 36); // IEEE-754 short float
        Map("BCR", "Counter", 15, 16, 37);   // Binary counter reading / totals
        Map("PSI", "Digital", 20);            // Packed single-point information

        // Command type indicates wire payload shape, NOT ACTCON/ACTTERM verdict.
        Map("SPC", "Command", 45, 58);
        Map("DPC", "Command", 46, 59);
        Map("RCS", "Command", 47, 60);
        Map("SEN", "Command", 48, 61); // Setpoint normalized
        Map("SEI", "Command", 49, 62); // Setpoint scaled integer
        Map("SEF", "Command", 50, 63); // Setpoint short float
        Map("BSC", "Command", 51, 64); // Bitstring command
        Map("GI",  "Service", 100);
        Map("CIG", "Service", 101);    // Counter interrogation, not a BCR value
        Map("RD",  "Service", 102);
        Map("CSY", "Service", 103);
        Map("TST", "Service", 104, 107);
        Map("RST", "Service", 105);
        return items;
    }
}
