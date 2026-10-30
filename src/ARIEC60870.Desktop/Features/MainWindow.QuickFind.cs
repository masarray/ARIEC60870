// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private bool _quickFindReady;
    private readonly DispatcherTimer _quickFindDebounce = new(DispatcherPriority.Background)
    { Interval = TimeSpan.FromMilliseconds(180) };
    private string _traceQuickQuery = "";
    private string _traceQuickScope = "All frames";
    private string _eventsQuickQuery = "";
    private string _valuesQuickQuery = "";
    private string _controlsQuickQuery = "";
    private string _giQuickQuery = "";

    private void QuickFindWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_quickFindReady) return;
        _quickFindReady = true;
        _quickFindDebounce.Tick += (_, _) => { _quickFindDebounce.Stop(); ApplyQuickFind(); };
        _traceQuickScope = (TraceQuickScopeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All frames";
        CollectionViewSource.GetDefaultView(FrameTraceRows).Filter =
            o => o is EvidenceRow row && QuickFindMatcher.MatchesTrace(row,_traceQuickQuery,_traceQuickScope);
        CollectionViewSource.GetDefaultView(ValueRows).Filter =
            o => o is ValueRow row && QuickFindMatcher.MatchesValue(row,_valuesQuickQuery);
        CollectionViewSource.GetDefaultView(ControlCaseRows).Filter =
            o => o is Iec10xCommandCasebook.CommandCase row && QuickFindMatcher.MatchesControl(row,_controlsQuickQuery);
        CollectionViewSource.GetDefaultView(GiCaseRows).Filter =
            o => o is Iec10xGiCasebook.GiCase row && QuickFindMatcher.MatchesGi(row,_giQuickQuery);
        ApplyQuickFind();
    }
    private void QuickFindTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_quickFindReady) return;
        _quickFindDebounce.Stop();
        _quickFindDebounce.Start();
    }
    private void TraceQuickScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_quickFindReady) return;
        _traceQuickScope = (TraceQuickScopeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All frames";
        RefreshQuickFindView(FrameTraceRows);
        UpdateQuickFindCounts();
        ScrollLatestProtocolTraceIfAutoScroll();
    }
    private void QuickFindClear_Click(object sender, RoutedEventArgs e)
    {
        var key = (sender as Button)?.Tag as string;
        TextBox? box = key switch
        {
            "Trace" => TraceQuickFindBox,
            "Events" => EventsQuickFindBox,
            "Values" => ValuesQuickFindBox,
            "Controls" => ControlsQuickFindBox,
            "GI" => GiQuickFindBox,
            _ => null
        };
        if (box is null) return;
        box.Clear();
        box.Focus();
        _quickFindDebounce.Stop();
        ApplyQuickFind();
    }
    private void ApplyQuickFind()
    {
        if (!_quickFindReady) return;
        _traceQuickQuery = TraceQuickFindBox.Text.Trim();
        _eventsQuickQuery = EventsQuickFindBox.Text.Trim();
        _valuesQuickQuery = ValuesQuickFindBox.Text.Trim();
        _controlsQuickQuery = ControlsQuickFindBox.Text.Trim();
        _giQuickQuery = GiQuickFindBox.Text.Trim();
        RefreshQuickFindView(FrameTraceRows);
        RefreshQuickFindView(ValueRows);
        RefreshQuickFindView(ControlCaseRows);
        RefreshQuickFindView(GiCaseRows);
        ApplyRelayEventFilter();
        UpdateQuickFindCounts();
        ScrollLatestProtocolTraceIfAutoScroll();
        ScrollLatestEventIfAutoScroll();
    }
    private static void RefreshQuickFindView(IEnumerable source) =>
        CollectionViewSource.GetDefaultView(source)?.Refresh();

    private void UpdateQuickFindCounts()
    {
        TraceQuickCountText.Text = $"{FrameTraceGrid.Items.Count} / {FrameTraceRows.Count} in window";
        ValuesQuickCountText.Text = $"{ValueGrid.Items.Count} / {ValueRows.Count} values";
        ControlsQuickCountText.Text = $"{ControlCasesGrid.Items.Count} / {ControlCaseRows.Count} cases";
        GiQuickCountText.Text = $"{GiCasesGrid.Items.Count} / {GiCaseRows.Count} cases";
    }
    private void ClearTraceQuickFindForNavigation()
    {
        if (!_quickFindReady) return;
        _quickFindDebounce.Stop();
        TraceQuickFindBox.Clear();
        TraceQuickScopeCombo.SelectedIndex = 0;
        _traceQuickQuery = "";
        _traceQuickScope = "All frames";
        RefreshQuickFindView(FrameTraceRows);
    }
}
