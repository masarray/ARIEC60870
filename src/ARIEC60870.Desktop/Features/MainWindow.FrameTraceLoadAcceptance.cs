// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Monitor;

namespace ARIEC60870.Desktop;

/// <summary>
/// Real Windows WPF load acceptance for the existing two-panel Engineer Mode.
/// Runs only via the explicit --observation-ui-acceptance test entry point.
/// Never opens COM, transmits a frame, or changes production frame retention.
/// </summary>
public partial class MainWindow
{
    private sealed record FrameTraceLoadMetrics(
        int SourceFramesFed,
        int RetainedRows,
        int RollingCollectionResets,
        int HeldCollectionMutations,
        bool SourceAdvancedWhileHeld,
        bool AutoFollowTailRealized,
        bool HeldSelectionSurvived,
        bool NewestFrameObserved,
        double FeedAndFlushMilliseconds,
        int InspectorSelectionsMeasured,
        double SelectionP50Milliseconds,
        double SelectionP95Milliseconds,
        double SelectionMaximumMilliseconds,
        bool SelectionP95Under100Milliseconds,
        string TimingScope);

    private void DrainFrameTraceLoadBatch()
    {
        // Live production intentionally consumes only a bounded number of
        // queued frames per dispatcher tick. Test ingestion can be faster than
        // those ticks; wait for the existing budgeted UI path to consume every
        // source frame before checking visible counts. Do not bypass the
        // queue or raise the production budget just to satisfy acceptance.
        var ticks = 0;
        while (!_pendingEvidence.IsEmpty && ticks++ < 32)
            FlushUiQueues();
        if (!_pendingEvidence.IsEmpty)
            throw new InvalidOperationException(
                $"WPF test queue failed to drain after {ticks} bounded flushes: {_pendingEvidence.Count} pending.");
    }

    private async Task<FrameTraceLoadMetrics> MeasureFrameTraceUnderLoadAsync(string outputDirectory)
    {
        const int framesToFeed = 10_000;
        const int batchSize = 100;
        const int selectionsToMeasure = 150;
        var baseline = DateTime.UtcNow;
        byte[] fixedFrame = [0x10, 0x5B, 0x69, 0xC4, 0x16];

        // Separate the perf fixture from prior GI, Event and epoch fixtures.
        // All source-specific assertions above have already run.
        MainTabControl.SelectedIndex = 1;
        ClearSessionView(clearLog: false);
        SetTraceSearchSurface(archive: false);
        TraceQuickFindBox.Clear();
        TraceQuickScopeCombo.SelectedIndex = 1; // All frames, no hidden filter
        ApplyQuickFind();

        var feedClock = Stopwatch.StartNew();
        for (var i = 0; i < 1300; i++)
        {
            FeedPassiveFrameToExistingViews(new PassiveObservedFrame(
                0, 5L * i, baseline.AddMilliseconds(40d * i),
                "Fixed", true, fixedFrame));
            if ((i + 1) % batchSize == 0) DrainFrameTraceLoadBatch();
        }
        DrainFrameTraceLoadBatch();
        if (FrameTraceRows.Count != MaxVisibleFrameTraceRows)
            throw new InvalidOperationException(
                $"Initial 1,300 frames did not fill the live preview: visible={FrameTraceRows.Count}, " +
                $"source={_protocolTraceStore.Count}, queued={_pendingEvidence.Count}, activeTab={MainTabControl.SelectedIndex}.");

        // First exercise continuous AUTO after the live ring is full.
        // A source sample must reach the actual WPF tail without a Reset.
        var resets = 0;
        var heldCollectionMutations = 0;
        NotifyCollectionChangedEventHandler monitor = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset) resets++;
        };
        FrameTraceRows.CollectionChanged += monitor;
        var autoFollowTailRealized = false;
        bool sourceAdvancedWhileHeld;
        bool heldSelectionSurvived;
        bool newestFrameObserved;
        try
        {
            for (var i = 1300; i < 1500; i++)
            {
                FeedPassiveFrameToExistingViews(new PassiveObservedFrame(
                    0, 5L * i, baseline.AddMilliseconds(40d * i),
                    "Fixed", true, fixedFrame));
                if ((i + 1) % batchSize == 0) DrainFrameTraceLoadBatch();
            }
            DrainFrameTraceLoadBatch();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            UpdateLayout();
            var lastAuto = FrameTraceRows.LastOrDefault();
            autoFollowTailRealized = lastAuto is not null &&
                lastAuto.Source.RawStreamOffset == 5L * 1499 &&
                FrameTraceGrid.ItemContainerGenerator.ContainerFromItem(lastAuto)
                    is ListBoxItem { IsVisible: true, ActualHeight: > 0 };
            if (!autoFollowTailRealized || resets != 0 ||
                FrameTraceRows.Count != MaxVisibleFrameTraceRows)
                throw new InvalidOperationException(
                    $"AUTO rollover failed to realize the newest physical frame: " +
                    $"realized={autoFollowTailRealized}, resets={resets}, shown={FrameTraceRows.Count}.");

            var held = FrameTraceRows[300];
            FrameTraceGrid.SelectedItem = held;
            FrameTraceGrid.ScrollIntoView(held);
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            UpdateLayout();
            if (!ReferenceEquals(_selectedFrameRow, held) ||
                AutoScrollLatestToggleButton.IsChecked != false)
                throw new InvalidOperationException(
                    "Held frame did not pin inspector and disable automatic tail follow.");

            var heldWindow = FrameTraceRows.ToArray();
            var heldContainer =
                FrameTraceGrid.ItemContainerGenerator.ContainerFromItem(held) as ListBoxItem;
            var heldY = heldContainer?.TranslatePoint(new Point(), FrameTraceGrid).Y
                ?? double.NaN;
            NotifyCollectionChangedEventHandler heldMonitor = (_, _) => heldCollectionMutations++;
            FrameTraceRows.CollectionChanged += heldMonitor;
            try
            {
                for (var i = 1500; i < framesToFeed; i++)
                {
                    FeedPassiveFrameToExistingViews(new PassiveObservedFrame(
                        0, 5L * i, baseline.AddMilliseconds(40d * i),
                        "Fixed", true, fixedFrame));
                    if ((i + 1) % batchSize == 0)
                        DrainFrameTraceLoadBatch();
                }
                DrainFrameTraceLoadBatch();
            }
            finally { FrameTraceRows.CollectionChanged -= heldMonitor; }

            await Dispatcher.Yield(DispatcherPriority.Loaded);
            UpdateLayout();
            var heldYAfter = (FrameTraceGrid.ItemContainerGenerator.ContainerFromItem(held)
                as ListBoxItem)?.TranslatePoint(new Point(), FrameTraceGrid).Y ?? double.NaN;
            heldSelectionSurvived = ReferenceEquals(FrameTraceGrid.SelectedItem, held) &&
                ReferenceEquals(_selectedFrameRow, held) && FrameTraceRows.Contains(held) &&
                TraceInspectorRawText.Text == held.TimelineHex &&
                double.IsFinite(heldY) && double.IsFinite(heldYAfter) &&
                Math.Abs(heldYAfter - heldY) <= 1.25;
            sourceAdvancedWhileHeld = _protocolTraceStore.Snapshot().LastOrDefault()?
                .Source.RawStreamOffset == 5L * (framesToFeed - 1) &&
                _protocolTraceRowsDeferredWhileFrozen >= framesToFeed - 1500;
            if (!heldSelectionSurvived || !sourceAdvancedWhileHeld ||
                heldCollectionMutations != 0 || !FrameTraceRows.SequenceEqual(heldWindow) ||
                FrameTraceGrid.Items.Count != heldWindow.Length ||
                resets != 0)
                throw new InvalidOperationException(
                    $"10,000-frame HOLD viewport instability: selected={heldSelectionSurvived}, " +
                    $"sourceLatest={sourceAdvancedWhileHeld}, WPF mutations={heldCollectionMutations}, " +
                    $"resets={resets}, shown={FrameTraceRows.Count}.");

            // Explicit AUTO is one catch-up to the bounded source ring, never a
            // series of per-frame redraws while the user was examining HOLD.
            ResumeLiveViews(clearSelection: true, followLive: true);
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            UpdateLayout();
            newestFrameObserved =
                FrameTraceRows.LastOrDefault()?.Source.RawStreamOffset == 5L * (framesToFeed - 1);
            if (!newestFrameObserved || resets != 0 ||
                FrameTraceRows.Count != MaxVisibleFrameTraceRows ||
                _protocolTraceRowsDeferredWhileFrozen != 0 ||
                _protocolTraceViewDirtyWhileFrozen)
                throw new InvalidOperationException(
                    $"AUTO resume failed to reconcile latest source: newest={newestFrameObserved}, " +
                    $"resets={resets}, retained={FrameTraceRows.Count}.");
        }
        finally { FrameTraceRows.CollectionChanged -= monitor; }
        feedClock.Stop();

        // Measure a real selection + ScrollIntoView + Loaded layout for every
        // sample. Do not claim the UI is under 100ms merely from a green CI.
        // Deterministic varied rows exercise virtualization and inspector decode.
        var samples = new double[selectionsToMeasure];
        for (var sample = 0; sample < selectionsToMeasure; sample++)
        {
            var row = FrameTraceRows[(sample * 53 + 127) % FrameTraceRows.Count];
            var started = Stopwatch.GetTimestamp();
            FrameTraceGrid.SelectedItem = row;
            FrameTraceGrid.ScrollIntoView(row);
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            UpdateLayout();
            samples[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (!ReferenceEquals(_selectedFrameRow, row) ||
                SelectedLineSummaryText.Text != FrameInspectorFieldPresentation.HumanSummary(row) ||
                TraceInspectorRawText.Text != row.TimelineHex ||
                (FrameInterpreterPanel.Visibility == Visibility.Visible &&
                 SelectedFrameProperties.Count == 0))
                throw new InvalidOperationException(
                    $"Stale inspector or lost source HEX after selection sample {sample}.");
        }
        // Exercise the operator's on-demand reveal after fast, collapsed
        // selection: Full Properties must be exact for the latest frame.
        var lastSelected = FrameTraceGrid.SelectedItem as EvidenceRow
            ?? throw new InvalidOperationException("Selection vanished before dock reopen.");
        TraceFrameDetails_Click(this, new RoutedEventArgs());
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        if (FrameInterpreterPanel.Visibility != Visibility.Visible ||
            SelectedFrameProperties.FirstOrDefault(x => x.Field == "Observed frame number")?.Value
                != lastSelected.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            TraceInspectorRawText.Text != lastSelected.TimelineHex)
            throw new InvalidOperationException("Lazy Full Properties did not hydrate the latest selection.");
        TraceFrameDetailsClose_Click(this, new RoutedEventArgs());
        if (SelectedFrameProperties.Count != 0 || SelectedDecodedPoints.Count != 0)
            throw new InvalidOperationException("Collapsed inspector retained expensive UI projections.");

        Array.Sort(samples);
        var p50 = samples[(int)Math.Ceiling(samples.Length * 0.50) - 1];
        var p95 = samples[(int)Math.Ceiling(samples.Length * 0.95) - 1];

        // Quick Find counters are intentionally coalesced at Background
        // priority rather than recomputed for every Add/Remove. The synthetic
        // probe previously rendered at Loaded priority before that queued
        // refresh could execute, producing a misleading "0 / 0" screenshot.
        // Require the same eventual consistency a real idle UI promises.
        await Dispatcher.Yield(DispatcherPriority.Background);
        AssertTraceCountInSync();
        SaveRealWpfBitmap(outputDirectory, "engineer-trace-10000-frames.png");
        return new FrameTraceLoadMetrics(
            framesToFeed, FrameTraceRows.Count, resets,
            heldCollectionMutations, sourceAdvancedWhileHeld, autoFollowTailRealized,
            heldSelectionSurvived, newestFrameObserved,
            Math.Round(feedClock.Elapsed.TotalMilliseconds, 2),
            samples.Length, Math.Round(p50, 2), Math.Round(p95, 2),
            Math.Round(samples[^1], 2), p95 < 100,
            "Synthetic 10,000 original fixed FT1.2 frames in 100-frame UI batches; " +
            "AUTO rollover must realize the live tail; HOLD must produce zero WPF collection mutations " +
            "while source continues receiving; AUTO must catch up without Reset. " +
            "150 deterministic WPF selection/ScrollIntoView/Loaded-layout samples at actual CI viewport. " +
            "Hardware RX rates, 50,000-frame full WPF capture and physical DPI are not measured.");
    }
}
