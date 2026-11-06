// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ARIEC60870.Monitor;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

/// <summary>Passive live RX monitoring; full raw journaling is explicit opt-in.</summary>
public partial class MainWindow
{
    private const int PassiveTraceTabIndex = 1;
    private const int MaxVisiblePassiveRows = 600;
    private readonly ObservableCollection<PassiveRxRow> _passiveRawRows = new();
    private DispatcherTimer? _passiveUiTimer;
    private bool _passiveUiRefreshInFlight;
    private int _passiveCotWidth = 2;
    private int _passiveCaWidth = 2;
    private int _passiveIoaWidth = 3;
    private PassiveMonitorSession? _passiveMonitorSession;
    private VirtualInlineRelaySession? _virtualRelaySession;
    private PassiveProcessEventHistory? _passiveProcessEventHistory;
    private bool _passiveLedgerOwnsProcessEvents;
    private bool _passiveLedgerFactProjection;
    private bool _passiveLedgerChangedAtSource;
    private bool _passiveLedgerHadBaselineAtSource;
    private bool _passiveLedgerReadFailed;
    private long _passiveLedgerCursor;
    private long _passiveLedgerPresentationSkipped;
    private long _passiveLedgerProjectionRejected;
    private Task<(string Directory, PassiveCaptureManifest Manifest)>? _passiveCaptureTask;
    private (string Directory, PassiveCaptureManifest Manifest)? _passiveCaptureResult;
    private bool _closingAfterPassiveStop;
    private Iec10xAsduDecoder? _passiveAsduDecoder;
    // Typed, bounded operator interpretation. Never drives TX or the raw journal.
    private readonly Iec10xObservedControlTimeline _passiveControlTimeline = new();
    private long _passiveFrameSequence;
    private long _passiveUndecodableShown;
    private PassiveIec101TransactionCorrelator? _passiveReplayCorrelator;
    private PassiveIec101GiTracker? _passiveReplayGiTracker;
    private PassiveIec101FailureTimeline? _passiveReplayTimeline;
    private PassiveIec101FailureTimeline[]? _passiveReplayTimelines;
    private readonly Dictionary<(byte Stream, long Offset), PassiveGiEvent> _passiveGiNotes = new();
    private readonly Dictionary<(byte Stream, long Offset), PassiveControlEvidence> _passiveControlNotes = new();
    private readonly Dictionary<(byte Stream, long Offset), PassiveTransactionEvent> _passiveTransactionNotes = new();
    // GUI-only aggregation; all events remain durable in transactions.jsonl.
    private readonly Dictionary<(PassiveTransactionKind Kind, int? Address), DateTime> _passiveFindingLastShown = new();
    private DateTime _passiveFindingWindowEndUtc = DateTime.MinValue;
    private int _passiveFindingWindowCount;
    private int _passiveLinkAddressWidth;
    private readonly bool[] _passiveAutoLinkWidthReported = new bool[4];
    // Physical stream identity is immutable. Protocol-confirmed primary/secondary
    // direction is a separate property: ports may be selected in either order.
    private readonly PassiveIec101PortRoles _passivePortRoles = new();
    private string _passiveOriginalSubtitle = string.Empty;
    private string _passiveRoleStatus = string.Empty;
    private DateTime _passiveCaptureStartUtc;
    private bool _passiveSilenceReported;

    private void UpdatePassiveAutoRoleStatus()
    {
        if (_virtualRelaySession is not null)
        {
            PassiveScadaRoleLabel.Text = _virtualRelaySession.GetProgress().MasterToSlave.IngressPort +
                " · Master → Slave";
            PassiveRtuRoleLabel.Text = _virtualRelaySession.GetProgress().SlaveToMaster.IngressPort +
                " · Slave → Master";
            return;
        }
        var status = _passivePortRoles.DescribeAll();
        if (status == _passiveRoleStatus) return;
        _passiveRoleStatus = status;
        PassiveScadaRoleLabel.Text = ((PortComboBox.SelectedItem as string) ?? "Input 1") +
            " · " + _passivePortRoles.KnownDirectionLabel(0);
        PassiveRtuRoleLabel.Text = ((PassiveRtuPortComboBox.SelectedItem as string) ?? "Input 2") +
            " · " + _passivePortRoles.KnownDirectionLabel(1);
        // Do not mutate port configuration, underlying COM handles, or journal
        // source IDs. Only the operator description follows proven FT1.2 roles.
        if (_passiveOriginalSubtitle.Length > 0)
            SessionSubtitleText.Text = _passiveOriginalSubtitle + " · " + status;
    }
    private PassiveTimelineEvidenceNavigator? _passiveAnomalyNavigator;
    private string? _passiveAnomalyDirectory;
    private long? _passiveAnomalyBeforeOffset;
    private bool _passiveAnomalyLoading;
    private bool _passiveAnomalyHasOlder;
    private CancellationTokenSource? _passiveAnomalyReadCancellation;
    private PassiveReplayProfile? _passiveLastReplayProfile;
    private string? _passiveLastReplayDirectory;

    private sealed record PassiveAnomalyChoice(PassiveTimelineHit Hit)
    {
        public string Label => Hit.Label;
    }


    public ObservableCollection<PassiveRxRow> PassiveRawRows => _passiveRawRows;

    /// <summary>Windows CI verifies the actual integrated Setup-to-Trace workflow without any COM calls.</summary>
    public void RunPassiveGuiSmokeProbe()
    {
        var index = -1;
        for (var i = 0; i < ProtocolModeComboBox.Items.Count; i++)
        {
            if ((ProtocolModeComboBox.Items[i] as ComboBoxItem)?.Content?.ToString()?
                .Contains("Passive Monitor", StringComparison.OrdinalIgnoreCase) == true)
            {
                index = i;
                break;
            }
        }
        if (index < 0) throw new InvalidOperationException("Passive IEC-101 profile absent from existing Setup dropdown.");
        ProtocolModeComboBox.SelectedIndex = index;
        if (!IsIec101PassiveMonitorSelected() ||
            PassiveSecondaryPortPanel.Visibility != Visibility.Visible ||
            PassiveSetupRightPanel.Visibility != Visibility.Visible ||
            ActiveSetupRightPanel.Visibility != Visibility.Collapsed ||
            ActiveSerialHandshakePanel.Visibility != Visibility.Collapsed ||
            CommandDockPanel.Visibility != Visibility.Collapsed ||
            MainTabControl.SelectedIndex != PassiveTraceTabIndex ||
            NavFrameButton.Tag?.ToString() != "1" ||
            NavEventButton.Visibility != Visibility.Visible ||
            NavValueButton.Visibility != Visibility.Visible ||
            NavOperatorButton.Visibility != Visibility.Collapsed ||
            NavTriggersButton.Visibility != Visibility.Collapsed ||
            NavFindingsButton.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Passive mode is not correctly embedded in Setup/Trace; active controls remain visible.");

        // The entire existing evidence toolbox must remain on the left rail;
        // this explicitly catches the regression that hid capture/export/Signals.
        foreach (var tool in new System.Windows.Controls.Control[]
        {
            ExportReportButton, OpenCaptureButton, SaveCaptureRailButton,
            ExportTraceRailButton, AutoScrollLatestToggleButton, ClearButton,
            SignalListButton, HelpButton
        })
            if (tool.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Passive profile unexpectedly hid an existing field tool: " + tool.Name);
        if (ActionRailScroll.Visibility != Visibility.Visible ||
            !ActionRailScroll.IsMeasureValid)
            throw new InvalidOperationException("Existing tool rail is not accessible.");

        // Exercise the actual config builder with no physical COM opening.
        PortComboBox.Items.Add("COM201");
        PortComboBox.SelectedItem = "COM201";
        PassiveRtuPortComboBox.Items.Add("COM202");
        PassiveRtuPortComboBox.SelectedItem = "COM202";
        BaudComboBox.Text = "1200";
        SelectComboContent(SerialModeComboBox, "8E2");
        PassiveFrameGapBox.Text = "1500";
        SelectComboContent(PassiveLinkModeCombo, "Balanced");
        PassiveCaptureFolderBox.Text = Path.GetTempPath();
        var profile = BuildPassiveCaptureOptions();
        if (profile.RecordFullSession)
            throw new InvalidOperationException("Passive Start Monitor must be live-only by default.");
        PassiveFullRecordingCheckBox.IsChecked = true;
        if (!BuildPassiveCaptureOptions().RecordFullSession)
            throw new InvalidOperationException("Explicit full raw session recording option did not reach engine.");
        PassiveFullRecordingCheckBox.IsChecked = false;
        if (profile.ScadaTx.PortName != "COM201" || profile.RtuTx.PortName != "COM202" ||
            profile.ScadaTx.StopBits != StopBits.Two || profile.RtuTx.StopBits != StopBits.Two ||
            profile.ScadaTx.Parity != Parity.Even || profile.FrameGapMilliseconds != 1500 ||
            profile.LinkMode != "Balanced" || profile.ScadaTx.DataBits != 8)
            throw new InvalidOperationException("IEC-101 passive serial/config profile was not applied.");

        // P3: select both redundant circuits within existing Setup. All
        // four COM identities must survive the profile builder; legacy mode
        // remains two RX ports when the option is unchecked.
        PassiveLinkBScadaPortComboBox.Items.Add("COM203");
        PassiveLinkBScadaPortComboBox.SelectedItem = "COM203";
        PassiveLinkBRtuPortComboBox.Items.Add("COM204");
        PassiveLinkBRtuPortComboBox.SelectedItem = "COM204";
        PassiveLinkBEnabledCheckBox.IsChecked = true;
        if (PassiveLinkBPortsPanel.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Four-RX Link B Setup controls are not visible.");
        var four = BuildPassiveCaptureOptions();
        if (four.StreamCount != 4 ||
            four.Sources[2].PortName != "COM203" ||
            four.Sources[3].PortName != "COM204")
            throw new InvalidOperationException("P3 redundant receive-only configuration was not applied.");
        PassiveLinkBEnabledCheckBox.IsChecked = false;
        if (BuildPassiveCaptureOptions().StreamCount != 2 ||
            PassiveLinkBPortsPanel.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Legacy two-RX mode was not restored.");

        // Verify the offline replay profile uses ONLY IEC-101 interpretation fields;
        // it does not require selected COM ports, Start or an active Master engine.
        SelectComboContent(PassiveLinkWidthCombo, "2");
        SelectComboContent(PassiveCotWidthCombo, "1");
        SelectComboContent(PassiveCaWidthCombo, "1");
        SelectComboContent(PassiveIoaWidthCombo, "2");
        var replayProfile = ReadPassiveSetupReplayProfile();
        if (replayProfile.LinkAddressWidth != 2 || replayProfile.CotWidth != 1 ||
            replayProfile.CommonAddressWidth != 1 ||
            replayProfile.InformationAddressWidth != 2 ||
            replayProfile.FrameGapMilliseconds != 1500 ||
            replayProfile.LinkMode != "Balanced")
            throw new InvalidOperationException("P2C offline decoder Setup profile is not wired correctly.");

        // End-to-end GUI probe: real FT1.2 user-data frame → existing IEC-101
        // decoder → normal Frame Trace, Values, and Event Log. No hardware opened.
        var asdu = new byte[] { 1, 1, 3, 0, 1, 0, 7, 0, 0, 1 };
        var payload = new byte[] { 0x08, 0x69 }.Concat(asdu).ToArray();
        var testFrame = new byte[] { 0x68, (byte)payload.Length, (byte)payload.Length, 0x68 }
            .Concat(payload)
            .Concat(new byte[] { (byte)(payload.Sum(x => x) & 0xff), 0x16 })
            .ToArray();
        _passiveLinkAddressWidth = 1;
        _passiveAsduDecoder = new Iec10xAsduDecoder(2, 2, 3);
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(1, 0, DateTime.UtcNow,
            "Variable", true, testFrame));
        FlushUiQueues();
        if (FrameTraceRows.Count == 0 || ValueRows.Count == 0 || RelayEventRows.Count == 0)
            throw new InvalidOperationException("FT1.2 passive response was not projected to Frame Trace, Values and Event Log.");
        if (!FrameTraceRows.Any(x => x.TypeId == "1" && x.RawHex.Contains("680C0C68", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Frame Trace did not retain interpreted Type 1 with full wire HEX.");

        // HOLD is a viewport policy, not a loss/drop policy. Verify the real
        // WPF Trace list accepts a new RX while auto-follow has been disabled.
        var framesBeforeHold = FrameTraceRows.Count;
        EnterLineMonitorHoldForUserSelection();
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(1, 140,
            DateTime.UtcNow.AddMilliseconds(20), "Variable", true, testFrame));
        FlushUiQueues();
        if (FrameTraceRows.Count <= framesBeforeHold ||
            AutoScrollLatestToggleButton.IsChecked != false)
            throw new InvalidOperationException(
                "HOLD incorrectly stopped incoming frames from appearing in live Trace.");
        ResumeLiveViews(clearSelection: true, followLive: true);
        // Regression for real field screenshots: a selected Event must still
        // open its exact original frame even AFTER the bounded Trace rolls over.
        // The Event retains the typed evidence, and the Trace pin is read-only.
        var sourceEvent = RelayEventRows.FirstOrDefault(row => row.OriginalFrameEvidence is not null);
        if (sourceEvent is null)
            throw new InvalidOperationException("Event has no original frame evidence.");
        var originalReference = sourceEvent.EvidenceReference;
        _protocolTraceStore.Clear(); // simulate old frame evicted by routine polling
        MainTabControl.SelectedIndex = 3;
        RelayEventGrid.SelectedItem = sourceEvent;
        EventOpenFrame_Click(this, new RoutedEventArgs());
        if (MainTabControl.SelectedIndex != PassiveTraceTabIndex ||
            FrameTraceGrid.SelectedItem is not EvidenceRow pinned ||
            !Iec10xObservationIdentity.SamePhysicalFrame(pinned.Source,
                originalReference, sourceEvent.PhysicalSource,
                Iec60870ProtocolMode.Iec101, sourceEvent.Sequence) ||
            AutoScrollLatestToggleButton.IsChecked != false)
            throw new InvalidOperationException(
                "Event Open Frame did not pin the exact original after Trace eviction.");
        ResumeLiveViews(clearSelection: true, followLive: true);
        ClearSessionView(clearLog: true);

        // P2B: an actual request/reply pair must be reflected in the same Frame
        // Trace and interpreter, without spinning up a separate analyzer window.
        _passiveReplayCorrelator = new PassiveIec101TransactionCorrelator(
            new PassiveTransactionOptions(LinkMode: "Unbalanced"));
        var now = DateTime.UtcNow;
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(0, 0, now,
            "Fixed", true, new byte[] { 0x10, 0x49, 0x69, 0xB2, 0x16 }));
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(1, 0, now.AddMilliseconds(60),
            "Fixed", true, new byte[] { 0x10, 0x0B, 0x69, 0x74, 0x16 }));
        FlushUiQueues();
        if (!FrameTraceRows.Any(x => x.Detail.Contains("Secondary link status response (FC11)",
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Passive request/reply correlation did not reach the existing Frame Trace.");
        _passiveReplayCorrelator = null;
        _passiveTransactionNotes.Clear();
        ClearSessionView(clearLog: true);

        // P2D: GI Type 100 activation/confirmation/termination must appear in
        // the EXISTING Frame Trace (no separate event window or simulated TX).
        _passiveReplayGiTracker = new PassiveIec101GiTracker();
        var giNow = DateTime.UtcNow;
        static byte[] GiWire(byte control, byte cot)
        {
            var asdu = new byte[] { 100, 1, cot, 0, 1, 0, 0, 0, 0, 20 };
            var body = new byte[] { control, 0x69 }.Concat(asdu).ToArray();
            return new byte[] { 0x68, (byte)body.Length, (byte)body.Length, 0x68 }
                .Concat(body)
                .Concat(new byte[] { (byte)(body.Sum(x => x) & 0xFF), 0x16 }).ToArray();
        }
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(0, 0, giNow,
            "Variable", true, GiWire(0x53, 6)));
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(1, 0, giNow.AddMilliseconds(40),
            "Variable", true, GiWire(0x08, 7)));
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(1, 18, giNow.AddMilliseconds(100),
            "Variable", true, GiWire(0x08, 10)));
        FlushUiQueues();
        if (!FrameTraceRows.Any(x => x.Detail.Contains("GI Completed", StringComparison.Ordinal)))
            throw new InvalidOperationException("GI activation-to-ACTTERM lifecycle did not reach existing Frame Trace.");
        _passiveReplayGiTracker = null;
        _passiveReplayTimeline = null;
        _passiveGiNotes.Clear();
        ClearSessionView(clearLog: true);

        // R1: actual typed Master SELECT and RTU negative ACTCON must explain
        // the failed control directly in the existing Trace (no extra window).
        _passiveControlTimeline.Reset();
        static byte[] ControlWire(byte control, byte cot, byte qualifier)
        {
            var asdu = new byte[] { 46, 1, cot, 0, 1, 0, 123, 0, 0, qualifier };
            var body = new byte[] { control, 0x69 }.Concat(asdu).ToArray();
            return new byte[] { 0x68, (byte)body.Length, (byte)body.Length, 0x68 }
                .Concat(body)
                .Concat(new byte[] { (byte)(body.Sum(x => x) & 0xFF), 0x16 }).ToArray();
        }
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(0, 0, giNow.AddMilliseconds(300),
            "Variable", true, ControlWire(0x53, 6, 0x82)));
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(1, 100, giNow.AddMilliseconds(340),
            "Variable", true, ControlWire(0x08, 0x47, 0x82)));
        FlushUiQueues();
        if (!FrameTraceRows.Any(x => x.ProtocolTraceMeaning.Contains(
                "RTU NEGATIVE CONFIRMATION (P/N=1, COT=7", StringComparison.Ordinal) &&
            x.ProtocolTraceTitle.Contains("Command rejected · negative ACTCON", StringComparison.Ordinal)))
            throw new InvalidOperationException("Passive control negative ACTCON was not explained in Frame Trace.");
        if (!FrameTraceRows.Any(x => x.ProtocolTraceMeaning.Contains(
                "MASTER SELECT COT=6", StringComparison.Ordinal)))
            throw new InvalidOperationException("Passive Master SELECT command sequence was not explained.");
        _passiveControlTimeline.Reset();
        ClearSessionView(clearLog: true);

        // P2E WPF integration: existing Capture status receives a 5-second
        // evidence summary without adding navigation or opening COM hardware.
        var timelineProbe = new PassiveIec101FailureTimeline(giNow);
        timelineProbe.ObserveFrame(new PassiveObservedFrame(0, 0, giNow,
            "Fixed", true, new byte[] { 0x10, 0x49, 0x69, 0xB2, 0x16 }),
            ApplyPassiveTimelineWindow);
        timelineProbe.AdvanceTime(giNow.AddSeconds(5), ApplyPassiveTimelineWindow);
        // The first interval contains FC9; the newly started second interval
        // is intentionally empty. Assert before flushing that empty tail.
        if (!PassiveStatusMessageText.Text.Contains("LinkStatus=1", StringComparison.Ordinal))
            throw new InvalidOperationException("P2E failure timeline did not reach the existing Capture view.");
        timelineProbe.Complete(ApplyPassiveTimelineWindow);

        // Regression: an unexpected serial byte sequence and a correct 2-octet
        // Link Status observed under a 1-octet Setup MUST appear in Frame Trace.
        // These are actual protocol evidence rows, not merely a raw-byte count.
        _passiveLinkAddressWidth = 1;
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(0, 0, giNow.AddMilliseconds(200),
            "Noise", false, new byte[] { 0x99, 0x88 }));
        FeedPassiveFrameToExistingViews(new PassiveObservedFrame(0, 2, giNow.AddMilliseconds(220),
            "LinkWidthMismatch", true,
            new byte[] { 0x10, 0x49, 0x69, 0x01, 0xB3, 0x16 }, 2));
        FlushUiQueues();
        if (!FrameTraceRows.Any(x => x.Detail.Contains("Unrecognized serial bytes", StringComparison.Ordinal)) ||
            !FrameTraceRows.Any(x => x.Detail.Contains("Link Address width mismatch", StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "Passive malformed bytes / alternate-width Link Status are missing from Frame Trace.");
        ClearSessionView(clearLog: true);

        ProtocolModeComboBox.SelectedIndex = 1; // existing active IEC-101 profile
        // OBS Stage B intentionally starts with the command dock closed.
        // Active mode must expose the deliberate Commands affordance and be
        // able to open its dock, not force a permanent 310px obstruction.
        if (IsIec101PassiveMonitorSelected() ||
            ActiveSetupRightPanel.Visibility != Visibility.Visible ||
            WorkspaceCommandButton.Visibility != Visibility.Visible ||
            NavFrameButton.Tag?.ToString() != "1")
            throw new InvalidOperationException("Active Master layout lost its explicit Commands affordance.");
        var dockPreference = _commandDockExpanded;
        _commandDockExpanded = true;
        ApplyCommandDockLayout();
        if (CommandDockPanel.Visibility != Visibility.Visible ||
            CommandDockColumn.Width.Value < 280)
            throw new InvalidOperationException("Active Master command dock could not be explicitly opened.");
        _commandDockExpanded = dockPreference;
        ApplyCommandDockLayout();
        ProtocolModeComboBox.SelectedIndex = index;
    }

    private void PassiveLinkBSelectionChanged(object sender, RoutedEventArgs e) =>
        ApplyPassiveSourceLayout();

    private bool IsVirtualInlineSelected() => PassiveSourceComboBox?.SelectedIndex == 1;

    private void PassiveSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ApplyPassiveSourceLayout();

    private void ApplyPassiveSourceLayout()
    {
        if (PassiveLinkBPortsPanel is null || PassiveCaptureSourcePanel is null ||
            PassiveLinkBEnabledCheckBox is null || PassiveRtuPortLabel is null)
            return;
        var virtualMode = IsVirtualInlineSelected();
        PassiveVirtualHelpText.Visibility = virtualMode ? Visibility.Visible : Visibility.Collapsed;
        PassiveLinkBEnabledCheckBox.Visibility = virtualMode ? Visibility.Collapsed : Visibility.Visible;
        PassiveLinkBPortsPanel.Visibility = !virtualMode && PassiveLinkBEnabledCheckBox.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
        PassiveRtuPortLabel.Text = virtualMode ? "SLAVE-SIDE RELAY · COM" : "Link A · RTU TX";
        SerialConnectionTitleText.Text = virtualMode ? "MASTER-SIDE RELAY · COM" : "SCADA TX · COM port";
        SetupConnectButton.Content = virtualMode ? "Start Relay & Monitor" : "Start Monitor";
        if (IsIec101PassiveMonitorSelected()) UpdateConnectToggleVisual(_sessionCancellation is not null);
    }

    private bool IsIec101PassiveMonitorSelected() =>
        ((ProtocolModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty)
            .Contains("Passive Monitor", StringComparison.OrdinalIgnoreCase);

    private static string DefaultPassiveCaptureDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "ARIEC60870", "PassiveCaptures");
    }

    /// <summary>Preserve other protocol settings while recording the passive profile independently.</summary>
    private void SavePassiveSetupPreferences(bool silent)
    {
        try
        {
            var path = SetupPreferencesPath;
            SetupPreferences prefs;
            try
            {
                prefs = File.Exists(path)
                    ? JsonSerializer.Deserialize<SetupPreferences>(File.ReadAllText(path, Encoding.UTF8)) ?? new SetupPreferences()
                    : new SetupPreferences();
            }
            catch (JsonException)
            {
                prefs = new SetupPreferences();
            }

            prefs.ProtocolMode = "Iec101Passive";
            prefs.PassiveCaptureSource = IsVirtualInlineSelected()
                ? "Virtual Inline (VSPE)" : "Physical RX-only";
            prefs.PortName = (PortComboBox.SelectedItem as string)?.Trim() ?? string.Empty;
            prefs.PassiveRtuPortName = (PassiveRtuPortComboBox.SelectedItem as string)?.Trim() ?? string.Empty;
            prefs.PassiveLinkBEnabled = PassiveLinkBEnabledCheckBox.IsChecked == true;
            prefs.PassiveLinkBScadaPortName = (PassiveLinkBScadaPortComboBox.SelectedItem as string)?.Trim() ?? string.Empty;
            prefs.PassiveLinkBRtuPortName = (PassiveLinkBRtuPortComboBox.SelectedItem as string)?.Trim() ?? string.Empty;
            prefs.PassiveOutputDirectory = PassiveCaptureFolderBox.Text.Trim();
            prefs.PassiveLinkWidth = ReadComboInt(PassiveLinkWidthCombo, "Passive link address width");
            prefs.PassiveCotWidth = ReadComboInt(PassiveCotWidthCombo, "Passive COT width");
            prefs.PassiveCaWidth = ReadComboInt(PassiveCaWidthCombo, "Passive CA width");
            prefs.PassiveIoaWidth = ReadComboInt(PassiveIoaWidthCombo, "Passive IOA width");
            prefs.PassiveFrameGapMilliseconds = ReadInt(PassiveFrameGapBox, "Passive frame gap", 100, 10000);
            prefs.PassiveLinkMode = (PassiveLinkModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Auto";
            if (int.TryParse(BaudComboBox.Text, out var baud)) prefs.BaudRate = baud;
            prefs.SerialMode = (SerialModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "8E1";
            prefs.SavedUtc = DateTime.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            if (!silent) AppendSessionLog("Passive two-COM setup saved.");
        }
        catch (Exception ex)
        {
            if (!silent)
                ModernMessageBox.Show(this, ex.Message, "Passive setup save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private PassiveCaptureOptions BuildPassiveCaptureOptions()
    {
        var first = (PortComboBox.SelectedItem as string)?.Trim() ?? string.Empty;
        var second = (PassiveRtuPortComboBox.SelectedItem as string)?.Trim() ?? string.Empty;
        var baud = ReadComboInt(BaudComboBox, "Baudrate");
        var serial = (SerialModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "8E1";
        var parity = serial switch
        {
            "8E1" or "8E2" => Parity.Even,
            "8N1" or "8N2" => Parity.None,
            "8O1" or "8O2" => Parity.Odd,
            _ => throw new InvalidOperationException("Choose a valid 8-bit serial mode: E/N/O parity, 1 or 2 stop bits.")
        };
        var stopBits = serial.EndsWith("2", StringComparison.Ordinal) ? StopBits.Two : StopBits.One;
        var output = string.IsNullOrWhiteSpace(PassiveCaptureFolderBox.Text)
            ? DefaultPassiveCaptureDirectory() : PassiveCaptureFolderBox.Text.Trim();
        static int Width(ComboBox combo) => int.Parse(
            (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);
        var isVirtual = IsVirtualInlineSelected();
        var isLinkB = !isVirtual && PassiveLinkBEnabledCheckBox.IsChecked == true;
        var bScada = (PassiveLinkBScadaPortComboBox.SelectedItem as string)?.Trim() ?? string.Empty;
        var bRtu = (PassiveLinkBRtuPortComboBox.SelectedItem as string)?.Trim() ?? string.Empty;
        if (isLinkB && (string.IsNullOrWhiteSpace(bScada) || string.IsNullOrWhiteSpace(bRtu)))
            throw new InvalidOperationException("Select both Link B SCADA TX and RTU TX receiver COM ports.");
        var options = new PassiveCaptureOptions(output,
            new PassiveSerialSource(first, isVirtual ? "Virtual Master -> Slave ingress" :
                "Link A SCADA TX -> RTU", baud, parity, 8, stopBits),
            new PassiveSerialSource(second, isVirtual ? "Virtual Slave -> Master ingress" :
                "Link A RTU TX -> SCADA", baud, parity, 8, stopBits),
            LinkAddressWidth: Width(PassiveLinkWidthCombo),
            CotWidth: Width(PassiveCotWidthCombo),
            CommonAddressWidth: Width(PassiveCaWidthCombo),
            InformationAddressWidth: Width(PassiveIoaWidthCombo),
            FrameGapMilliseconds: ReadInt(PassiveFrameGapBox, "Passive frame gap", 100, 10000),
            LinkMode: (PassiveLinkModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Auto",
            LinkBScadaTx: isLinkB ? new PassiveSerialSource(bScada, "Link B SCADA TX -> RTU", baud, parity, 8, stopBits) : null,
            LinkBRtuTx: isLinkB ? new PassiveSerialSource(bRtu, "Link B RTU TX -> SCADA", baud, parity, 8, stopBits) : null,
            RecordFullSession: PassiveFullRecordingCheckBox.IsChecked == true,
            CaptureSource: isVirtual ? "Virtual Inline (VSPE)" : "Physical RX-only");
        options.Validate();

        var directory = Path.GetFullPath(output);
        var diskRoot = Path.GetPathRoot(directory)!;
        if (options.RecordFullSession && Directory.Exists(diskRoot) &&
            new DriveInfo(diskRoot).AvailableFreeSpace < 200L * 1024 * 1024)
            throw new IOException("Capture drive has less than 200 MB available.");
        return options;
    }

    private void PassiveBrowseCaptureFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog
        {
            Title = "Select IEC-101 passive capture directory",
            Multiselect = false
        };
        if (Directory.Exists(PassiveCaptureFolderBox.Text))
            picker.InitialDirectory = PassiveCaptureFolderBox.Text;
        if (picker.ShowDialog(this) == true)
            PassiveCaptureFolderBox.Text = picker.FolderName;
    }

    /// <summary>The existing main Connect/Stop action, explicitly isolated from active IEC-101 Master.</summary>
    private async Task RunPassiveMonitorAsync()
    {
        if (_sessionCancellation is not null) return;
        PassiveCaptureOptions options;
        try { options = BuildPassiveCaptureOptions(); }
        catch (Exception ex)
        {
            ModernMessageBox.Show(this, ex.Message, "Passive monitor setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var virtualMode = IsVirtualInlineSelected();
        SavePassiveSetupPreferences(silent: true);
        // Each new session removes the previous session's temporary disk cache.
        // Full raw recording, when opted in, is separately preserved.
        ReleaseSessionTraceHistory();
        _sessionCancellation = new CancellationTokenSource();
        _virtualRelaySession = virtualMode
            ? new VirtualInlineRelaySession(options.ScadaTx, options.RtuTx) : null;
        var sources = _virtualRelaySession is { } virtualRelay
            ? virtualRelay.CreateMonitorTaps()
            : options.Sources.Select(x => (IReadOnlySerialTap)new WindowsReadOnlySerialTap(x)).ToArray();
        var session = new PassiveMonitorSession(options, sources);
        PassiveSourceComboBox.IsEnabled = false;
        _passiveMonitorSession = session;
        _passiveCaptureResult = null;
        ClearSessionView(clearLog: true);
        _passiveCaptureStartUtc = DateTime.UtcNow;
        _passiveSilenceReported = false;
        UpdatePassiveTraceEmptyState(session.GetProgress(), options.ScadaTx.PortName,
            options.RtuTx.PortName, options.StreamCount == 4
                ? $"{options.LinkBScadaTx!.PortName}/{options.LinkBRtuTx!.PortName}" : "");
        PrepareLiveTraceForConnect();
        _passiveRawRows.Clear();
        _passiveFrameSequence = 0;
        _passiveLedgerOwnsProcessEvents = false;
        _passiveLedgerFactProjection = false;
        _passiveLedgerReadFailed = false;
        _passiveLedgerCursor = 0;
        _passiveLedgerPresentationSkipped = 0;
        _passiveLedgerProjectionRejected = 0;
        _passiveControlTimeline.Reset();
        _passivePortRoles.Reset();
        _passiveRoleStatus = string.Empty;
        PassiveScadaRoleLabel.Text = "Input 1 · auto detecting";
        PassiveRtuRoleLabel.Text = "Input 2 · auto detecting";
        _passiveControlNotes.Clear();
        _passiveUndecodableShown = 0;
        _passiveReplayCorrelator = null;
        _passiveReplayGiTracker = null;
        _passiveReplayTimeline = null;
        _passiveReplayTimelines = null;
        _passiveGiNotes.Clear();
        _passiveTransactionNotes.Clear();
        _passiveFindingLastShown.Clear();
        _passiveFindingWindowEndUtc = DateTime.MinValue;
        _passiveFindingWindowCount = 0;
        _passiveAsduDecoder = new Iec10xAsduDecoder(
            options.CotWidth, options.CommonAddressWidth, options.InformationAddressWidth);
        _passiveCotWidth = options.CotWidth;
        _passiveCaWidth = options.CommonAddressWidth;
        _passiveIoaWidth = options.InformationAddressWidth;
        _passiveLinkAddressWidth = options.LinkAddressWidth;
        Array.Clear(_passiveAutoLinkWidthReported);
        AppendSessionLog($"{options.CaptureSource} IEC-101 profile: {options.LinkMode}; Link={options.LinkAddressWidth}, COT={options.CotWidth}, CA={options.CommonAddressWidth}, IOA={options.InformationAddressWidth}; gap={options.FrameGapMilliseconds} ms.");
        if (virtualMode) AppendSessionLog(
            $"ACTIVE virtual byte relay {options.ScadaTx.PortName} ↔ {options.RtuTx.PortName}; " +
            "COM Open is not proof that the two external VSPE peers are receiving bytes.");
        PassiveScadaCountText.Text = "0 bytes";
        PassiveRtuCountText.Text = "0 bytes";
        PassiveIntegrityText.Text = virtualMode ? "Opening virtual relay..." : "Opening receivers...";
        PassiveStatusMessageText.Text = virtualMode
            ? "Active VSPE relay · byte forwarding ON · peer receipt not verified"
            : options.RecordFullSession
                ? "Opening monitor with full raw journal…" : "Opening live monitor · recording OFF";
        PassiveCapturePathText.Text = options.RecordFullSession
            ? options.OutputDirectory : "Live monitor · no recording";
        PassiveSelectedRawText.Text = "Select a received raw-byte chunk.";
        PassiveVerifyButton.IsEnabled = false;
        PassiveFreezeCheckBox.IsChecked = false;
        SetRunUiState(isRunning: true);
        MainTabControl.SelectedIndex = PassiveTraceTabIndex;
        UpdateSegmentedNav(false);
        // Never inherit a hidden focus/search filter from the prior capture.
        // Every fresh physical monitor starts on all raw TX/RX.
        ClearTraceQuickFindForNavigation();
        SessionSubtitleText.Text = virtualMode
            ? $"Virtual IEC-101 inline · {options.ScadaTx.PortName} ↔ {options.RtuTx.PortName}"
            : options.StreamCount == 4
                ? $"Passive IEC-101 A/B · A {options.ScadaTx.PortName}/{options.RtuTx.PortName} · B {options.LinkBScadaTx!.PortName}/{options.LinkBRtuTx!.PortName}"
                : $"Passive IEC-101 Link A · {options.ScadaTx.PortName}/{options.RtuTx.PortName}";
        _passiveOriginalSubtitle = SessionSubtitleText.Text;
        UpdatePassiveAutoRoleStatus();
        UpdateStableHeader("Monitoring", virtualMode
            ? "ACTIVE VSPE relay · peer receipt not verified." : options.RecordFullSession
                ? "Receive-only monitoring · full raw journal enabled." : "Receive-only monitoring · recording OFF.");
        AppendSessionLog("Passive monitor started: " + SessionSubtitleText.Text +
            (options.RecordFullSession ? " · full raw journal ON" : " · temporary session Trace, no permanent recording"));
        _passiveUiTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(160)
        };
        _passiveUiTimer.Tick -= PassiveUiTimer_Tick;
        _passiveUiTimer.Tick += PassiveUiTimer_Tick;
        _passiveUiTimer.Start();

        try
        {
            _passiveCaptureTask = session.RunAsync(_sessionCancellation.Token);
            _passiveFrameHistory = session.TraceHistory;
            _passiveProcessEventHistory = session.ProcessEventHistory;
            _passiveLedgerOwnsProcessEvents = _passiveProcessEventHistory is not null;
            var result = await _passiveCaptureTask;
            _passiveCaptureResult = result;
            // Let any in-flight prepare batch finish on its original session.
            // Never overlap two GUI consumers of the single-reader preview feed.
            while (_passiveUiRefreshInFlight)
                await Task.Delay(10);
            // Reconcile the latest 420 facts after source completion, even if
            // the preview ring had already overwritten earlier frames.
            for (var pass = 0; pass < 5; pass++)
                await RefreshPassiveUiAsync();
            FlushUiQueues();
            _passiveLedgerOwnsProcessEvents = false;
            UpdatePassiveTraceEmptyState(session.GetProgress(), options.ScadaTx.PortName,
                options.RtuTx.PortName, options.StreamCount == 4
                    ? $"{options.LinkBScadaTx!.PortName}/{options.LinkBRtuTx!.PortName}" : "");
            PassiveCapturePathText.Text = options.RecordFullSession
                ? result.Directory : "Live monitor · no recording";
            PassiveIntegrityText.Text = result.Manifest.Status;
            PassiveStatusMessageText.Text = result.Manifest.Warnings.Length > 0
                ? string.Join(" | ", result.Manifest.Warnings.Take(2))
                : options.RecordFullSession ? "Full journal saved. Select Verify to check it."
                : "Monitor stopped · no session recording was created. Save selected Trace frames separately.";
            PassiveVerifyButton.IsEnabled = options.RecordFullSession;
            AppendSessionLog(options.RecordFullSession
                ? "Passive raw journal finalized: " + result.Manifest.Status + " · " + result.Directory
                : "Passive live monitor ended; temporary Trace remains until next session or close: " + result.Manifest.Status);
            UpdateStableHeader(result.Manifest.Status is "MonitorStopped" or "NoApplicationLossDetected"
                    ? "Stopped" : "Degraded",
                options.RecordFullSession ? "Full journal saved · verification available." : "Live monitoring ended · no automatic recording.");
        }
        catch (Exception ex)
        {
            PassiveIntegrityText.Text = "Monitor failed";
            PassiveStatusMessageText.Text = ex.Message;
            UpdateStableHeader("Faulted", "Passive monitor failed: " + ex.Message);
            AppendSessionLog("Passive RX monitor failed: " + ex.Message);
        }
        finally
        {
            _passiveUiTimer?.Stop();
            if (_virtualRelaySession is { } completedRelay)
            {
                var transport = completedRelay.GetProgress();
                AppendSessionLog($"Virtual relay terminal: {transport.Status}; " +
                    $"M→S RX {transport.MasterToSlave.IngressBytes:N0} / TX {transport.MasterToSlave.WriteCompletedBytes:N0} B; " +
                    $"S→M RX {transport.SlaveToMaster.IngressBytes:N0} / TX {transport.SlaveToMaster.WriteCompletedBytes:N0} B." +
                    (transport.Fault is null ? "" : " FAULT: " + transport.Fault));
                completedRelay.Dispose();
                _virtualRelaySession = null;
            }
            PassiveSourceComboBox.IsEnabled = true;
            _passiveMonitorSession = null;
            _passiveCaptureTask = null;
            _sessionCancellation?.Dispose();
            _sessionCancellation = null;
            SetRunUiState(isRunning: false);
        }
    }

    private void StopPassiveMonitor()
    {
        if (_passiveMonitorSession is null || _sessionCancellation is null) return;
        PassiveIntegrityText.Text = "Stopping…";
        UpdateStableHeader("Stopping", _virtualRelaySession is null
            ? "Disconnecting passive receivers and finalizing optional journal."
            : "Stopping both virtual directions and finalizing monitor evidence.");
        _sessionCancellation.Cancel();
    }

    private async void PassiveUiTimer_Tick(object? sender, EventArgs e)
    {
        try { await RefreshPassiveUiAsync(); }
        catch (Exception ex)
        {
            // Never allow an async DispatcherTimer callback to take down WPF.
            AppendSessionLog("Passive UI projection failed: " + ex.Message);
            _sessionCancellation?.Cancel();
        }
    }

    // Presentation-only classification of physical UART RX versus FT1.2 framing.
    // Never confuse a started COM handle with bytes or valid process evidence.
    private void UpdatePassiveTraceEmptyState(PassiveMonitorProgress progress,
        string scadaPort, string rtuPort, string linkBPorts)
    {
        // Status remains visible even with fully decoded frames. The two byte
        // counters are not interchangeable: Read is at the driver boundary.
        var rxA = Math.Max(progress.ReaderScadaBytes, progress.ScadaBytes);
        var rxB = Math.Max(progress.ReaderRtuBytes, progress.RtuBytes);
        TracePassiveSourceStatusText.Text =
            $"{scadaPort} {rxA:N0} B · {rtuPort} {rxB:N0} B · FT1.2 {progress.ValidFt12Frames:N0}" +
            (progress.RawPreviewDropped > 0 ? $" · view skip {progress.RawPreviewDropped:N0}" : "") +
            (string.IsNullOrEmpty(progress.ReaderFaultPorts) ? "" :
                " · RX FAULT " + progress.ReaderFaultPorts);
        TracePassiveSourceStatusText.ToolTip =
            $"{scadaPort} RX/queued/written: {rxA:N0}/{progress.QueuedScadaBytes:N0}/{progress.ScadaBytes:N0} B\n" +
            $"{rtuPort} RX/queued/written: {rxB:N0}/{progress.QueuedRtuBytes:N0}/{progress.RtuBytes:N0} B\n" +
            (progress.StreamCount == 4 ? $"Link B {linkBPorts} RX {progress.ReaderLinkBScadaBytes:N0}/{progress.ReaderLinkBRtuBytes:N0} B\n" : "") +
            $"Valid FT1.2={progress.ValidFt12Frames:N0}; untrusted={progress.UntrustedFrameCandidates:N0}; pending={progress.IncompleteFrameBytes:N0} B\n" +
            $"Raw preview published={progress.RawPreviewPublished:N0}; consumed={progress.RawPreviewConsumed:N0}; skipped={progress.RawPreviewDropped:N0}. Preview is not an archive.";
        if (_virtualRelaySession is { } inlineRelay)
        {
            var p = inlineRelay.GetProgress();
            TracePassiveSourceStatusText.Text +=
                $" · TX {p.MasterToSlave.WriteCompletedBytes:N0}/{p.SlaveToMaster.WriteCompletedBytes:N0} B";
            TracePassiveSourceStatusText.ToolTip +=
                $"\nACTIVE relay TX attempted/completed/uncertain M→S: " +
                $"{p.MasterToSlave.WriteAttemptedBytes:N0}/{p.MasterToSlave.WriteCompletedBytes:N0}/{p.MasterToSlave.WriteUncertainBytes:N0} B" +
                $"\nACTIVE relay TX attempted/completed/uncertain S→M: " +
                $"{p.SlaveToMaster.WriteAttemptedBytes:N0}/{p.SlaveToMaster.WriteCompletedBytes:N0}/{p.SlaveToMaster.WriteUncertainBytes:N0} B" +
                $"\nNot a passive electrical tap. No peer-received proof.";
        }
        TracePassiveSourceStatusText.Visibility = Visibility.Visible;
        if (FrameTraceGrid.Items.Count > 0)
        {
            TracePassiveEmptyState.Visibility = Visibility.Collapsed;
            return;
        }
        var result = PassiveNoFrameDiagnosis.Evaluate(progress,
            scadaPort, rtuPort, linkBPorts,
            FrameTraceRows.Count, _pendingEvidence.Count);
        TracePassiveEmptyTitleText.Text = result.Title;
        TracePassiveEmptyDetailText.Text = result.Detail;
        // These are real UART read chunks, NOT synthetic FT1.2 frames.
        // A failed/stalled decoder must not leave an RX-active workstation blank.
        TracePassiveRawChunkGrid.Visibility = _passiveRawRows.Count == 0
            ? Visibility.Collapsed : Visibility.Visible;
        TracePassiveEmptyState.Visibility = Visibility.Visible;
        // One reportable diagnostic per silent session, not per UI timer tick.
        // Start status alone must not masquerade as verified wire acquisition.
        if (!_passiveSilenceReported && progress.Status == "Monitoring" &&
            Math.Max(progress.ReaderScadaBytes, progress.ScadaBytes) +
            Math.Max(progress.ReaderRtuBytes, progress.RtuBytes) +
            Math.Max(progress.ReaderLinkBScadaBytes, progress.LinkBScadaBytes) +
            Math.Max(progress.ReaderLinkBRtuBytes, progress.LinkBRtuBytes) == 0 &&
            DateTime.UtcNow - _passiveCaptureStartUtc >= TimeSpan.FromSeconds(5))
        {
            _passiveSilenceReported = true;
            AddUiDiagnostic("Warning", "IEC-101 RX-only", "IEC101-PASSIVE-NO-UART",
                "No UART bytes on passive receive inputs",
                result.Title + ". " + result.Detail,
                "Verify the COM adapters are tapped into the active SCADA TX and RTU TX circuits with reference ground, serial baud/parity/stop configuration, and approved isolation. Passive monitoring never sends a poll.");
        }
    }

    private async Task RefreshPassiveUiAsync()
    {
        var session = _passiveMonitorSession;
        if (session is null || _passiveUiRefreshInFlight) return;
        _passiveUiRefreshInFlight = true;
        try
        {
        var progress = session.GetProgress();
        UpdatePassiveTraceEmptyState(progress, session.SourcePortNames[0],
            session.SourcePortNames[1],
            progress.StreamCount == 4
                ? $"{session.SourcePortNames[2]}/{session.SourcePortNames[3]}" : "");
        PassiveScadaCountText.Text = Math.Max(progress.ReaderScadaBytes, progress.ScadaBytes).ToString("N0") + " RX bytes" +
            (progress.StreamCount == 4 ? $" · B {progress.LinkBScadaBytes:N0}" : "");
        PassiveRtuCountText.Text = Math.Max(progress.ReaderRtuBytes, progress.RtuBytes).ToString("N0") + " RX bytes" +
            (progress.StreamCount == 4 ? $" · B {progress.LinkBRtuBytes:N0}" : "");
        PassiveIntegrityText.Text = progress.IntegrityWarning
            ? "Integrity warning" : progress.Status == "Recording" ? "Full journal active"
            : progress.Status == "Monitoring" ? "Live only · no disk recording" : progress.Status;
        if (_virtualRelaySession is { } relay)
        {
            var transport = relay.GetProgress();
            PassiveScadaCountText.Text =
                $"{transport.MasterToSlave.IngressBytes:N0} RX / {transport.MasterToSlave.WriteCompletedBytes:N0} TX bytes";
            PassiveRtuCountText.Text =
                $"{transport.SlaveToMaster.IngressBytes:N0} RX / {transport.SlaveToMaster.WriteCompletedBytes:N0} TX bytes";
            PassiveStatusMessageText.Text = transport.Fault ??
                $"Virtual relay {transport.Status} · TX uncertain " +
                $"{transport.MasterToSlave.WriteUncertainBytes + transport.SlaveToMaster.WriteUncertainBytes:N0} B · " +
                $"monitor ingested {transport.MasterToSlave.ConsumedBytes + transport.SlaveToMaster.ConsumedBytes:N0} B";
            if (transport.Fault is not null)
                PassiveIntegrityText.Text = "Virtual relay fault · capture incomplete";
        }
        var eventProgress = session.GetProcessEventProgress();
        // These are typed process observations, not a claim that all are
        // physical SOEs or that missing remote FIFO events were transmitted.
        PassiveIntegrityText.Text += $" · OBS {eventProgress.AcceptedObjects:N0}" +
            $" ({eventProgress.IndexedObjects:N0} indexed)" +
            (eventProgress.UnindexedObjects > 0 ? $" · {eventProgress.UnindexedObjects:N0} unindexed" : "") +
            (eventProgress.PreviewFramesDropped > 0 ? $" · {eventProgress.PreviewFramesDropped:N0} frame previews skipped" : "") +
            (progress.RawPreviewDropped > 0 ? $" · {progress.RawPreviewDropped:N0} raw chunk previews skipped" : "") +
            (_passiveLedgerPresentationSkipped > 0 ? $" · {_passiveLedgerPresentationSkipped:N0} older event rows not visible" : "") +
            (eventProgress.DecodeAccounting is { } decode && decode.InvalidVariableFrames > 0
                ? $" · {decode.InvalidVariableFrames:N0} invalid FT1.2" : "") +
            (eventProgress.DecodeAccounting is { } counted && counted.AsdusRejected > 0
                ? $" · {counted.AsdusRejected:N0} rejected ASDUs" : "");
        PassiveIntegrityText.Foreground = (Brush)FindResource(
            progress.IntegrityWarning || eventProgress.UnindexedObjects > 0 ||
            _passiveLedgerReadFailed || eventProgress.DecodeAccounting is
                { InvalidVariableFrames: > 0 } or { AsdusRejected: > 0 }
                ? "BadBrush" : "Ink900Brush");
        if (!string.IsNullOrWhiteSpace(progress.CaptureDirectory))
            PassiveCapturePathText.Text = progress.CaptureDirectory;

        // Lossless correlator runs in the journal writer. The UI receives only its
        // bounded presentation events; the complete transaction trail is on disk.
        var correlated = 0;
        while (correlated < 240 && session.TryReadTransaction(out var entry))
        {
            ApplyPassiveTransactionEvent(entry);
            correlated++;
        }
        var giCount = 0;
        while (giCount < 96 && session.TryReadGiEvent(out var giEvent))
        {
            ApplyPassiveGiEvent(giEvent);
            giCount++;
        }

        // The writer emits every command stage BEFORE the bounded frame preview.
        // UI events remain bounded, but full recording keeps controls.jsonl.
        var controlCount = 0;
        while (controlCount < 240 && session.TryReadControl(out var controlNote))
        {
            if (_passiveControlNotes.Count >= 512)
                _passiveControlNotes.Remove(_passiveControlNotes.Keys.First());
            _passiveControlNotes[(controlNote.StreamId, controlNote.StreamOffset)] = controlNote;
            if (controlNote.NeedsAttention)
                AddRateLimitedDiagnostic("IEC101-PASSIVE-CONTROL-REJECTED", "Warning", "IEC-101",
                    "RTU control response needs investigation",
                    controlNote.Explanation +
                    $" · RX{controlNote.StreamId}, offset {controlNote.StreamOffset}.",
                    "Inspect matching Master request, Type, COT P/N, CA, IOA and S/E.",
                    TimeSpan.FromSeconds(1));
            controlCount++;
        }

        // Frame reconstruction is independent of the raw sample view and its Freeze flag.
        // Every observed frame is projected onto the EXISTING Evidence/Trace/Values/Event pipeline.
        // Only immutable byte->ASDU preparation leaves the Dispatcher. GUI event
        // projection remains sequential; frames are processed in source-feed order.
        // One bounded batch task per nonempty UI period, never one task per frame.
        var frames = new List<PassiveObservedFrame>(180);
        while (frames.Count < 180 && session.TryReadFrame(out var frame))
            frames.Add(frame);
        if (frames.Count > 0)
        {
            var linkWidth = _passiveLinkAddressWidth;
            var cotWidth = _passiveCotWidth;
            var caWidth = _passiveCaWidth;
            var ioaWidth = _passiveIoaWidth;
            var prepared = await Task.Run(() => PassiveFramePreparation.PrepareBatch(
                frames, linkWidth, cotWidth, caWidth, ioaWidth));
            foreach (var item in prepared)
                FeedPassiveFrameToExistingViews(item.Frame, prepared: item);
        }

        // Process facts are paged from the writer-owned indexed journal, NOT
        // from the 512-frame DropOldest UI preview. Page read and ASDU decode
        // run off Dispatcher, WPF projection stays on its existing UI thread.
        if (_passiveProcessEventHistory is { } history && !_passiveLedgerReadFailed)
        {
            try
            {
                var latest = session.GetProcessEventProgress().IndexedObjects;
                if (latest - _passiveLedgerCursor > 420)
                {
                    var skipped = latest - _passiveLedgerCursor - 420;
                    _passiveLedgerCursor += skipped;
                    _passiveLedgerPresentationSkipped += skipped;
                }
                var cursor = _passiveLedgerCursor;
                var link = _passiveLinkAddressWidth;
                var cot = _passiveCotWidth;
                var ca = _passiveCaWidth;
                var ioa = _passiveIoaWidth;
                var page = await Task.Run(() => PassiveProcessEventPreparation.PreparePage(
                    history.ReadAfter(cursor, 120), link, cot, ca, ioa));
                foreach (var fact in page)
                {
                    if (fact.Fact.Sequence != _passiveLedgerCursor + 1)
                    {
                        _passiveLedgerReadFailed = true;
                        AppendSessionLog("Process event index sequence mismatch; no guessed event promoted.");
                        break;
                    }
                    _passiveLedgerCursor = fact.Fact.Sequence;
                    if (!ProjectPassiveProcessEventFact(fact))
                        _passiveLedgerProjectionRejected++;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException)
            {
                _passiveLedgerReadFailed = true;
                AppendSessionLog("Process event journal read failed; original capture remains: " + ex.Message);
            }
        }

        // Dedicated five-second summaries, never per-frame rendering.
        var summaryCount = 0;
        while (summaryCount < 24 && session.TryReadTimelineWindow(out var window))
        {
            ApplyPassiveTimelineWindow(window);
            summaryCount++;
        }

        // The shared 75-ms coordinator alone flushes the WPF projection.
        // Passive Freeze only controls the optional raw-chunk preview.
        if (PassiveFreezeCheckBox.IsChecked == true) return;
        var count = 0;
        while (count < 65 && session.TryReadPreview(out var sample))
        {
            var row = new PassiveRxRow(
                sample.EstimatedUtc.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
                (sample.StreamId < 2 ? "A " : "B ") +
                _passivePortRoles.KnownDirectionLabel(sample.StreamId),
                sample.Sequence, sample.ByteCount, Convert.ToHexString(sample.FirstBytes));
            _passiveRawRows.Add(row);
            if (_passiveRawRows.Count > MaxVisiblePassiveRows)
                _passiveRawRows.RemoveAt(0);
            count++;
        }
        if (count > 0 && PassiveRawGrid.SelectedItem is null)
            PassiveRawGrid.ScrollIntoView(_passiveRawRows[^1]);
        // Counters and raw-byte samples are published after this tick's reads.
        // Update the zero-frame viewport once more with committed source truth.
        if (count > 0)
            UpdatePassiveTraceEmptyState(session.GetProgress(),
                session.SourcePortNames[0], session.SourcePortNames[1],
                progress.StreamCount == 4
                    ? $"{session.SourcePortNames[2]}/{session.SourcePortNames[3]}" : "");
        }
        finally { _passiveUiRefreshInFlight = false; }
    }


    /// <summary>
    /// Event Log consumes source-qualified writer facts. A valid event survives
    /// WPF preview rollover; event provenance is its original physical frame.
    /// </summary>
    private bool ProjectPassiveProcessEventFact(PassiveProcessEventPreparation.Prepared prepared)
    {
        if (!prepared.Trusted || prepared.Frame is null ||
            prepared.Asdu is not { } decode || prepared.Object is not { } obj)
            return false;
        var seen = prepared.Fact.Frame;
        if (PassiveIec101FrameRoles.Classify(seen, _passiveLinkAddressWidth) !=
            PassiveWireRole.Secondary)
            return false;
        var item = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = ++_passiveFrameSequence,
            TimestampUtc = seen.EstimatedUtc,
            Direction = FrameDirection.SlaveToMaster,
            Category = "Passive Point",
            PollingReason = "PASSIVE-LEDGER",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            RawHex = prepared.RawHex,
            PhysicalSource = seen.StreamId < 2 ? "Link A" : "Link B",
            PhysicalStreamId = seen.StreamId,
            RawStreamOffset = seen.StreamOffset,
            Frame = prepared.Frame,
            LinkAddress = prepared.Frame.LinkAddress,
            TypeId = decode.TypeId,
            TypeName = decode.TypeName,
            VariableStructureQualifier = decode.VariableStructureQualifier,
            IsSequenceAsdu = decode.IsSequence,
            ObjectCount = decode.ObjectCount,
            CauseOfTransmission = decode.CauseOfTransmission,
            CauseDetails = decode.CauseDetails,
            CommandDetails = decode.CommandDetails,
            AsduDetails = decode,
            InformationObjectDetails = obj,
            QualityDetails = obj.QualityDetails,
            QualityText = obj.QualityText,
            CauseName = decode.CauseName,
            OriginatorAddress = decode.OriginatorAddress,
            CommonAddressNumber = decode.CommonAddress,
            InformationObjectAddress = obj.InformationObjectAddress,
            ObjectSummary = obj.ReadableSummary,
            IsRelayValue = true,
            IsRelayEdgeEvent = decode.CauseOfTransmission == 3,
            SignalKey = $"L{seen.StreamId / 2}:{decode.CommonAddress}:{obj.InformationObjectAddress}",
            SignalName = $"IOA {obj.InformationObjectAddress}",
            SignalGroup = seen.StreamId < 2 ? "IEC-101 Link A" : "IEC-101 Link B",
            SignalType = decode.TypeName,
            SignalDisplayValue = obj.ShortValue,
            SignalRawValue = obj.ValueText,
            RelayTimestampText = obj.TimestampText,
            RelayTimestampInvalid = obj.TimestampText.Contains("invalid", StringComparison.OrdinalIgnoreCase)
        };
        _passiveLedgerFactProjection = true;
        _passiveLedgerChangedAtSource = prepared.Fact.WasChanged;
        _passiveLedgerHadBaselineAtSource = prepared.Fact.HadBaseline;
        try { UpdateValueAndEventViews(item); }
        finally
        {
            _passiveLedgerFactProjection = false;
            _passiveLedgerChangedAtSource = false;
            _passiveLedgerHadBaselineAtSource = false;
        }
        return true;
    }

    private EvidenceRow? FeedPassiveFrameToExistingViews(PassiveObservedFrame seen,
        bool inspectionOnly = false, PassiveFramePreparation.Prepared? prepared = null)
    {
        PassiveGiEvent? giNote = null;
        PassiveTransactionEvent? transaction = null;
        // Historical disk reads are projections, not new wire observations.
        if (!inspectionOnly)
        {
            // Replay has a full, verified stream and re-derives the same findings here.
            // Live capture derives findings upstream, before WPF's bounded frame queue.
            var replayTimeline = _passiveReplayTimelines is { } ts
                ? ts[seen.StreamId / 2] : _passiveReplayTimeline;
            var circuitId = seen.StreamId / 2;
            replayTimeline?.ObserveFrame(seen with { StreamId = (byte)(seen.StreamId % 2) },
                x => ApplyPassiveTimelineWindow(x with { CircuitId = circuitId }));
            _passiveReplayCorrelator?.Observe(seen, x =>
            {
                var txTimeline = _passiveReplayTimelines is { } all
                    ? all[x.ObservedStream / 2] : _passiveReplayTimeline;
                txTimeline?.ObserveTransaction(x with
                {
                    ObservedStream = (byte)(x.ObservedStream % 2)
                });
                ApplyPassiveTransactionEvent(x);
            });
            _passiveReplayGiTracker?.Observe(seen, x =>
            {
                var giTimeline = _passiveReplayTimelines is { } all
                    ? all[x.StreamId / 2] : _passiveReplayTimeline;
                giTimeline?.ObserveGi(x with { StreamId = (byte)(x.StreamId % 2) });
                ApplyPassiveGiEvent(x);
            });
            _passiveGiNotes.TryGetValue((seen.StreamId, seen.StreamOffset), out giNote);
            _passiveGiNotes.Remove((seen.StreamId, seen.StreamOffset));
            _passiveTransactionNotes.TryGetValue((seen.StreamId, seen.StreamOffset), out transaction);
            _passiveTransactionNotes.Remove((seen.StreamId, seen.StreamOffset));
        }
        var txNote = transaction is null ? "" :
            $" · {transaction.Meaning} · Req RX stream {transaction.RequestStream} offset {transaction.RequestOffset}" +
            (transaction.HostDelayMilliseconds is { } ms ? $" · host Δ≈{ms:0} ms" : "");
        var raw = seen.RawBytes;
        var forbiddenSingleA2 = seen.Format == "Single" && raw.Length == 1 && raw[0] == 0xA2;
        var profileMismatch = seen.Format == "LinkWidthMismatch";
        var isNoise = seen.Format == "Noise";
        if (!inspectionOnly && (profileMismatch || isNoise)) _passiveUndecodableShown++;
        var controlOffset = seen.Format is "Fixed" or "LinkWidthMismatch" ? 1 :
            seen.Format == "Variable" ? 4 : -1;
        var control = controlOffset >= 0 && raw.Length > controlOffset ? (byte?)raw[controlOffset] : null;
        var linkWidth = seen.DetectedLinkAddressWidth ?? _passiveLinkAddressWidth;
        if (!inspectionOnly && seen.StreamId < _passiveAutoLinkWidthReported.Length &&
            seen.Format == "Variable" && linkWidth != _passiveLinkAddressWidth &&
            !_passiveAutoLinkWidthReported[seen.StreamId])
        {
            _passiveAutoLinkWidthReported[seen.StreamId] = true;
            AppendSessionLog($"IEC-101 RX{seen.StreamId}: {linkWidth}-byte Link Address confirmed by two valid fixed frames (Setup={_passiveLinkAddressWidth}); subsequent ASDU decoding uses observed width. Check Setup profile.");
        }
        var address = 0;
        if (control.HasValue)
            for (var i = 0; i < linkWidth; i++) address |= raw[controlOffset + 1 + i] << (8 * i);
        var asduOffset = 5 + linkWidth;
        var asduCount = seen.Format == "Variable" ? Math.Max(0, raw[1] - 1 - linkWidth) : 0;
        byte[] asduRaw = prepared?.AsduRaw ?? (seen.Format == "Variable" && asduCount > 0 &&
            raw.Length >= asduOffset + asduCount
                ? raw.AsSpan(asduOffset, asduCount).ToArray() : Array.Empty<byte>());
        var link = control.HasValue ? LinkControlDecoder.Decode(control.Value) : null;
        var decode = prepared is not null ? prepared.Decode :
            seen.ChecksumValid && asduRaw.Length > 0 ? _passiveAsduDecoder?.Decode(asduRaw) : null;
        // A received byte sequence is always visible in Trace. Only a fully
        // verified frame AND complete supported ASDU can update process facts.
        var wire = prepared?.Wire ??
            PassiveWirePresentation.Classify(seen, _passiveLinkAddressWidth, decode);
        var trustedDecode = wire.TrustedProcess ? decode : null;
        var format = seen.Format switch
        {
            "Fixed" => Ft12FrameFormat.FixedLength,
            "Variable" => Ft12FrameFormat.VariableLength,
            "Partial" or "Noise" or "LinkWidthMismatch" => Ft12FrameFormat.Malformed,
            _ => Ft12FrameFormat.SingleCharacter
        };
        var frame = new Ft12FrameDecode
        {
            Format = format, RawBytes = raw, Control = control,
            LinkAddress = control.HasValue ? address : null,
            LinkAddressSize = linkWidth, LinkControl = link,
            AsduBytes = asduRaw, IsChecksumValid = seen.ChecksumValid,
            IsLengthValid = wire.TrustedWire,
            Issues = profileMismatch
                ? new[] { $"IEC-101 Link Address width mismatch: Setup={_passiveLinkAddressWidth}, " +
                    $"observed fixed frame has {linkWidth} octet(s). Check Setup or re-decode the raw capture." }
                : isNoise
                    ? new[] { "Unrecognized serial bytes; exact bytes preserved in raw journal. " +
                        "Check baud/parity, source direction, RS-232 wiring and IEC-101 profile." }
                    : forbiddenSingleA2
                        ? new[] { "IEC-101 prohibits the A2 single-character response." }
                        : seen.ChecksumValid ? Array.Empty<string>() : new[] { "Bad FT1.2 checksum or incomplete frame." }
        };
        // The FT1.2 PRM bit (after checksum/structure validation), not the
        // user's COM dropdown order, determines unbalanced primary/secondary
        // traffic. E5 is a secondary-only short response. Unknown stays
        // unknown on malformed/noise frames.
        var wireRole = PassiveIec101FrameRoles.Classify(seen, _passiveLinkAddressWidth);
        if (!inspectionOnly)
        {
            var oldRole = _passivePortRoles.KnownRole(seen.StreamId);
            _passivePortRoles.Observe(seen, _passiveLinkAddressWidth);
            // UI role badges only change when physical source interpretation
            // changes, never with every cyclic frame. No per-frame text churn.
            if (oldRole != _passivePortRoles.KnownRole(seen.StreamId))
                UpdatePassiveAutoRoleStatus();
        }
        var direction = wireRole switch
        {
            PassiveWireRole.Primary => FrameDirection.MasterToSlave,
            PassiveWireRole.Secondary => FrameDirection.SlaveToMaster,
            _ => FrameDirection.Unknown
        };
        // Live is writer-owned, so a skipped WPF preview cannot produce a
        // fabricated match. Offline replay reconstructs the same typed stages.
        PassiveControlEvidence? upstream = null;
        if (!inspectionOnly) _passiveControlNotes.Remove((seen.StreamId, seen.StreamOffset), out upstream);
        var controlAnalysis = inspectionOnly ? string.Empty : _passiveMonitorSession is not null
            ? upstream?.Explanation ?? string.Empty
            : _passiveControlTimeline.Observe(direction, decode,
                seen.StreamId < 2 ? "Link A" : "Link B", seen.StreamOffset,
                seen.ChecksumValid && !profileMismatch && !forbiddenSingleA2 && !isNoise,
                control.HasValue ? address : null);
        var prefix = (seen.StreamId < 2 ? "Link A · " : "Link B · ") +
            (direction == FrameDirection.MasterToSlave ? "SCADA → RTU (PRM)" :
             direction == FrameDirection.SlaveToMaster ? "RTU → SCADA (PRM/E5)" :
             "direction unconfirmed");
        var function = seen.Format switch
        {
            "Partial" => "Incomplete FT1.2 frame",
            "Noise" => "Unrecognized serial bytes (raw preserved)",
            "LinkWidthMismatch" => $"Fixed link frame · {linkWidth}-octet address detected; Setup={_passiveLinkAddressWidth}",
            _ => link?.FunctionName ?? (raw.Length == 1 && raw[0] == 0xE5
                ? "E5 short response (ACK or no-data by request context)"
                : raw.Length == 1 && raw[0] == 0xA2
                    ? "A2 single response (not permitted in IEC-101)"
                    : "Unknown FT1.2 candidate")
        };
        var meaning = wire.Status == PassiveWirePresentation.Kind.Decoded
            ? trustedDecode!.ShortMeaning
            : wire.Status == PassiveWirePresentation.Kind.LinkOnly ? function
            : wire.Message;
        // A valid FT1.2 envelope with an unknown ASDU is still a physical
        // frame; do not rewrite it into a fake decoded signal or hide it.
        var first = trustedDecode?.FirstObject;
        var rawHexOnce = prepared?.RawHex ?? Convert.ToHexString(raw);

        var ev = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = inspectionOnly ? 0 : ++_passiveFrameSequence,
            TimestampUtc = seen.EstimatedUtc, Direction = direction,
            Category = !wire.NeedsAttention
                ? "Passive Traffic" : "RX Warning",
            DataClass = link?.FunctionCode switch { 10 => "Class 1", 11 => "Class 2", _ => "-" },
            PollingReason = "PASSIVE-IEC101", ProtocolMode = Iec60870ProtocolMode.Iec101,
            Summary = prefix + " · " + function,
            Detail = meaning + $" · Link {address} · RX offset {seen.StreamOffset}" + txNote +
                (giNote is null ? "" : $" · GI {giNote.Kind} (CA={giNote.CommonAddress}, QOI={giNote.Qualifier}, data={giNote.InterrogatedDataFrames}; request RX{giNote.RequestStreamId} offset {giNote.RequestOffset})") +
                     (decode?.Issues.Count > 0 ? " · " + string.Join("; ", decode.Issues) : ""),
            OperatorMessage = meaning, ProtocolMeaning = meaning,
            OperatorAction = "Observed only; no commands transmitted",
            ControlObservation = controlAnalysis,
            RawHex = rawHexOnce,
            PhysicalSource = seen.StreamId < 2 ? "Link A" : "Link B",
            PhysicalStreamId = seen.StreamId,
            RawStreamOffset = seen.StreamOffset,
            Frame = frame, LinkAddress = control.HasValue ? address : null,
            TypeId = decode?.TypeId,
            TypeName = decode?.TypeName ?? "",
            VariableStructureQualifier = decode?.VariableStructureQualifier,
            IsSequenceAsdu = decode?.IsSequence,
            ObjectCount = decode?.ObjectCount,
            CauseOfTransmission = decode?.CauseOfTransmission,
            CauseDetails = decode?.CauseDetails,
            CommandDetails = decode?.CommandDetails,
            AsduDetails = decode,
            InformationObjectDetails = first,
            QualityDetails = first?.QualityDetails,
            CauseName = decode?.CauseName ?? "",
            OriginatorAddress = decode?.OriginatorAddress,
            CommonAddressNumber = decode?.CommonAddress,
            InformationObjectAddress = first?.InformationObjectAddress,
            ObjectSummary = decode?.ObjectSummary ?? "",
            QualityText = first?.QualityText ?? "",
            IsRelayEdgeEvent = direction == FrameDirection.SlaveToMaster && first is not null &&
                trustedDecode?.CauseOfTransmission == 3 && trustedDecode.TypeId is >= 1 and <= 44,
            IsRelayValue = direction == FrameDirection.SlaveToMaster && first is not null && trustedDecode is not null &&
                trustedDecode.TypeId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 30 or 31 or 32 or 34 or 35 or 36 or 37,
            SignalKey = first is null || decode is null ? "" :
                $"L{seen.StreamId / 2}:{decode.CommonAddress}:{first.InformationObjectAddress}",
            SignalName = first is null ? "" : $"IOA {first.InformationObjectAddress}",
            SignalGroup = seen.StreamId < 2 ? "IEC-101 Link A" : "IEC-101 Link B",
            SignalType = decode?.TypeName ?? "",
            SignalDisplayValue = first?.ShortValue ?? "",
            SignalRawValue = first?.ValueText ?? "",
            RelayTimestampText = first?.TimestampText ?? "",
            RelayTimestampInvalid = first?.TimestampText.Contains("invalid", StringComparison.OrdinalIgnoreCase) == true
        };
        if (inspectionOnly)
            return new EvidenceRow(ev, ResolveIoaPoint(ev));
        OnEvidenceReceived(this, ev);
        if (_passiveMonitorSession is null &&
            (controlAnalysis.StartsWith("RTU NEGATIVE CONFIRMATION", StringComparison.Ordinal) ||
             controlAnalysis.StartsWith("RTU APPLICATION ERROR", StringComparison.Ordinal)))
            AddRateLimitedDiagnostic("IEC101-PASSIVE-CONTROL-REJECTED", "Warning", "IEC-101",
                "RTU control response needs investigation",
                controlAnalysis + $" · Link {(seen.StreamId < 2 ? "A" : "B")} RX{seen.StreamId} offset {seen.StreamOffset}.",
                "Select this RTU frame in Trace, inspect the preceding Master command and compare Type, COT P/N, CA, IOA, S/E and QOC.",
                TimeSpan.FromSeconds(1));

        // Multi-object ASDUs must update EVERY point, not only the first IOA.
        if (direction == FrameDirection.SlaveToMaster && trustedDecode is not null &&
            trustedDecode.TypeId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 30 or 31 or 32 or 34 or 35 or 36 or 37)
        {
            foreach (var obj in trustedDecode.Objects.Skip(1))
            {
                UpdateValueAndEventViews(new Iec103MasterEvidenceEvent
                {
                    SequenceNumber = _passiveFrameSequence,
                    TimestampUtc = seen.EstimatedUtc,
                    Direction = direction,
                    ProtocolMode = Iec60870ProtocolMode.Iec101,
                    Category = "Passive Point",
                    PollingReason = "PASSIVE-POINT",
                    RawHex = rawHexOnce,
                    PhysicalSource = seen.StreamId < 2 ? "Link A" : "Link B",
                    PhysicalStreamId = seen.StreamId,
                    RawStreamOffset = seen.StreamOffset,
                    TypeId = trustedDecode.TypeId, TypeName = trustedDecode.TypeName,
                    CauseOfTransmission = trustedDecode.CauseOfTransmission,
                    CauseDetails = trustedDecode.CauseDetails,
                    AsduDetails = decode,
                    InformationObjectDetails = obj,
                    QualityDetails = obj.QualityDetails,
                    CommandDetails = Iec10xCommandDetails.Decode(trustedDecode.TypeId, obj, trustedDecode.CauseDetails!),
                    QualityText = obj.QualityText,
                    CauseName = trustedDecode.CauseName, CommonAddressNumber = trustedDecode.CommonAddress,
                    InformationObjectAddress = obj.InformationObjectAddress,
                    ObjectSummary = obj.ReadableSummary,
                    IsRelayValue = true,
                    IsRelayEdgeEvent = trustedDecode.CauseOfTransmission == 3 && trustedDecode.TypeId is >= 1 and <= 44,
                    SignalKey = $"L{seen.StreamId / 2}:{trustedDecode.CommonAddress}:{obj.InformationObjectAddress}",
                    SignalName = $"IOA {obj.InformationObjectAddress}",
                    SignalGroup = seen.StreamId < 2 ? "IEC-101 Link A" : "IEC-101 Link B",
                    SignalType = trustedDecode.TypeName,
                    SignalDisplayValue = obj.ShortValue,
                    SignalRawValue = obj.ValueText,
                    RelayTimestampText = obj.TimestampText,
                    RelayTimestampInvalid = obj.TimestampText.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        return null;
    }

    private void ApplyPassiveTimelineWindow(PassiveTimelineWindow item)
    {
        var seconds = (int)(item.EndUtc - item.StartUtc).TotalSeconds;
        var summary = item.CompressedEmptyWindows > 0
            ? $"{item.CompressedEmptyWindows} consecutive {seconds / Math.Max(1L, item.CompressedEmptyWindows)}s windows without received frame observations (compressed)"
            : $"Physical port0={item.Source0Frames}, port1={item.Source1Frames}, " +
              $"Class1/2={item.Class1Polls}/{item.Class2Polls}, " +
              $"LinkStatus={item.LinkStatusPolls}, Retry={item.Retries}, Timeout={item.ResponseTimeouts}, " +
              $"GI={item.GiActivations}/{item.GiCompleted}";
        PassiveStatusMessageText.Text = "IEC-101 Link " +
            (item.CircuitId == 0 ? "A" : "B") + " · " +
            item.StartUtc.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) +
            "–" + item.EndUtc.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) +
            " UTC · " + summary;

        // The full time-series is stored by the writer in timeline-windows.jsonl.
        // Only exceptional windows get a bounded operator diagnostic here.
        if (!item.HasAnomaly) return;
        AddUiDiagnostic("Info", "Capture", "IEC101-FAILURE-TIMELINE",
            "IEC-101 observation window contains unusual link behavior",
            $"Host-estimated UTC {item.StartUtc:O} to {item.EndUtc:O} · " +
            $"Physical RX0/RX1={item.Source0Frames}/{item.Source1Frames}; retry={item.Retries}; " +
            $"timeouts={item.ResponseTimeouts}; DFC={item.DfcBusy}; unknown primary FC={item.UnknownPrimaryFunctions}; " +
            $"FC15={item.ServiceNotImplementedReplies}; GI exceptions={item.GiExceptions}; invalid={item.InvalidFrames}. " +
            $"First abnormal evidence RX={item.FirstAnomalyStream?.ToString() ?? "-"}, " +
            $"offset={item.FirstAnomalyOffset?.ToString() ?? "-"}." +
            (item.HostTimestampOrderUncertain ? " Cross-port host timestamp order uncertain." : ""),
            "Open Frame Trace and compare complete raw journals around this time. " +
            "This window is not proof of NUC switching or the health of an unobserved redundant link.");
    }

    private void ApplyPassiveGiEvent(PassiveGiEvent item)
    {
        // Only the bounded UI annotations are cached. All phases are preserved
        // losslessly in gi-events.jsonl (or reproducible by verified offline replay).
        if (_passiveGiNotes.Count >= 256)
            _passiveGiNotes.Remove(_passiveGiNotes.Keys.First());
        _passiveGiNotes[(item.StreamId, item.StreamOffset)] = item;

        var severity = item.Kind switch
        {
            PassiveGiKind.TimedOut or PassiveGiKind.NegativeConfirmation or
            PassiveGiKind.Superseded or PassiveGiKind.CapacityEviction =>
                FindingSeverity.Warning,
            PassiveGiKind.UnmatchedResponse or PassiveGiKind.DataBeforeConfirmation or
            PassiveGiKind.RepeatedActivation => FindingSeverity.Info,
            _ => (FindingSeverity?)null
        };
        if (severity is null) return;

        // Reuse the established UI backpressure policy without affecting disk.
        var now = DateTime.UtcNow;
        if (now >= _passiveFindingWindowEndUtc)
        {
            _passiveFindingWindowEndUtc = now.AddSeconds(1);
            _passiveFindingWindowCount = 0;
        }
        if (_passiveFindingWindowCount >= 20) return;
        var key = (PassiveTransactionKind.IncompatibleReply, (int?)item.CommonAddress);
        if (_passiveFindingLastShown.TryGetValue(key, out var last) &&
            now - last < TimeSpan.FromSeconds(3)) return;
        if (_passiveFindingLastShown.Count >= 128) _passiveFindingLastShown.Clear();
        _passiveFindingLastShown[key] = now;
        _passiveFindingWindowCount++;

        OnFindingRaised(this, new Iec103MasterFinding
        {
            TimestampUtc = item.EstimatedUtc,
            Severity = severity.Value,
            Id = "IEC101-PASSIVE-GI-" + item.Kind.ToString().ToUpperInvariant(),
            Title = item.Kind switch
            {
                PassiveGiKind.TimedOut => "IEC-101 GI did not finish in the observation window",
                PassiveGiKind.NegativeConfirmation => "IEC-101 GI activation rejected by RTU",
                PassiveGiKind.Superseded => "Repeated GI activation interrupted an incomplete GI",
                _ => "IEC-101 GI observation: " + item.Kind
            },
            Evidence = item.Meaning + $" | CA={item.CommonAddress} QOI={item.Qualifier}" +
                $" | Observed RX={item.StreamId} offset={item.StreamOffset}" +
                $" | GI activation RX={item.RequestStreamId} offset={item.RequestOffset}" +
                $" | Interrogated data frames={item.InterrogatedDataFrames}",
            Impact = "Only the monitored physical IEC-101 line is observed; GI does not prove NUC failover.",
            Recommendation = "Review the Type 100 COT6/7/10 sequence in Frame Trace, compare full raw journals and NUC event timestamps."
        });
    }

    private void ApplyPassiveTransactionEvent(PassiveTransactionEvent item)
    {
        if (item.Kind is PassiveTransactionKind.Matched or PassiveTransactionKind.NegativeReply)
        {
            if (_passiveTransactionNotes.Count >= 512)
                _passiveTransactionNotes.Remove(_passiveTransactionNotes.Keys.First());
            _passiveTransactionNotes[(item.ObservedStream, item.ObservedOffset)] = item;
        }

        // Preserve the existing calm operator UI: no finding for every normal poll,
        // Class 1 access demand or standby Link Status. All transactions remain in
        // the full raw/derived journals; only meaningful diagnostics enter Findings.
        var severity = item.Kind switch
        {
            PassiveTransactionKind.ResponseTimeout or
            PassiveTransactionKind.DfcBusy or
            PassiveTransactionKind.IncompatibleReply or
            PassiveTransactionKind.DirectionConflict or
            PassiveTransactionKind.SourceOrderConflict or
            PassiveTransactionKind.CapacityEviction => FindingSeverity.Warning,
            PassiveTransactionKind.NegativeReply or
            PassiveTransactionKind.RetryObserved or
            PassiveTransactionKind.RequestSuperseded or
            PassiveTransactionKind.ReusedConfirmedFcb or
            PassiveTransactionKind.AmbiguousSingleReply or
            PassiveTransactionKind.InvalidFrame => FindingSeverity.Info,
            _ => (FindingSeverity?)null
        };
        if (severity is null) return;

        // Protect the application's existing pending Findings queue from prolonged
        // corrupt traffic or repeated timeouts. Emit at most 20 per wall-clock
        // second and coalesce repeated (kind, link address) for 3 seconds.
        // Neither rule suppresses the complete disk-backed transaction journal.
        var now = DateTime.UtcNow;
        if (now >= _passiveFindingWindowEndUtc)
        {
            _passiveFindingWindowEndUtc = now.AddSeconds(1);
            _passiveFindingWindowCount = 0;
        }
        if (_passiveFindingWindowCount >= 20) return;
        var key = (item.Kind, item.LinkAddress);
        if (_passiveFindingLastShown.TryGetValue(key, out var last) &&
            now - last < TimeSpan.FromSeconds(3)) return;
        if (_passiveFindingLastShown.Count >= 128)
            _passiveFindingLastShown.Clear();
        _passiveFindingLastShown[key] = now;
        _passiveFindingWindowCount++;
        OnFindingRaised(this, new Iec103MasterFinding
        {
            TimestampUtc = item.EstimatedUtc,
            Severity = severity.Value,
            Id = "IEC101-PASSIVE-" + item.Kind.ToString().ToUpperInvariant(),
            Title = item.Kind switch
            {
                PassiveTransactionKind.ResponseTimeout => "No IEC-101 reply observed within timeout",
                PassiveTransactionKind.DfcBusy => "RTU reports link busy (DFC=1)",
                PassiveTransactionKind.RetryObserved => "IEC-101 request retransmission observed",
                PassiveTransactionKind.DirectionConflict => "Primary control direction differs from selected link mode",
                _ => "IEC-101 passive observation: " + item.Kind
            },
            Evidence = item.Meaning +
                $" | RX stream={item.ObservedStream} offset={item.ObservedOffset}" +
                $" | Request stream={item.RequestStream?.ToString() ?? "-"} offset={item.RequestOffset?.ToString() ?? "-"}" +
                $" | Link={item.LinkAddress?.ToString() ?? "unknown"}" +
                $" | Primary FC={item.RequestFunction?.ToString() ?? "-"}; Secondary FC={item.ReplyFunction?.ToString() ?? "-"}",
            Impact = "The passive trace supports this observation; the second redundant line is not observed by two COM inputs.",
            Recommendation = "Open Frame Trace or the full raw journal and compare request/reply bytes, FCB/FCV, ACD/DFC and SCADA switching history."
        });
    }

    private async void PassiveFindAnomalies_Click(object sender, RoutedEventArgs e)
    {
        _passiveAnomalyBeforeOffset = null;
        await LoadPassiveAnomaliesAsync();
    }

    private async void PassiveOlderAnomalies_Click(object sender, RoutedEventArgs e)
    {
        if (_passiveAnomalyHasOlder)
            await LoadPassiveAnomaliesAsync();
    }

    /// <summary>
    /// UI holds one page only. Sparse indexing and SHA verification run off the
    /// Dispatcher; source bytes are never pulled from an untrusted JSONL row.
    /// </summary>
    private async Task LoadPassiveAnomaliesAsync()
    {
        if (_passiveAnomalyLoading) return;
        if (_passiveMonitorSession is not null || _passiveReplayCancellation is not null)
        {
            PassiveStatusMessageText.Text =
                "Stop the current monitor/replay before navigating a saved raw journal.";
            return;
        }

        var directory = _passiveCaptureResult?.Directory ??
            (_passiveAnomalyDirectory is not null &&
             string.Equals(PassiveCapturePathText.Text, _passiveAnomalyDirectory,
                 StringComparison.OrdinalIgnoreCase)
                ? _passiveAnomalyDirectory : PassiveCapturePathText.Text);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) ||
            !File.Exists(Path.Combine(directory, "manifest.json")))
        {
            PassiveStatusMessageText.Text =
                "Open a finalized raw journal manifest.json. Live-only monitoring does not create one.";
            return;
        }

        _passiveAnomalyLoading = true;
        PassiveFindAnomaliesButton.IsEnabled = false;
        PassiveOlderAnomaliesButton.IsEnabled = false;
        PassiveStatusMessageText.Text = "Verifying capture and indexing timeline...";
        try
        {
            var cached = string.Equals(_passiveAnomalyDirectory, directory,
                StringComparison.OrdinalIgnoreCase) ? _passiveAnomalyNavigator : null;
            var navigator = await Task.Run(() => cached ??
                PassiveTimelineEvidenceNavigator.OpenVerified(directory));
            var cursor = _passiveAnomalyBeforeOffset;
            var hits = await Task.Run(() => navigator.Search(
                new PassiveTimelineSearch(Limit: 60, BeforeOffset: cursor)));
            _passiveAnomalyNavigator = navigator;
            _passiveAnomalyDirectory = directory;
            _passiveAnomalyHasOlder = hits.Count == 60;
            if (hits.Count > 0)
                _passiveAnomalyBeforeOffset = hits[^1].FileOffset;
            PassiveAnomalyPicker.ItemsSource = hits.Select(x => new PassiveAnomalyChoice(x)).ToArray();
            PassiveAnomalyPicker.SelectedIndex = hits.Count > 0 ? 0 : -1;
            PassiveStatusMessageText.Text = hits.Count == 0
                ? "No more abnormal IEC-101 windows in this verified capture."
                : "Indexed " + navigator.SparseBlockCount +
                  " blocks · showing " + hits.Count +
                  " abnormal windows. Select one for verified frame bytes; Older pages backwards.";
        }
        catch (Exception ex)
        {
            _passiveAnomalyHasOlder = false;
            PassiveStatusMessageText.Text = "Anomaly navigation unavailable: " + ex.Message;
            AddUiDiagnostic("Warning", "Capture", "IEC101-P4A-NAVIGATION",
                "Unable to browse indexed passive capture",
                ex.ToString(),
                "Check manifest, raw SHA/CRC integrity and timeline-windows.jsonl; do not rely on unverified derived indexes.");
        }
        finally
        {
            _passiveAnomalyLoading = false;
            PassiveFindAnomaliesButton.IsEnabled = true;
            PassiveOlderAnomaliesButton.IsEnabled = _passiveAnomalyHasOlder;
        }
    }

    private async void PassiveAnomalyPicker_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        _passiveAnomalyReadCancellation?.Cancel();
        if (PassiveAnomalyPicker.SelectedItem is not PassiveAnomalyChoice choice ||
            _passiveAnomalyNavigator is not { } navigator)
            return;

        var cancellation = new CancellationTokenSource();
        _passiveAnomalyReadCancellation = cancellation;
        var hit = choice.Hit;
        PassiveSelectedRawText.Text = "Finding original RX frame by verified source byte offset...";
        try
        {
            var profile = string.Equals(_passiveLastReplayDirectory, _passiveAnomalyDirectory,
                StringComparison.OrdinalIgnoreCase) ? _passiveLastReplayProfile : null;
            var frame = await Task.Run(
                () => navigator.ReadVerifiedFrame(hit, profile, cancellation.Token),
                cancellation.Token);
            if (cancellation.IsCancellationRequested ||
                PassiveAnomalyPicker.SelectedItem is not PassiveAnomalyChoice selected ||
                selected.Hit.FileOffset != hit.FileOffset)
                return;
            PassiveSelectedRawText.Text = frame is null
                ? hit.Label + " · RX " +
                  (hit.PhysicalAnomalyStream?.ToString() ?? "-") + " offset " +
                  (hit.Window.FirstAnomalyOffset?.ToString() ?? "-") +
                  " · No exact decoded frame under the selected profile; re-decode or inspect the raw journal."
                : "VERIFIED RAW · Link " + (hit.Window.CircuitId == 0 ? "A" : "B") +
                  " · RX " + frame.StreamId + " byte offset " + frame.StreamOffset +
                  " · " + frame.EstimatedUtc.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'") +
                  " · " + frame.Format + " · " + Convert.ToHexString(frame.RawBytes);
        }
        catch (OperationCanceledException)
        {
            // Changing the selected historical row is normal cancellation.
        }
        catch (Exception ex)
        {
            PassiveSelectedRawText.Text =
                "Original frame could not be authenticated or located: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_passiveAnomalyReadCancellation, cancellation))
                _passiveAnomalyReadCancellation = null;
            cancellation.Dispose();
        }
    }

    private void PassiveRawGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PassiveRawGrid.SelectedItem is PassiveRxRow row)
            PassiveSelectedRawText.Text = row.Hex +
                (row.ByteCount > 48 ? " … (first 48 bytes preview; full source on disk)" : "");
    }

    private void PassiveOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _passiveCaptureResult?.Directory ?? _passiveMonitorSession?.GetProgress().CaptureDirectory;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            PassiveStatusMessageText.Text = "No recording directory yet; start a passive session first.";
            return;
        }
        try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception ex) { PassiveStatusMessageText.Text = "Folder open failed: " + ex.Message; }
    }

    private async void PassiveVerify_Click(object sender, RoutedEventArgs e)
    {
        if (_passiveCaptureResult is not { } result || _passiveMonitorSession is not null) return;
        PassiveVerifyButton.IsEnabled = false;
        PassiveStatusMessageText.Text = "Checking capture integrity…";
        try
        {
            var audit = await Task.Run(() => PassiveJournalVerifier.Verify(result.Directory));
            PassiveIntegrityText.Text = audit.Complete ? "VERIFIED" : "INCOMPLETE";
            PassiveIntegrityText.Foreground = (Brush)FindResource(audit.Complete ? "GoodBrush" : "BadBrush");
            PassiveStatusMessageText.Text = audit.Complete
                ? "Verified " + audit.Streams.Sum(x => x.RawBytes).ToString("N0") + " bytes."
                : "Audit failed: " + string.Join(" | ", audit.Problems.Take(3));
        }
        catch (Exception ex)
        {
            PassiveIntegrityText.Text = "VERIFY ERROR";
            PassiveStatusMessageText.Text = ex.Message;
        }
        finally { PassiveVerifyButton.IsEnabled = _passiveCaptureResult is not null; }
    }

    private async void MainWindow_ClosingPassive(object? sender, CancelEventArgs e)
    {
        if (_closingAfterPassiveStop)
        {
            ReleaseSessionTraceHistory();
            return;
        }
        if (_passiveMonitorSession is null && _passiveReplayCancellation is null)
        {
            ReleaseSessionTraceHistory();
            return;
        }
        if (_passiveReplayCancellation is { } replay && _passiveReplayWork is { } replayTask)
        {
            e.Cancel = true;
            _closingAfterPassiveStop = true;
            IsEnabled = false;
            replay.Cancel();
            try { await replayTask; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppendSessionLog("Offline replay shutdown: " + ex.Message); }
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            Close();
            return;
        }
        if (_passiveMonitorSession is null) return;
        e.Cancel = true;
        _closingAfterPassiveStop = true;
        IsEnabled = false;
        try
        {
            _sessionCancellation?.Cancel();
            if (_passiveCaptureTask is { } task)
                await task;
        }
        catch (Exception ex)
        {
            AppendSessionLog("Passive monitor close warning: " + ex.Message);
        }
        finally
        {
            // Finish the UI async continuation before closing the owner.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            Close();
        }
    }
}

public sealed record PassiveRxRow(
    string Utc, string Direction, long Sequence, int ByteCount, string Hex);
