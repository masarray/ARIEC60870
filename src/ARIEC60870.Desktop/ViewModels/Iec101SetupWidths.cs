// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// IEC-101 active-master FT1.2/ASDU widths belong to the IEC-101 profile,
/// not the fixed IEC-103 interpretation or the IEC-104 APCI workspace.
/// Never invent a known-good field profile from a previous protocol mode.
/// </summary>
public readonly record struct Iec101SetupWidths(int Link, int Cot, int Ca, int Ioa)
{
    public static Iec101SetupWidths GenericDefaults => new(2, 2, 2, 3);

    public bool IsValid => Link is 1 or 2 && Cot is 1 or 2 &&
        Ca is 1 or 2 && Ioa is >= 1 and <= 3;

    public bool IsAllOne => Link == 1 && Cot == 1 && Ca == 1 && Ioa == 1;

    public static Iec101SetupWidths Restore(
        int schema, string? savedMode, Iec101SetupWidths legacy, out bool reviewRequired,
        bool persistedReviewRequired = false)
    {
        // Schema 2 persists operator-review provenance independently.
        // A Save during migration may not certify IEC-101 widths.
        if (schema == 2)
        {
            reviewRequired = persistedReviewRequired || !legacy.IsValid;
            return legacy.IsValid ? legacy : GenericDefaults;
        }

        // Unknown future schema: fail closed until a valid profile is reviewed.
        if (schema > 2)
        {
            reviewRequired = true;
            return GenericDefaults;
        }

        // Old prefs used one set of widths for every protocol. IEC-103
        // forced all four to 1, then overwrote IEC-101 on application exit.
        // An IEC-104 save could similarly replace the preceding IEC-101
        // profile; neither provides valid evidence of the old IEC-101 widths.
        var fromOtherProtocol = schema < 1 &&
            !string.Equals(savedMode, "Iec101", StringComparison.OrdinalIgnoreCase);
        reviewRequired = fromOtherProtocol || !legacy.IsValid;
        return reviewRequired ? GenericDefaults : legacy;
    }
}
