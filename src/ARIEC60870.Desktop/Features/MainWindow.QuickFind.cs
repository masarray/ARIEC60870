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
    private bool _quickCountQueued;
    private readonly DispatcherTimer _quickFindDebounce = new(DispatcherPriority.Background)
    { Interval = TimeSpan.FromMilliseconds(180) };
    private string _traceQuickQuery = "";
    private string _traceQuickScope = "Activity focus";
    private string _eventsQuickQuery = "";
    private string _valuesQuickQuery = "";
    private string _valuesQuickScope = "All points";
    private string _controlsQuickQuery = "";
    private string _giQuickQuery = "";

    private void QuickFindWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_quickFindReady) return;
        _quickFindReady = true;
        _quickFindDebounce.Tick += (_, _) => { _quickFindDebounce.Stop(); ApplyQuickFind(); };
        _traceQuickScope = (TraceQuickScopeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Activity focus";
        CollectionViewSource.GetDefaultView(FrameTraceRows).Filter =
            o => o is EvidenceRow row && QuickFindMatcher.MatchesTrace(row,_traceQuickQuery,_traceQuickScope);
        _valuesQuickScope = (ValueQuickScopeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All points";
        CollectionViewSource.GetDefaultView(ValueRows).Filter =
            o => o is ValueRow row && QuickFindMatcher.MatchesValue(row, _valuesQuickQuery, _valuesQuickScope);
        CollectionViewSource.GetDefaultView(ControlCaseRows).Filter =
            o => o is Iec10xCommandCasebook.CommandCase row && QuickFindMatcher.MatchesControl(row,_controlsQuickQuery);
        CollectionViewSource.GetDefaultView(GiCaseRows).Filter =
            o => o is Iec10xGiCasebook.GiCase row && QuickFindMatcher.MatchesGi(row,_giQuickQuery);
        RelayEventRows.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset &&
                string.Equals(_eventCommittedScope, CurrentEventScopeKey(), StringComparison.Ordinal))
                _eventLiveResetCount++;
        };
        FrameTraceRows.CollectionChanged += (_, _) => QueueQuickFindCountRefresh();
        ValueRows.CollectionChanged += (_, _) => QueueQuickFindCountRefresh();
        ControlCaseRows.CollectionChanged += (_, _) => QueueQuickFindCountRefresh();
        GiCaseRows.CollectionChanged += (_, _) => QueueQuickFindCountRefresh();
        UpdateQuickFindClearButtons();
        ApplyQuickFind();
        UpdateQuickFindCounts();
    }

    private void QueueQuickFindCountRefresh()
    {
        if (!_quickFindReady || _quickCountQueued) return;
        _quickCountQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _quickCountQueued = false;
            UpdateQuickFindCounts();
        }), DispatcherPriority.Background);
    }

    private void QuickFindTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateQuickFindClearButtons();
        if (!_quickFindReady) return;
        _quickFindDebounce.Stop();
        _quickFindDebounce.Start();
    }
    private void UpdateQuickFindClearButtons()
    {
        if (TraceSearchClearButton is not null && TraceQuickFindBox is not null)
            TraceSearchClearButton.Visibility = string.IsNullOrEmpty(TraceQuickFindBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        if (ValuesSearchClearButton is not null && ValuesQuickFindBox is not null)
            ValuesSearchClearButton.Visibility = string.IsNullOrEmpty(ValuesQuickFindBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        if (EventsSearchClearButton is not null && EventsQuickFindBox is not null)
            EventsSearchClearButton.Visibility = string.IsNullOrEmpty(EventsQuickFindBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        if (ControlsSearchClearButton is not null && ControlsQuickFindBox is not null)
            ControlsSearchClearButton.Visibility = string.IsNullOrEmpty(ControlsQuickFindBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        if (GISearchClearButton is not null && GiQuickFindBox is not null)
            GISearchClearButton.Visibility = string.IsNullOrEmpty(GiQuickFindBox.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void TraceQuickScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_quickFindReady) return;
        _traceQuickScope = (TraceQuickScopeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Activity focus";
        RefreshQuickFindView(FrameTraceRows);
        UpdateQuickFindCounts();
        ScrollLatestProtocolTraceIfAutoScroll();
    }
    private void ValueQuickScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_quickFindReady) return;
        _valuesQuickScope = (ValueQuickScopeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All points";
        RefreshQuickFindView(ValueRows);
        UpdateQuickFindCounts();
    }

    // Context-only scope selection: filters keep their previous state without
    // occupying the data grid toolbar or introducing another binding state.
    private static void SelectScopeFromMenu(object sender, ComboBox scope)
    {
        var wanted = (sender as MenuItem)?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(wanted)) return;
        for (var i = 0; i < scope.Items.Count; i++)
        {
            if ((scope.Items[i] as ComboBoxItem)?.Content?.ToString() != wanted) continue;
            scope.SelectedIndex = i;
            return;
        }
    }

    private void ValueScopeMenu_Click(object sender, RoutedEventArgs e) =>
        SelectScopeFromMenu(sender, ValueQuickScopeCombo);

    private void EventScopeMenu_Click(object sender, RoutedEventArgs e) =>
        SelectScopeFromMenu(sender, EventLogFilterComboBox);

    private void TraceScopeMenu_Click(object sender, RoutedEventArgs e) =>
        SelectScopeFromMenu(sender, TraceQuickScopeCombo);

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
        // A search in one workspace must not invalidate unrelated WPF views.
        var trace = TraceQuickFindBox.Text.Trim();
        var events = EventsQuickFindBox.Text.Trim();
        var values = ValuesQuickFindBox.Text.Trim();
        var controls = ControlsQuickFindBox.Text.Trim();
        var gi = GiQuickFindBox.Text.Trim();
        if (_traceQuickQuery != trace)
        {
            _traceQuickQuery = trace;
            RefreshQuickFindView(FrameTraceRows);
            ScrollLatestProtocolTraceIfAutoScroll();
        }
        if (_eventsQuickQuery != events)
        {
            _eventsQuickQuery = events;
            ApplyRelayEventFilter();
            ScrollLatestEventIfAutoScroll();
        }
        if (_valuesQuickQuery != values) { _valuesQuickQuery = values; RefreshQuickFindView(ValueRows); }
        if (_controlsQuickQuery != controls) { _controlsQuickQuery = controls; RefreshQuickFindView(ControlCaseRows); }
        if (_giQuickQuery != gi) { _giQuickQuery = gi; RefreshQuickFindView(GiCaseRows); }
        UpdateQuickFindCounts();
    }
    private static void RefreshQuickFindView(IEnumerable source) =>
        CollectionViewSource.GetDefaultView(source)?.Refresh();

    private void UpdateQuickFindCounts()
    {
        TraceQuickCountText.Text = $"{FrameTraceGrid.Items.Count} / {FrameTraceRows.Count} frames" +
            (_traceQuickScope == "Activity focus" ? " · Focus" :
                _traceQuickScope == "All frames" ? "" : " · " + _traceQuickScope);
        ValuesQuickCountText.Text = $"{ValueGrid.Items.Count} / {ValueRows.Count} values" +
            (_valuesQuickScope == "All points" ? "" : " · " + _valuesQuickScope);
        ControlsQuickCountText.Text = $"{ControlCasesGrid.Items.Count} / {ControlCaseRows.Count} cases";
        GiQuickCountText.Text = $"{GiCasesGrid.Items.Count} / {GiCaseRows.Count} cases";
    }
    private void ClearTraceQuickFindForNavigation()
    {
        if (!_quickFindReady) return;
        _quickFindDebounce.Stop();
        TraceQuickFindBox.Clear();
        // Exact frame navigation intentionally reveals all raw traffic.
        TraceQuickScopeCombo.SelectedIndex = 1;
        _traceQuickQuery = "";
        _traceQuickScope = "All frames";
        RefreshQuickFindView(FrameTraceRows);
        UpdateQuickFindCounts();
    }
}
