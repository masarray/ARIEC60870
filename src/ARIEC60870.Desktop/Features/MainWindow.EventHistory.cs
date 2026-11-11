// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Model;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ARIEC60870.Desktop.ViewModels;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private EventHistoryJournal? _eventHistoryJournal;
    private readonly ObservableRangeCollection<RelayEventRow> _historicalEventRows = new();
    private long _eventHistoryObserved;
    private long _eventHistoryUnindexed;
    private bool _eventHistoryWriteFailed;
    private bool _eventHistoryMode;
    private long _eventHistoryCeiling;
    private long _eventHistoryPageStart;
    private int _eventHistoryGeneration;

    private void ResetEventHistory()
    {
        _eventHistoryGeneration++;
        _eventHistoryMode = false;
        _eventHistoryCeiling = 0;
        _eventHistoryPageStart = 0;
        _eventHistoryObserved = 0;
        _eventHistoryUnindexed = 0;
        _eventHistoryWriteFailed = false;
        _historicalEventRows.Clear();
        if (RelayEventGrid is not null)
        {
            RelayEventGrid.SetBinding(ItemsControl.ItemsSourceProperty,
                new Binding(nameof(RelayEventRows)) { Source = this });
            EventsQuickFindBox.IsEnabled = true;
            EventIncludeToggle.IsEnabled = true;
            EventExcludeToggle.IsEnabled = true;
        }
        _eventHistoryJournal?.Dispose();
        _eventHistoryJournal = null;
        try { _eventHistoryJournal = new EventHistoryJournal(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _eventHistoryWriteFailed = true;
            AppendSessionLog("Temporary Event History unavailable: " + ex.Message);
        }
        RefreshEventHistoryFooter();
    }

    /// <summary>
    /// WPF Event Log observation, NOT a claim that the RTU generated or delivered
    /// the same number of source events. Writer-owned passive statistics remain
    /// independent; the preview ring can roll over without deleting this index.
    /// </summary>
    private void RecordEventHistory(RelayEventRow row)
    {
        row.EventNumber = ++_eventHistoryObserved;
        if (_eventHistoryJournal is null || _eventHistoryWriteFailed)
        {
            _eventHistoryUnindexed++;
            return;
        }
        try { _eventHistoryJournal.Append(row); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or JsonException or NotSupportedException)
        {
            _eventHistoryWriteFailed = true;
            _eventHistoryUnindexed++;
            AppendSessionLog("Event History storage failed; existing indexed rows remain available. " +
                             "Subsequent Event Log observations are unindexed: " + ex.Message);
        }
    }

    private void RefreshEventHistoryFooter()
    {
        if (EventHistoryStatusText is null) return;
        var indexed = _eventHistoryJournal?.Count ?? 0;
        var newer = _eventHistoryMode ? Math.Max(0, indexed - _eventHistoryCeiling) : 0;
        EventHistoryStatusText.Text =
            $"{indexed:N0} UI events indexed" +
            (_eventHistoryUnindexed > 0 ? $" · {_eventHistoryUnindexed:N0} unindexed (INCOMPLETE)" : "") +
            (newer > 0 ? $" · +{newer:N0} newer" : "") +
            " · temporary until next session";
        if (_eventHistoryMode)
        {
            var pages = EventHistoryPagination.PageCount(_eventHistoryCeiling);
            EventHistoryPageText.Text =
                $"{_eventHistoryPageStart / EventHistoryPagination.PageSize + 1:N0} / {pages:N0} · " +
                $"{_eventHistoryPageStart + 1:N0}–" +
                $"{Math.Min(_eventHistoryCeiling, _eventHistoryPageStart + EventHistoryPagination.PageSize):N0}";
            EventsQuickCountText.Text = "History · all categories";
        }
        else
            EventHistoryPageText.Text = "Live · latest";
        EventHistoryBrowseButton.IsEnabled = indexed > 0 && !_eventHistoryMode;
        EventHistoryPreviousButton.IsEnabled = _eventHistoryMode && _eventHistoryPageStart > 0;
        EventHistoryNextButton.IsEnabled = _eventHistoryMode &&
            _eventHistoryPageStart + EventHistoryPagination.PageSize < _eventHistoryCeiling;
        EventHistoryJumpBox.IsEnabled = _eventHistoryMode;
        EventHistoryJumpButton.IsEnabled = _eventHistoryMode;
        EventHistoryLatestButton.IsEnabled = _eventHistoryMode;
    }

    private async void EventHistoryBrowse_Click(object sender, RoutedEventArgs e)
    {
        var indexed = _eventHistoryJournal?.Count ?? 0;
        if (indexed <= 0) return;
        _eventHistoryCeiling = indexed; // immutable page horizon during browsing
        _eventHistoryPageStart = 0;
        _eventHistoryMode = true;
        _eventHistoryGeneration++;
        EventsAutoScrollButton.IsChecked = false; // stops only WPF follow
        EventsQuickFindBox.IsEnabled = false;
        EventIncludeToggle.IsEnabled = false;
        EventExcludeToggle.IsEnabled = false;
        RelayEventGrid.ItemsSource = _historicalEventRows;
        RefreshEventHistoryFooter();
        await ShowEventHistoryPageAsync(0);
    }

    private async Task ShowEventHistoryPageAsync(long start)
    {
        var journal = _eventHistoryJournal;
        if (!_eventHistoryMode || journal is null || start < 0 ||
            start >= _eventHistoryCeiling) return;
        var generation = ++_eventHistoryGeneration;
        _eventHistoryPageStart = start;
        RefreshEventHistoryFooter();
        try
        {
            var take = (int)Math.Min(EventHistoryPagination.PageSize,
                _eventHistoryCeiling - start);
            var page = await Task.Run(() => journal.ReadAfter(start, take));
            if (generation != _eventHistoryGeneration || !_eventHistoryMode ||
                !ReferenceEquals(journal, _eventHistoryJournal)) return;
            if (page.Count != take || page.Select((item, i) => item.EventNumber == start + i + 1)
                    .Any(ok => !ok))
                throw new InvalidDataException("Historical event sequence is incomplete.");
            // Reconstruct ONLY the selected view. Never re-ingest process objects,
            // update Values, touch Class 1/2 polling or increment SOE counters.
            _historicalEventRows.ReplaceRange(page.Select(item => item.ToRow()));
            RefreshEventHistoryFooter();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
                                   ObjectDisposedException or JsonException)
        {
            if (generation != _eventHistoryGeneration || !_eventHistoryMode) return;
            _historicalEventRows.Clear();
            EventHistoryPageText.Text = "History read error";
            AppendSessionLog("Event History page integrity error: " + ex.Message);
        }
    }

    private async void EventHistoryPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (!_eventHistoryMode || _eventHistoryPageStart == 0) return;
        await ShowEventHistoryPageAsync(Math.Max(0,
            _eventHistoryPageStart - EventHistoryPagination.PageSize));
    }

    private async void EventHistoryNext_Click(object sender, RoutedEventArgs e)
    {
        if (!_eventHistoryMode ||
            _eventHistoryPageStart + EventHistoryPagination.PageSize >= _eventHistoryCeiling)
            return;
        await ShowEventHistoryPageAsync(_eventHistoryPageStart + EventHistoryPagination.PageSize);
    }

    private async void EventHistoryJump_Click(object sender, RoutedEventArgs e)
    {
        if (!_eventHistoryMode) return;
        if (!long.TryParse(EventHistoryJumpBox.Text.Trim(), out var number) ||
            number < 1 || number > _eventHistoryCeiling)
        {
            EventHistoryPageText.Text = $"Enter # 1–{_eventHistoryCeiling:N0}";
            return;
        }
        await ShowEventHistoryPageAsync(EventHistoryPagination.StartForEvent(number));
        var selected = _historicalEventRows.FirstOrDefault(x => x.EventNumber == number);
        if (selected is not null)
        {
            RelayEventGrid.SelectedItem = selected;
            RelayEventGrid.ScrollIntoView(selected);
        }
    }

    private void EventHistoryLatest_Click(object sender, RoutedEventArgs e)
    {
        LeaveEventHistory();
        EventsAutoScrollButton.IsChecked = true;
        ScrollLatestEventIfAutoScroll();
    }

    /// <summary>
    /// Real WPF Event Log paging acceptance with >420 synthetic typed events.
    /// Tests the actual bound DataGrid, source identity, live rollover and
    /// historical navigation. No physical COM or serial polling is opened.
    /// </summary>
    public async Task RunEventHistoryGuiSmokeProbeAsync()
    {
        ClearSessionView(clearLog: false);
        MainTabControl.SelectedIndex = 3;
        var bytes = new byte[] { 0x10, 0x08, 0x69, 0x71, 0x16 };
        var frame = Ft12Parser.ForIec101(1).Decode(bytes);
        if (!frame.IsChecksumValid) throw new InvalidOperationException("Invalid fixture FT1.2");
        for (var i = 1; i <= 620; i++)
        {
            var original = new Iec103MasterEvidenceEvent
            {
                SequenceNumber = i,
                TimestampUtc = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc),
                Direction = FrameDirection.SlaveToMaster,
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                PhysicalSource = "SINGLE",
                Frame = frame,
                LinkAddress = 105,
                RawHex = frame.Hex,
                TypeId = 30,
                CommonAddressNumber = 105,
                InformationObjectAddress = i
            };
            AppendProtocolEvent(new Iec103RelayEventLogEntry
            {
                EvidenceSequenceNumber = original.SequenceNumber,
                ProtocolMode = original.ProtocolMode,
                PhysicalSource = original.PhysicalSource,
                EvidenceReference = original.EvidenceReference,
                RawHex = original.RawHex,
                CommonAddress = 105,
                InformationObjectAddress = i,
                TypeId = 30,
                SignalName = "IOA " + i,
                NewValue = (i & 1) == 0 ? "ON" : "OFF",
                CauseOfTransmission = "3"
            }, original);
        }
        ApplyRelayEventFilter();
        if (_eventHistoryJournal?.Count != 620 || _relayEventStore.Snapshot().Count != 420)
            throw new InvalidOperationException("Live Event Log rollover lost indexed source events.");
        _eventHistoryCeiling = 620;
        _eventHistoryMode = true;
        RelayEventGrid.ItemsSource = _historicalEventRows;
        await ShowEventHistoryPageAsync(0);
        if (_historicalEventRows.Count != 100 || _historicalEventRows[0].EventNumber != 1)
            throw new InvalidOperationException("First historical Event page is not source-backed.");
        await ShowEventHistoryPageAsync(200);
        if (_historicalEventRows.Count != 100 || _historicalEventRows[0].EventNumber != 201)
            throw new InvalidOperationException("Middle historical Event page is incorrect.");
        await ShowEventHistoryPageAsync(600);
        if (_historicalEventRows.Count != 20 || _historicalEventRows[^1].EventNumber != 620 ||
            _historicalEventRows[^1].OriginalFrameEvidence?.RawHex != frame.Hex ||
            _historicalEventRows[^1].OriginalFrameEvidence?.SequenceNumber != 620)
            throw new InvalidOperationException("Last Event page lost original frame provenance.");
        LeaveEventHistory();
        if (!ReferenceEquals(RelayEventGrid.ItemsSource, RelayEventRows) ||
            RelayEventRows.Count != 420)
            throw new InvalidOperationException("Latest failed to restore the bounded live Event Log.");
    }

    private void LeaveEventHistory()
    {
        if (!_eventHistoryMode) return;
        _eventHistoryGeneration++;
        _eventHistoryMode = false;
        _eventHistoryCeiling = 0;
        _eventHistoryPageStart = 0;
        _historicalEventRows.Clear();
        RelayEventGrid.SetBinding(ItemsControl.ItemsSourceProperty,
            new Binding(nameof(RelayEventRows)) { Source = this });
        EventsQuickFindBox.IsEnabled = true;
        EventIncludeToggle.IsEnabled = true;
        EventExcludeToggle.IsEnabled = true;
        _eventRowsDeferredWhileManualScroll = false;
        ApplyRelayEventFilter();
        _relayEventRowsDirty = false;
        RefreshEventHistoryFooter();
    }
}
