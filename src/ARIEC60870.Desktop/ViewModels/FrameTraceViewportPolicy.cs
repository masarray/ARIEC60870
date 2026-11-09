// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Inline Trace row geometry only; no user mode and no protocol-policy changes.
/// Use measured WPF viewport width, not the overall window or frame contents.
/// </summary>
public static class FrameTraceViewportPolicy
{
    public const double MinimumPairedLaneWidth = 780;

    public static string RowLayout(double viewportWidth) =>
        double.IsFinite(viewportWidth) &&
        viewportWidth > 0 && viewportWidth < MinimumPairedLaneWidth
            ? "Stacked" : "Paired";
}
