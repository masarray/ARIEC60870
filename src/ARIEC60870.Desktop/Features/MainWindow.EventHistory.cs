// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private EventHistoryJournal? _eventHistoryJournal;
    private EventHistoryAsyncWriter? _eventHistoryWriter;

    // Legacy ring still feeds other evidence counters, but the operator's
    // Event grid has exactly ONE source: numbered, indexed history pages.
    private readonly ObservableRangeCollection<RelayEventRow> _historicalEventRows = new();
    private EventHistoryQuerySnapshot _eventHistoryQuery = new(0, null);
    private long _eventHistoryObserved;
    private long _eventHistoryPageStart;
    private long _nextEventHistoryFooterRefreshTick;
    private int _eventHistoryQueryGeneration;
    private int _eventHistoryPageGeneration;
    private CancellationTokenSource? _eventHistoryQueryCancel;
    private CancellationTokenSource? _eventHistoryCsvCancel;
    private bool _eventHistoryQueryBusy;
    private bool _eventHistoryRefreshBusy;
    private bool _eventHistoryPageBusy;
    private bool _eventHistoryFollowingLatest = true;
    private bool _eventHistoryFailureNoticeLogged;
    private bool _eventHistoryCsvRunning;

    private void ResetEventHistory()
    {
        _eventHistoryQueryCancel?.Cancel();
        _eventHistoryQueryCancel?.Dispose();
        _eventHistoryQueryCancel = null;
        _eventHistoryCsvCancel?.Cancel();
        _eventHistoryQueryGeneration++;
        _eventHistoryPageGeneration++;
        _eventHistoryQuery = new EventHistoryQuerySnapshot(0, null);
        _eventHistoryPageStart = 0;
        _eventHistoryObserved = 0;
        _eventHistoryFollowingLatest = true;
        _eventHistoryQueryBusy = false;
        _eventHistoryRefreshBusy = false;
        _eventHistoryPageBusy = false;
        _eventHistoryFailureNoticeLogged = false;
        _historicalEventRows.Clear();
        if (RelayEventGrid is not null)
        {
            RelayEventGrid.ItemsSource = _historicalEventRows;
        }
        _eventHistoryWriter?.Dispose();
        _eventHistoryWriter = null;
        _eventHistoryJournal = null;
        try
        {
            _eventHistoryJournal = new EventHistoryJournal();
            _eventHistoryWriter = new EventHistoryAsyncWriter(_eventHistoryJournal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _eventHistoryJournal?.Dispose();
            _eventHistoryJournal = null;
            AppendSessionLog("Event History index unavailable: " + ex.Message);
        }
        RefreshEventHistoryFooter();
    }

    /// <summary>
    /// Event History acceptance is independent of claims about outstation
    /// generated events; every row has a local immutable Event #.
    /// </summary>
    private void RecordEventHistory(RelayEventRow row)
    {
        row.EventNumber = ++_eventHistoryObserved;
        _eventHistoryWriter?.TryEnqueue(row);
    }

    // Kept as an internal compatibility entry point for existing UI flush
    // paths; there is no bottom footer or Live/History mode in the interface.
    private void RefreshEventHistoryFooter()
    {
        if (EventsQuickCountText is null) return;
        var indexed = _eventHistoryJournal?.Count ?? 0;
        var pending = _eventHistoryWriter?.PendingCount ?? 0;
        var unindexed = Math.Max(0, _eventHistoryObserved - indexed - pending);
        var failure = _eventHistoryWriter?.FailureReason;
        EventsQuickCountText.Text = _eventHistoryQuery.IsFiltered
            ? $"{_eventHistoryQuery.Count:N0} / {indexed:N0} events"
            : $"{indexed:N0} events";
        if (unindexed > 0) EventsQuickCountText.Text += $" · {unindexed:N0} unindexed";
        if (failure is not null && !_eventHistoryFailureNoticeLogged)
        {
            _eventHistoryFailureNoticeLogged = true;
            AppendSessionLog(failure + " Indexed rows remain available; " +
                "unindexed rows do not prove RTU FIFO loss.");
        }
        var matches = _eventHistoryQuery.Count;
        if (matches == 0)
            EventHistoryPageText.Text = "0–0";
        else
        {
            var from = Math.Min(_eventHistoryPageStart, matches - 1);
            var to = Math.Min(matches, from + EventHistoryPagination.PageSize);
            EventHistoryPageText.Text = $"{from + 1:N0}–{to:N0}";
        }
        EventHistoryPreviousButton.IsEnabled = matches > 0 && _eventHistoryPageStart > 0;
        EventHistoryNextButton.IsEnabled =
            _eventHistoryPageStart + EventHistoryPagination.PageSize < matches;
        EventHistoryLatestButton.IsEnabled = indexed > 0;
        EventHistoryExportButton.IsEnabled = indexed > 0 || _eventHistoryCsvRunning;
    }

    private async void RefreshEventHistoryFooterWhenDue()
    {
        var now = Environment.TickCount64;
        if (now < _nextEventHistoryFooterRefreshTick) return;
        _nextEventHistoryFooterRefreshTick = now + 350;
        RefreshEventHistoryFooter();

        // Update the *count and filter index* even when an older page is
        // pinned. Only the latest-page follower may replace visible rows.
        var journal = _eventHistoryJournal;
        if (journal is null || !IsEventsTabActive() ||
            _eventHistoryQueryBusy || _eventHistoryRefreshBusy ||
            _eventHistoryPageBusy || _eventHistoryCsvRunning ||
            journal.Count <= _eventHistoryQuery.Ceiling)
            return;

        _eventHistoryRefreshBusy = true;
        var generation = _eventHistoryQueryGeneration;
        var previous = _eventHistoryQuery;
        var ceiling = journal.Count;
        var search = _eventsQuickQuery;
        var included = _eventIncludeCategories.ToArray();
        var excluded = _eventExcludeCategories.ToArray();
        try
        {
            var next = await Task.Run(() => EventHistoryQueryEngine.Extend(
                journal, previous, ceiling, search, included, excluded, ClassifyEventCategory));
            if (generation != _eventHistoryQueryGeneration ||
                !ReferenceEquals(journal, _eventHistoryJournal) ||
                !ReferenceEquals(previous, _eventHistoryQuery)) return;
            _eventHistoryQuery = next;
            if (_eventHistoryFollowingLatest && RelayEventGrid?.SelectedItem is null)
                await ShowEventHistoryPageAsync(LastHistoryPageStart());
            // Else: leave original row objects and scrollbar exactly where
            // the engineer was inspecting; only toolbar counts advance.
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
                                   ObjectDisposedException or JsonException)
        {
            AppendSessionLog("Event History refresh failed: " + ex.Message);
        }
        finally
        {
            _eventHistoryRefreshBusy = false;
            RefreshEventHistoryFooter();
        }
    }

    private long LastHistoryPageStart() => _eventHistoryQuery.Count == 0 ? 0 :
        (_eventHistoryQuery.Count - 1) / EventHistoryPagination.PageSize *
        EventHistoryPagination.PageSize;

    private void RequestEventHistoryQuery() => _ = RefreshEventHistoryQueryAsync();

    private async Task RefreshEventHistoryQueryAsync()
    {
        var journal = _eventHistoryJournal;
        if (journal is null) return;
        _eventHistoryQueryCancel?.Cancel();
        _eventHistoryQueryCancel?.Dispose();
        var cts = new CancellationTokenSource();
        var token = cts.Token; // capture before another rapid query can dispose the source
        _eventHistoryQueryCancel = cts;
        var generation = ++_eventHistoryQueryGeneration;
        _eventHistoryQueryBusy = true;
        var scopeQuery = _eventsQuickQuery;
        var included = _eventIncludeCategories.ToArray();
        var excluded = _eventExcludeCategories.ToArray();
        var ceiling = journal.Count;
        try
        {
            var result = await Task.Run(() => EventHistoryQueryEngine.Build(
                journal, ceiling, scopeQuery, included, excluded,
                ClassifyEventCategory, token), token);
            if (generation != _eventHistoryQueryGeneration ||
                !ReferenceEquals(journal, _eventHistoryJournal)) return;
            _eventHistoryQuery = result;
            _eventHistoryFollowingLatest = true;
            RelayEventGrid.SelectedItem = null;
            await ShowEventHistoryPageAsync(LastHistoryPageStart());
        }
        catch (OperationCanceledException) { /* superseded query */ }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
                                   ObjectDisposedException or JsonException)
        {
            if (generation == _eventHistoryQueryGeneration)
                AppendSessionLog("Event History query failed: " + ex.Message);
        }
        finally
        {
            if (generation == _eventHistoryQueryGeneration)
            {
                _eventHistoryQueryBusy = false;
                RefreshEventHistoryFooter();
            }
            // Keep the active CTS valid until the next query or session reset.
            // Cancel() on an already-disposed CTS would break rapid filtering.
        }
    }

    private async Task ShowEventHistoryPageAsync(long start)
    {
        var journal = _eventHistoryJournal;
        if (journal is null) return;
        var snapshot = _eventHistoryQuery;
        var generation = ++_eventHistoryPageGeneration;
        if (snapshot.Count == 0)
        {
            _eventHistoryPageStart = 0;
            _historicalEventRows.Clear();
            RefreshEventHistoryFooter();
            return;
        }
        start = Math.Clamp(start, 0, LastHistoryPageStart());
        _eventHistoryPageStart = start;
        _eventHistoryPageBusy = true;
        RefreshEventHistoryFooter();
        try
        {
            var page = await Task.Run(() => snapshot.ReadPage(journal, start));
            if (generation != _eventHistoryPageGeneration ||
                !ReferenceEquals(snapshot, _eventHistoryQuery) ||
                !ReferenceEquals(journal, _eventHistoryJournal)) return;
            if (page.Count != Math.Min(EventHistoryPagination.PageSize, snapshot.Count - start))
                throw new InvalidDataException("Event History page is incomplete.");
            _historicalEventRows.ReplaceRange(page.Select(x => x.ToRow()));
            RefreshEventHistoryFooter();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
                                   ObjectDisposedException or JsonException)
        {
            if (generation != _eventHistoryPageGeneration) return;
            _historicalEventRows.Clear();
            AppendSessionLog("Event History page integrity failure: " + ex.Message);
        }
        finally
        {
            if (generation == _eventHistoryPageGeneration)
                _eventHistoryPageBusy = false;
        }
    }

    private async void EventHistoryPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (_eventHistoryPageStart == 0) return;
        _eventHistoryFollowingLatest = false;
        RelayEventGrid.SelectedItem = null;
        await ShowEventHistoryPageAsync(Math.Max(0,
            _eventHistoryPageStart - EventHistoryPagination.PageSize));
    }

    private async void EventHistoryNext_Click(object sender, RoutedEventArgs e)
    {
        if (_eventHistoryPageStart + EventHistoryPagination.PageSize >=
            _eventHistoryQuery.Count) return;
        _eventHistoryFollowingLatest = false;
        RelayEventGrid.SelectedItem = null;
        await ShowEventHistoryPageAsync(_eventHistoryPageStart + EventHistoryPagination.PageSize);
    }

    private async void EventHistoryLatest_Click(object sender, RoutedEventArgs e)
        => await JumpEventHistoryToLatestAsync();

    private async Task JumpEventHistoryToLatestAsync()
    {
        _eventHistoryFollowingLatest = true;
        RelayEventGrid.SelectedItem = null;
        if (_eventHistoryJournal?.Count > _eventHistoryQuery.Ceiling)
        {
            await RefreshEventHistoryQueryAsync();
            return;
        }
        await ShowEventHistoryPageAsync(LastHistoryPageStart());
    }

    private void EventHistoryExport_Click(object sender, RoutedEventArgs e)
    {
        if (_eventHistoryCsvRunning)
        {
            _eventHistoryCsvCancel?.Cancel();
            return;
        }
        if (EventHistoryExportButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = EventHistoryExportButton;
            menu.IsOpen = true;
        }
    }

    private async void EventHistoryExportScope_Click(object sender, RoutedEventArgs e)
    {
        if (_eventHistoryCsvRunning || _eventHistoryJournal is null) return;
        var all = (sender as MenuItem)?.Tag as string == "All";
        var journal = _eventHistoryJournal;
        if (_eventHistoryQueryBusy)
        {
            AppendSessionLog("CSV export: complete the active Event History query first.");
            return;
        }
        var snapshot = all
            ? new EventHistoryQuerySnapshot(journal.Count, null)
            : _eventHistoryQuery;
        if (snapshot.Count == 0) return;
        var save = new SaveFileDialog
        {
            Title = all ? "Export all indexed events" : "Export filtered events",
            Filter = "CSV (UTF-8)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = "ArSee-Events-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv"
        };
        if (save.ShowDialog(this) != true) return;
        _eventHistoryCsvRunning = true;
        _eventHistoryCsvCancel = new CancellationTokenSource();
        EventHistoryExportButton.Content = "Cancel export";
        RefreshEventHistoryFooter();
        try
        {
            var exported = await EventHistoryCsvExporter.ExportAsync(
                journal, snapshot, save.FileName, _eventHistoryCsvCancel.Token);
            AppendSessionLog($"CSV exported {exported:N0} indexed events: {save.FileName}");
        }
        catch (OperationCanceledException)
        {
            AppendSessionLog("Event CSV export cancelled. Partial file discarded.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
                                   UnauthorizedAccessException or JsonException or
                                   ObjectDisposedException)
        {
            // A session reset may close its temporary journal during export.
            // Never let an async-void WPF handler crash on this race.
            AppendSessionLog("Event CSV export failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Event CSV export failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _eventHistoryCsvCancel.Dispose();
            _eventHistoryCsvCancel = null;
            _eventHistoryCsvRunning = false;
            EventHistoryExportButton.Content = "Export CSV";
            RefreshEventHistoryFooter();
        }
    }

    /// <summary>
    /// Real WPF smoke: one Event grid, 620 indexed SOEs, page 1/middle/last,
    /// full-source search and CSV, while source preview remains at 420.
    /// </summary>
    public async Task RunEventHistoryGuiSmokeProbeAsync()
    {
        ClearSessionView(clearLog: false);
        MainTabControl.SelectedIndex = 3;
        var frame = Ft12Parser.ForIec101(1).Decode([0x10, 0x08, 0x69, 0x71, 0x16]);
        if (!frame.IsChecksumValid) throw new InvalidOperationException("Bad fixture frame.");
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
                EvidenceSequenceNumber = i,
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
        await (_eventHistoryWriter?.DrainAsync() ?? Task.CompletedTask);
        await RefreshEventHistoryQueryAsync();
        if (_eventHistoryJournal?.Count != 620 || _relayEventStore.Snapshot().Count != 420 ||
            !ReferenceEquals(RelayEventGrid.ItemsSource, _historicalEventRows))
            throw new InvalidOperationException("Unified Event History did not retain all 620 indexed rows.");
        await ShowEventHistoryPageAsync(0);
        if (_historicalEventRows.Count != 100 || _historicalEventRows[0].EventNumber != 1)
            throw new InvalidOperationException("Event History first page unavailable.");
        await ShowEventHistoryPageAsync(200);
        if (_historicalEventRows[0].EventNumber != 201)
            throw new InvalidOperationException("Event History middle page unavailable.");
        await ShowEventHistoryPageAsync(600);
        if (_historicalEventRows.Count != 20 ||
            _historicalEventRows[^1].EventNumber != 620 ||
            _historicalEventRows[^1].OriginalFrameEvidence?.RawHex != frame.Hex)
            throw new InvalidOperationException("Last Event History page lost original frame.");
        EventsQuickFindBox.Text = "#1";
        _eventsQuickQuery = "#1";
        await RefreshEventHistoryQueryAsync();
        if (_eventHistoryQuery.Count != 1 || _historicalEventRows[0].EventNumber != 1)
            throw new InvalidOperationException("Search was limited to the previous 420-event ring.");
        var exportFile = Path.Combine(Path.GetTempPath(),
            "ArSee-event-smoke-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var exported = await EventHistoryCsvExporter.ExportAsync(
                _eventHistoryJournal!, _eventHistoryQuery, exportFile);
            if (exported != 1 || File.ReadAllLines(exportFile).Length != 2)
                throw new InvalidOperationException("Filtered Event CSV export is not source-complete.");
        }
        finally
        {
            if (File.Exists(exportFile)) File.Delete(exportFile);
        }
        EventsQuickFindBox.Clear();
        _eventsQuickQuery = "";
        await RefreshEventHistoryQueryAsync();
        await JumpEventHistoryToLatestAsync();
        if (_historicalEventRows.Count != 20 || _historicalEventRows[^1].EventNumber != 620)
            throw new InvalidOperationException("Latest failed to return to the latest indexed page.");
    }
}
