// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Windows;
using ARIEC60870.Desktop.ViewModels;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

/// <summary>Independent, bounded read-only archive page navigation. No transport
/// or casebook mutations: each request verifies the original portable bytes
/// before the Dispatcher exposes at most 150 results.</summary>
public partial class MainWindow
{
    private const int RecordedTracePageSize = 150;
    private string? _lastOpenedAriecFile;
    private string _traceArchiveQuery = string.Empty;
    private int _traceArchivePage;
    private bool _traceArchiveFinding;
    private bool _recordedArchiveViewActive;
    private long _recordedArchiveGeneration;
    private CancellationTokenSource? _recordedArchiveCancellation;
    private CaptureArchiveIndex? _recordedArchiveIndex;
    private bool _discardRecordedIndexAfterSearch;

    private async void TraceRecordedFind_Click(object sender, RoutedEventArgs e)
        => await ShowRecordedArchivePageAsync(0, TraceRecordedQueryBox.Text.Trim());

    private async void TraceRecordedNext_Click(object sender, RoutedEventArgs e)
        => await ShowRecordedArchivePageAsync(_traceArchivePage + 1, _traceArchiveQuery);

    private async void TraceRecordedPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_traceArchivePage > 0)
            await ShowRecordedArchivePageAsync(_traceArchivePage - 1, _traceArchiveQuery);
    }

    private void TraceRecordedCancel_Click(object sender, RoutedEventArgs e)
    {
        CancelRecordedArchiveSearch();
        TraceRecordedStatusText.Text = "Archive scan canceled. Previous Trace evidence is unchanged.";
    }

    private void CancelRecordedArchiveSearch()
    {
        _recordedArchiveGeneration++;
        _recordedArchiveCancellation?.Cancel();
    }

    private void ReleaseRecordedArchiveIndex()
    {
        if (_traceArchiveFinding)
            _discardRecordedIndexAfterSearch = true;
        else
        {
            _recordedArchiveIndex?.Dispose();
            _recordedArchiveIndex = null;
        }
    }

    private void ResetRecordedArchiveSource()
    {
        CancelRecordedArchiveSearch();
        ReleaseRecordedArchiveIndex();
        _lastOpenedAriecFile = null;
        _traceArchivePage = 0;
        _traceArchiveQuery = string.Empty;
        _recordedArchiveViewActive = false;
        if (TraceRecordedNextButton is not null) TraceRecordedNextButton.IsEnabled = false;
        if (TraceRecordedPrevButton is not null) TraceRecordedPrevButton.IsEnabled = false;
        if (TraceRecordedStatusText is not null)
            TraceRecordedStatusText.Text = "Archive: select .ariec for complete capture search";
    }

    private void ExitRecordedArchiveView()
    {
        if (!_recordedArchiveViewActive) return;
        CancelRecordedArchiveSearch();
        ReleaseRecordedArchiveIndex();
        _recordedArchiveViewActive = false;
        TraceRecordedNextButton.IsEnabled = false;
        TraceRecordedPrevButton.IsEnabled = false;
        TraceRecordedStatusText.Text = "Archive results closed · Trace returned to recent session/capture preview.";
    }

    private async Task ShowRecordedArchivePageAsync(int requestedPage, string requestedQuery)
    {
        if (_traceArchiveFinding || requestedPage < 0) return;
        if (_sessionCancellation is not null ||
            _passiveMonitorSession is not null ||
            _passiveReplayCancellation is not null)
        {
            TraceRecordedStatusText.Text =
                "Stop the active session/replay before opening a portable archive.";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        var generation = ++_recordedArchiveGeneration;
        try
        {
            var query = CaptureArchiveQuery.Parse(requestedQuery);
            if (string.IsNullOrWhiteSpace(_lastOpenedAriecFile) ||
                !File.Exists(_lastOpenedAriecFile))
            {
                var chooser = new OpenFileDialog
                {
                    Title = "Find original frames in portable .ariec capture",
                    Filter = "ARIEC portable capture (*.ariec;*.zip)|*.ariec;*.zip",
                    CheckFileExists = true
                };
                if (chooser.ShowDialog(this) != true) return;
                _lastOpenedAriecFile = chooser.FileName;
            }

            _traceArchiveFinding = true;
            _recordedArchiveCancellation = cancellation;
            TraceRecordedFindButton.IsEnabled = false;
            TraceRecordedPrevButton.IsEnabled = false;
            TraceRecordedNextButton.IsEnabled = false;
            TraceRecordedCancelButton.IsEnabled = true;
            TraceRecordedStatusText.Text = "Reading all recorded frames and checking archive integrity…";

            var path = _lastOpenedAriecFile!;
            var cached = _recordedArchiveIndex;
            if (cached is not null && !cached.IsCurrentSource(path))
            {
                cached.Dispose();
                cached = null;
                _recordedArchiveIndex = null;
            }
            var search = await Task.Run(() =>
            {
                var index = cached ?? CaptureArchiveReader.OpenIndexed(path, cancellation.Token);
                try
                {
                    var page = index.Search(query,
                        skipMatching: (long)requestedPage * RecordedTracePageSize,
                        pageSize: RecordedTracePageSize, cancellationToken: cancellation.Token);
                    return (Index: index, Page: page, NewIndex: cached is null);
                }
                catch
                {
                    if (cached is null) index.Dispose();
                    throw;
                }
            }, cancellation.Token);

            // Opening another capture/session or pressing Cancel invalidates
            // the old request, even if it finished while the user changed scope.
            // A session/capture switch invalidates the request generation and
            // cancels its token in ClearSessionView/PrepareLiveTraceForConnect.
            // These monotonic guards cover changes across the await boundary.
            if (cancellation.IsCancellationRequested ||
                generation != _recordedArchiveGeneration)
            {
                if (search.NewIndex) search.Index.Dispose();
                return;
            }
            if (search.NewIndex) _recordedArchiveIndex = search.Index;
            var result = search.Page;

            ClearTraceQuickFindForNavigation();
            var rows = result.Rows.Select(snapshot => new EvidenceRow(snapshot)).ToArray();
            EnterLineMonitorHoldForUserSelection();
            _historicalTraceViewActive = true;
            _recordedArchiveViewActive = true;
            TraceOlderButton.Visibility = Visibility.Collapsed;
            TraceNewerButton.Visibility = Visibility.Collapsed;
            FrameTraceRows.ReplaceRange(rows);
            FrameTraceGrid.SelectedItems.Clear();
            var selected = rows.FirstOrDefault();
            FrameTraceGrid.SelectedItem = selected;
            if (selected is not null) FrameTraceGrid.ScrollIntoView(selected);
            ApplySelectedEvidenceRowToInspector(selected);

            _traceArchivePage = requestedPage;
            _traceArchiveQuery = requestedQuery;
            FrameTimelineCaption.Text = "Portable .ariec · full-archive search";
            var first = rows.Length == 0 ? 0 : (long)requestedPage * RecordedTracePageSize + 1;
            var last = (long)requestedPage * RecordedTracePageSize + rows.Length;
            var trust = result.IntegrityVerified
                ? "SHA-256 matches manifest (integrity; not digital signature)"
                : "LEGACY UNVERIFIED · manifest hash unavailable";
            TraceRecordedStatusText.Text =
                $"Matches {first:N0}–{last:N0} / {result.MatchingRows:N0} · {result.TotalRows:N0} scanned · " +
                trust + " · verified indexed snapshot (no ZIP rescan per page)";
            TraceRecordedPrevButton.IsEnabled = requestedPage > 0;
            TraceRecordedNextButton.IsEnabled = last < result.MatchingRows;
        }
        catch (OperationCanceledException)
        {
            if (generation == _recordedArchiveGeneration)
                TraceRecordedStatusText.Text = "Archive search canceled; existing evidence preserved.";
        }
        catch (IOException ex) { ReportArchiveSearchFailure(ex, generation); }
        catch (UnauthorizedAccessException ex) { ReportArchiveSearchFailure(ex, generation); }
        catch (FormatException ex) { ReportArchiveSearchFailure(ex, generation); }
        catch (ArgumentException ex) { ReportArchiveSearchFailure(ex, generation); }
        finally
        {
            if (ReferenceEquals(_recordedArchiveCancellation, cancellation))
                _recordedArchiveCancellation = null;
            _traceArchiveFinding = false;
            if (_discardRecordedIndexAfterSearch)
            {
                _recordedArchiveIndex?.Dispose();
                _recordedArchiveIndex = null;
                _discardRecordedIndexAfterSearch = false;
            }
            TraceRecordedFindButton.IsEnabled = true;
            TraceRecordedCancelButton.IsEnabled = false;
        }
    }

    private void ReportArchiveSearchFailure(Exception error, long generation)
    {
        if (generation != _recordedArchiveGeneration) return;
        TraceRecordedStatusText.Text = "Archive search failed: " + error.Message;
        AddUiDiagnostic("Warning", "Capture", "ARIEC-ARCHIVE-SEARCH-FAILED",
            "Unable to read original portable archive", error.Message,
            "Verify ZIP structure, manifest integrity, search syntax and original field widths.", error);
    }
}
