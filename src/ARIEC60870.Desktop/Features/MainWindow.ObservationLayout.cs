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

    private void TraceInspectorToggle_Click(object sender, RoutedEventArgs e)
    {
        var expanded = FrameInterpreterPanel.Visibility == Visibility.Visible;
        if (expanded && TraceInspectorColumn.Width.Value > 0)
            _lastTraceInspectorWidth = TraceInspectorColumn.ActualWidth > 0
                ? TraceInspectorColumn.ActualWidth : TraceInspectorColumn.Width.Value;

        // Retain the current decoded selected frame while hiding its inspector.
        FrameInterpreterPanel.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        TraceInspectorSplitter.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        TraceInspectorGutterColumn.Width = expanded ? new GridLength(0) : new GridLength(6);
        TraceInspectorColumn.Width = expanded
            ? new GridLength(0)
            : new GridLength(Math.Max(240, _lastTraceInspectorWidth));
        TraceInspectorToggle.Content = expanded ? "Show details" : "Hide details";
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
