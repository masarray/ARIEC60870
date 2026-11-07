// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Human-readable IEC-101/104 24-bit IOA presentation. The formatted decimal
/// IOA is never used as a decoder or as a point matching key.
/// </summary>
public static class Iec10xIoaPresentation
{
    public static string StructuredTooltip(int? ioa)
    {
        if (ioa is not int address || address is < 0 or > 0xFFFFFF)
            return "No decoded IOA (application address) available.";

        var low = address & 0xFF;
        var mid = (address >> 8) & 0xFF;
        var high = (address >> 16) & 0xFF;
        return $"IOA {address.ToString(CultureInfo.InvariantCulture)}\n" +
               $"IOA1 (LOW)  = {low}\n" +
               $"IOA2 (MID)  = {mid}\n" +
               $"IOA3 (HIGH) = {high}";
    }
}
