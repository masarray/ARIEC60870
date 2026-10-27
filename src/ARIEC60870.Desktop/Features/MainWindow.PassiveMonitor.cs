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
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

/// <summary>Passive RX capture lives in existing Setup, Connect and Trace workspaces.</summary>
public partial class MainWindow
{
    private const int PassiveTraceTabIndex = 10;
    private const int MaxVisiblePassiveRows = 600;
    private readonly ObservableCollection<PassiveRxRow> _passiveRawRows = new();
    private DispatcherTimer? _passiveUiTimer;
    private PassiveMonitorSession? _passiveMonitorSession;
    private Task<(string Directory, PassiveCaptureManifest Manifest)>? _passiveCaptureTask;
    private (string Directory, PassiveCaptureManifest Manifest)? _passiveCaptureResult;
    private bool _closingAfterPassiveStop;

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
            NavFrameButton.Tag?.ToString() != "10")
            throw new InvalidOperationException("Passive mode is not correctly embedded in Setup/Trace; active controls remain visible.");

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
            "8E1" => Parity.Even,
            "8N1" => Parity.None,
            "8O1" => Parity.Odd,
            _ => throw new InvalidOperationException("Unsupported serial framing. Choose 8E1 / 8N1 / 8O1.")
        };
        var output = string.IsNullOrWhiteSpace(PassiveCaptureFolderBox.Text)
            ? DefaultPassiveCaptureDirectory() : PassiveCaptureFolderBox.Text.Trim();
        var options = new PassiveCaptureOptions(output,
            new PassiveSerialSource(first, "SCADA TX -> RTU", baud, parity, 8, StopBits.One),
            new PassiveSerialSource(second, "RTU TX -> SCADA", baud, parity, 8, StopBits.One));
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
        _passiveRawRows.Clear();
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
