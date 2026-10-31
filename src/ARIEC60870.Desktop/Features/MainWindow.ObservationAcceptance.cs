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
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
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

        // A forensic line monitor must NEVER start in a view that silently
        // omits Master TX polls while showing their RTU replies.
        if (TraceQuickScopeCombo.SelectedIndex != 1)
            throw new InvalidOperationException(
                "Trace must default to ALL TX/RX frames, never the lossy Focus view.");
        // Selected-tab capsule must not paint the old bright blue border.
        if (NavFrameButton.BorderBrush is SolidColorBrush navBorder &&
            navBorder.Color == (FindResource("AccentBrush") as SolidColorBrush)?.Color)
            throw new InvalidOperationException(
                "Selected segmented navigation retained the rejected accent border.");
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

        // Seed typed SOE twice: once before Values/Trace screenshots, then
        // on the live Events tab to prove new rows update an active grid.
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
        // Real passive IEC101 decode: Type13 spontaneous floating measurement
        // and Type15 spontaneous integrated totals. Each must enter Events;
        // GI/interrogated snapshots remain only in Values/GI.
        var measurement = new byte[] { 13, 1, 3, 0, 1, 0, 21, 0, 0, 0, 0, 0xB4, 0x41, 0 };
        var counter = new byte[] { 15, 1, 3, 0, 1, 0, 22, 0, 0, 0xD2, 4, 0, 0, 0 };
        var factOffset = 1433;
        foreach (var asdu in new[] { measurement, counter })
        {
            var payload = new byte[] { 0x08, 0x69 }.Concat(asdu).ToArray();
            var wire = new byte[] { 0x68, (byte)payload.Length, (byte)payload.Length, 0x68 }
                .Concat(payload)
                .Concat(new byte[] { (byte)(payload.Sum(x => x) & 0xff), 0x16 }).ToArray();
            FeedPassiveFrameToExistingViews(new PassiveObservedFrame(1, factOffset * 5L,
                baseline.AddMilliseconds(factOffset * 40d), "Variable", true, wire));
            factOffset++;
        }
        FlushUiQueues();
        if (_relayEventStore.Snapshot().Count() < 2 || RelayEventRows.Count < 2)
            throw new InvalidOperationException("Spontaneous IEC-101 SOE test frames did not populate the Events store and WPF projection.");
        if (!RelayEventRows.Any(row => row.TypeId == "13" && row.TypeBadge?.Label == "FLT") ||
            !RelayEventRows.Any(row => row.TypeId == "15" && row.TypeBadge?.Label == "BCR"))
            throw new InvalidOperationException(
                "Decoded IEC-101 spontaneous FLT and BCR badges are missing from Events.");
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
        // Explicit DP ON and OFF in the production IEC-101 decode/projection
        // path. The previous fixture only exercised SP; screenshots must prove
        // both visual types and two distinct state backgrounds.
        for (var i = 0; i < 2; i++)
        {
            var dpAsdu = new byte[] { 3, 1, 20, 0, 1, 0, (byte)(9 + i), 0, 0, (byte)(i == 0 ? 2 : 1) };
            var dpPayload = new byte[] { 0x08, 0x69 }.Concat(dpAsdu).ToArray();
            var dpWire = new byte[] { 0x68, (byte)dpPayload.Length, (byte)dpPayload.Length, 0x68 }
                .Concat(dpPayload)
                .Concat(new byte[] { (byte)(dpPayload.Sum(x => x) & 0xff), 0x16 })
                .ToArray();
            FeedPassiveFrameToExistingViews(new PassiveObservedFrame(1, (1441 + i) * 5L,
                baseline.AddMilliseconds((1441 + i) * 40d), "Variable", true, dpWire));
        }
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
        var txSourceCount = FrameTraceRows.Count(row => row.Direction == "TX");
        var txVisibleCount = FrameTraceGrid.Items.OfType<EvidenceRow>()
            .Count(row => row.Direction == "TX");
        if (focusVisibleFrames < 3 || txVisibleCount != txSourceCount ||
            !FrameTraceGrid.Items.Contains(multiObjectRow))
            throw new InvalidOperationException(
                $"Focus dropped an actual Master TX request or decoded ASDU: TX source={txSourceCount}, visible={txVisibleCount}.");
        // Screenshots demonstrate the truthful default ALL-FRAMES analyzer,
        // not a silently narrowed inspection view.
        TraceQuickScopeCombo.SelectedIndex = 1;
        ApplyQuickFind();

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
            if (!TraceAutoScrollButton.IsVisible ||
                TraceWindowSearchBar.Visibility != Visibility.Visible ||
                TraceArchiveSearchBar.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Two Trace search surfaces are simultaneously displayed.");
            AssertTraceCountInSync();
            AssertOneLineToolbar(TraceWindowSearchBar, TraceQuickCountText,
                TraceAutoScrollButton, FrameTraceGrid, "Trace", 9);
            AssertSearchFlushLeft(TraceWindowSearchBar, FrameTraceGrid, "Trace");

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

        // Visual field screenshots must demonstrate both ON and OFF states
        // rendered from source-backed IEC101 two-object values; no mock panels.
        if (TraceQuickScopeCombo.Visibility != Visibility.Collapsed ||
            TraceArchiveModeButton.Visibility != Visibility.Collapsed ||
            TraceInspectorToggle.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Trace wastes toolbar space on secondary actions/filters.");

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
            if (view.Index == 2)
            {
                AssertOneLineToolbar(ValuesQuickFindBox, ValuesQuickCountText,
                    null, ValueGrid, "Values", 11);
                AssertSearchFlushLeft(ValuesSearchShell, ValueGrid, "Values");
                if (ValueOpenFrameButton.Visibility != Visibility.Collapsed ||
                    ValueQuickScopeCombo.Visibility != Visibility.Collapsed ||
                    SelectedValueContextText.IsVisible ||
                    ValueGrid.Columns[1] is not DataGridTemplateColumn valueStateColumn ||
                    valueStateColumn.ClipboardContentBinding is not System.Windows.Data.Binding
                        { Path.Path: "OperatorValue" })
                    throw new InvalidOperationException("Values grid-first layout, status or clipboard contract missing.");
                if (ValueGrid.Items.Count < 2 || ValueRows.Count < 2)
                    throw new InvalidOperationException("Values has no realized process data after valid decoded two-point ASDU.");
                var target = ValueRows.FirstOrDefault(x => x.IoAddress == "8");
                var onPoint = ValueRows.FirstOrDefault(x => x.IoAddress == "7");
                var dpOn = ValueRows.FirstOrDefault(x => x.IoAddress == "9");
                var dpOff = ValueRows.FirstOrDefault(x => x.IoAddress == "10");
                if (onPoint?.DiscreteBadge != new DigitalStateBadge("SP", "ON") ||
                    target?.DiscreteBadge != new DigitalStateBadge("SP", "OFF") ||
                    dpOn?.DiscreteBadge != new DigitalStateBadge("DP", "ON") ||
                    dpOff?.DiscreteBadge != new DigitalStateBadge("DP", "OFF"))
                    throw new InvalidOperationException(
                        "Source-backed [SP] and [DP] ON/OFF state projections are absent from Values.");
                if (onPoint.TypeBadge?.Label != "SPS" || dpOn.TypeBadge?.Label != "DPS" ||
                    !ValueRows.Any(row => row.TypeBadge?.Label == "FLT") ||
                    !ValueRows.Any(row => row.TypeBadge?.Label == "BCR"))
                    throw new InvalidOperationException(
                        "Typed SPS/DPS/FLT/BCR presentation is incomplete in the real Values projection.");
                var valuesTsv = BuildTabSeparatedText(ValueGrid);
                if (!valuesTsv.Contains("SP=OFF", StringComparison.Ordinal) ||
                    !valuesTsv.Contains("DP=ON", StringComparison.Ordinal))
                    throw new InvalidOperationException("Grid badge template erased original source values from TSV export.");
                if (target is null)
                    throw new InvalidOperationException("IOA 8 unavailable for point investigation.");
                ValueGrid.SelectedItem = target;
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                if (!ValueOpenFrameButton.IsEnabled ||
                    !SelectedValueContextText.Text.Contains("IOA 8", StringComparison.Ordinal))
                    throw new InvalidOperationException("Selected Value has no source navigation/context.");
                ValueQuickScopeCombo.SelectedIndex = 2; // Unmapped, never guessed name
                if (ValueGrid.Items.Count < 2)
                    throw new InvalidOperationException("Unmapped filter hid real process values.");
                ValueQuickScopeCombo.SelectedIndex = 4; // Device time is not host time
                if (ValueGrid.Items.Count < 2)
                    throw new InvalidOperationException("Missing device timestamps were invented.");
                ValueQuickScopeCombo.SelectedIndex = 0;
                ValueGrid.SelectedItem = target;
                UpdateLayout();
                SaveRealWpfBitmap(outputDirectory, "point-investigation-values.png");
                ValueOpenFrame_Click(ValueOpenFrameButton, new RoutedEventArgs());
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                if (MainTabControl.SelectedIndex != 1 ||
                    FrameTraceGrid.SelectedItem is not EvidenceRow original ||
                    !Iec10xObservationIdentity.SamePhysicalFrame(original.Source, target.EvidenceReference,
                        target.PhysicalSource, Iec60870ProtocolMode.Iec101, 0) ||
                    DecodedPointGrid.SelectedItem is not DecodedPointRow selectedPoint ||
                    selectedPoint.Address != "IOA 8")
                    throw new InvalidOperationException("Point-to-Wire navigation lost original frame or IOA 8.");
                SaveRealWpfBitmap(outputDirectory, "point-investigation-original-frame.png");
                MainTabControl.SelectedIndex = 2;
                await Dispatcher.Yield(DispatcherPriority.Loaded);
            }
            if (view.Index == 11)
            {
                AssertOneLineToolbar(ControlsSearchShell, ControlsQuickCountText,
                    null, ControlCasesGrid, "Control cases", 9);
                AssertSearchFlushLeft(ControlsSearchShell, ControlCasesGrid, "Control cases");
                if (ControlCasesGrid.ContextMenu?.Items.Count < 2 ||
                    ControlCaseStagesGrid.ContextMenu?.Items.Count < 2)
                    throw new InvalidOperationException("Controls context actions missing.");
                if (ControlCasesGrid.ActualHeight < 100 || ControlCaseStagesGrid.ActualHeight < 70)
                    throw new InvalidOperationException("Controls case/stage grid lost usable workspace.");
            }
            if (view.Index == 12)
            {
                AssertOneLineToolbar(GISearchShell, GiQuickCountText,
                    null, GiCasesGrid, "GI cases", 9);
                AssertSearchFlushLeft(GISearchShell, GiCasesGrid, "GI cases");
                if (GiCasesGrid.ContextMenu?.Items.Count < 2 ||
                    GiStagesGrid.ContextMenu?.Items.Count < 2)
                    throw new InvalidOperationException("GI context actions missing.");
                if (GiCasesGrid.ActualHeight < 100 || GiStagesGrid.ActualHeight < 70)
                    throw new InvalidOperationException("GI case/stage grid lost usable workspace.");
            }
            if (view.Index == 3)
            {
                AssertOneLineToolbar(EventsQuickFindBox, EventsQuickCountText,
                    EventsAutoScrollButton, RelayEventGrid, "Events", 9);
                AssertSearchFlushLeft(EventsSearchShell, RelayEventGrid, "Events");
                if (!EventsAutoScrollButton.IsVisible ||
                    EventLogFilterComboBox.Visibility != Visibility.Visible ||
                    Grid.GetRow(RelayEventGrid) != 2 ||
                    RelayEventGrid.Columns[2] is not DataGridTemplateColumn eventStateColumn ||
                    eventStateColumn.ClipboardContentBinding is not System.Windows.Data.Binding
                        { Path.Path: "OperatorValue" })
                    throw new InvalidOperationException("Event Log compact toolbar/status/clipboard contract missing.");
                // A visible counter without any realized WPF event rows must
                // fail acceptance. The older Stage-C evidence showed 2/2
                // recent yet a visually empty Event Log.
                EventLogFilterComboBox.SelectedIndex = 0;
                EventsQuickFindBox.Clear();
                ApplyQuickFind();
                // Also exercise spontaneous arrivals while Events itself is
                // visible; the prior acceptance specifically protects this
                // live collection reconciliation/realization path.
                SeedSpontaneousEventRows();
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
                if (_eventProjectionFilteredCount != RelayEventRows.Count ||
                    _eventProjectionSourceCount < _eventProjectionFilteredCount ||
                    _eventProjectionVersion == 0 || _eventLiveResetCount != 0)
                    throw new InvalidOperationException(
                        $"Event projection invariant failure: v={_eventProjectionVersion}, source={_eventProjectionSourceCount}, filtered={_eventProjectionFilteredCount}, projected={RelayEventRows.Count}, resets={_eventLiveResetCount}.");
                if (RelayEventGrid.Items.Count != RelayEventRows.Count)
                    throw new InvalidOperationException(
                        $"Event bound grid count mismatch: {RelayEventGrid.Items.Count}/{RelayEventRows.Count}.");
                if (!BuildTabSeparatedText(RelayEventGrid).Contains("Single Point ON", StringComparison.Ordinal))
                    throw new InvalidOperationException("Event status text missing from tab-separated export.");
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
        TraceQuickScopeCombo.SelectedIndex = 1;
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
                totalSyntheticFrames = 1435,
                focusVisibleFrames,
                interpretedPointRows,
                pointToWireNavigationPassed = true,
                gridFirstToolbarPassed = true,
                searchAndAutoSameBaselinePassed = true,
                eventProjectionVersion = _eventProjectionVersion,
                eventProjectionEpoch = _eventProjectionEpoch,
                eventProjectionLiveResetCount = _eventLiveResetCount,
                eventProjectionMismatchCount = _eventProjectionMismatchCount,
                eventProjectionSourceCount = _eventProjectionSourceCount,
                eventProjectionFilteredCount = _eventProjectionFilteredCount,
                digitalStateBadgesPassed = true,
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

    /// <summary>
    /// Assert actual rendered WPF Y coordinates, not just shared Grid.Row in XAML:
    /// search, counter and follow toggle must have the SAME baseline and
    /// the data viewport must start immediately below a 32px toolbar.
    /// This prevents a future two-row regression hiding in a valid build.
    /// </summary>
    private void AssertOneLineToolbar(FrameworkElement search, FrameworkElement count,
        FrameworkElement? follow, FrameworkElement dataGrid, string workspace, double maxGap)
    {
        if (!search.IsVisible || !count.IsVisible || (follow is not null && !follow.IsVisible))
            throw new InvalidOperationException(workspace + " header control missing from rendered WPF.");
        static double MidY(FrameworkElement x, FrameworkElement window) =>
            x.TranslatePoint(new Point(0, x.ActualHeight / 2), window).Y;

        var searchY = MidY(search, this);
        var countY = MidY(count, this);
        var followY = follow is null ? searchY : MidY(follow, this);
        if (search.ActualWidth < 80 || search.ActualHeight < 20 ||
            Math.Abs(searchY - countY) > 3 ||
            Math.Abs(searchY - followY) > 3)
            throw new InvalidOperationException(
                $"{workspace} header not aligned in one row: search={searchY:0.0} count={countY:0.0} AUTO={followY:0.0}; width={search.ActualWidth:0}.");
        var headerBottom = search.TranslatePoint(new Point(0, search.ActualHeight), this).Y;
        var gridTop = dataGrid.TranslatePoint(new Point(0, 0), this).Y;
        if (gridTop - headerBottom > maxGap + 1 || gridTop < headerBottom)
            throw new InvalidOperationException(
                $"{workspace} header is consuming unexpected vertical space: bottom={headerBottom:0.0} grid-top={gridTop:0.0}.");
    }

    // A MaxWidth Border aligned Stretch inside a wide Grid column is
    // centered by WPF. This real-render assertion prevents Search drifting
    // to the middle of the header while the data grid remains flush left.
    private void AssertSearchFlushLeft(FrameworkElement searchShell, FrameworkElement grid,
        string workspace)
    {
        var searchLeft = searchShell.TranslatePoint(new Point(0, 0), this).X;
        var gridLeft = grid.TranslatePoint(new Point(0, 0), this).X;
        if (!searchShell.IsVisible || Math.Abs(searchLeft - gridLeft) > 3 ||
            searchShell.ActualWidth > 244)
            throw new InvalidOperationException(
                $"{workspace} Search not flush left with data grid: search={searchLeft:0.0}, grid={gridLeft:0.0}, width={searchShell.ActualWidth:0.0}.");
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
