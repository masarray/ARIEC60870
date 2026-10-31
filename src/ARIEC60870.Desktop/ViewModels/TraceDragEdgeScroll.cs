// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>Pointer-edge scrolling, not coupled to capture or UI source refresh.</summary>
public static class TraceDragEdgeScroll
{
    public static double ScrollDelta(double pointerY, double viewportHeight)
    {
        if (double.IsNaN(pointerY) || double.IsNaN(viewportHeight) ||
            viewportHeight <= 80) return 0;
        const double edge = 36;
        if (pointerY < edge)
            return -Math.Clamp((edge - pointerY) * 1.1, 15, 64);
        if (pointerY > viewportHeight - edge)
            return Math.Clamp((pointerY - (viewportHeight - edge)) * 1.1, 15, 64);
        return 0;
    }
}
