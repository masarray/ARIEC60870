// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>Presentation only: the decoded original value and raw HEX never change.</summary>
public static class IecOperatorValueFormatter
{
    public static string ForGrid(string? protocol, string? typeId, string? original)
    {
        if (string.IsNullOrWhiteSpace(original)) return original ?? "-";
        if (protocol is not ("101" or "104")) return original;
        if (typeId is "5" or "6" or "32")
        {
            // Strip only a bare, decoded step position. Keep transient and
            // quality suffixes, malformed values and the original raw evidence.
            var stepText = original.Trim();
            if (stepText.StartsWith("ST=", StringComparison.OrdinalIgnoreCase) &&
                sbyte.TryParse(stepText.AsSpan(3), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var step) && step is >= -64 and <= 63)
                return step.ToString(CultureInfo.InvariantCulture);
            return original;
        }
        if (typeId is not ("13" or "14" or "36")) return original;
        var text = original.Trim();
        if (!text.StartsWith("Float=", StringComparison.OrdinalIgnoreCase))
            return original;
        var numeric = text["Float=".Length..].Trim();
        var suffix = numeric.IndexOf(',');
        if (suffix >= 0) numeric = numeric[..suffix].Trim();
        return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
               && double.IsFinite(value) ? numeric : original;
    }
}
