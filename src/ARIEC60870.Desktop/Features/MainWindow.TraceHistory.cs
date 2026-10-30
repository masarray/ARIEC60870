// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Windows;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Monitor;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private PassiveSessionTraceHistory? _passiveFrameHistory;
    private bool _historicalTraceViewActive;
    private int _historyNavigationGeneration;

    private void ReleaseSessionTraceHistory()
    {
        _historyNavigationGeneration++;
        _historicalTraceViewActive = false;
        if (TraceOlderButton is not null) TraceOlderButton.Visibility = Visibility.Collapsed;
        if (TraceNewerButton is not null) TraceNewerButton.Visibility = Visibility.Collapsed;
        if (FrameTimelineCaption is not null) FrameTimelineCaption.Text = "Frame timeline";
        var history = _passiveFrameHistory;
        _passiveFrameHistory = null;
        if (history is not null)
        {
            try { history.Dispose(); }
            catch (IOException ex) { AppendSessionLog("Temporary Trace cleanup failed: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { AppendSessionLog("Temporary Trace cleanup denied: " + ex.Message); }
        }
    }

    internal static bool TryParsePassiveHistoryReference(string reference, out byte stream, out long offset)
    {
        stream = 0;
        offset = 0;
        if (string.IsNullOrWhiteSpace(reference)) return false;
        var parts = reference.Split('|');
        return parts.Length == 4 && parts[0] == "101" &&
            parts[1].StartsWith("stream:", StringComparison.Ordinal) &&
            byte.TryParse(parts[1]["stream:".Length..], NumberStyles.None,
                CultureInfo.InvariantCulture, out stream) && stream < 4 &&
            parts[2].StartsWith("byte:", StringComparison.Ordinal) &&
            long.TryParse(parts[2]["byte:".Length..], NumberStyles.None,
                CultureInfo.InvariantCulture, out offset) && offset >= 0 &&
            parts[3].StartsWith("object:", StringComparison.Ordinal) &&
            int.TryParse(parts[3]["object:".Length..], NumberStyles.None,
                CultureInfo.InvariantCulture, out var objectIndex) && objectIndex >= 0;
    }

    private async Task<bool> TryOpenHistoryWindowAsync(
        string reference, string source, Iec60870ProtocolMode protocol,
        int before = 80, int after = 80)
    {
        var history = _passiveFrameHistory;
        if (history is null || protocol != Iec60870ProtocolMode.Iec101 ||
            !TryParsePassiveHistoryReference(reference, out var sid, out var offset))
            return false;
        var expectedSource = sid < 2 ? "Link A" : "Link B";
        if (!string.Equals(expectedSource, source, StringComparison.OrdinalIgnoreCase))
            return false;

        var generation = ++_historyNavigationGeneration;
        IReadOnlyList<PassiveObservedFrame> observed;
        try
        {
            observed = await Task.Run(() => history.ReadWindow(sid, offset, before, after));
        }
        catch (ObjectDisposedException) { return false; }
        catch (IOException ex)
        {
            AppendSessionLog("Temporary Trace read failed: " + ex.Message);
            return false;
        }
        catch (InvalidDataException ex)
        {
            AppendSessionLog("Temporary Trace invalid: " + ex.Message);
            return false;
        }
        if (generation != _historyNavigationGeneration || history != _passiveFrameHistory ||
            observed.Count == 0) return false;

        var rows = new List<EvidenceRow>(observed.Count);
        foreach (var item in observed)
        {
            var projected = FeedPassiveFrameToExistingViews(item, inspectionOnly: true);
            if (projected is not null) rows.Add(projected);
        }
        var original = rows.FirstOrDefault(x =>
            x.Source.PhysicalStreamId == sid && x.Source.RawStreamOffset == offset &&
            Iec10xObservationIdentity.SamePhysicalFrame(x.Source, reference, source, protocol, 0));
        if (original is null) return false;

        // HOLD prevents auto-follow, not reception. The live writer continues
        // appending full history to disk while this bounded window is displayed.
        MainTabControl.SelectedIndex = 1;
        EnterLineMonitorHoldForUserSelection();
        _historicalTraceViewActive = true;
        FrameTraceRows.ReplaceRange(rows);
        _protocolTraceViewDirtyWhileFrozen = false;
        FrameTimelineCaption.Text = $"Session history · {rows.Count} frames";
        TraceOlderButton.Visibility = Visibility.Visible;
        TraceNewerButton.Visibility = Visibility.Visible;
        FrameTraceGrid.SelectedItems.Clear();
        FrameTraceGrid.SelectedItem = original;
        FrameTraceGrid.ScrollIntoView(original);
        ApplySelectedEvidenceRowToInspector(original);
        return true;
    }

    private async void TraceOlder_Click(object sender, RoutedEventArgs e)
    {
        if (FrameTraceRows.FirstOrDefault() is not { } first) return;
        await OpenAdjacentHistoryWindowAsync(first, before: 160, after: 0);
    }

    private async void TraceNewer_Click(object sender, RoutedEventArgs e)
    {
        if (FrameTraceRows.LastOrDefault() is not { } last) return;
        await OpenAdjacentHistoryWindowAsync(last, before: 0, after: 160);
    }

    private async Task OpenAdjacentHistoryWindowAsync(EvidenceRow anchor, int before, int after)
    {
        var ev = anchor.Source;
        if (ev.ProtocolMode != Iec60870ProtocolMode.Iec101) return;
        await TryOpenHistoryWindowAsync(ev.EvidenceReference, ev.PhysicalSource,
            ev.ProtocolMode, before, after);
    }
}
