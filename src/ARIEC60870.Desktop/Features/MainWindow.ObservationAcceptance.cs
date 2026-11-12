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
using ARIEC60870.Core.Model;
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
    private static bool HasVisibleRawLine(DependencyObject node, string expected)
    {
        if (node is TextBlock text && text.Text == expected &&
            text.Visibility == Visibility.Visible && text.IsVisible)
            return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (HasVisibleRawLine(VisualTreeHelper.GetChild(node, i), expected))
                return true;
        return false;
    }

    private static bool IsTraceMonospace(TextBlock text) =>
        text.FontFamily.Source.Contains("Cascadia Mono", StringComparison.Ordinal) &&
        text.FontFamily.Source.Contains("Consolas", StringComparison.Ordinal);

    private static TextBlock? FindVisibleTraceText(DependencyObject node, string expected)
    {
        if (node is TextBlock text && text.Text == expected &&
            text.Visibility == Visibility.Visible && text.IsVisible)
            return text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var found = FindVisibleTraceText(VisualTreeHelper.GetChild(node, i), expected);
            if (found is not null) return found;
        }
        return null;
    }

    private static bool HasVisibleForensicHeading(DependencyObject node, string expected)
    {
        if (node is TextBlock text && text.Text == expected &&
            text.Visibility == Visibility.Visible && text.IsVisible)
            return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (HasVisibleForensicHeading(VisualTreeHelper.GetChild(node, i), expected))
                return true;
        return false;
    }

    private static ScrollViewer? FindTraceScroller(DependencyObject node)
    {
        if (node is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var found = FindTraceScroller(VisualTreeHelper.GetChild(node, i));
            if (found is not null) return found;
        }
        return null;
    }

    private static bool HasTraceInk(DependencyObject node, string text, string ink)
    {
        if (node is TextBlock block && block.Text == text &&
            block.Foreground is SolidColorBrush brush &&
            string.Equals(brush.Color.ToString(), "#FF" + ink.TrimStart('#'),
                StringComparison.OrdinalIgnoreCase))
            return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (HasTraceInk(VisualTreeHelper.GetChild(node, i), text, ink))
                return true;
        return false;
    }

    private static PassiveFramePreparation.Prepared AssertSinglePreparedForUi(
        PassiveObservedFrame observed) =>
        PassiveFramePreparation.PrepareBatch([observed], 1, 2, 2, 3)[0];

    /// <summary>
    /// Windows-only real WPF proof of Setup switch geometry; the prior large
    /// product mockup is not authoritative. Do not open COM or change settings.
    /// </summary>
    private async Task VerifySetupSwitchGeometryAsync(string outputDirectory)
    {
        var previousMode = ProtocolModeComboBox.SelectedIndex;
        var previousVisibility = SetupOverlay.Visibility;
        try
        {
            SetupOverlay.Visibility = Visibility.Visible;
            // Active IEC-101 settings use the same actual CheckBox instances as
            // normal operation; verify rendered geometry rather than XAML text.
            ProtocolModeComboBox.SelectedIndex = 1;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            UpdateLayout();
            VerifyVisibleSetupSwitches(
            [
                SerialRtsCheckBox, SerialDtrCheckBox,
                ResetRemoteLinkCheckBox, ResetFcbCheckBox,
                Class2StartupCheckBox, ClockSyncCheckBox, GiCheckBox
            ]);
            // Clean IEC-101 profiles must show Reset Remote Link ON before Connect.
            // A previously saved operator OFF is intentionally authoritative.
            if (_iec101ResetRemoteLinkOperatorOverride is null &&
                ResetRemoteLinkCheckBox.IsChecked != true)
                throw new InvalidOperationException(
                    "Fresh IEC-101 startup must show Reset Remote Link ON.");
            SaveRealWpfBitmap(outputDirectory, "setup-iec101-compact-switches.png");

            // Passive mode keeps the identical style and Checked/Unchecked
            // event handlers. Ensure both conditional switches align too.
            ProtocolModeComboBox.SelectedIndex = 3;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            UpdateLayout();
            VerifyVisibleSetupSwitches(
            [
                PassiveLinkBEnabledCheckBox, PassiveFullRecordingCheckBox
            ]);
            SaveRealWpfBitmap(outputDirectory, "setup-passive-compact-switches.png");
        }
        finally
        {
            ProtocolModeComboBox.SelectedIndex = previousMode;
            SetupOverlay.Visibility = previousVisibility;
            UpdateLayout();
        }
    }

    private static void VerifyVisibleSetupSwitches(CheckBox[] switches)
    {
        foreach (var control in switches)
        {
            control.ApplyTemplate();
            if (control.Template.FindName("SwitchTrack", control) is not Border track ||
                control.Template.FindName("SwitchThumb", control) is not System.Windows.Shapes.Ellipse thumb ||
                control.Template.FindName("SwitchCaption", control) is not ContentPresenter caption ||
                !control.IsVisible)
                throw new InvalidOperationException(
                    $"Setup switch {control.Name} is not visible or its template is incomplete.");

            var trackTopLeft = track.TranslatePoint(new Point(), control);
            var captionTopLeft = caption.TranslatePoint(new Point(), control);
            var centerY = trackTopLeft.Y + track.ActualHeight / 2;
            var captionY = captionTopLeft.Y + caption.ActualHeight / 2;
            var captionGap = captionTopLeft.X - trackTopLeft.X - track.ActualWidth;

            if (Math.Abs(track.ActualWidth - 28) > 0.6 ||
                Math.Abs(track.ActualHeight - 16) > 0.6 ||
                Math.Abs(thumb.ActualWidth - 10) > 0.6 ||
                Math.Abs(thumb.ActualHeight - 10) > 0.6 ||
                Math.Abs(centerY - captionY) > 1.5 ||
                captionGap < 5 || captionGap > 9)
                throw new InvalidOperationException(
                    $"Setup switch {control.Name} geometry: track={track.ActualWidth:0.0}x{track.ActualHeight:0.0}, " +
                    $"thumb={thumb.ActualWidth:0.0}x{thumb.ActualHeight:0.0}, " +
                    $"vertical offset={Math.Abs(centerY-captionY):0.00}, caption gap={captionGap:0.0}.");
        }
    }

    public async Task RunObservationUiAcceptanceProbeAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        await VerifySetupSwitchGeometryAsync(outputDirectory);
        var metrics = new List<object>();
        var constrainedViewportNames = new List<string>();
        var eventGridRealizedRows = 0;
        var interpretedPointRows = 0;
        var focusVisibleFrames = 0;
        var resetEvents = 0;
        var totalFrames = 0;
        var testStart = Stopwatch.StartNew();
        // The executable acceptance first runs two other GUI smoke probes in
        // the SAME MainWindow. Simulate the actual new passive Start boundary:
        // clear their historical fixture frames before testing zero-RX UX.
        ClearSessionView(clearLog: false);
        // Engineer observation starts with full-width raw+decoded trace,
        // while the optional right properties panel is folded to the edge.
        if (_traceInspectorPreferredVisible ||
            FrameInterpreterPanel.Visibility != Visibility.Collapsed ||
            TraceInspectorColumn.Width.Value > 0 ||
            TraceInspectorToggle.Visibility != Visibility.Visible)
            throw new InvalidOperationException(
                "The optional Frame Details panel is not fully collapsed on startup.");
        if (FrameTraceRows.Count != 0 || FrameTraceGrid.Items.Count != 0)
            throw new InvalidOperationException("New capture scope retained earlier smoke-probe frames.");
        // Field-empty-state acceptance: display wire counters and distinguish
        // silent COM from a WPF rendering failure on the actual Raw Frames view.
        UpdatePassiveTraceEmptyState(new PassiveMonitorProgress(
            "Monitoring", "", 0, 0, 0, 0, 0, 0, false), "COM4", "COM3", "");
        if (TracePassiveEmptyState.Visibility != Visibility.Visible ||
            !TracePassiveEmptyTitleText.Text.Contains("No UART bytes", StringComparison.Ordinal) ||
            !TracePassiveEmptyDetailText.Text.Contains("COM4", StringComparison.Ordinal))
            throw new InvalidOperationException("Silent passive capture has no visible serial diagnosis.");
        // Verified reader receipt must not be misdiagnosed as a silent line
        // merely because the shared writer has not committed its queue.
        UpdatePassiveTraceEmptyState(new PassiveMonitorProgress(
            "Monitoring", "", 0, 0, 0, 0, 0, 0, false,
            ReaderScadaBytes: 2), "COM4", "COM3", "");
        if (!TracePassiveEmptyTitleText.Text.Contains("writer pipeline pending", StringComparison.Ordinal) ||
            !TracePassiveSourceStatusText.Text.Contains("COM4 2", StringComparison.Ordinal))
            throw new InvalidOperationException("RX reader activity was mistaken for a silent UART.");
        // Direct UART samples must be displayed without a completed frame.
        _passiveRawRows.Add(new PassiveRxRow("16:31:01.200", "A RX0",
            1, 2, "680C"));
        UpdatePassiveTraceEmptyState(new PassiveMonitorProgress(
            "Monitoring", "", 2, 0, 1, 0, 0, 0, false,
            IncompleteFrameBytes: 2), "COM4", "COM3", "");
        if (TracePassiveRawChunkGrid.Visibility != Visibility.Visible ||
            TracePassiveRawChunkGrid.Items.Count != 1 ||
            !((PassiveRxRow)TracePassiveRawChunkGrid.Items[0]).Hex.Equals("680C", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Actual UART RX bytes were hidden because no FT1.2 frame had completed.");
        _passiveRawRows.Clear();
        TracePassiveEmptyState.Visibility = Visibility.Collapsed;

        // A private IEC-101 Type with a valid FT1.2 checksum must appear as
        // vendor-specific raw evidence in the SAME Trace as valid frames.
        // It must NEVER create fabricated process Values/SOE, and must not
        // be collapsed into an invalid/unknown frame after decoder hardening.
        var unknownAsdu = new byte[]
            { 0xFA, 0x01, 0x03, 0x00, 0x69, 0x00, 0x01, 0x00, 0x00, 0x42 };
        var unknownBody = new byte[] { 0x08, 0x69 }.Concat(unknownAsdu).ToArray();
        var unknownWire = new byte[] { 0x68, (byte)unknownBody.Length,
                (byte)unknownBody.Length, 0x68 }
            .Concat(unknownBody)
            .Concat(new byte[] { (byte)(unknownBody.Sum(x => x) & 255), 0x16 })
            .ToArray();
        var unknownObserved = new PassiveObservedFrame(1, 407, DateTime.UtcNow,
            "Variable", true, unknownWire);
        var unknownPrepared = AssertSinglePreparedForUi(unknownObserved);
        FeedPassiveFrameToExistingViews(unknownObserved, prepared: unknownPrepared);
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(0, 408,
            DateTime.UtcNow, "Noise", false, [0x51, 0x52, 0x53]));
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(1, 411,
            DateTime.UtcNow, "Partial", false, [0x68, 0x0C]));
        FlushUiQueues();
        var vendorOpaqueVisible = FrameTraceRows.Any(x =>
            x.RawHex == Convert.ToHexString(unknownWire) &&
            x.Source.AsduDetails is { IsOpaqueVendorSpecific: true, TypeId: 250 } &&
            x.Source.OperatorMessage.Contains("Vendor-specific IEC-101 TI 250", StringComparison.Ordinal) &&
            x.Source.OperatorMessage.Contains("payload opaque", StringComparison.Ordinal));
        var noiseVisible = FrameTraceRows.Any(x =>
            x.RawHex == "515253" &&
            x.Source.OperatorMessage.Contains("Unrecognized serial bytes", StringComparison.Ordinal));
        var partialVisible = FrameTraceRows.Any(x =>
            x.RawHex == "680C" &&
            x.Source.OperatorMessage.Contains("Partial FT1.2", StringComparison.Ordinal));
        if (!vendorOpaqueVisible || !noiseVisible || !partialVisible ||
            ValueRows.Count != 0 || RelayEventRows.Count != 0)
            throw new InvalidOperationException(
                $"Wire-first Trace mismatch: vendorOpaque={vendorOpaqueVisible}, noise={noiseVisible}, " +
                $"partial={partialVisible}, values={ValueRows.Count}, events={RelayEventRows.Count}, " +
                $"trace={FrameTraceRows.Count}, raw=" +
                string.Join("|", FrameTraceRows.Select(x => x.RawHex).Take(10)));
        ClearSessionView(clearLog: false);
                // A forensic line monitor must NEVER start in a view that silently
        // omits Master TX polls while showing their RTU replies.
        if (TraceQuickScopeCombo.SelectedIndex != 1 ||
            MainTabControl.SelectedIndex != 1 ||
            !Equals(NavFrameButton.Content, "Raw Frames") ||
            !ReferenceEquals(GetSegmentedNavButtons().FirstOrDefault(), NavFrameButton))
            throw new InvalidOperationException(
                "Raw Frames must be first/default with the unfiltered TX/RX timeline.");
        // Selected-tab capsule must not paint the old bright blue border.
        if (NavFrameButton.BorderBrush is SolidColorBrush navBorder &&
            navBorder.Color == (FindResource("AccentBrush") as SolidColorBrush)?.Color)
            throw new InvalidOperationException(
                "Selected segmented navigation retained the rejected accent border.");
        // Regression for a real field failure: the COM adapter plugged into
        // "SCADA" receives secondary PRM=0, while the one labeled "RTU"
        // receives primary PRM=1. In older builds these raw frames appeared
        // in Trace but ALL Values and Events were silently filtered out.
        var previousSubtitle = SessionSubtitleText.Text;
        _passivePortRoles.Reset();
        _passiveRoleStatus = string.Empty;
        _passiveLinkAddressWidth = 1;
        _passiveAsduDecoder = new Iec10xAsduDecoder(2, 2, 3);
        var reversedAsdu = new byte[]
        {
            13, 1, 3, 0, 1, 0, 24, 0, 0, 0, 0, 0xB4, 0x41, 0
        };
        var reversedPayload = new byte[] { 0x08, 0x69 }.Concat(reversedAsdu).ToArray();
        var reversedWire = new byte[] { 0x68, (byte)reversedPayload.Length,
                (byte)reversedPayload.Length, 0x68 }
            .Concat(reversedPayload)
            .Concat(new byte[] { (byte)(reversedPayload.Sum(x => x) & 255), 0x16 })
            .ToArray();
        var swappedPhysical = new[]
        {
            new PassiveObservedFrame(1, 0, DateTime.UtcNow, "Fixed", true,
                new byte[] { 0x10, 0x5B, 0x69, 0xC4, 0x16 }),
            new PassiveObservedFrame(0, 0, DateTime.UtcNow.AddMilliseconds(25),
                "Variable", true, reversedWire)
        };
        // Exercise the PERF-02 prepared route, not a one-off mock decode.
        var preparedReverse = PassiveFramePreparation.PrepareBatch(
            swappedPhysical, 1, 2, 2, 3);
        foreach (var frame in preparedReverse)
            FeedPassiveFrameToExistingViews(frame.Frame, prepared: frame);
        FlushUiQueues();
        ApplyRelayEventFilter();
        if (!ValueRows.Any(row => row.IoaSortKey == 24) ||
            !RelayEventRows.Any(row => row.Signal.Contains("24", StringComparison.Ordinal)) ||
            !_protocolTraceStore.Snapshot().Any(row =>
                row.Source.PhysicalStreamId == 0 &&
                row.Source.Direction == FrameDirection.SlaveToMaster &&
                row.Source.AsduDetails?.TypeId == 13) ||
            !_protocolTraceStore.Snapshot().Any(row =>
                row.Source.PhysicalStreamId == 1 &&
                row.Source.Direction == FrameDirection.MasterToSlave) ||
            !_passivePortRoles.DescribeAll().Contains("auto-reversed", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Reversed physical COM inputs lost received FLT Values/Events or misclassified TX/RX.");
        // SICAM field regression: Setup uses 1-byte Link Address while the
        // physical SCADA/RTU circuit transmits 2-byte address 69 00.
        // Two checksum-valid fixed frames establish the observed width; a
        // real 19-object Type31/COT3/CP56 ASDU must populate the Event grid.
        var fieldScanner = new PassiveFt12Scanner(1);
        var fieldFrames = new List<PassiveFt12Frame>();
        byte[] fieldStatus = [0x10, 0x0B, 0x69, 0x00, 0x74, 0x16];
        byte[] fieldAck = [0x10, 0x00, 0x69, 0x00, 0x69, 0x16];
        fieldScanner.Feed(fieldStatus, 0, fieldFrames.Add);
        fieldScanner.Feed(fieldAck, fieldStatus.Length, fieldFrames.Add);
        var fieldPayload = new List<byte> { 0x28, 0x69, 0x00, 0x1F, 0x13, 0x03, 0x00, 0x69, 0x00 };
        for (var i = 0; i < 19; i++)
        {
            var ms = 0xAB2A + i * 100;
            fieldPayload.AddRange([0xF6, 0x03, 0xFF, (byte)(i % 2 == 0 ? 2 : 0),
                (byte)ms, (byte)(ms >> 8), 0x04, 0x11, 0x85, 0x0B, 0x1A]);
        }
        var fieldWire = new byte[] { 0x68, 0xDA, 0xDA, 0x68 }.Concat(fieldPayload)
            .Concat(new byte[] { (byte)(fieldPayload.Sum(x => x) & 255), 0x16 }).ToArray();
        fieldScanner.Feed(fieldWire, fieldStatus.Length + fieldAck.Length, fieldFrames.Add);
        var fieldObserved = fieldFrames.Select((f, i) =>
            new PassiveObservedFrame(0, f.ByteOffset,
                DateTime.UtcNow.AddMilliseconds(i * 30), f.Format,
                f.ChecksumValid, f.RawBytes, f.DetectedLinkAddressWidth)).ToArray();
        var initialFieldEvents = RelayEventRows.Count;
        foreach (var prepared in PassiveFramePreparation.PrepareBatch(
            fieldObserved, linkAddressWidth: 1, cotWidth: 2, caWidth: 2, ioaWidth: 3))
            FeedPassiveFrameToExistingViews(prepared.Frame, prepared: prepared);
        FlushUiQueues();
        ApplyRelayEventFilter();
        if (fieldObserved[^1].DetectedLinkAddressWidth != 2 ||
            RelayEventRows.Count - initialFieldEvents < 19 ||
            !_protocolTraceStore.Snapshot().Any(row =>
                row.Source.AsduDetails is { TypeId: 31, ObjectCount: 19 } &&
                row.Source.Direction == FrameDirection.SlaveToMaster) ||
            !RelayEventRows.Any(row => row.RelayTime.Contains("2026-11-05", StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "SICAM 2-byte Link Address field SOE did not produce all 19 typed CP56 events in the WPF Event Log.");

        // Verify the actual decoded SICAM CP56 frame renders its remote clock,
        // validity and GEN flags inside the Engineer Mode Frame Interpreter,
        // rather than only in the Event Log / Full Properties.
        MainTabControl.SelectedIndex = 1;
        TraceQuickScopeCombo.SelectedIndex = 1;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        var timedFrameRow = FrameTraceRows.LastOrDefault(row =>
            row.Source.AsduDetails is { TypeId: 31, ObjectCount: 19 } &&
            row.Source.AsduDetails.Objects.Any(obj => obj.Cp56TimeDetails is not null));
        if (timedFrameRow is null ||
            !timedFrameRow.TimelineSignalDigest.Contains("05-11-2026", StringComparison.Ordinal) ||
            !timedFrameRow.TimelineSignalDigest.Contains("Timestamp Valid", StringComparison.Ordinal) ||
            !timedFrameRow.TimelineSignalDigest.Contains("Genuine (GEN=0)", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Source-typed CP56 device time, validity or genuine flag absent from inline Frame Interpreter.");
        FrameTraceGrid.SelectedItem = timedFrameRow;
        FrameTraceGrid.ScrollIntoView(timedFrameRow);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        if (FrameTraceGrid.ItemContainerGenerator.ContainerFromItem(timedFrameRow)
                is not ListBoxItem timedVisual ||
            !HasVisibleRawLine(timedVisual, timedFrameRow.TimelineSignalDigest))
            throw new InvalidOperationException(
                "Actual WPF Frame Interpreter did not display typed CP56 time after frame selection.");
        SaveRealWpfBitmap(outputDirectory, "engineer-device-time-valid-genuine.png");

        // P0-B actual-WPF acceptance: visual frame preview can be retained
        // without producing SOE. The complete writer-owned per-object facts
        // alone add the 19 Event rows, with exact original stream/offset.
        var ledgerStart = RelayEventRows.Count;
        var journalFrame = fieldObserved[^1] with { StreamOffset = 5000 };
        _passiveLedgerOwnsProcessEvents = true;
        try
        {
            var visual = PassiveFramePreparation.PrepareBatch(
                [journalFrame], 1, 2, 2, 3).Single();
            FeedPassiveFrameToExistingViews(journalFrame, prepared: visual);
            FlushUiQueues();
            ApplyRelayEventFilter();
            if (RelayEventRows.Count != ledgerStart)
                throw new InvalidOperationException(
                    "WPF preview duplicated a process event despite writer-owned event authority.");
            var facts = Enumerable.Range(1, 19).Select(index =>
                new PassiveProcessEventFact(index, journalFrame, index, false,
                    index > 1)).ToArray();
            var sourcePage = PassiveProcessEventPreparation.PreparePage(
                facts, linkWidth: 1, cotWidth: 2, caWidth: 2, ioaWidth: 3);
            if (sourcePage.Any(fact => !fact.Trusted ||
                !ProjectPassiveProcessEventFact(fact)))
                throw new InvalidOperationException(
                    "Canonical indexed process event failed to project into the WPF Event Log.");
            FlushUiQueues();
            ApplyRelayEventFilter();
            if (RelayEventRows.Count != ledgerStart + 19)
                throw new InvalidOperationException(
                    "19 writer-owned process events did not produce exactly 19 Event rows.");
        }
        finally { _passiveLedgerOwnsProcessEvents = false; }

        // The actual session starts clean; the acceptance-only frames never
        // become part of its retained forensic source.
        MainTabControl.SelectedIndex = 1;
        ClearSessionView(clearLog: true);
        _passivePortRoles.Reset();
        _passiveRoleStatus = string.Empty;
        _passiveOriginalSubtitle = previousSubtitle;
        SessionSubtitleText.Text = previousSubtitle;
        PassiveScadaRoleLabel.Text = "Input 1 · auto detecting";
        PassiveRtuRoleLabel.Text = "Input 2 · auto detecting";
        // The prior passive GUI + history smoke probes have already exercised
        // source-qualified navigation; this stage stress-tests the live WPF ring.
        
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

        // P0: true physical TX/RX is never discardable "routine UI noise".
        // Test at the classification boundary exercised by queue backpressure,
        // without sending on a COM/TCP port or waiting for 5k backlog.
        foreach (var sample in new[]
        {
            new Iec103MasterEvidenceEvent {
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                Direction = FrameDirection.MasterToSlave,
                RawHex = "10 5B 69 C4 16",
                Summary = "Request Class 2", DataClass = "Class 2" },
            new Iec103MasterEvidenceEvent {
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                Direction = FrameDirection.MasterToSlave,
                RawHex = "10 7A 69 E3 16",
                Summary = "Request Class 1", DataClass = "Class 1" },
            new Iec103MasterEvidenceEvent {
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                Direction = FrameDirection.SlaveToMaster,
                RawHex = "E5", Summary = "Link ACK / NO DATA" }
        })
            if (ClassifyLowValueBackpressureBucket(sample) != string.Empty)
                throw new InvalidOperationException(
                    "Real physical TX/RX frame was eligible for UI backpressure data loss.");
        if (ClassifyLowValueBackpressureBucket(new Iec103MasterEvidenceEvent
            {
                Summary = "background poll", RawHex = string.Empty
            }) == string.Empty)
            throw new InvalidOperationException(
                "Synthetic non-wire activity no longer permits UI-only coalescing.");

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

        // The unselected timeline always displays actual wire bytes alongside
        // meaning. Complete bytes remain in the selected-frame inspector.
        FrameTraceGrid.SelectedItems.Clear();
        var unselected = FrameTraceRows[^15];
        FrameTraceGrid.ScrollIntoView(unselected);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        var unselectedContainer =
            FrameTraceGrid.ItemContainerGenerator.ContainerFromItem(unselected) as ListBoxItem;
        if (unselectedContainer is null ||
            !HasVisibleRawLine(unselectedContainer, unselected.TimelineTitle) ||
            !HasVisibleRawLine(unselectedContainer, unselected.TimelineHex))
            throw new InvalidOperationException(
                "Engineer Frame Trace must render actual HEX bytes in each unselected timeline row.");
        // The red-marked field reference requires actual two-lane geometry:
        // frame number/time + HEX BELOW them on the left, with the source-typed
        // interpretation beside—not under—the HEX. Merely binding both texts
        // to a vertical StackPanel is a product regression.
        var rawTextBlock = FindVisibleTraceText(unselectedContainer, unselected.TimelineHex);
        var meaningTextBlock = FindVisibleTraceText(unselectedContainer, unselected.TimelineTitle);
        var timeTextBlock = FindVisibleTraceText(unselectedContainer, unselected.Time);
        if (rawTextBlock is null || meaningTextBlock is null || timeTextBlock is null)
            throw new InvalidOperationException("Raw-first two-lane frame observation is not rendered.");
        if (!IsTraceMonospace(rawTextBlock) || !IsTraceMonospace(meaningTextBlock) ||
            !IsTraceMonospace(timeTextBlock))
            throw new InvalidOperationException("Raw HEX, frame heading and observed time must share a monospace font.");
        var rawTop = rawTextBlock.TranslatePoint(new Point(), unselectedContainer);
        var rawRight = rawTextBlock.TranslatePoint(
            new Point(rawTextBlock.ActualWidth, 0), unselectedContainer).X;
        var meaningLeft = meaningTextBlock.TranslatePoint(new Point(), unselectedContainer).X;
        var timeTop = timeTextBlock.TranslatePoint(new Point(), unselectedContainer);
        if (rawTop.Y <= timeTop.Y + 4 || rawTop.X >= meaningLeft ||
            rawRight > meaningLeft - 2 || meaningTextBlock.ActualWidth < 90 ||
            rawTextBlock.ActualWidth < 80)
            throw new InvalidOperationException(
                $"Frame Trace HEX/interpretation lanes overlap or are stacked: " +
                $"raw=({rawTop.X:0},{rawTop.Y:0})..{rawRight:0}, " +
                $"meaningX={meaningLeft:0}, timeY={timeTop.Y:0}.");
        if (ScrollViewer.GetHorizontalScrollBarVisibility(FrameTraceGrid) != ScrollBarVisibility.Disabled)
            throw new InvalidOperationException(
                "Compact timeline should trim long text rather than force horizontal scrolling.");
        if (HasVisibleForensicHeading(unselectedContainer, "LINK LAYER") ||
            HasVisibleForensicHeading(unselectedContainer, "ASDU HEADER") ||
            HasVisibleForensicHeading(unselectedContainer, "INFORMATION OBJECTS") ||
            HasVisibleForensicHeading(unselectedContainer, "FRAME VALIDATION"))
            throw new InvalidOperationException(
                "Old multi-column forensic dump still appears inside a timeline row.");

        var selected = FrameTraceRows[Math.Max(0, FrameTraceRows.Count - 120)];
        FrameTraceGrid.SelectedItem = selected;
        // Selection, including keyboard paths, must stop AUTO before any
        // queued frames are appended. This is not a capture pause.
        if (AutoScrollLatestToggleButton.IsChecked != false)
            throw new InvalidOperationException(
                "Selecting a raw frame left automatic tail-follow enabled.");
        if (!ReferenceEquals(_selectedFrameRow, selected) ||
            SelectedLineSummaryText.Text != FrameInspectorFieldPresentation.HumanSummary(selected) ||
            TraceInspectorRawText.Text != selected.TimelineHex ||
            SelectedLineTrustText.Text != EngineerFramePresentation.TrustSummary(selected.Source))
            throw new InvalidOperationException(
                "Selecting the frame did not immediately update meaning and original bytes.");
        FrameTraceGrid.ScrollIntoView(selected);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        var selectedContainer =
            FrameTraceGrid.ItemContainerGenerator.ContainerFromItem(selected) as ListBoxItem;
        var selectedYBefore = selectedContainer?.TranslatePoint(new Point(), FrameTraceGrid).Y
            ?? double.NaN;
        var heldFrameRows = FrameTraceRows.ToArray();
        var heldCollectionChanges = 0;
        NotifyCollectionChangedEventHandler countHeldChanges = (_, _) => heldCollectionChanges++;
        FrameTraceRows.CollectionChanged += countHeldChanges;
        try
        {
            for (var i = 1400; i < 1430; i++)
                FeedPassiveFrameToExistingViews(new PassiveObservedFrame(0, i * 5L,
                    baseline.AddMilliseconds(i * 40d), "Fixed", true, bytes));
            FlushUiQueues();
        }
        finally { FrameTraceRows.CollectionChanged -= countHeldChanges; }
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        if (!ReferenceEquals(FrameTraceGrid.SelectedItem, selected))
            throw new InvalidOperationException("Held source-qualified Trace selection jumped.");
        selectedContainer = FrameTraceGrid.ItemContainerGenerator.ContainerFromItem(selected) as ListBoxItem;
        var selectedYAfter = selectedContainer?.TranslatePoint(new Point(), FrameTraceGrid).Y
            ?? double.NaN;
        if (!double.IsFinite(selectedYBefore) || !double.IsFinite(selectedYAfter) ||
            Math.Abs(selectedYAfter - selectedYBefore) > 1.25)
            throw new InvalidOperationException(
                $"HOLD raw-frame viewport moved during RX/TX: before={selectedYBefore:0.0}, after={selectedYAfter:0.0}.");
        if (heldCollectionChanges != 0 || !FrameTraceRows.SequenceEqual(heldFrameRows) ||
            FrameTraceGrid.Items.Count != heldFrameRows.Length)
            throw new InvalidOperationException(
                $"HOLD must not update the WPF ListBox: changes={heldCollectionChanges}, " +
                $"visible={FrameTraceRows.Count}, original={heldFrameRows.Length}.");
        if (_protocolTraceStore.Snapshot().LastOrDefault()?.Source.RawStreamOffset != (1429L * 5) ||
            _protocolTraceStore.Count != MaxVisibleFrameTraceRows ||
            _protocolTraceRowsDeferredWhileFrozen < 30 ||
            !FrameTraceRows.Contains(selected))
            throw new InvalidOperationException(
                "HOLD froze source ingestion or dropped the selected visible evidence.");
        if (AutoScrollLatestToggleButton.IsChecked != false)
            throw new InvalidOperationException("Held Trace unexpectedly resumed follow.");
        ResumeLiveViews(clearSelection: true, followLive: true);
        if (FrameTraceRows.LastOrDefault()?.Source.RawStreamOffset != (1429L * 5) ||
            _protocolTraceRowsDeferredWhileFrozen != 0 ||
            _protocolTraceViewDirtyWhileFrozen)
            throw new InvalidOperationException("AUTO did not catch up to the newest source evidence after HOLD.");

        // P0: repeated drag updates at an unchanged range must be true no-ops,
        // not WPF SelectedItems.Clear/repopulate/scroll/flicker cycles.
        var selectionEvents = 0;
        SelectionChangedEventHandler onSelection = (_, _) => selectionEvents++;
        FrameTraceGrid.SelectionChanged += onSelection;
        try
        {
            BeginProtocolTraceSelectionBatch();
            var first = Math.Max(0, FrameTraceGrid.Items.Count - 15);
            SelectProtocolTraceRange(FrameTraceGrid, first, first + 4, additive: false);
            if (FrameTraceGrid.SelectedItems.Count != 5)
                throw new InvalidOperationException("Drag block did not select five visible frames.");
            var initialEvents = selectionEvents;
            SelectProtocolTraceRange(FrameTraceGrid, first, first + 4, additive: false);
            if (selectionEvents != initialEvents)
                throw new InvalidOperationException("Unchanged drag range triggered WPF selection churn.");
            SelectProtocolTraceRange(FrameTraceGrid, first, first + 7, additive: false);
            if (FrameTraceGrid.SelectedItems.Count != 8 ||
                selectionEvents != initialEvents + 3)
                throw new InvalidOperationException("Drag extension rebuilt selection instead of applying three deltas.");
        }
        finally
        {
            EndProtocolTraceSelectionBatch(FrameTraceGrid);
            FrameTraceGrid.SelectionChanged -= onSelection;
            ResumeLiveViews(clearSelection: true, followLive: true);
        }

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
            // The first DP event is ON with the source IV flag active.
            // State still decodes as ON; an engineer must also see IV=1.
            var dpAsdu = new byte[] { 3, 1, 20, 0, 1, 0, (byte)(9 + i), 0, 0, (byte)(i == 0 ? 0x82 : 1) };
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
        // Source-true presentation requirement: the verified FT1.2 Link 105
        // and ASDU CA 1 belong on the FIRST GI line, not beneath the IOAs.
        if (multiObjectRow.TimelineTitle != "GI Data · Single-Point · Link 105 · CA 1" ||
            !multiObjectRow.HasInlineHeadlineIdentity ||
            !multiObjectRow.TimelineSignalDigest.Contains("No Timestamp", StringComparison.Ordinal) ||
            multiObjectRow.TimelineSignalDigest.Contains("Timestamp: No Timestamp", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Physical IEC101 GI must put Link/CA on first heading and avoid a duplicate timestamp label.");

        // Integration proof: production passive frame -> canonical FT1.2/ASDU
        // -> typed DIQ -> own displayed TI3 value. IV must not change ON[10].
        var dpOnFrame = FrameTraceRows.LastOrDefault(row =>
            row.Source.AsduDetails is { TypeId: 3 } asdu &&
            asdu.Objects.Any(obj => obj.QualityDetails?.Raw == 0x82));
        var dpOffFrame = FrameTraceRows.LastOrDefault(row =>
            row.Source.AsduDetails is { TypeId: 3 } asdu &&
            asdu.Objects.Any(obj => obj.QualityDetails?.Raw == 0x01));
        if (dpOnFrame is null || dpOffFrame is null ||
            !dpOnFrame.TimelineSignalDigest.Contains("Value: ON [10]", StringComparison.Ordinal) ||
            !dpOnFrame.TimelineSignalDigest.Contains("Quality: Invalid", StringComparison.Ordinal) ||
            !dpOffFrame.TimelineSignalDigest.Contains("Value: OFF [01]", StringComparison.Ordinal) ||
            !dpOffFrame.TimelineSignalDigest.Contains("Quality: Good", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Live IEC101 DIQ bits must render their exact two-bit state and independent quality.");
        FrameTraceGrid.ScrollIntoView(dpOnFrame);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        SaveRealWpfBitmap(outputDirectory, "engineer-dp-on-quality-invalid.png");
        FrameTraceGrid.ScrollIntoView(dpOffFrame);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        SaveRealWpfBitmap(outputDirectory, "engineer-dp-off-quality-good.png");
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
        FrameTraceGrid.ScrollIntoView(multiObjectRow);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        if (FrameTraceGrid.ItemContainerGenerator.ContainerFromItem(multiObjectRow)
                is not ListBoxItem giHeaderRow ||
            FindVisibleTraceText(giHeaderRow, multiObjectRow.TimelineTitle) is null ||
            FindVisibleTraceText(giHeaderRow, multiObjectRow.TimelineEngineeringIdentity) is not null)
            throw new InvalidOperationException(
                "Real WPF GI headline missing Link/CA or duplicating the identity underneath.");
        SaveRealWpfBitmap(outputDirectory, "engineer-gi-first-line-link-ca.png");
        // Real WPF bindings: TX is dark blue, RX dark green, regardless of
        // the narrative wording. Purely visual classification retains all rows.
        foreach (var probeRow in new[]
        {
            FrameTraceRows.Last(row => row.Direction == "TX"),
            FrameTraceRows.Last(row => row.Direction == "RX")
        })
        {
            FrameTraceGrid.ScrollIntoView(probeRow);
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            UpdateLayout();
            if (FrameTraceGrid.ItemContainerGenerator.ContainerFromItem(probeRow)
                    is not ListBoxItem container ||
                !HasTraceInk(container, probeRow.Direction, probeRow.TraceInk) ||
                FindVisibleTraceText(container, probeRow.TimelineHex) is not TextBlock coloredHex ||
                coloredHex.Foreground is not SolidColorBrush rawHexBrush ||
                rawHexBrush.Color != (Color)ColorConverter.ConvertFromString(probeRow.TraceInk) ||
                container.Template.FindName("TraceDirectionStripe", container)
                    is not Border directionStripe ||
                directionStripe.Background is not SolidColorBrush stripeBrush ||
                stripeBrush.Color != (probeRow.Direction == "TX"
                    ? Color.FromRgb(0x25, 0x63, 0xB8) : Color.FromRgb(0x16, 0x80, 0x5D)))
                throw new InvalidOperationException(
                    "Frame Trace must keep physically visible, source-typed TX blue / RX green stripes.");
            SaveRealWpfBitmap(outputDirectory, probeRow.Direction == "TX"
                ? "engineer-source-tx-blue.png" : "engineer-source-rx-green.png");
        }

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
            if (viewport.Width <= 1024)
            {
                // No inherited wide forensic grid may force a horizontal pan
                // across the compact timeline at laptop-sized resolutions.
                var scroller = FindTraceScroller(FrameTraceGrid);
                if (scroller is null || scroller.ScrollableWidth > 6)
                    throw new InvalidOperationException(
                        $"Timeline overflows horizontally at {viewport.Name}.");
            }
            if (FrameInterpreterPanel.Visibility != Visibility.Collapsed ||
                TraceInspectorColumn.ActualWidth > 1 ||
                !TraceInspectorToggle.IsVisible)
                throw new InvalidOperationException(
                    $"Collapsed Frame Details occupies trace width at {viewport.Name}.");
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
            TraceInspectorToggle.Visibility != Visibility.Visible)
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
                ValueGrid.ScrollIntoView(target);
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                UpdateLayout();
                if (ValueGrid.ItemContainerGenerator.ContainerFromItem(target)
                    is not DataGridRow { IsVisible: true, ActualHeight: > 14 })
                    throw new InvalidOperationException(
                        "Values has source facts but its recycled WPF row is blank/unrealized.");
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
                AssertResponsiveEventJournalToolbar();
                AssertSearchFlushLeft(EventsSearchShell, RelayEventGrid, "Events");
                if (!EventIncludeToggle.IsVisible || !EventExcludeToggle.IsVisible ||
                    !EventHistoryPreviousButton.IsVisible ||
                    !EventHistoryNextButton.IsVisible ||
                    !EventHistoryLatestButton.IsVisible ||
                    !EventHistoryExportButton.IsVisible ||
                    Grid.GetRow(RelayEventGrid) != 2 ||
                    RelayEventGrid.Columns.FirstOrDefault(col =>
                        string.Equals(col.Header?.ToString(), "State / value",
                            StringComparison.Ordinal)) is not DataGridTemplateColumn eventStateColumn ||
                    eventStateColumn.ClipboardContentBinding is not System.Windows.Data.Binding
                        { Path.Path: "OperatorValue" })
                    throw new InvalidOperationException(
                        "Unified Event History top toolbar or clipboard contract missing.");

                UpdateLayout();
                var includeXBefore = EventIncludeToggle.TranslatePoint(
                    new Point(), EventsSearchShell).X;
                var original = EventsQuickCountText.Text;
                EventsQuickCountText.Text = "99,999 / 999,999 events";
                UpdateLayout();
                var includeXAfter = EventIncludeToggle.TranslatePoint(
                    new Point(), EventsSearchShell).X;
                EventsQuickCountText.Text = original;
                if (Math.Abs(includeXAfter - includeXBefore) > 1)
                    throw new InvalidOperationException(
                        "Event History filters moved when the count grew.");

                SetEventFilters(EventCategories, Array.Empty<string>());
                EventsQuickFindBox.Clear();
                ApplyQuickFind();
                SeedSpontaneousEventRows();
                await (_eventHistoryWriter?.DrainAsync() ?? Task.CompletedTask);
                await RefreshEventHistoryQueryAsync();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                UpdateLayout();
                if (_historicalEventRows.Count == 0 ||
                    !ReferenceEquals(RelayEventGrid.ItemsSource, _historicalEventRows) ||
                    RelayEventGrid.Items.Count != _historicalEventRows.Count)
                    throw new InvalidOperationException(
                        "Unified Event grid is not backed by its indexed source.");
                if (_eventHistoryQuery.Count < _historicalEventRows.Count ||
                    _historicalEventRows.Count > EventHistoryPagination.PageSize)
                    throw new InvalidOperationException(
                        "Unified Event Journal page exceeded 100 source rows.");
                if (!_relayEventStore.Snapshot().Any(row => row.TypeBadge?.Label == "FLT"))
                    throw new InvalidOperationException(
                        "Source event ledger missed a floating measurement.");

                SetEventFilters(["Measurement"], Array.Empty<string>());
                await RefreshEventHistoryQueryAsync();
                if (_historicalEventRows.Where((row, index) =>
                        row.DisplayNumber != _eventHistoryPageStart + index + 1).Any())
                    throw new InvalidOperationException(
                        "Filtered Event Journal displays skipped or duplicated visible row numbers.");
                if (_eventHistoryQuery.Count == 0 ||
                    _historicalEventRows.Any(row => ClassifyEventCategory(row) != "Measurement") ||
                    EventIncludeToggle.Content?.ToString() != "Include: 1")
                    throw new InvalidOperationException(
                        "Full-history Include Measurement filter missed analog events.");

                SetEventFilters(EventCategories, ["Measurement"]);
                await RefreshEventHistoryQueryAsync();
                if (_historicalEventRows.Any(row =>
                        ClassifyEventCategory(row) == "Measurement") ||
                    EventExcludeToggle.Content?.ToString() != "Exclude: 1")
                    throw new InvalidOperationException(
                        "Full-history Exclude Measurement filter failed.");

                SetEventFilters(EventCategories, Array.Empty<string>());
                await RefreshEventHistoryQueryAsync();
                if (_eventHistoryQuery.Count == 0 ||
                    RelayEventGrid.Items.Count != _historicalEventRows.Count)
                    throw new InvalidOperationException(
                        "Removing Event filters failed to restore indexed history.");
                var lastEvent = _historicalEventRows[^1];
                RelayEventGrid.ScrollIntoView(lastEvent);
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                UpdateLayout();
                if (RelayEventGrid.ItemContainerGenerator.ContainerFromItem(lastEvent)
                        is not DataGridRow realized ||
                    !realized.IsVisible || realized.ActualHeight < 12)
                    throw new InvalidOperationException(
                        "Indexed Event page has no realized WPF DataGridRow.");
                eventGridRealizedRows = RelayEventGrid.Items.Count;
            }
            SaveRealWpfBitmap(outputDirectory, "field-1366x768-" + view.Name + ".png");
        }
        // One source-backed History page stays fixed when 1,000 additional
        // event observations arrive. No fake AUTO/HOLD mode or 420-row gate.
        MainTabControl.SelectedIndex = 3;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        await (_eventHistoryWriter?.DrainAsync() ?? Task.CompletedTask);
        await RefreshEventHistoryQueryAsync();
        await ShowEventHistoryPageAsync(0);
        if (_historicalEventRows.Count == 0)
            throw new InvalidOperationException("No indexed Event History for pinned-page regression.");
        _eventHistoryFollowingLatest = false;
        var heldRows = _historicalEventRows.ToArray();
        var heldEvent = heldRows[heldRows.Length / 2];
        RelayEventGrid.SelectedItem = heldEvent;
        RelayEventGrid.ScrollIntoView(heldEvent);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        var eventScroll = RelayEventGrid.Template.FindName("DG_ScrollViewer",
            RelayEventGrid) as ScrollViewer;
        var heldOffset = eventScroll?.VerticalOffset;
        for (var i = 0; i < 1000; i++)
            AppendProtocolEvent(new Iec103RelayEventLogEntry
            {
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                SignalName = "Synthetic Event History viewport regression",
                NewValue = (i % 31).ToString(System.Globalization.CultureInfo.InvariantCulture),
                TypeId = 13
            });
        FlushUiQueues();
        await (_eventHistoryWriter?.DrainAsync() ?? Task.CompletedTask);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        if (!_historicalEventRows.SequenceEqual(heldRows) ||
            !ReferenceEquals(RelayEventGrid.SelectedItem, heldEvent) ||
            heldOffset is double previous && eventScroll is not null &&
                Math.Abs(eventScroll.VerticalOffset - previous) > 0.5)
            throw new InvalidOperationException(
                "New events moved the operator's pinned Event History page.");
        RelayEventGrid.SelectedItem = null;
        await JumpEventHistoryToLatestAsync();
        if (_historicalEventRows.Count == 0 ||
            _historicalEventRows[^1].EventNumber != _eventHistoryJournal?.Count)
            throw new InvalidOperationException(
                "Latest did not reveal the most recently indexed Event.");

        MainTabControl.SelectedIndex = 1;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        TraceQuickScopeCombo.SelectedIndex = 1;
        // Frame selection never opens the optional property dock on its own.
        TraceFrameDetailsClose_Click(this, new RoutedEventArgs());
        FrameTraceGrid.SelectedItem = multiObjectRow;
        FrameTraceGrid.ScrollIntoView(multiObjectRow);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        if (FrameInterpreterPanel.Visibility != Visibility.Collapsed ||
            TraceInspectorColumn.ActualWidth > 1 ||
            !multiObjectRow.TimelineSignalDigest.Contains("TI-", StringComparison.Ordinal) ||
            !multiObjectRow.TimelineEngineeringCause.Contains("Cause Of Transmission: ", StringComparison.Ordinal) ||
            !multiObjectRow.TimelineEngineeringIdentity.Contains("Link ", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Inline engineering decode must expose verified Link/TI/COT while Frame Details is closed.");

        // Real WPF geometry and text acceptance: the original raw lane must be
        // narrower than the interpreter, and separate IOA/Value objects must
        // actually occupy distinct visual lines, not a single | delimited row.
        if (FrameTraceGrid.ItemContainerGenerator.ContainerFromItem(multiObjectRow)
                is not ListBoxItem multiVisual)
            throw new InvalidOperationException("Selected multi-object frame was not realized.");
        var rawSignalHex = FindVisibleTraceText(multiVisual, multiObjectRow.TimelineHex);
        var visibleSignalText = FindVisibleTraceText(multiVisual, multiObjectRow.TimelineSignalDigest);
        if (rawSignalHex is null || visibleSignalText is null ||
            !IsTraceMonospace(rawSignalHex) || !IsTraceMonospace(visibleSignalText))
            throw new InvalidOperationException("Original HEX and per-IOA Frame Interpreter must both render in monospace.");
        var signalLines = multiObjectRow.TimelineSignalDigest.Split(Environment.NewLine);
        var valueLines = signalLines.Where(line =>
            line.StartsWith("IOA ", StringComparison.Ordinal)).ToArray();
        if (rawSignalHex is null || visibleSignalText is null ||
            valueLines.Length != 2 ||
            !valueLines[0].Contains("IOA ", StringComparison.Ordinal) ||
            !valueLines[1].Contains("IOA ", StringComparison.Ordinal) ||
            multiObjectRow.Source.AsduDetails?.Objects.Any(obj => obj.Cp56TimeDetails is not null) == true &&
            (!multiObjectRow.TimelineSignalDigest.Contains("Timestamp Valid", StringComparison.Ordinal) ||
             !multiObjectRow.TimelineSignalDigest.Contains("Genuine (GEN=0)", StringComparison.Ordinal)) ||
            visibleSignalText.Text.Contains(" | ", StringComparison.Ordinal) ||
            visibleSignalText.ActualWidth < rawSignalHex.ActualWidth * 1.55 ||
            visibleSignalText.ActualHeight < 24)
            throw new InvalidOperationException(
                $"Source-backed vertical signals or 32:68 raw/interpreter layout failed: " +
                $"IOA-lines={signalLines.Length}, hexWidth={rawSignalHex?.ActualWidth:0.0}, " +
                $"meaningWidth={visibleSignalText?.ActualWidth:0.0}, " +
                $"meaningHeight={visibleSignalText?.ActualHeight:0.0}.");
        SaveRealWpfBitmap(outputDirectory, "engineer-vertical-signal-lines.png");
        SaveRealWpfBitmap(outputDirectory, "engineer-inline-full-width-trace.png");

        // Open and close the actual right-edge details dock. All source-decoded
        // IOAs, quality flags, and event time must reside in ONE properties list.
        TraceFrameDetails_Click(this, new RoutedEventArgs());
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        if (FrameInterpreterPanel.Visibility != Visibility.Visible ||
            TraceInspectorColumn.ActualWidth < 320 ||
            !FramePropertiesExpander.IsExpanded ||
            SelectedDecodedPoints.Count != 2 ||
            SelectedFrameProperties.Count(x => x.Field.StartsWith("Object #", StringComparison.Ordinal)) != 2 ||
            SelectedLineDirectionText.IsVisible ||
            DecodedPointGrid.IsVisible)
            throw new InvalidOperationException(
                "Right dock must display only Full Properties, with all decoded source IOAs.");
        var qualityDisplayIndex = VisibleFrameProperties.ToList()
            .FindIndex(x => x.Field == "Quality" && x.Depth == 0);
        if (qualityDisplayIndex < 0 ||
            !VisibleFrameProperties.Skip(qualityDisplayIndex + 1).Any(
                x => x.Depth == 1 && x.Field == "Quality details") ||
            !VisibleFrameProperties.Skip(qualityDisplayIndex + 1).Any(
                x => x.Depth == 2 && x.Field == "Invalid (IV)") ||
            FramePropertiesGrid.RowDetailsVisibilityMode != DataGridRowDetailsVisibilityMode.Collapsed)
            throw new InvalidOperationException(
                "Full Properties must show a compact, aligned quality hierarchy without multiline DataGrid row details.");
        if (!FramePropertiesGrid.FontFamily.Source.Contains("Cascadia Mono", StringComparison.Ordinal) ||
            !TraceInspectorRawText.FontFamily.Source.Contains("Cascadia Mono", StringComparison.Ordinal))
            throw new InvalidOperationException("Frame Details decoded properties and original HEX must use monospace.");
        interpretedPointRows = SelectedDecodedPoints.Count;
        var vsqRow = SelectedFrameProperties.FirstOrDefault(x => x.Field == "Value structure (VSQ)");
        var ioaRow = SelectedFrameProperties.FirstOrDefault(x => x.Field == "IOA");
        var qualityRow = SelectedFrameProperties.FirstOrDefault(x => x.Field == "Quality");
        if (vsqRow?.Children is not { Count: 3 } ||
            vsqRow.Children[0].Value != "False" ||
            vsqRow.Children[1].Value != "2" ||
            ioaRow?.Children is not { Count: 3 } ||
            ioaRow.Children[0].Field != "IOA1 (low)" ||
            ioaRow.Children[2].Field != "IOA3 (high)" ||
            qualityRow?.Children is not { Count: > 1 } ||
            !qualityRow.Children.Any(x => x.Field == "Invalid (IV)" && x.Value == "False"))
            throw new InvalidOperationException(
                "The retained canonical VSQ/IOA/quality byte details are incorrect.");
        // WPF acceptance must exercise the actual disclosure workflow, not
        // the retired selection-triggered RowDetails expansion.
        var vsqVisible = VisibleFrameProperties.FirstOrDefault(
            x => x.Field == "Value structure (VSQ)" && x.Depth == 0)
            ?? throw new InvalidOperationException("VSQ root missing from visual property tree.");
        FramePropertiesGrid.ScrollIntoView(vsqVisible);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        if (FramePropertiesGrid.ItemContainerGenerator.ContainerFromItem(vsqVisible)
                is not DataGridRow)
            throw new InvalidOperationException("The VSQ parent row was not generated by WPF.");

        // Run the same click handler as the chevron, then verify that a genuine
        // child DataGridRow is generated within the aligned Name / Value grid.
        FramePropertyDisclosure_Click(new Button { Tag = vsqVisible.Key },
            new RoutedEventArgs(Button.ClickEvent));
        var sequenceVisual = VisibleFrameProperties.FirstOrDefault(
            x => x.Depth == 1 && x.Field == "Sequence (SQ)" && x.Value == "False");
        if (sequenceVisual is null)
            throw new InvalidOperationException(
                "VSQ disclosure did not reveal the source-decoded SQ=false child.");
        FramePropertiesGrid.ScrollIntoView(sequenceVisual);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        if (FramePropertiesGrid.ItemContainerGenerator.ContainerFromItem(sequenceVisual)
                is not DataGridRow childRow || !childRow.IsVisible)
            throw new InvalidOperationException(
                "Expanded VSQ child row is not visible in real WPF.");

        // Capture the reference-like nested Quality Details, not a giant
        // expanded help paragraph or hidden row-details popover.
        var qualityVisible = VisibleFrameProperties.First(x =>
            x.Field == "Quality" && x.Depth == 0);
        FramePropertiesGrid.ScrollIntoView(qualityVisible);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        SaveRealWpfBitmap(outputDirectory, "engineer-right-full-properties-only.png");

        // Visual proof for the quality-child complaint: the screenshot must
        // show the actual decoded IV/NT/BL/SB/raw rows, not just the open arrow
        // with those rows scrolled out of sight.
        var lastQualityChild = VisibleFrameProperties.LastOrDefault(
            x => x.Depth == 2 && x.Field == "Raw quality");
        if (lastQualityChild is null)
            throw new InvalidOperationException(
                "Selected WPF frame lost the complete source quality child tree.");
        FramePropertiesGrid.ScrollIntoView(lastQualityChild);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        if (FramePropertiesGrid.ItemContainerGenerator.ContainerFromItem(lastQualityChild)
                is not DataGridRow rawQualityVisual || !rawQualityVisual.IsVisible)
            throw new InvalidOperationException(
                "Decoded quality flags are present but not visible in the WPF property viewport.");
        SaveRealWpfBitmap(outputDirectory, "engineer-right-quality-details-expanded.png");

        // Real responsive WPF acceptance with the dock OPEN: a narrower frame
        // viewport must stack the actual HEX above the decoded interpretation.
        // Test actual sizes, not requested top-level dimensions (runner may clamp).
        foreach (var viewport in new (string Name, double Width, double Height)[]
        {
            ("desktop-1540x960", 1540, 960),
            ("field-1366x768", 1366, 768),
            ("compact-1024x576", 1024, 576)
        })
        {
            Width = viewport.Width;
            Height = viewport.Height;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            UpdateLayout();
            UpdateTraceRowLayout();
            FrameTraceGrid.ScrollIntoView(multiObjectRow);
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            UpdateLayout();
            if (FrameInterpreterPanel.Visibility != Visibility.Visible ||
                FrameTraceGrid.ItemContainerGenerator.ContainerFromItem(multiObjectRow)
                    is not ListBoxItem adaptiveRow)
                throw new InvalidOperationException("Inspector-open adaptive Frame Trace lost the selected physical row.");

            // The narrowed inspector must leave the majority of width for the
            // frame interpreter at ordinary field-workstation resolutions.
            if (FrameTraceGrid.ActualWidth >= 840 &&
                FrameTraceGrid.ActualWidth < TraceInspectorColumn.ActualWidth * 1.6)
                throw new InvalidOperationException(
                    "Frame Details expanded too wide relative to the Raw Frames interpreter.");
            var expectedLayout = FrameTraceViewportPolicy.RowLayout(FrameTraceGrid.ActualWidth);
            if (!Equals(FrameTraceGrid.Tag, expectedLayout))
                throw new InvalidOperationException(
                    $"Incorrect responsive layout at {viewport.Name}: actual width={FrameTraceGrid.ActualWidth:0}, tag={FrameTraceGrid.Tag}, expected={expectedLayout}.");

            var hex = FindVisibleTraceText(adaptiveRow, multiObjectRow.TimelineHex);
            var signals = FindVisibleTraceText(adaptiveRow, multiObjectRow.TimelineSignalDigest);
            var heading = FindVisibleTraceText(adaptiveRow, multiObjectRow.TimelineTitle);
            var identity = FindVisibleTraceText(adaptiveRow, multiObjectRow.TimelineEngineeringIdentity);
            var cause = FindVisibleTraceText(adaptiveRow, multiObjectRow.TimelineEngineeringCause);
            if (hex is null || signals is null || heading is null || cause is null ||
                (multiObjectRow.HasInlineHeadlineIdentity
                    ? identity is not null ||
                      !heading.Text.Contains(" · CA ", StringComparison.Ordinal)
                    : identity is null) ||
                signals.TextWrapping != TextWrapping.Wrap ||
                signals.TextTrimming != TextTrimming.None ||
                heading.TextTrimming != TextTrimming.None ||
                cause.TextWrapping != TextWrapping.Wrap ||
                cause.TextTrimming != TextTrimming.None ||
                signals.ActualWidth < 130 || signals.ActualHeight < 16 ||
                !signals.Text.Contains("IOA ", StringComparison.Ordinal) ||
                !cause.Text.Contains("Cause Of Transmission: ", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Frame interpreter clipped/hidden at {viewport.Name}: trace={FrameTraceGrid.ActualWidth:0}, layout={expectedLayout}, signals={signals?.ActualWidth:0}x{signals?.ActualHeight:0}.");

            var hexX = hex.TranslatePoint(new Point(0, 0), adaptiveRow).X;
            var hexY = hex.TranslatePoint(new Point(0, 0), adaptiveRow).Y;
            var signalX = signals.TranslatePoint(new Point(0, 0), adaptiveRow).X;
            var signalY = signals.TranslatePoint(new Point(0, 0), adaptiveRow).Y;
            if (expectedLayout == "Stacked"
                ? signalY < hexY + hex.ActualHeight - 1
                : signalX <= hexX + hex.ActualWidth - 3)
                throw new InvalidOperationException(
                    $"Frame lanes overlap at {viewport.Name}: {expectedLayout}, HEX ({hexX:0},{hexY:0}), interpret ({signalX:0},{signalY:0}).");

            // VirtualizingStackPanel can overreport ScrollableWidth for recycled
            // offscreen row extents after width changes, even with horizontal
            // scrolling disabled. Test the actual visible WPF evidence bounds:
            // bytes, interpretation, Link/CA and COT must fit in the viewport.
            var scroller = FindTraceScroller(FrameTraceGrid);
            if (scroller is null ||
                ScrollViewer.GetHorizontalScrollBarVisibility(FrameTraceGrid) != ScrollBarVisibility.Disabled)
                throw new InvalidOperationException("Frame Trace permits horizontal panning when docked.");
            var viewportRight = scroller.TranslatePoint(
                new Point(scroller.ViewportWidth, 0), adaptiveRow).X;
            foreach (var text in new[] { hex, signals, heading, cause })
            {
                var right = text.TranslatePoint(new Point(text.ActualWidth, 0), adaptiveRow).X;
                if (right > viewportRight + 3)
                    throw new InvalidOperationException(
                        $"Docked Frame Trace visual evidence crosses viewport at {viewport.Name}: " +
                        $"text={text.Text[..Math.Min(32, text.Text.Length)]}, right={right:0}, viewport={viewportRight:0}, " +
                        $"trace={FrameTraceGrid.ActualWidth:0}, layout={expectedLayout}.");
            }
            SaveRealWpfBitmap(outputDirectory, "engineer-inspector-open-" + viewport.Name + ".png");
        }
        Width = 1366;
        Height = 768;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        UpdateLayout();

        // Interactive hex inspector must render actual original byte CELLS,
        // verified field ranges, and field-to-byte selection/highlighting.
        // A giant ASCII diagram TextBox is explicitly forbidden.
        var window = new FrameByteMapWindow(multiObjectRow) { Owner = this };
        window.Show();
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        await Dispatcher.Yield(DispatcherPriority.Render);
        window.UpdateLayout();
        if (!window.IsVisible || window.HexViewportHeight < 180 ||
            window.OriginalByteCount < 10 || window.FieldCount < 5 ||
            !window.SelectField("asdu-type") ||
            window.SelectedFieldKey != "asdu-type" ||
            window.SelectedFieldHex.Length != 2 ||
            window.HighlightedByteCount != 1 ||
            !window.FieldReport.Contains("Type ID", StringComparison.Ordinal) ||
            window.FieldReport.Contains("└─", StringComparison.Ordinal))
        {
            window.Close();
            throw new InvalidOperationException(
                "Interactive HEX field selector, physical source range or 16-octet view failed.");
        }
        SaveRealWpfElementBitmap(outputDirectory, "engineer-interactive-hex-field-inspector.png",
            (FrameworkElement)window.Content);
        window.Close();

        TraceFrameDetailsClose_Click(this, new RoutedEventArgs());
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        if (FrameInterpreterPanel.Visibility != Visibility.Collapsed ||
            TraceInspectorColumn.ActualWidth > 1 ||
            !ReferenceEquals(FrameTraceGrid.SelectedItem, multiObjectRow))
            throw new InvalidOperationException(
                "Closing properties must restore full trace width and retain held selection.");

        // Active invalid quality must remain readable directly in the inline
        // interpreter when the optional property dock is hidden.
        var invalidPointFrame = FrameTraceRows.LastOrDefault(x =>
            x.TimelineAlerts.Contains("IV=1", StringComparison.Ordinal));
        if (invalidPointFrame is null)
            throw new InvalidOperationException("WPF fixture lost its IV=1 process-quality frame.");
        FrameTraceGrid.SelectedItem = invalidPointFrame;
        FrameTraceGrid.ScrollIntoView(invalidPointFrame);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        UpdateLayout();
        var invalidContainer =
            FrameTraceGrid.ItemContainerGenerator.ContainerFromItem(invalidPointFrame) as ListBoxItem;
        if (invalidContainer is null ||
            !HasVisibleRawLine(invalidContainer, invalidPointFrame.TimelineHex) ||
            !HasVisibleRawLine(invalidContainer, invalidPointFrame.TimelineReadableAlerts) ||
            !invalidPointFrame.TimelineReadableAlerts.Contains("Invalid (IV): TRUE", StringComparison.Ordinal) ||
            !invalidPointFrame.TimelineEngineeringIdentity.Contains("CA ", StringComparison.Ordinal) ||
            invalidPointFrame.Source.AsduDetails?.Objects.Any(
                x => x.QualityDetails?.Invalid == true) != true)
            throw new InvalidOperationException(
                "IV=1, Link/CA identity, and original HEX are not visible or source-backed.");
        SaveRealWpfBitmap(outputDirectory, "engineer-inline-invalid-quality.png");
        FrameTraceGrid.SelectedItem = multiObjectRow;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        if (FrameInterpreterPanel.Visibility != Visibility.Collapsed ||
            !ReferenceEquals(_selectedFrameRow, multiObjectRow) ||
            SelectedFrameProperties.Count != 0 || SelectedDecodedPoints.Count != 0)
            throw new InvalidOperationException(
                "Collapsed Full Properties must defer projections while preserving selected source.");

        // Reopening after another frame was selected must build the ACTUAL
        // current source frame rather than resurrect stale offscreen objects.
        TraceFrameDetails_Click(this, new RoutedEventArgs());
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        if (FrameInterpreterPanel.Visibility != Visibility.Visible ||
            SelectedFrameProperties.Count(x => x.Field.StartsWith("Object #", StringComparison.Ordinal)) != 2 ||
            TraceInspectorRawText.Text != multiObjectRow.TimelineHex)
            throw new InvalidOperationException("Reopened Full Properties lost the current decoded frame.");
        TraceFrameDetailsClose_Click(this, new RoutedEventArgs());

        // Reproduce field failure: a full live Event journal suddenly became one
        // row when a narrower engine result arrived at end of session. Also
        // verify > one dispatcher batch of pending evidence is drained before
        // terminal summary rather than being discarded on the next Connect.
        FlushUiQueues();
        var retainedAtFinish = _relayEventStore.Snapshot().ToArray();
        var projectedAtFinish = RelayEventRows.ToArray();
        // The Event Journal grid is now sourced from indexed history, not
        // the 420-row live preview. Terminal completion must preserve the
        // currently displayed history page independently of that preview.
        var historyPageAtFinish = _historicalEventRows.ToArray();
        var valuesAtFinish = ValueRows.ToArray();
        if (retainedAtFinish.Length < 2 || projectedAtFinish.Length < 2 ||
            valuesAtFinish.Length < 2)
            throw new InvalidOperationException("Terminal regression lacks mixed live Events/Values.");
        var terminalEventResets = 0;
        var terminalValueResets = 0;
        NotifyCollectionChangedEventHandler eventWatch = (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) terminalEventResets++;
        };
        NotifyCollectionChangedEventHandler valueWatch = (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) terminalValueResets++;
        };
        RelayEventRows.CollectionChanged += eventWatch;
        ValueRows.CollectionChanged += valueWatch;
        try
        {
            const int terminalBacklog = 280; // greater than one bounded UI flush
            for (var i = 0; i < terminalBacklog; i++)
            {
                _pendingEvidence.Enqueue(new SessionEvidenceEnvelope(
                    System.Threading.Volatile.Read(ref _eventProjectionEpoch),
                    new Iec103MasterEvidenceEvent
                {
                    SequenceNumber = 8_000_000L + i,
                    ProtocolMode = Iec60870ProtocolMode.Iec103,
                    Direction = FrameDirection.MasterToSlave,
                    Category = "Info",
                    DataClass = "Test",
                    Summary = "Synthetic terminal receive-queue drain"
                }));
            }
            await ApplyFinalResultAsync(new Iec103MasterRunResult
            {
                CompletedNormally = true,
                CompletionReason = "Synthetic completion (WPF regression only)",
                // An engine edge summary must NEVER replace the richer live log.
                EventLog = [new Iec103RelayEventLogEntry
                {
                    EvidenceSequenceNumber = 9_000_000,
                    SignalName = "summary-only event"
                }],
                ValuePoints = [new Iec103ValuePoint
                {
                    Key = "summary-only-value",
                    SignalName = "summary-only value"
                }]
            });
        }
        finally
        {
            RelayEventRows.CollectionChanged -= eventWatch;
            ValueRows.CollectionChanged -= valueWatch;
        }
        if (!_pendingEvidence.IsEmpty || !_pendingFindings.IsEmpty ||
            !retainedAtFinish.SequenceEqual(_relayEventStore.Snapshot()) ||
            !projectedAtFinish.SequenceEqual(RelayEventRows) ||
            !historyPageAtFinish.SequenceEqual(_historicalEventRows) ||
            !ReferenceEquals(RelayEventGrid.ItemsSource, _historicalEventRows) ||
            !valuesAtFinish.SequenceEqual(ValueRows) ||
            RelayEventGrid.Items.Count != historyPageAtFinish.Length ||
            terminalEventResets != 0 || terminalValueResets != 0 ||
            DpiText.Text != _dpiCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException(
                $"Terminal completion lost live observations or reset WPF: " +
                $"retained={_relayEventStore.Snapshot().Count}/{retainedAtFinish.Length}, " +
                $"preview={RelayEventRows.Count}/{projectedAtFinish.Length}, " +
                $"journal UI={RelayEventGrid.Items.Count}/{historyPageAtFinish.Length}, " +
                $"values={ValueRows.Count}/{valuesAtFinish.Length}, " +
                $"pending={_pendingEvidence.Count}, resets={terminalEventResets}/{terminalValueResets}.");
        // A new Start changes the epoch. Late active Master callbacks, including
        // already-queued old results, must not modify this session's Values,
        // Events, diagnostics or dual-link state.
        var oldEpoch = System.Threading.Volatile.Read(ref _eventProjectionEpoch);
        ClearSessionView(clearLog: false);
        var nextEpoch = System.Threading.Volatile.Read(ref _eventProjectionEpoch);
        if (nextEpoch == oldEpoch)
            throw new InvalidOperationException("New observation scope failed to advance its epoch.");
        var late = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 9_100_001,
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            Category = "RX",
            TypeId = 1,
            CauseOfTransmission = 3,
            CommonAddressNumber = 105,
            InformationObjectAddress = 901,
            SignalDisplayValue = "SP=ON"
        };
        var lateFinding = new Iec103MasterFinding
        {
            Id = "STALE-SOURCE",
            Title = "Stale previous-session finding"
        };
        OnMasterEvidenceReceived(oldEpoch, this, late);
        OnMasterFindingRaised(oldEpoch, this, lateFinding);
        // Also cover the hard race: the old callback passed the initial epoch
        // check, then the UI cleared the scope before it was dequeued.
        _pendingEvidence.Enqueue(new SessionEvidenceEnvelope(oldEpoch, late));
        _pendingFindings.Enqueue(new SessionFindingEnvelope(oldEpoch, lateFinding));
        FlushUiQueues();
        if (_rxCount != 0 || RelayEventRows.Count != 0 || ValueRows.Count != 0 ||
            FindingRows.Count != 0 || _pendingEvidence.Count != 0 ||
            _pendingFindings.Count != 0 || _staleSessionEvidenceRejected < 2 ||
            _staleSessionFindingsRejected < 2)
            throw new InvalidOperationException(
                "Previous session callback/queue entries contaminated a new session epoch.");

        // A new trusted callback must continue working normally. Epoch guard
        // blocks old sources; it must not accidentally block the current one.
        OnMasterEvidenceReceived(nextEpoch, this, late);
        FlushUiQueues();
        if (_rxCount != 1 || RelayEventRows.Count != 1 ||
            !ValueRows.Any(x => x.IoAddress == "901") ||
            !RelayEventRows.Any(x => x.IoAddress == "901"))
            throw new InvalidOperationException(
                "Session epoch protection also blocked current trusted process evidence.");
        var staleEpochReceptionPassed = true;
        // Stage D: measure the actual two-panel WPF renderer with 10,000
        // synthetic original frames after the protocol/epoch probes.
        // This runs once in the same Windows CI acceptance, not as another
        // scheduled workflow, timer, or independent UI implementation.
        var frameTraceLoad = await MeasureFrameTraceUnderLoadAsync(outputDirectory);
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
                staleEpochReceptionPassed,
                staleSessionEvidenceRejected = _staleSessionEvidenceRejected,
                staleSessionFindingsRejected = _staleSessionFindingsRejected,
                eventProjectionLiveResetCount = _eventLiveResetCount,
                eventProjectionMismatchCount = _eventProjectionMismatchCount,
                eventProjectionSourceCount = _eventProjectionSourceCount,
                eventProjectionFilteredCount = _eventProjectionFilteredCount,
                digitalStateBadgesPassed = true,
                retainedTraceRows = totalFrames,
                steadyStateCollectionResetEvents = resetEvents,
                syntheticAppendAndFlushMilliseconds = appendAndFlush.Elapsed.TotalMilliseconds,
                frameTraceLoad,
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
    /// <summary>
    /// All Event Journal actions must remain above the grid. On constrained
    /// CI desktops the responsive WrapPanel is allowed exactly two compact
    /// rows; checking the first search row alone falsely reports a 30px gap.
    /// Unlike Trace/Values, Event Journal intentionally has more actions.
    /// </summary>
    private void AssertResponsiveEventJournalToolbar()
    {
        if (EventsSearchShell.Parent is not WrapPanel toolbar ||
            !toolbar.IsVisible || toolbar.ActualHeight < 26 ||
            toolbar.ActualHeight > 72)
            throw new InvalidOperationException(
                "Event Journal toolbar missing or occupying more than two compact rows.");
        FrameworkElement[] controls =
        [
            EventsSearchShell, EventIncludeToggle, EventExcludeToggle,
            EventsQuickCountText, EventHistoryPreviousButton,
            EventHistoryPageText, EventHistoryNextButton,
            EventHistoryLatestButton, EventHistoryExportButton
        ];
        foreach (var control in controls)
        {
            if (!control.IsVisible || control.ActualWidth < 20 ||
                control.ActualHeight < 12)
                throw new InvalidOperationException(
                    "Event Journal toolbar has a hidden or collapsed control.");
            var topLeft = control.TranslatePoint(new Point(), toolbar);
            if (topLeft.X < -2 || topLeft.Y < -2 ||
                topLeft.X + control.ActualWidth > toolbar.ActualWidth + 2 ||
                topLeft.Y + control.ActualHeight > toolbar.ActualHeight + 2)
                throw new InvalidOperationException(
                    "Event Journal control is clipped outside the responsive toolbar.");
        }

        // Inspect actual WPF templates, not screenshots or XAML text alone.
        // All Event Journal actions must share Search's 32-DIP optical height.
        System.Windows.Controls.Primitives.ButtonBase[] actions =
        [
            EventIncludeToggle, EventExcludeToggle, EventHistoryPreviousButton,
            EventHistoryNextButton, EventHistoryLatestButton, EventHistoryExportButton
        ];
        foreach (var action in actions)
        {
            action.ApplyTemplate();
            if (action.Template.FindName("EventJournalButtonSurface", action) is not Border surface ||
                Math.Abs(action.ActualHeight - EventsSearchShell.ActualHeight) > 0.6 ||
                Math.Abs(action.ActualHeight - 32) > 0.6 ||
                Math.Abs(surface.CornerRadius.TopLeft - 16) > 0.6 ||
                surface.CornerRadius.TopLeft != surface.CornerRadius.BottomRight)
                throw new InvalidOperationException(
                    $"Event Journal action {action.Name} lost the shared pill 32-DIP design token.");
        }
        // Exact operator-approved Lucide chevrons: slender stroke, round caps.
        // 32x32 controls with radius 16 must be circular, not tiny square buttons.
        if (EventHistoryPreviousButton.Content is not System.Windows.Shapes.Path previous ||
            EventHistoryNextButton.Content is not System.Windows.Shapes.Path next ||
            Math.Abs(EventHistoryPreviousButton.ActualWidth - 32) > 0.6 ||
            Math.Abs(EventHistoryNextButton.ActualWidth - 32) > 0.6 ||
            previous.Data.ToString() != Geometry.Parse("M15,18 L9,12 L15,6").ToString() ||
            next.Data.ToString() != Geometry.Parse("M9,18 L15,12 L9,6").ToString() ||
            Math.Abs(previous.StrokeThickness - 2) > 0.1 ||
            Math.Abs(next.StrokeThickness - 2) > 0.1 ||
            previous.Fill is not null || next.Fill is not null)
            throw new InvalidOperationException(
                "Event Journal navigation must show 32-DIP circles with Lucide 2-DIP stroke chevrons.");
        var bottom = toolbar.TranslatePoint(
            new Point(0, toolbar.ActualHeight), this).Y;
        var gridTop = RelayEventGrid.TranslatePoint(new Point(), this).Y;
        if (gridTop < bottom || gridTop - bottom > 9)
            throw new InvalidOperationException(
                $"Event Journal grid is not immediately below the toolbar: " +
                $"toolbar-bottom={bottom:0.0}, grid-top={gridTop:0.0}.");
    }

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

    private void SaveRealWpfElementBitmap(string output, string filename, FrameworkElement visual)
    {
        visual.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using var stream = File.Create(Path.Join(output, filename));
        encoder.Save(stream);
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
