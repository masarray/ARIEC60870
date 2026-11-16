// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Core.Mapping;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Rebuilds display names on a Signal List switch, outside the RX hot path.
/// Source/CA conflicts are inferred from real observed Value rows, never
/// seeded placeholders; an alias cannot borrow another station's name.
/// </summary>
public static class Iec10xValueNameReconciler
{
    public static void Apply(IReadOnlyCollection<ValueRow> rows, Iec10xPointDisplayIndex names)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(names);
        var firstCaBySource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var conflictingSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (row.Protocol is not ("101" or "104") ||
                string.IsNullOrWhiteSpace(row.EvidenceReference) ||
                !int.TryParse(row.CommonAddress, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ca))
                continue;
            var source = Source(row);
            if (!firstCaBySource.TryAdd(source, ca) && firstCaBySource[source] != ca)
                conflictingSources.Add(source);
        }

        foreach (var row in rows)
        {
            if (row.Protocol is not ("101" or "104") ||
                !int.TryParse(row.CommonAddress, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ca) ||
                !int.TryParse(row.IoAddress, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ioa) ||
                !int.TryParse(row.TypeId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var type))
                continue;

            // A strict physical CA/IOA/TI match is always admissible. A
            // read-only alias additionally needs observed evidence, a unique
            // profile IOA and no conflicting stations on the same source.
            var allowAlias = !string.IsNullOrWhiteSpace(row.EvidenceReference) &&
                !conflictingSources.Contains(Source(row));
            var match = names.Resolve(ca, ioa, type, allowNameOnly: allowAlias);
            row.ApplyDisplayName(match.Point?.Name ?? $"IOA {ioa}",
                match.Point?.Group ?? "Unmapped", match.Point is not null);
        }
    }

    private static string Source(ValueRow row)
        => string.IsNullOrWhiteSpace(row.PhysicalSource) ? "SINGLE" : row.PhysicalSource.Trim();
}
