// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>Orthogonal opt-in inclusion and exclusion; exclusion always wins.</summary>
public static class EventCategoryFilter
{
    public static bool Allows(string category,
        IReadOnlySet<string> include, IReadOnlySet<string> exclude) =>
        include.Contains(category) && !exclude.Contains(category);
}
