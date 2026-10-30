// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
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
        => Iec10xObservationIdentity.TryParsePassiveFrameReference(reference, out stream, out offset);

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


    /// <summary>Real WPF session-history smoke without opening any physical COM.</summary>
    public async Task RunPassiveHistoryGuiSmokeProbeAsync()
    {
        ReleaseSessionTraceHistory();
        var history = new PassiveSessionTraceHistory(2);
        _passiveFrameHistory = history;
        try
        {
            _passiveLinkAddressWidth = 1;
            _passiveAsduDecoder = new ARIEC60870.Master.Protocol.Iec10x.Iec10xAsduDecoder(2, 2, 3);
            var now = DateTime.UtcNow;
            static byte[] ControlWire(byte control, byte cot)
            {
                // IEC101 CA105, IOA68539, TI46, DCS=ON + SELECT.
                var asdu = new byte[] { 46, 1, cot, 0, 105, 0, 0xBB, 0x0B, 0x01, 0x82 };
                var body = new byte[] { control, 0x69 }.Concat(asdu).ToArray();
                return new byte[] { 0x68, (byte)body.Length, (byte)body.Length, 0x68 }
                    .Concat(body).Concat(new byte[] { (byte)(body.Sum(x => x) & 0xFF), 0x16 })
                    .ToArray();
            }
            // More than the 1,200-row WPF ring, with independent
            // physical stream offsets and the real field-case identity.
            for (var i = 0; i < 1800; i++)
            {
                var when = now.AddMilliseconds(i * 20d);
                history.Append(new PassiveObservedFrame(0, i * 6L + 12, when,
                    i == 635 ? "Variable" : "Fixed", true,
                    i == 635 ? ControlWire(0x53, 6) :
                        new byte[] { 0x10, 0x5B, 0x69, 0xC4, 0x16 }));
                history.Append(new PassiveObservedFrame(1, i * 6L, when.AddMilliseconds(9),
                    i == 635 ? "Variable" : "Single", true,
                    i == 635 ? ControlWire(0x08, 0x47) : new byte[] { 0xE5 }));
            }
            if (history.FrameCount != 3600)
                throw new InvalidOperationException("The temporary index lost frames.");

            _protocolTraceStore.Clear();
            if (!await TryOpenHistoryWindowAsync("101|stream:0|byte:3822|object:*",
                "Link A", Iec60870ProtocolMode.Iec101) ||
                FrameTraceGrid.SelectedItem is not EvidenceRow select ||
                select.Source.PhysicalStreamId != 0 || select.Source.RawStreamOffset != 3822 ||
                select.Source.TypeId != 46 || select.Source.CauseOfTransmission != 6 ||
                !FrameTraceRows.Any(x => x.Source.PhysicalStreamId == 1 &&
                    x.Source.RawStreamOffset == 3810))
                throw new InvalidOperationException("Wildcard SELECT failed exact TX/RX navigation.");

            if (!await TryOpenHistoryWindowAsync("101|stream:1|byte:3810|object:1",
                "Link A", Iec60870ProtocolMode.Iec101) ||
                MainTabControl.SelectedIndex != 1 || !_historicalTraceViewActive ||
                FrameTraceGrid.SelectedItem is not EvidenceRow reject ||
                reject.Source.PhysicalStreamId != 1 || reject.Source.RawStreamOffset != 3810 ||
                reject.Source.TypeId != 46 || reject.Source.CauseOfTransmission != 7 ||
                FrameTraceRows.Count < 2 ||
                !FrameTraceRows.Any(x => x.Source.PhysicalStreamId == 0 &&
                    x.Source.RawStreamOffset == 3822) ||
                AutoScrollLatestToggleButton.IsChecked != false)
                throw new InvalidOperationException("Negative ACTCON failed exact RX/TX navigation.");

            var visibleCount = FrameTraceRows.Count;
            var frameSequence = _passiveFrameSequence;
            // New live traffic must leave a deliberately browsed page stable.
            FeedPassiveFrameToExistingViews(new PassiveObservedFrame(1, 15000,
                now.AddMinutes(2), "Single", true, new byte[] { 0xE5 }));
            FlushUiQueues();
            if (FrameTraceRows.Count != visibleCount ||
                _passiveFrameSequence != frameSequence + 1)
                throw new InvalidOperationException("Live RX altered the held history window.");
            ResumeLiveViews(clearSelection: true, followLive: true);
            if (_historicalTraceViewActive || AutoScrollLatestToggleButton.IsChecked != true ||
                !FrameTraceRows.Any(x => x.Source.RawStreamOffset == 15000 &&
                    x.Source.PhysicalStreamId == 1))
                throw new InvalidOperationException("Resume failed to restore live Trace.");
        }
        finally { ReleaseSessionTraceHistory(); }
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
