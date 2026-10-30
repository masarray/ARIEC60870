// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;

namespace ARIEC60870.Desktop;

/// <summary>
/// Presentation-only control of observation surface area. No protocol state,
/// raw evidence, source identity or RX/TX session ownership is modified here.
/// </summary>
public partial class MainWindow
{
    private double _lastTraceInspectorWidth = 300;
    private bool _traceInspectorPreferredVisible = true;
    private bool _traceInspectorForcedOpen;

    // Responsive decisions run on Window size/command layout changes, not on
    // incoming frames. Preserve the operator's preference across width changes.
    private void UpdateObservationViewportLayout()
    {
        if (TraceInspectorColumn is null || FrameInterpreterPanel is null) return;
        var widthAvailable = ActualWidth - 160 - CommandDockColumn.ActualWidth;
        var enoughRoom = widthAvailable >= 990;
        ApplyTraceInspectorVisibility(_traceInspectorPreferredVisible &&
            (enoughRoom || _traceInspectorForcedOpen));
    }

    private void ApplyTraceInspectorVisibility(bool visible)
    {
        if (TraceInspectorColumn is null) return;
        if (visible && FrameInterpreterPanel.Visibility != Visibility.Visible)
        {
            TraceInspectorColumn.Width = new GridLength(Math.Max(240, _lastTraceInspectorWidth));
            TraceInspectorGutterColumn.Width = new GridLength(6);
            FrameInterpreterPanel.Visibility = Visibility.Visible;
            TraceInspectorSplitter.Visibility = Visibility.Visible;
        }
        else if (!visible && FrameInterpreterPanel.Visibility != Visibility.Collapsed)
        {
            if (TraceInspectorColumn.ActualWidth > 0)
                _lastTraceInspectorWidth = TraceInspectorColumn.ActualWidth;
            FrameInterpreterPanel.Visibility = Visibility.Collapsed;
            TraceInspectorSplitter.Visibility = Visibility.Collapsed;
            TraceInspectorGutterColumn.Width = new GridLength(0);
            TraceInspectorColumn.Width = new GridLength(0);
        }

        TraceInspectorToggle.Content = visible ? "Hide details" : "Show details";
        TraceInspectorToggle.ToolTip = visible
            ? "Hide the selected frame inspector and return width to Trace"
            : "Open the selected frame inspector; on narrow windows its width may reduce Trace";
    }

    private void TraceInspectorToggle_Click(object sender, RoutedEventArgs e)
    {
        var visible = FrameInterpreterPanel.Visibility == Visibility.Visible;
        if (visible)
        {
            _traceInspectorPreferredVisible = false;
            _traceInspectorForcedOpen = false;
        }
        else
        {
            _traceInspectorPreferredVisible = true;
            _traceInspectorForcedOpen = true; // explicit action overrides auto-collapse
        }
        ApplyTraceInspectorVisibility(!visible);
    }

    private void TraceArchiveMode_Click(object sender, RoutedEventArgs e)
    {
        SetTraceSearchSurface(archive: true);
        TraceRecordedQueryBox.Focus();
    }

    private void TraceCurrentMode_Click(object sender, RoutedEventArgs e)
    {
        // Scope changes are operator-directed. Cancel any stale ZIP read before
        // returning to recent Trace; never alter the authoritative raw ledger.
        if (_traceArchiveFinding) CancelRecordedArchiveSearch();
        if (_recordedArchiveViewActive) ResumeLiveViews(clearSelection: true, followLive: true);
        SetTraceSearchSurface(archive: false);
        TraceQuickFindBox.Focus();
    }

    private void SetTraceSearchSurface(bool archive)
    {
        TraceWindowSearchBar.Visibility = archive ? Visibility.Collapsed : Visibility.Visible;
        TraceArchiveSearchBar.Visibility = archive ? Visibility.Visible : Visibility.Collapsed;
    }
}
