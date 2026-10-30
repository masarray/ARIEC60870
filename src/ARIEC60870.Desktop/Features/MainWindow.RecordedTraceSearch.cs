// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Windows;
using ARIEC60870.Desktop.ViewModels;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

/// <summary>
/// Read-only paged full-file search. SHA validation, streaming and filtering run
/// outside the Dispatcher; the live bounded ring and active transport are never
/// overwritten. Each page is at most 150 frame rows in the existing Trace.
/// </summary>
public partial class MainWindow
{
    private const int RecordedTracePageSize = 150;
    private string? _lastOpenedAriecFile;
    private string _traceArchiveQuery = string.Empty;
    private int _traceArchivePage;
    private bool _traceArchiveFinding;

    private async void TraceRecordedFind_Click(object sender, RoutedEventArgs e)
    {
        _traceArchiveQuery = TraceRecordedQueryBox.Text.Trim();
        _traceArchivePage = 0;
        await ShowRecordedArchivePageAsync();
    }

    private async void TraceRecordedNext_Click(object sender, RoutedEventArgs e)
    {
        if (_traceArchiveFinding) return;
        _traceArchivePage++;
        await ShowRecordedArchivePageAsync();
    }

    private async void TraceRecordedPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_traceArchiveFinding || _traceArchivePage == 0) return;
        _traceArchivePage--;
        await ShowRecordedArchivePageAsync();
    }

    private async Task ShowRecordedArchivePageAsync()
    {
        if (_traceArchiveFinding) return;
        // Historical capture navigation must not mutate a running field session
        // or co-exist with its active RX/replay producers.
        if (_sessionCancellation is not null ||
            _passiveMonitorSession is not null ||
            _passiveReplayCancellation is not null)
        {
            TraceRecordedStatusText.Text = "Stop the active session/replay before searching a recorded archive.";
            return;
        }

        try
        {
            var query = CaptureArchiveQuery.Parse(_traceArchiveQuery);
            if (string.IsNullOrWhiteSpace(_lastOpenedAriecFile) ||
                !File.Exists(_lastOpenedAriecFile))
            {
                var chooser = new OpenFileDialog
                {
                    Title = "Find frames in verified portable .ariec capture",
                    Filter = "ARIEC portable capture (*.ariec;*.zip)|*.ariec;*.zip",
                    CheckFileExists = true
                };
                if (chooser.ShowDialog(this) != true) return;
                _lastOpenedAriecFile = chooser.FileName;
            }

            _traceArchiveFinding = true;
            TraceRecordedFindButton.IsEnabled = false;
            TraceRecordedPrevButton.IsEnabled = false;
            TraceRecordedNextButton.IsEnabled = false;
            TraceRecordedStatusText.Text = "Verifying original archive SHA-256 and scanning all frames…";

            var path = _lastOpenedAriecFile!;
            var page = _traceArchivePage;
            var result = await Task.Run(() => CaptureArchiveReader.Search(path, query,
                skipMatching: (long)page * RecordedTracePageSize,
                pageSize: RecordedTracePageSize));

            var rows = result.Rows.Select(snapshot => new EvidenceRow(snapshot)).ToArray();
            EnterLineMonitorHoldForUserSelection();
            FrameTraceRows.ReplaceRange(rows);
            if (rows.Length > 0)
            {
                FrameTraceGrid.SelectedItem = rows[0];
                FrameTraceGrid.ScrollIntoView(rows[0]);
                ApplySelectedEvidenceRowToInspector(rows[0]);
            }
            else
            {
                FrameTraceGrid.SelectedItem = null;
                ApplySelectedEvidenceRowToInspector(null);
            }
            FrameTimelineCaption.Text = "Recorded .ariec · search results";
            var first = rows.Length == 0 ? 0 : (long)page * RecordedTracePageSize + 1;
            var last = (long)page * RecordedTracePageSize + rows.Length;
            TraceRecordedStatusText.Text =
                $"Matches {first:N0}–{last:N0} of {result.MatchingRows:N0} · scanned {result.TotalRows:N0} records · " +
                result.TrustDescription + " · read-only; RESUME restores recent live Trace";
            TraceRecordedPrevButton.IsEnabled = page > 0;
            TraceRecordedNextButton.IsEnabled = last < result.MatchingRows;
        }
        catch (Exception ex)
        {
            TraceRecordedStatusText.Text = "Archive search failed: " + ex.Message;
            AddUiDiagnostic("Warning", "Capture", "ARIEC-ARCHIVE-SEARCH-FAILED",
                "Cannot search original .ariec archive", ex.Message,
                "Check filter grammar, portable capture integrity and recorded profile.", ex);
        }
        finally
        {
            _traceArchiveFinding = false;
            TraceRecordedFindButton.IsEnabled = true;
        }
    }
}
