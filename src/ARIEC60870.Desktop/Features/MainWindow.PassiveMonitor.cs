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
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

/// <summary>Passive RX capture lives in existing Setup, Connect and Trace workspaces.</summary>
public partial class MainWindow
{
    private const int PassiveTraceTabIndex = 1;
    private const int MaxVisiblePassiveRows = 600;
    private readonly ObservableCollection<PassiveRxRow> _passiveRawRows = new();
    private DispatcherTimer? _passiveUiTimer;
    private PassiveMonitorSession? _passiveMonitorSession;
    private Task<(string Directory, PassiveCaptureManifest Manifest)>? _passiveCaptureTask;
    private (string Directory, PassiveCaptureManifest Manifest)? _passiveCaptureResult;
    private bool _closingAfterPassiveStop;
    private Iec10xAsduDecoder? _passiveAsduDecoder;
    private long _passiveFrameSequence;
    private long _passiveUndecodableShown;
    private PassiveIec101TransactionCorrelator? _passiveReplayCorrelator;
    private PassiveIec101GiTracker? _passiveReplayGiTracker;
    private PassiveIec101FailureTimeline? _passiveReplayTimeline;
    private PassiveIec101FailureTimeline[]? _passiveReplayTimelines;
    private readonly Dictionary<(byte Stream, long Offset), PassiveGiEvent> _passiveGiNotes = new();
    private readonly Dictionary<(byte Stream, long Offset), PassiveTransactionEvent> _passiveTransactionNotes = new();
    // GUI-only aggregation; all events remain durable in transactions.jsonl.
    private readonly Dictionary<(PassiveTransactionKind Kind, int? Address), DateTime> _passiveFindingLastShown = new();
    private DateTime _passiveFindingWindowEndUtc = DateTime.MinValue;
    private int _passiveFindingWindowCount;
    private int _passiveLinkAddressWidth;
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
            NavOperatorButton.Visibility != Visibility.Visible ||
            NavTriggersButton.Tag?.ToString() != "10")
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
        if (IsIec101PassiveMonitorSelected() ||
            ActiveSetupRightPanel.Visibility != Visibility.Visible ||
            CommandDockPanel.Visibility != Visibility.Visible ||
            NavFrameButton.Tag?.ToString() != "1")
            throw new InvalidOperationException("Switching back to Active Master did not restore the original layout.");

        ProtocolModeComboBox.SelectedIndex = index;
    }

    private void PassiveLinkBSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (PassiveLinkBPortsPanel is not null)
            PassiveLinkBPortsPanel.Visibility =
                PassiveLinkBEnabledCheckBox?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
        var isLinkB = PassiveLinkBEnabledCheckBox.IsChecked == true;
        var bScada = (PassiveLinkBScadaPortComboBox.SelectedItem as string)?.Trim() ?? string.Empty;
        var bRtu = (PassiveLinkBRtuPortComboBox.SelectedItem as string)?.Trim() ?? string.Empty;
        if (isLinkB && (string.IsNullOrWhiteSpace(bScada) || string.IsNullOrWhiteSpace(bRtu)))
            throw new InvalidOperationException("Select both Link B SCADA TX and RTU TX receiver COM ports.");
        var options = new PassiveCaptureOptions(output,
            new PassiveSerialSource(first, "Link A SCADA TX -> RTU", baud, parity, 8, stopBits),
            new PassiveSerialSource(second, "Link A RTU TX -> SCADA", baud, parity, 8, stopBits),
            LinkAddressWidth: Width(PassiveLinkWidthCombo),
            CotWidth: Width(PassiveCotWidthCombo),
            CommonAddressWidth: Width(PassiveCaWidthCombo),
            InformationAddressWidth: Width(PassiveIoaWidthCombo),
            FrameGapMilliseconds: ReadInt(PassiveFrameGapBox, "Passive frame gap", 100, 10000),
            LinkMode: (PassiveLinkModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Auto",
            LinkBScadaTx: isLinkB ? new PassiveSerialSource(bScada, "Link B SCADA TX -> RTU", baud, parity, 8, stopBits) : null,
            LinkBRtuTx: isLinkB ? new PassiveSerialSource(bRtu, "Link B RTU TX -> SCADA", baud, parity, 8, stopBits) : null);
        options.Validate();

        var directory = Path.GetFullPath(output);
        var diskRoot = Path.GetPathRoot(directory)!;
        if (Directory.Exists(diskRoot) && new DriveInfo(diskRoot).AvailableFreeSpace < 200L * 1024 * 1024)
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
    private async Task RunPassiveRecordingAsync()
    {
        if (_sessionCancellation is not null) return;
        PassiveCaptureOptions options;
        try { options = BuildPassiveCaptureOptions(); }
        catch (Exception ex)
        {
            ModernMessageBox.Show(this, ex.Message, "Passive monitor setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SavePassiveSetupPreferences(silent: true);
        _sessionCancellation = new CancellationTokenSource();
        var session = new PassiveMonitorSession(options,
            options.Sources.Select(x => (IReadOnlySerialTap)new WindowsReadOnlySerialTap(x)).ToArray());
        _passiveMonitorSession = session;
        _passiveCaptureResult = null;
        ClearSessionView(clearLog: true);
        _passiveRawRows.Clear();
        _passiveFrameSequence = 0;
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
        _passiveLinkAddressWidth = options.LinkAddressWidth;
        AppendSessionLog($"Passive IEC-101 profile: {options.LinkMode}; Link={options.LinkAddressWidth}, COT={options.CotWidth}, CA={options.CommonAddressWidth}, IOA={options.InformationAddressWidth}; gap={options.FrameGapMilliseconds} ms.");
        PassiveScadaCountText.Text = "0 bytes";
        PassiveRtuCountText.Text = "0 bytes";
        PassiveIntegrityText.Text = "Opening receivers...";
        PassiveStatusMessageText.Text = "Preparing capture…";
        PassiveCapturePathText.Text = options.OutputDirectory;
        PassiveSelectedRawText.Text = "Select a received raw-byte chunk.";
        PassiveVerifyButton.IsEnabled = false;
        PassiveFreezeCheckBox.IsChecked = false;
        SetRunUiState(isRunning: true);
        MainTabControl.SelectedIndex = PassiveTraceTabIndex;
        UpdateSegmentedNav(false);
        SessionSubtitleText.Text = options.StreamCount == 4
            ? $"Passive IEC-101 A/B · A {options.ScadaTx.PortName}/{options.RtuTx.PortName} · B {options.LinkBScadaTx!.PortName}/{options.LinkBRtuTx!.PortName}"
            : $"Passive IEC-101 Link A · {options.ScadaTx.PortName}/{options.RtuTx.PortName}";
        UpdateStableHeader("Recording", "Listening to both serial directions.");
        AppendSessionLog("Passive recording started: " + SessionSubtitleText.Text);
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
            var result = await _passiveCaptureTask;
            _passiveCaptureResult = result;
            RefreshPassiveUi();
            PassiveCapturePathText.Text = result.Directory;
            PassiveIntegrityText.Text = result.Manifest.Status;
            PassiveStatusMessageText.Text = result.Manifest.Warnings.Length == 0
                ? "Recording saved. Select Verify to check the file."
                : string.Join(" | ", result.Manifest.Warnings.Take(2));
            PassiveVerifyButton.IsEnabled = true;
            AppendSessionLog("Passive RX capture finalized: " + result.Manifest.Status + " · " + result.Directory);
            UpdateStableHeader(result.Manifest.Status == "NoApplicationLossDetected" ? "Recorded" : "Degraded",
                "Recording saved. Verification available in Passive Monitor.");
        }
        catch (Exception ex)
        {
            PassiveIntegrityText.Text = "Capture failed";
            PassiveStatusMessageText.Text = ex.Message;
            UpdateStableHeader("Faulted", "Passive recorder failed: " + ex.Message);
            AppendSessionLog("Passive RX recorder failed: " + ex.Message);
        }
        finally
        {
            _passiveUiTimer?.Stop();
            _passiveMonitorSession = null;
            _passiveCaptureTask = null;
            _sessionCancellation?.Dispose();
            _sessionCancellation = null;
            SetRunUiState(isRunning: false);
        }
    }

    private void StopPassiveRecording()
    {
        if (_passiveMonitorSession is null || _sessionCancellation is null) return;
        PassiveIntegrityText.Text = "Saving…";
        UpdateStableHeader("Saving", "Finishing capture.");
        _sessionCancellation.Cancel();
    }

    private void PassiveUiTimer_Tick(object? sender, EventArgs e) => RefreshPassiveUi();

    private void RefreshPassiveUi()
    {
        var session = _passiveMonitorSession;
        if (session is null) return;
        var progress = session.GetProgress();
        PassiveScadaCountText.Text = progress.ScadaBytes.ToString("N0") + " bytes" +
            (progress.StreamCount == 4 ? $" · B {progress.LinkBScadaBytes:N0}" : "");
        PassiveRtuCountText.Text = progress.RtuBytes.ToString("N0") + " bytes" +
            (progress.StreamCount == 4 ? $" · B {progress.LinkBRtuBytes:N0}" : "");
        PassiveIntegrityText.Text = progress.IntegrityWarning
            ? "Integrity warning" : progress.Status == "Recording" ? "No app loss detected" : progress.Status;
        PassiveIntegrityText.Foreground = (Brush)FindResource(progress.IntegrityWarning ? "BadBrush" : "Ink900Brush");
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

        // Frame reconstruction is independent of the raw sample view and its Freeze flag.
        // Every observed frame is projected onto the EXISTING Evidence/Trace/Values/Event pipeline.
        var framed = 0;
        while (framed < 180 && session.TryReadFrame(out var frame))
        {
            FeedPassiveFrameToExistingViews(frame);
            framed++;
        }

        // Dedicated five-second summaries, never per-frame rendering.
        var summaryCount = 0;
        while (summaryCount < 24 && session.TryReadTimelineWindow(out var window))
        {
            ApplyPassiveTimelineWindow(window);
            summaryCount++;
        }

        // The Passive Monitor owns its 160-ms refresh timer. Active-Master
        // UI flushing is not guaranteed to run in this mode; without this,
        // evidence accumulates but Frame Trace looks frozen despite RX bytes.
        // Always flush, even when ONLY the raw sample table is frozen.
        FlushUiQueues();

        if (PassiveFreezeCheckBox.IsChecked == true) return;
        var count = 0;
        while (count < 65 && session.TryReadPreview(out var sample))
        {
            var row = new PassiveRxRow(
                sample.EstimatedUtc.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
                (sample.StreamId < 2 ? "A " : "B ") +
                ((sample.StreamId & 1) == 0 ? "SCADA → RTU" : "RTU → SCADA"),
                sample.Sequence, sample.ByteCount, Convert.ToHexString(sample.FirstBytes));
            _passiveRawRows.Add(row);
            if (_passiveRawRows.Count > MaxVisiblePassiveRows)
                _passiveRawRows.RemoveAt(0);
            count++;
        }
        if (count > 0 && PassiveRawGrid.SelectedItem is null)
            PassiveRawGrid.ScrollIntoView(_passiveRawRows[^1]);
    }

    private void FeedPassiveFrameToExistingViews(PassiveObservedFrame seen)
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
        _passiveGiNotes.TryGetValue((seen.StreamId, seen.StreamOffset), out var giNote);
        _passiveGiNotes.Remove((seen.StreamId, seen.StreamOffset));
        _passiveTransactionNotes.TryGetValue((seen.StreamId, seen.StreamOffset), out var transaction);
        _passiveTransactionNotes.Remove((seen.StreamId, seen.StreamOffset));
        var txNote = transaction is null ? "" :
            $" · {transaction.Meaning} · Req RX stream {transaction.RequestStream} offset {transaction.RequestOffset}" +
            (transaction.HostDelayMilliseconds is { } ms ? $" · host Δ≈{ms:0} ms" : "");
        var raw = seen.RawBytes;
        var forbiddenSingleA2 = seen.Format == "Single" && raw.Length == 1 && raw[0] == 0xA2;
        var profileMismatch = seen.Format == "LinkWidthMismatch";
        var isNoise = seen.Format == "Noise";
        if (profileMismatch || isNoise) _passiveUndecodableShown++;
        var controlOffset = seen.Format is "Fixed" or "LinkWidthMismatch" ? 1 :
            seen.Format == "Variable" ? 4 : -1;
        var control = controlOffset >= 0 && raw.Length > controlOffset ? (byte?)raw[controlOffset] : null;
        var linkWidth = seen.DetectedLinkAddressWidth ?? _passiveLinkAddressWidth;
        var address = 0;
        if (control.HasValue)
            for (var i = 0; i < linkWidth; i++) address |= raw[controlOffset + 1 + i] << (8 * i);
        var asduOffset = 5 + linkWidth;
        var asduCount = seen.Format == "Variable" ? Math.Max(0, raw[1] - 1 - linkWidth) : 0;
        byte[] asduRaw = seen.Format == "Variable" && asduCount > 0
            ? raw.AsSpan(asduOffset, asduCount).ToArray() : Array.Empty<byte>();
        var link = control.HasValue ? LinkControlDecoder.Decode(control.Value) : null;
        var decode = seen.ChecksumValid && asduRaw.Length > 0 ? _passiveAsduDecoder?.Decode(asduRaw) : null;
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
            IsLengthValid = true,
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
        var direction = (seen.StreamId & 1) == 0 ? FrameDirection.MasterToSlave : FrameDirection.SlaveToMaster;
        var prefix = (seen.StreamId < 2 ? "Link A · " : "Link B · ") +
            ((seen.StreamId & 1) == 0 ? "SCADA → RTU" : "RTU → SCADA");
        var function = seen.Format switch
        {
            "Partial" => "Incomplete FT1.2 frame",
            "Noise" => "Unrecognized serial bytes (raw preserved)",
            "LinkWidthMismatch" => $"Fixed link frame · {linkWidth}-octet address detected; Setup={_passiveLinkAddressWidth}",
            _ => link?.FunctionName ?? (raw[0] == 0xE5
                ? "E5 short response (ACK or no-data by request context)"
                : "A2 single response (not permitted in IEC-101)")
        };
        var meaning = decode?.ShortMeaning ?? function;
        if (!seen.ChecksumValid && seen.Format != "Partial")
            meaning = "Bad checksum · " + meaning;
        var first = decode?.FirstObject;
        var rawHexOnce = Convert.ToHexString(raw);

        var ev = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = ++_passiveFrameSequence,
            TimestampUtc = seen.EstimatedUtc, Direction = direction,
            Category = seen.ChecksumValid && !forbiddenSingleA2 && !profileMismatch && !isNoise
                ? "Passive Traffic" : "RX Warning",
            DataClass = link?.FunctionCode switch { 10 => "Class 1", 11 => "Class 2", _ => "-" },
            PollingReason = "PASSIVE-IEC101", ProtocolMode = Iec60870ProtocolMode.Iec101,
            Summary = prefix + " · " + function,
            Detail = meaning + $" · Link {address} · RX offset {seen.StreamOffset}" + txNote +
                (giNote is null ? "" : $" · GI {giNote.Kind} (CA={giNote.CommonAddress}, QOI={giNote.Qualifier}, data={giNote.InterrogatedDataFrames}; request RX{giNote.RequestStreamId} offset {giNote.RequestOffset})") +
                     (decode?.Issues.Count > 0 ? " · " + string.Join("; ", decode.Issues) : ""),
            OperatorMessage = meaning, ProtocolMeaning = meaning,
            OperatorAction = "Observed only; no commands transmitted",
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
            IsRelayEdgeEvent = seen.ChecksumValid && (seen.StreamId & 1) == 1 && first is not null &&
                decode?.CauseOfTransmission == 3 && decode.TypeId is >= 1 and <= 44,
            IsRelayValue = seen.ChecksumValid && (seen.StreamId & 1) == 1 && first is not null && decode is not null &&
                decode.TypeId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 30 or 31 or 32 or 34 or 35 or 36 or 37,
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
        OnEvidenceReceived(this, ev);

        // Multi-object ASDUs must update EVERY point, not only the first IOA.
        if (seen.ChecksumValid && (seen.StreamId & 1) == 1 && decode is not null &&
            decode.TypeId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 30 or 31 or 32 or 34 or 35 or 36 or 37)
        {
            foreach (var obj in decode.Objects.Skip(1))
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
                    TypeId = decode.TypeId, TypeName = decode.TypeName,
                    CauseOfTransmission = decode.CauseOfTransmission,
                    CauseDetails = decode.CauseDetails,
                    AsduDetails = decode,
                    InformationObjectDetails = obj,
                    QualityDetails = obj.QualityDetails,
                    CommandDetails = Iec10xCommandDetails.Decode(decode.TypeId, obj, decode.CauseDetails!),
                    QualityText = obj.QualityText,
                    CauseName = decode.CauseName, CommonAddressNumber = decode.CommonAddress,
                    InformationObjectAddress = obj.InformationObjectAddress,
                    ObjectSummary = obj.ReadableSummary,
                    IsRelayValue = true,
                    IsRelayEdgeEvent = decode.CauseOfTransmission == 3 && decode.TypeId is >= 1 and <= 44,
                    SignalKey = $"L{seen.StreamId / 2}:{decode.CommonAddress}:{obj.InformationObjectAddress}",
                    SignalName = $"IOA {obj.InformationObjectAddress}",
                    SignalGroup = seen.StreamId < 2 ? "IEC-101 Link A" : "IEC-101 Link B",
                    SignalType = decode.TypeName,
                    SignalDisplayValue = obj.ShortValue,
                    SignalRawValue = obj.ValueText,
                    RelayTimestampText = obj.TimestampText,
                    RelayTimestampInvalid = obj.TimestampText.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
    }

    private void ApplyPassiveTimelineWindow(PassiveTimelineWindow item)
    {
        var seconds = (int)(item.EndUtc - item.StartUtc).TotalSeconds;
        var summary = item.CompressedEmptyWindows > 0
            ? $"{item.CompressedEmptyWindows} consecutive {seconds / Math.Max(1L, item.CompressedEmptyWindows)}s windows without received frame observations (compressed)"
            : $"RX SCADA={item.Source0Frames}, RTU={item.Source1Frames}, " +
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
            $"RX SCADA/RTU={item.Source0Frames}/{item.Source1Frames}; retry={item.Retries}; " +
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
                "Finish the recording/replay before navigating verified historical anomalies.";
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
                "Open a finalized passive manifest.json or complete a recording first.";
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
        if (_closingAfterPassiveStop) return;
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
            AppendSessionLog("Passive capture close warning: " + ex.Message);
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
