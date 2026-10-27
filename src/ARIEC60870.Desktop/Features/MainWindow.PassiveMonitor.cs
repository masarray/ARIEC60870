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
    private int _passiveLinkAddressWidth;

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

        ProtocolModeComboBox.SelectedIndex = 1; // existing active IEC-101 profile
        if (IsIec101PassiveMonitorSelected() ||
            ActiveSetupRightPanel.Visibility != Visibility.Visible ||
            CommandDockPanel.Visibility != Visibility.Visible ||
            NavFrameButton.Tag?.ToString() != "1")
            throw new InvalidOperationException("Switching back to Active Master did not restore the original layout.");

        ProtocolModeComboBox.SelectedIndex = index;
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
        var options = new PassiveCaptureOptions(output,
            new PassiveSerialSource(first, "SCADA TX -> RTU", baud, parity, 8, stopBits),
            new PassiveSerialSource(second, "RTU TX -> SCADA", baud, parity, 8, stopBits),
            LinkAddressWidth: Width(PassiveLinkWidthCombo),
            CotWidth: Width(PassiveCotWidthCombo),
            CommonAddressWidth: Width(PassiveCaWidthCombo),
            InformationAddressWidth: Width(PassiveIoaWidthCombo),
            FrameGapMilliseconds: ReadInt(PassiveFrameGapBox, "Passive frame gap", 100, 10000),
            LinkMode: (PassiveLinkModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Auto");
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
            new WindowsReadOnlySerialTap(options.ScadaTx),
            new WindowsReadOnlySerialTap(options.RtuTx));
        _passiveMonitorSession = session;
        _passiveCaptureResult = null;
        ClearSessionView(clearLog: true);
        _passiveRawRows.Clear();
        _passiveFrameSequence = 0;
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
        SessionSubtitleText.Text = "Passive IEC-101 RX · " + options.ScadaTx.PortName + " (SCADA TX) + " +
            options.RtuTx.PortName + " (RTU TX)";
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
        PassiveScadaCountText.Text = progress.ScadaBytes.ToString("N0") + " bytes";
        PassiveRtuCountText.Text = progress.RtuBytes.ToString("N0") + " bytes";
        PassiveIntegrityText.Text = progress.IntegrityWarning
            ? "Integrity warning" : progress.Status == "Recording" ? "No app loss detected" : progress.Status;
        PassiveIntegrityText.Foreground = (Brush)FindResource(progress.IntegrityWarning ? "BadBrush" : "Ink900Brush");
        if (!string.IsNullOrWhiteSpace(progress.CaptureDirectory))
            PassiveCapturePathText.Text = progress.CaptureDirectory;

        // Frame reconstruction is independent of the raw sample view and its Freeze flag.
        // Every observed frame is projected onto the EXISTING Evidence/Trace/Values/Event pipeline.
        var framed = 0;
        while (framed < 180 && session.TryReadFrame(out var frame))
        {
            FeedPassiveFrameToExistingViews(frame);
            framed++;
        }

        if (PassiveFreezeCheckBox.IsChecked == true) return;
        var count = 0;
        while (count < 65 && session.TryReadPreview(out var sample))
        {
            var row = new PassiveRxRow(
                sample.EstimatedUtc.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
                sample.StreamId == 0 ? "SCADA → RTU" : "RTU → SCADA",
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
        var raw = seen.RawBytes;
        var controlOffset = seen.Format == "Fixed" ? 1 : seen.Format == "Variable" ? 4 : -1;
        var control = controlOffset >= 0 && raw.Length > controlOffset ? (byte?)raw[controlOffset] : null;
        var linkWidth = _passiveLinkAddressWidth;
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
            "Partial" => Ft12FrameFormat.Malformed,
            _ => Ft12FrameFormat.SingleCharacter
        };
        var frame = new Ft12FrameDecode
        {
            Format = format, RawBytes = raw, Control = control,
            LinkAddress = control.HasValue ? address : null,
            LinkAddressSize = linkWidth, LinkControl = link,
            AsduBytes = asduRaw, IsChecksumValid = seen.ChecksumValid,
            IsLengthValid = true,
            Issues = seen.ChecksumValid ? Array.Empty<string>() : new[] { "Bad FT1.2 checksum." }
        };
        var direction = seen.StreamId == 0 ? FrameDirection.MasterToSlave : FrameDirection.SlaveToMaster;
        var prefix = seen.StreamId == 0 ? "SCADA → RTU" : "RTU → SCADA";
        var function = seen.Format == "Partial" ? "Incomplete frame" :
            link?.FunctionName ?? (raw[0] == 0xE5 ? "ACK (E5)" : "NACK (A2)");
        var meaning = decode?.ShortMeaning ?? function;
        if (!seen.ChecksumValid && seen.Format != "Partial")
            meaning = "Bad checksum · " + meaning;
        var first = decode?.FirstObject;

        var ev = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = ++_passiveFrameSequence,
            TimestampUtc = seen.EstimatedUtc, Direction = direction,
            Category = seen.ChecksumValid ? "Passive Traffic" : "RX Warning",
            DataClass = link?.FunctionCode switch { 10 => "Class 1", 11 => "Class 2", _ => "-" },
            PollingReason = "PASSIVE-IEC101", ProtocolMode = Iec60870ProtocolMode.Iec101,
            Summary = prefix + " · " + function,
            Detail = meaning + $" · Link {address} · RX offset {seen.StreamOffset}" +
                     (decode?.Issues.Count > 0 ? " · " + string.Join("; ", decode.Issues) : ""),
            OperatorMessage = meaning, ProtocolMeaning = meaning,
            OperatorAction = "Observed only; no commands transmitted",
            RawHex = Convert.ToHexString(raw),
            Frame = frame, LinkAddress = control.HasValue ? address : null,
            TypeId = decode?.TypeId,
            TypeName = decode?.TypeName ?? "",
            VariableStructureQualifier = decode?.VariableStructureQualifier,
            IsSequenceAsdu = decode?.IsSequence,
            ObjectCount = decode?.ObjectCount,
            CauseOfTransmission = decode?.CauseOfTransmission,
            CauseName = decode?.CauseName ?? "",
            OriginatorAddress = decode?.OriginatorAddress,
            CommonAddressNumber = decode?.CommonAddress,
            InformationObjectAddress = first?.InformationObjectAddress,
            ObjectSummary = decode?.ObjectSummary ?? "",
            QualityText = first?.QualityText ?? "",
            IsRelayEdgeEvent = seen.ChecksumValid && seen.StreamId == 1 && first is not null &&
                decode?.CauseOfTransmission == 3 && decode.TypeId is >= 1 and <= 44,
            IsRelayValue = seen.ChecksumValid && seen.StreamId == 1 && first is not null && decode is not null &&
                decode.TypeId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 30 or 31 or 32 or 34 or 35 or 36 or 37,
            SignalKey = first is null || decode is null ? "" : $"{decode.CommonAddress}:{first.InformationObjectAddress}",
            SignalName = first is null ? "" : $"IOA {first.InformationObjectAddress}",
            SignalGroup = "IEC-101",
            SignalType = decode?.TypeName ?? "",
            SignalDisplayValue = first?.ShortValue ?? "",
            SignalRawValue = first?.ValueText ?? "",
            RelayTimestampText = first?.TimestampText ?? "",
            RelayTimestampInvalid = first?.TimestampText.Contains("invalid", StringComparison.OrdinalIgnoreCase) == true
        };
        OnEvidenceReceived(this, ev);

        // Multi-object ASDUs must update EVERY point, not only the first IOA.
        if (seen.ChecksumValid && seen.StreamId == 1 && decode is not null &&
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
                    RawHex = Convert.ToHexString(raw),
                    TypeId = decode.TypeId, TypeName = decode.TypeName,
                    CauseOfTransmission = decode.CauseOfTransmission,
                    CauseName = decode.CauseName, CommonAddressNumber = decode.CommonAddress,
                    InformationObjectAddress = obj.InformationObjectAddress,
                    ObjectSummary = obj.ReadableSummary,
                    IsRelayValue = true,
                    IsRelayEdgeEvent = decode.CauseOfTransmission == 3 && decode.TypeId is >= 1 and <= 44,
                    SignalKey = $"{decode.CommonAddress}:{obj.InformationObjectAddress}",
                    SignalName = $"IOA {obj.InformationObjectAddress}",
                    SignalGroup = "IEC-101",
                    SignalType = decode.TypeName,
                    SignalDisplayValue = obj.ShortValue,
                    SignalRawValue = obj.ValueText,
                    RelayTimestampText = obj.TimestampText
                });
            }
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
        if (_closingAfterPassiveStop || _passiveMonitorSession is null) return;
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
