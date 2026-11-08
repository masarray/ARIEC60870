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
        bool HeldSelectionSurvived,
        bool NewestFrameObserved,
        double FeedAndFlushMilliseconds,
        int InspectorSelectionsMeasured,
        double SelectionP50Milliseconds,
        double SelectionP95Milliseconds,
        double SelectionMaximumMilliseconds,
        bool SelectionP95Under100Milliseconds,
        string TimingScope);

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
            if ((i + 1) % batchSize == 0) FlushUiQueues();
        }
        FlushUiQueues();
        if (FrameTraceRows.Count != MaxVisibleFrameTraceRows)
            throw new InvalidOperationException("Initial 1,300 WPF stress frames did not fill the bounded live preview.");

        var held = FrameTraceRows[300];
        FrameTraceGrid.SelectedItem = held;
        FrameTraceGrid.ScrollIntoView(held);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        if (!ReferenceEquals(_selectedFrameRow, held) ||
            AutoScrollLatestToggleButton.IsChecked != false)
            throw new InvalidOperationException("Held frame did not pin inspector and disable automatic tail follow.");

        var resets = 0;
        NotifyCollectionChangedEventHandler monitor = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset) resets++;
        };
        FrameTraceRows.CollectionChanged += monitor;
        try
        {
            for (var i = 1300; i < framesToFeed; i++)
            {
                FeedPassiveFrameToExistingViews(new PassiveObservedFrame(
                    0, 5L * i, baseline.AddMilliseconds(40d * i),
                    "Fixed", true, fixedFrame));
                if ((i + 1) % batchSize == 0)
                {
                    FlushUiQueues();
                    if (FrameTraceRows.Count > MaxVisibleFrameTraceRows)
                        throw new InvalidOperationException("Live Frame Trace exceeded its bounded window.");
                }
            }
            FlushUiQueues();
        }
        finally
        {
            FrameTraceRows.CollectionChanged -= monitor;
        }
        feedClock.Stop();

        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        var heldSelectionSurvived = ReferenceEquals(FrameTraceGrid.SelectedItem, held) &&
            ReferenceEquals(_selectedFrameRow, held) && FrameTraceRows.Contains(held) &&
            TraceInspectorRawText.Text == held.RawHex;
        var newestFrameObserved =
            FrameTraceRows.LastOrDefault()?.Source.RawStreamOffset == 5L * (framesToFeed - 1);
        if (!heldSelectionSurvived || !newestFrameObserved || resets != 0 ||
            FrameTraceRows.Count != MaxVisibleFrameTraceRows)
            throw new InvalidOperationException(
                $"10,000-frame WPF rollover invalid: held={heldSelectionSurvived}, newest={newestFrameObserved}, " +
                $"reset={resets}, retained={FrameTraceRows.Count}.");

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
                TraceInspectorRawText.Text != row.RawHex ||
                SelectedFrameProperties.Count == 0)
                throw new InvalidOperationException(
                    $"Stale inspector or lost source HEX after selection sample {sample}.");
        }
        Array.Sort(samples);
        var p50 = samples[(int)Math.Ceiling(samples.Length * 0.50) - 1];
        var p95 = samples[(int)Math.Ceiling(samples.Length * 0.95) - 1];

        SaveRealWpfBitmap(outputDirectory, "engineer-trace-10000-frames.png");
        return new FrameTraceLoadMetrics(
            framesToFeed, FrameTraceRows.Count, resets,
            heldSelectionSurvived, newestFrameObserved,
            Math.Round(feedClock.Elapsed.TotalMilliseconds, 2),
            samples.Length, Math.Round(p50, 2), Math.Round(p95, 2),
            Math.Round(samples[^1], 2), p95 < 100,
            "Synthetic 10,000 original fixed FT1.2 frames in 100-frame UI batches; " +
            "150 deterministic WPF selection/ScrollIntoView/Loaded-layout samples at actual CI viewport. " +
            "Hardware RX rates, 50,000-frame full WPF capture and physical DPI are not measured.");
    }
}
