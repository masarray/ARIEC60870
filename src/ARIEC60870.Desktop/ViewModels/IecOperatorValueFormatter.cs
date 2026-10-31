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
        if (protocol is not ("101" or "104") || typeId is not ("13" or "14" or "36"))
            return original;
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
