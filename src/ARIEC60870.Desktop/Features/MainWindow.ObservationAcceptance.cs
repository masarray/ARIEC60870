// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ARIEC60870.Monitor;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop;

/// <summary>
/// Stage C acceptance of ACTUAL WPF controls on Windows, not an image mockup.
/// Uses synthetic receive-only wire frames without opening COM or TCP.
/// The screenshot evidence is a logical-viewport render, not an OS-DPI FAT.
/// </summary>
public partial class MainWindow
{
    public async Task RunObservationUiAcceptanceProbeAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var metrics = new List<object>();
        var constrainedViewportNames = new List<string>();
        var eventGridRealizedRows = 0;
        var interpretedPointRows = 0;
        var focusVisibleFrames = 0;
        var resetEvents = 0;
        var totalFrames = 0;
        var testStart = Stopwatch.StartNew();

        // The prior passive GUI + history smoke probes have already exercised
        // source-qualified navigation; this stage stress-tests the live WPF ring.
        MainTabControl.SelectedIndex = 1;
        ClearSessionView(clearLog: true);
        SetTraceSearchSurface(archive: false);
        TraceQuickFindBox.Clear();
        TraceQuickScopeCombo.SelectedIndex = 1; // All frames for 1,400-frame retention stress
        ApplyQuickFind();

        var baseline = DateTime.UtcNow;
        var bytes = new byte[] { 0x10, 0x5B, 0x69, 0xC4, 0x16 };
        for (var i = 0; i < 25; i++)
            FeedPassiveFrameToExistingViews(new PassiveObservedFrame(0, i * 5L, baseline.AddMilliseconds(i * 40d),
                "Fixed", true, bytes));
        FlushUiQueues();
        if (FrameTraceRows.Count < 1) throw new InvalidOperationException("Synthetic FT1.2 feed did not reach Trace.");
        NotifyCollectionChangedEventHandler observe = (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resetEvents++;
        };
        FrameTraceRows.CollectionChanged += observe;
        var appendAndFlush = Stopwatch.StartNew();
        try
        {
            // 1,400 frames in coalesced batches: no complete WPF view Reset
            // after the initial population, while the 1,200-row ring rolls.
            for (var i = 25; i < 1400; i++)
            {
                FeedPassiveFrameToExistingViews(new PassiveObservedFrame(0, i * 5L,
                    baseline.AddMilliseconds(i * 40d), "Fixed", true, bytes));
                if ((i + 1) % 25 == 0) FlushUiQueues();
            }
            FlushUiQueues();
        }
        finally { FrameTraceRows.CollectionChanged -= observe; }
        appendAndFlush.Stop();
        totalFrames = FrameTraceRows.Count;
        if (resetEvents != 0)
            throw new InvalidOperationException($"Live trace Reset events after initial population: {resetEvents}.");
        if (totalFrames < 1000 || totalFrames > 1200)
            throw new InvalidOperationException($"Unexpected bounded Trace count {totalFrames}.");

        // Real WPF CollectionView must agree with the displayed quick counter.
        ApplyQuickFind();
        AssertTraceCountInSync();
        TraceQuickFindBox.Text = "this-query-must-not-match-any-frame-293081";
        ApplyQuickFind();
        if (FrameTraceGrid.Items.Count != 0) throw new InvalidOperationException("Trace query did not filter.");
        AssertTraceCountInSync();
        TraceQuickFindBox.Clear();
        ApplyQuickFind();
        AssertTraceCountInSync();

        var selected = FrameTraceRows[Math.Max(0, FrameTraceRows.Count - 120)];
        FrameTraceGrid.SelectedItem = selected;
        EnterLineMonitorHoldForUserSelection();
        for (var i = 1400; i < 1430; i++)
            FeedPassiveFrameToExistingViews(new PassiveObservedFrame(0, i * 5L,
                baseline.AddMilliseconds(i * 40d), "Fixed", true, bytes));
        FlushUiQueues();
        if (!ReferenceEquals(FrameTraceGrid.SelectedItem, selected))
            throw new InvalidOperationException("Held source-qualified Trace selection jumped.");
        if (AutoScrollLatestToggleButton.IsChecked != false)
            throw new InvalidOperationException("Held Trace unexpectedly resumed follow.");
        ResumeLiveViews(clearSelection: true, followLive: true);

        // Scope switches never start an archive search or change raw evidence.
        var beforeArchiveSwitch = FrameTraceRows.Select(x => x.Source.EvidenceReference).ToArray();
        SetTraceSearchSurface(archive: true);
        if (TraceArchiveSearchBar.Visibility != Visibility.Visible ||
            TraceWindowSearchBar.Visibility != Visibility.Collapsed ||
            _recordedArchiveViewActive)
            throw new InvalidOperationException("Archive search surface is not isolated.");
        SetTraceSearchSurface(archive: false);
        if (TraceWindowSearchBar.Visibility != Visibility.Visible ||
            TraceArchiveSearchBar.Visibility != Visibility.Collapsed ||
            !beforeArchiveSwitch.SequenceEqual(FrameTraceRows.Select(x => x.Source.EvidenceReference)))
            throw new InvalidOperationException("Search-only scope toggle mutated Trace evidence.");

        // Seed real decoded SOE evidence after the Event tab becomes visible.
        // This reuses the known-good IEC-101 spontaneous Type 1 wire fixture
        // from the passive GUI smoke and traverses the production passive
        // parser/projection path; it never opens a transport or transmits.
        void SeedSpontaneousEventRows()
        {
        _passiveLinkAddressWidth = 1;
        _passiveAsduDecoder = new Iec10xAsduDecoder(2, 2, 3);
        for (var i = 0; i < 2; i++)
        {
            var eventAsdu = new byte[] { 1, 1, 3, 0, 1, 0, (byte)(7 + i), 0, 0, 1 };
            var eventPayload = new byte[] { 0x08, 0x69 }.Concat(eventAsdu).ToArray();
            var eventWire = new byte[] { 0x68, (byte)eventPayload.Length, (byte)eventPayload.Length, 0x68 }
                .Concat(eventPayload)
                .Concat(new byte[] { (byte)(eventPayload.Sum(x => x) & 0xff), 0x16 })
                .ToArray();
            FeedPassiveFrameToExistingViews(new PassiveObservedFrame(1, (1430 + i) * 5L,
                baseline.AddMilliseconds((1430 + i) * 40d), "Variable", true, eventWire));
        }
        FlushUiQueues();
        if (_relayEventStore.Snapshot().Count() < 2 || RelayEventRows.Count < 2)
            throw new InvalidOperationException("Spontaneous IEC-101 SOE test frames did not populate the Events store and WPF projection.");
        }

        // Representative decoded process data must be visible in screenshot and
        // selectable from the same real WPF frame row. Never accept empty Values
        // screenshots as proof of an observation-oriented protocol test set.
        MainTabControl.SelectedIndex = 3;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        SeedSpontaneousEventRows();
        var multiAsdu = new byte[] { 1, 2, 20, 0, 1, 0, 7, 0, 0, 1, 8, 0, 0, 0 };
        var multiPayload = new byte[] { 0x08, 0x69 }.Concat(multiAsdu).ToArray();
        var multiWire = new byte[] { 0x68, (byte)multiPayload.Length, (byte)multiPayload.Length, 0x68 }
            .Concat(multiPayload)
            .Concat(new byte[] { (byte)(multiPayload.Sum(x => x) & 0xff), 0x16 })
            .ToArray();
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(1, 1440 * 5L,
            baseline.AddMilliseconds(1440 * 40d), "Variable", true, multiWire));
        FlushUiQueues();
        // Source store accepts frames even while another workspace is active;
        // the virtualized Trace projection is reconciled only on return.
        var sourceMultiObjectCount = _protocolTraceStore.Snapshot().Count(x => x.Source.AsduDetails?.Objects.Count == 2);
        MainTabControl.SelectedIndex = 1;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        var multiObjectRow = FrameTraceRows.LastOrDefault(x => x.Source.AsduDetails?.Objects.Count == 2);
        if (sourceMultiObjectCount == 0 || multiObjectRow is null || ValueRows.Count < 2)
            throw new InvalidOperationException(
                $"Two-point process ASDU absent after Trace reconciliation: source={sourceMultiObjectCount}, " +
                $"view={FrameTraceRows.Count(x => x.Source.AsduDetails?.Objects.Count == 2)}, Values={ValueRows.Count}.");
        TraceQuickScopeCombo.SelectedIndex = 0; // operator focus, never delete underlying frames
        ApplyQuickFind();
        focusVisibleFrames = FrameTraceGrid.Items.Count;
        if (focusVisibleFrames < 3 || focusVisibleFrames >= FrameTraceRows.Count ||
            !FrameTraceGrid.Items.Contains(multiObjectRow))
            throw new InvalidOperationException("Activity focus did not preserve process/GI frames while filtering routine polls.");

        var layouts = new (string Name, double Width, double Height)[]
        {
            ("desktop-1540x960", 1540, 960),
            ("field-1366x768", 1366, 768),
            ("compact-1024x576", 1024, 576),
            ("nominal-150pct-911x512dip", 911, 512)
        };
        foreach (var viewport in layouts)
        {
            Width = viewport.Width;
            Height = viewport.Height;
            MainTabControl.SelectedIndex = 1;
            UpdateObservationViewportLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            UpdateLayout();
            UpdateObservationViewportLayout();
            UpdateLayout();

            if (CommandDockColumn.ActualWidth > 1 ||
                CommandDockPanel.Visibility != Visibility.Collapsed ||
                WorkspaceCommandButton.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Passive RX-only layout exposes active command controls.");
            if (FrameTraceGrid.ActualWidth < 300 || FrameTraceGrid.ActualHeight < 95)
                throw new InvalidOperationException($"Trace viewport too small at {viewport.Name}: {FrameTraceGrid.ActualWidth:0}x{FrameTraceGrid.ActualHeight:0}.");
            var shouldAutoCollapse = viewport.Width < 1150;
            if (shouldAutoCollapse && TraceInspectorColumn.ActualWidth > 1)
                throw new InvalidOperationException($"Inspector retained width on narrow viewport {viewport.Name}.");
            if (TraceWindowSearchBar.Visibility != Visibility.Visible ||
                TraceArchiveSearchBar.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Two Trace search surfaces are simultaneously displayed.");
            AssertTraceCountInSync();

            // GitHub-hosted runners may clamp top-level WPF windows to the
            // actual desktop work area. Recording a 1540x960 *request* is not
            // evidence that its bitmap was rendered at 1540x960.
            var actualWindowWidth = Math.Round(ActualWidth, 1);
            var actualWindowHeight = Math.Round(ActualHeight, 1);
            var requestedWindowSizeApplied =
                Math.Abs(actualWindowWidth - viewport.Width) <= 3 &&
                Math.Abs(actualWindowHeight - viewport.Height) <= 3;
            if (!requestedWindowSizeApplied)
                constrainedViewportNames.Add(viewport.Name);
            SaveRealWpfBitmap(outputDirectory, viewport.Name + "-trace.png");
            metrics.Add(new
            {
                viewport = viewport.Name,
                requestedWindowSizeApplied,
                actualWindowWidthDip = actualWindowWidth,
                actualWindowHeightDip = actualWindowHeight,
                requestedWidthDip = viewport.Width,
                requestedHeightDip = viewport.Height,
                actualClientWidthDip = Math.Round(((FrameworkElement)Content).ActualWidth, 1),
                actualClientHeightDip = Math.Round(((FrameworkElement)Content).ActualHeight, 1),
                traceWidthDip = Math.Round(FrameTraceGrid.ActualWidth, 1),
                traceHeightDip = Math.Round(FrameTraceGrid.ActualHeight, 1),
                inspectorWidthDip = Math.Round(TraceInspectorColumn.ActualWidth, 1),
                commandDockWidthDip = Math.Round(CommandDockColumn.ActualWidth, 1),
                actualWindowsDpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX,
                note = requestedWindowSizeApplied
                    ? "Requested WPF window dimensions realized. Not an OS scaling or visual FAT certificate."
                    : "CI desktop constrained actual WPF window dimensions. Screenshot does NOT certify the named requested viewport."
            });
        }

        // One frame ledger; verify each view displays its own bound data.
        Width = 1366;
        Height = 768;
        foreach (var view in new (int Index, string Name)[]
        {
            (2, "values"), (3, "events"), (11, "controls"), (12, "gi")
        })
        {
            MainTabControl.SelectedIndex = view.Index;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            UpdateLayout();
            if (view.Index == 2 && (ValueGrid.Items.Count < 2 || ValueRows.Count < 2))
                throw new InvalidOperationException("Values has no realized process data after valid decoded two-point ASDU.");
            if (view.Index == 3)
            {
                // A visible counter without any realized WPF event rows must
                // fail acceptance. The older Stage-C evidence showed 2/2
                // recent yet a visually empty Event Log.
                EventLogFilterComboBox.SelectedIndex = 0;
                EventsQuickFindBox.Clear();
                ApplyQuickFind();
                ApplyRelayEventFilter();
                // Validate retained SOE projection after leaving and returning
                // to Events, which previously showed a 2/2 count but blank rows.
                MainTabControl.SelectedIndex = 1;
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                MainTabControl.SelectedIndex = 3;
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                ApplyRelayEventFilter();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                UpdateLayout();
                if (RelayEventGrid.Items.Count != RelayEventRows.Count)
                    throw new InvalidOperationException(
                        $"Event bound grid count mismatch: {RelayEventGrid.Items.Count}/{RelayEventRows.Count}.");
                if (_relayEventStore.Snapshot().Count() < 2 || RelayEventRows.Count < 2)
                    throw new InvalidOperationException(
                        $"SOE lost on tab return: source={_relayEventStore.Snapshot().Count()}, projection={RelayEventRows.Count}, bound={RelayEventGrid.Items.Count}, filter={EventLogFilterComboBox.SelectedIndex}, query='{_eventsQuickQuery}'.");
                if (RelayEventRows.Count > 0)
                {
                    var lastEvent = RelayEventRows[RelayEventRows.Count - 1];
                    RelayEventGrid.ScrollIntoView(lastEvent);
                    await Dispatcher.Yield(DispatcherPriority.Loaded);
                    UpdateLayout();
                    if (RelayEventGrid.ItemContainerGenerator.ContainerFromItem(lastEvent) is not DataGridRow realized ||
                        !realized.IsVisible || realized.ActualHeight < 12)
                        throw new InvalidOperationException(
                            "Event count is nonzero but newest event has no visible realized WPF DataGridRow.");
                    eventGridRealizedRows = RelayEventGrid.Items.Count;
                }
            }
            SaveRealWpfBitmap(outputDirectory, "field-1366x768-" + view.Name + ".png");
        }
        MainTabControl.SelectedIndex = 1;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        TraceQuickScopeCombo.SelectedIndex = 0;
        _traceInspectorPreferredVisible = true;
        _traceInspectorForcedOpen = true;
        UpdateObservationViewportLayout();
        FrameTraceGrid.SelectedItem = multiObjectRow;
        FrameTraceGrid.ScrollIntoView(multiObjectRow);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        if (DecodedPointSection.Visibility != Visibility.Visible ||
            SelectedDecodedPoints.Count != 2 || DecodedPointGrid.Items.Count != 2 ||
            FrameInterpreterPanel.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Multi-object frame has no visible decoded IOA/value/quality inspector.");
        var lastPoint = SelectedDecodedPoints[1];
        DecodedPointGrid.ScrollIntoView(lastPoint);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        if (DecodedPointGrid.ItemContainerGenerator.ContainerFromItem(lastPoint) is not DataGridRow pointRow ||
            !pointRow.IsVisible || pointRow.ActualHeight < 12)
            throw new InvalidOperationException("Decoded point count exists but the second IOA row is not visible in WPF.");
        interpretedPointRows = DecodedPointGrid.Items.Count;
        SaveRealWpfBitmap(outputDirectory, "interpretive-trace-2-points-inspector.png");
        _traceInspectorForcedOpen = false;
        testStart.Stop();
        File.WriteAllText(Path.Join(outputDirectory, "metrics.json"),
            JsonSerializer.Serialize(new
            {
                baselineMaster = "OBS-P0 Stage C acceptance",
                totalSyntheticFrames = 1433,
                focusVisibleFrames,
                interpretedPointRows,
                retainedTraceRows = totalFrames,
                steadyStateCollectionResetEvents = resetEvents,
                syntheticAppendAndFlushMilliseconds = appendAndFlush.Elapsed.TotalMilliseconds,
                entireProbeMilliseconds = testStart.Elapsed.TotalMilliseconds,
                syntheticNoHardware = true,
                eventGridRealizedRows,
                allRequestedWindowSizesApplied = constrainedViewportNames.Count == 0,
                constrainedViewportNames,
                passed = true,
                passedScope = "Actual rendered CI window sizes, bounded collection/selection and visible Event row checks only. Constrained requested sizes and physical DPI require separate acceptance.",
                layoutSnapshots = metrics
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void AssertTraceCountInSync()
    {
        var prefix = $"{FrameTraceGrid.Items.Count} / {FrameTraceRows.Count} ";
        if (!TraceQuickCountText.Text.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Trace counter does not match actual WPF view: {TraceQuickCountText.Text}, expected {prefix}.");
    }

    private void SaveRealWpfBitmap(string output, string filename)
    {
        // Only inspection harness calls UpdateLayout; NEVER inside the hot UI flush.
        var visual = (FrameworkElement)Content;
        var width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        frame.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var file = File.Create(Path.Join(output, filename));
        encoder.Save(file);
    }
}
