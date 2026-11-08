// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Plans bounded Frame Trace evictions without rebuilding the WPF collection.
/// Prefer evicting old, unselected rows; preserve the latest visible tail.
/// A pinned selection can outlive the canonical 1,200-row source ring without
/// stopping reception or inventing a new source of protocol evidence.
/// </summary>
public static class TraceRetentionPolicy
{
    public static IReadOnlyList<int> PlanRemovalIndices<T>(
        IReadOnlyList<T> rows, int maximumRows, IReadOnlySet<T> selected,
        int latestTailReserve = 64) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);
        ArgumentOutOfRangeException.ThrowIfNegative(latestTailReserve);

        var overflow = rows.Count - maximumRows;
        if (overflow <= 0) return Array.Empty<int>();

        var removals = new List<int>(overflow);
        // All newly arriving frames must remain eligible for display, even
        // when every old row is selected. Protect at least the overflow count
        // of newest frames, plus the normal 64-row recent tail.
        var reservedTail = Math.Min(maximumRows, Math.Max(latestTailReserve, overflow));
        var olderLimit = rows.Count - reservedTail;
        for (var i = 0; i < olderLimit && removals.Count < overflow; i++)
            if (!selected.Contains(rows[i])) removals.Add(i);

        // Only when every eligible old row is selected do we expire its
        // oldest selections to preserve the strict memory bound.
        for (var i = 0; i < olderLimit && removals.Count < overflow; i++)
            if (selected.Contains(rows[i])) removals.Add(i);

        // Covers tiny windows and oversized single arrivals without ever
        // leaking row objects. Do not silently discard the newest frames.
        for (var i = olderLimit; i < rows.Count && removals.Count < overflow; i++)
            if (!selected.Contains(rows[i])) removals.Add(i);
        for (var i = olderLimit; i < rows.Count && removals.Count < overflow; i++)
            if (selected.Contains(rows[i])) removals.Add(i);

        if (removals.Count != overflow)
            throw new InvalidOperationException("Trace retention must always preserve its configured bound.");

        // Removing highest indices first leaves all remaining planned indices
        // unchanged and avoids re-scanning selected rows on every eviction.
        removals.Sort((left, right) => right.CompareTo(left));
        return removals;
    }
}
