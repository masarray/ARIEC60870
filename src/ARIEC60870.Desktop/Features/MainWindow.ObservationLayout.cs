// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using ARIEC60870.Desktop.ViewModels;

namespace ARIEC60870.Desktop;

/// <summary>
/// Presentation-only control of observation surface area. No protocol state,
/// raw evidence, source identity or RX/TX session ownership is modified here.
/// </summary>
public partial class MainWindow
{
    private bool _traceInspectorPreferredVisible = false;
    private bool _traceInspectorForcedOpen;

    // Responsive decisions run on Window size/command layout changes, not on
    // incoming frames. Preserve the operator's preference across width changes.
    private void UpdateObservationViewportLayout()
    {
        if (TraceInspectorColumn is null || FrameInterpreterPanel is null) return;
        // One Engineer Mode: inspector stays available at every viewport size.
        // A narrow window may scroll, but must never silently hide frame details.
        ApplyTraceInspectorVisibility(_traceInspectorPreferredVisible || _traceInspectorForcedOpen);
        UpdateTraceRowLayout();
    }

    private void CopyVisibleTraceRows_Click(object sender, RoutedEventArgs e)
    {
        // Right-click retains an existing Ctrl/Shift selection and otherwise
        // targets the pointed frame. Do not mutate the canonical evidence.
        var rows = GetSelectedProtocolTraceRowsForCapture();
        if (rows.Count == 0) return;
        Clipboard.SetText(FrameTraceRowClipboardFormatter.Format(rows));
        AppendSessionLog($"Copied {rows.Count} displayed Raw Frames row(s) as plain text.");
    }

    private void FrameTraceGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Changes only when the actual row viewport crosses the readable-width
        // threshold. No per-frame layout work or async dispatcher timers.
        if (e.WidthChanged) UpdateTraceRowLayout();
    }

    private void UpdateTraceRowLayout()
    {
        if (FrameTraceGrid is null || FrameTraceGrid.ActualWidth <= 0) return;
        var target = FrameTraceViewportPolicy.RowLayout(FrameTraceGrid.ActualWidth);
        if (!Equals(FrameTraceGrid.Tag, target))
            FrameTraceGrid.Tag = target;
    }

    private void ApplyTraceInspectorVisibility(bool visible)
    {
        if (TraceInspectorColumn is null) return;
        if (visible && FrameInterpreterPanel.Visibility != Visibility.Visible)
        {
            TraceInspectorColumn.MinWidth = 285;
            TraceInspectorColumn.Width = new GridLength(2, GridUnitType.Star);
            TraceInspectorGutterColumn.Width = new GridLength(6);
            FrameInterpreterPanel.Visibility = Visibility.Visible;
            TraceInspectorSplitter.Visibility = Visibility.Visible;
        }
        else if (!visible && FrameInterpreterPanel.Visibility != Visibility.Collapsed)
        {
            FrameInterpreterPanel.Visibility = Visibility.Collapsed;
            TraceInspectorSplitter.Visibility = Visibility.Collapsed;
            TraceInspectorGutterColumn.Width = new GridLength(0);
            TraceInspectorColumn.MinWidth = 0;
            TraceInspectorColumn.Width = new GridLength(0);

            // Release only the optional dock projections once on collapse,
            // not on each Frame Trace selection. Original evidence and the
            // selected frame stay in their authoritative source stores.
            SelectedFrameProperties.Clear();
            SelectedDecodedPoints.Clear();
            SelectedProtocolMapLines.Clear();
            SelectedHexSegments.Clear();
        }

        TraceInspectorToggle.Content = visible ? "›" : "‹";
        TraceInspectorToggle.ToolTip = visible
            ? "Collapse Frame Details" : "Expand Frame Details; double-click any frame";
        System.Windows.Automation.AutomationProperties.SetName(TraceInspectorToggle,
            visible ? "Collapse Frame Details" : "Expand Frame Details");
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
        if (!visible && FrameTraceGrid.SelectedItem is EvidenceRow selected)
            ApplySelectedEvidenceRowToInspector(selected);
    }

    // The inspector is optional. The trace's own decode always remains visible.
    // Existing context-menu commands can focus it without building a second panel.
    private void TraceFrameDetails_Click(object sender, RoutedEventArgs e)
    {
        var row = FrameTraceGrid.SelectedItems.OfType<EvidenceRow>()
            .OrderBy(frame => frame.Sequence).LastOrDefault()
            ?? FrameTraceGrid.SelectedItem as EvidenceRow;
        if (row is null) return;
        OpenTraceInspectorForRow(row);
    }

    private void OpenTraceInspectorForRow(EvidenceRow row)
    {
        _traceInspectorPreferredVisible = true;
        _traceInspectorForcedOpen = true;
        ApplyTraceInspectorVisibility(true);
        ApplySelectedEvidenceRowToInspector(row);
    }

    private void TraceFrameDetailsClose_Click(object sender, RoutedEventArgs e)
    {
        _traceInspectorPreferredVisible = false;
        _traceInspectorForcedOpen = false;
        ApplyTraceInspectorVisibility(false);
        // Preserve selected evidence, user HOLD state, and source-backed
        // decoded projections. A later reopen must show the same frame.
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
