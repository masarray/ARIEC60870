// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ARIEC60870.Monitor;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

/// <summary>
/// Independent receive-only forensic capture workspace.
/// No Active Master/session dependencies and no on-wire command controls.
/// </summary>
public partial class PassiveMonitorWindow : Window
{
    private const int MaxVisibleSamples = 750;
    private const int MaxPreviewDrainPerTick = 72;

    private readonly DispatcherTimer _refreshTimer;
    private readonly ObservableCollection<PassiveMonitorUiRow> _recentRxRows = new();
    private PassiveMonitorSession? _session;
    private CancellationTokenSource? _sessionStop;
    private Task? _captureTask;
    private (string Directory, PassiveCaptureManifest Manifest)? _lastCapture;
    private bool _isStopping;
    private bool _closeAfterStop;

    public ReadOnlyObservableCollection<PassiveMonitorUiRow> RecentRxRows { get; }

    public PassiveMonitorWindow()
    {
        InitializeComponent();
        RecentRxRows = new ReadOnlyObservableCollection<PassiveMonitorUiRow>(_recentRxRows);
        DataContext = this;
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _refreshTimer.Tick += (_, _) => RefreshLiveStatus();
        _refreshTimer.Start();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
            documents = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        OutputDirectoryTextBox.Text = Path.Combine(documents, "ARIEC60870", "PassiveCaptures");
        RefreshSerialPorts();
    }

    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshSerialPorts();

    private void RefreshSerialPorts()
    {
        var previousScada = ScadaPortCombo.Text;
        var previousRtu = RtuPortCombo.Text;
        try
        {
            var available = SerialPort.GetPortNames()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ScadaPortCombo.ItemsSource = available;
            RtuPortCombo.ItemsSource = available;
            ScadaPortCombo.Text = !string.IsNullOrWhiteSpace(previousScada)
                ? previousScada : available.FirstOrDefault() ?? string.Empty;
            RtuPortCombo.Text = !string.IsNullOrWhiteSpace(previousRtu)
                ? previousRtu : available.Skip(1).FirstOrDefault() ?? string.Empty;
            if (available.Length < 2)
                CaptureWarningText.Text = "Two free USB serial RX ports are required. Connect both adapters and refresh.";
        }
        catch (Exception ex)
        {
            CaptureWarningText.Text = "Unable to enumerate serial ports: " + ex.Message;
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog
        {
            Title = "Choose a local folder for IEC-101 raw capture",
            Multiselect = false
        };
        if (Directory.Exists(OutputDirectoryTextBox.Text))
            picker.InitialDirectory = OutputDirectoryTextBox.Text;
        if (picker.ShowDialog(this) == true)
            OutputDirectoryTextBox.Text = picker.FolderName;
    }

    private static string SelectedComboItem(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Content is not null)
            return item.Content.ToString() ?? string.Empty;
        return combo.Text.Trim();
    }

    private void StartRecording_Click(object sender, RoutedEventArgs e)
    {
        if (_captureTask is { IsCompleted: false } || _isStopping)
            return;
        if (SafeTapCheckBox.IsChecked != true)
        {
            MessageBox.Show(this,
                "Check the RX-only tap safety confirmation first.\n" +
                "A COM port opening does not make an RS-232 connection electrically safe.",
                "Passive monitor wiring", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PassiveCaptureOptions options;
        try
        {
            var baud = int.Parse(SelectedComboItem(BaudCombo), System.Globalization.CultureInfo.InvariantCulture);
            var parity = Enum.Parse<Parity>(SelectedComboItem(ParityCombo), ignoreCase: true);
            var dataBits = int.Parse(SelectedComboItem(DataBitsCombo), System.Globalization.CultureInfo.InvariantCulture);
            var stopValue = SelectedComboItem(StopBitsCombo);
            var stop = stopValue switch
            {
                "1" => StopBits.One,
                "1.5" => StopBits.OnePointFive,
                "2" => StopBits.Two,
                _ => throw new ArgumentException("Unsupported stop bits.")
            };
            var scada = new PassiveSerialSource(ScadaPortCombo.Text.Trim(), "SCADA TX -> RTU",
                baud, parity, dataBits, stop);
            var rtu = new PassiveSerialSource(RtuPortCombo.Text.Trim(), "RTU TX -> SCADA",
                baud, parity, dataBits, stop);
            options = new PassiveCaptureOptions(OutputDirectoryTextBox.Text, scada, rtu);
            options.Validate();
            if (Directory.Exists(OutputDirectoryTextBox.Text))
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(OutputDirectoryTextBox.Text))!);
                if (drive.AvailableFreeSpace < 200L * 1024 * 1024)
                    throw new IOException("Less than 200 MB available in the capture drive.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Capture setup invalid",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _recentRxRows.Clear();
        SelectedHexText.Text = "Receiving raw bytes...";
        _lastCapture = null;
        _sessionStop?.Dispose();
        _sessionStop = new CancellationTokenSource();
        _session = new PassiveMonitorSession(options,
            new WindowsReadOnlySerialTap(options.ScadaTx),
            new WindowsReadOnlySerialTap(options.RtuTx));
        SetRecordingUi(true);
        CaptureStateText.Text = "Opening RX...";
        CapturePathText.Text = "Preparing capture files...";
        CaptureWarningText.Text = "RECEIVE ONLY: recording all incoming bytes to disk. This view shows sampled chunks.";
        _captureTask = CaptureAndFinalizeAsync(_session, _sessionStop.Token);
    }

    private async Task CaptureAndFinalizeAsync(PassiveMonitorSession session, CancellationToken token)
    {
        try
        {
            var result = await session.RunAsync(token);
            _lastCapture = result;
            RefreshLiveStatus();
            CapturePathText.Text = result.Directory;
            OpenCaptureFolderButton.IsEnabled = true;
            VerifyButton.IsEnabled = true;
            CaptureStateText.Text = result.Manifest.Status;
            if (result.Manifest.Warnings.Length > 0)
                CaptureWarningText.Text = string.Join(" | ", result.Manifest.Warnings.Take(3));
            else
                CaptureWarningText.Text = "Finalized. Click Verify Capture to check journal CRC, sequence, offsets and SHA256.";
        }
        catch (Exception ex)
        {
            CaptureStateText.Text = "Failed";
            CaptureWarningText.Text = "Capture failed: " + ex.Message;
        }
        finally
        {
            _isStopping = false;
            SetRecordingUi(false);
        }
    }

    private async void StopRecording_Click(object sender, RoutedEventArgs e)
    {
        try { await StopAndFinalizeAsync(); }
        catch (Exception ex) { CaptureWarningText.Text = "Stop error: " + ex.Message; }
    }

    private async Task StopAndFinalizeAsync()
    {
        if (_captureTask is null || _captureTask.IsCompleted) return;
        _isStopping = true;
        StopRecordingButton.IsEnabled = false;
        CaptureStateText.Text = "Finalizing...";
        _sessionStop?.Cancel();
        await _captureTask;
    }

    private void SetRecordingUi(bool recording)
    {
        StartRecordingButton.IsEnabled = !recording;
        StopRecordingButton.IsEnabled = recording;
        ScadaPortCombo.IsEnabled = !recording;
        RtuPortCombo.IsEnabled = !recording;
        BaudCombo.IsEnabled = !recording;
        ParityCombo.IsEnabled = !recording;
        DataBitsCombo.IsEnabled = !recording;
        StopBitsCombo.IsEnabled = !recording;
        BrowseFolderButton.IsEnabled = !recording;
        SafeTapCheckBox.IsEnabled = !recording;
        VerifyButton.IsEnabled = !recording && _lastCapture.HasValue;
    }

    private void RefreshLiveStatus()
    {
        var session = _session;
        if (session is null) return;
        var progress = session.GetProgress();
        ScadaBytesText.Text = $"{progress.ScadaBytes:N0} bytes";
        RtuBytesText.Text = $"{progress.RtuBytes:N0} bytes";
        if (_captureTask is { IsCompleted: false } && !_isStopping)
            CaptureStateText.Text = progress.Status;
        IntegrityText.Text = progress.IntegrityWarning
            ? "Integrity warning" : progress.Status == "Recording"
                ? "No app loss detected" : progress.Status;
        IntegrityText.Foreground = progress.IntegrityWarning
            ? (Brush)FindResource("BadBrush") : (Brush)FindResource("Ink900Brush");
        if (!string.IsNullOrWhiteSpace(progress.CaptureDirectory))
        {
            CapturePathText.Text = progress.CaptureDirectory;
            OpenCaptureFolderButton.IsEnabled = true;
        }

        if (FreezeViewCheckBox.IsChecked == true) return;
        var appended = 0;
        while (appended < MaxPreviewDrainPerTick && session.TryReadPreview(out var preview))
        {
            var item = new PassiveMonitorUiRow(
                preview.EstimatedUtc.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
                preview.StreamId == 0 ? "SCADA → RTU" : "RTU → SCADA",
                preview.Sequence,
                preview.ByteCount,
                Convert.ToHexString(preview.FirstBytes, 0, preview.FirstBytes.Length));
            _recentRxRows.Add(item);
            if (_recentRxRows.Count > MaxVisibleSamples)
                _recentRxRows.RemoveAt(0);
            appended++;
        }
        if (appended > 0 && TrafficGrid.SelectedItem is null && _recentRxRows.Count > 0)
            TrafficGrid.ScrollIntoView(_recentRxRows[^1]);
    }

    private void TrafficGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TrafficGrid.SelectedItem is PassiveMonitorUiRow row)
            SelectedHexText.Text = row.Hex + (row.ByteCount > 48 ? "  ... (preview; full data on disk)" : string.Empty);
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (!_lastCapture.HasValue || _captureTask is { IsCompleted: false }) return;
        VerifyButton.IsEnabled = false;
        CaptureWarningText.Text = "Verifying journal, CRC and SHA256...";
        try
        {
            var audit = await Task.Run(() => PassiveJournalVerifier.Verify(_lastCapture.Value.Directory));
            IntegrityText.Text = audit.Complete ? "VERIFIED" : "INCOMPLETE";
            IntegrityText.Foreground = audit.Complete
                ? (Brush)FindResource("GoodBrush") : (Brush)FindResource("BadBrush");
            CaptureWarningText.Text = audit.Complete
                ? $"PASS: {audit.Streams.Sum(x => x.RawBytes):N0} bytes from two RX directions verified (software/driver evidence only)."
                : "Audit failed: " + string.Join(" | ", audit.Problems.Take(4));
        }
        catch (Exception ex)
        {
            IntegrityText.Text = "VERIFY ERROR";
            CaptureWarningText.Text = ex.Message;
        }
        finally { VerifyButton.IsEnabled = _lastCapture.HasValue; }
    }

    private void OpenCaptureFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _session?.GetProgress().CaptureDirectory;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CaptureWarningText.Text = "Open folder failed: " + ex.Message;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_captureTask is { IsCompleted: false } && !_closeAfterStop)
        {
            e.Cancel = true;
            _closeAfterStop = true;
            IsEnabled = false;
            _ = FinishBeforeCloseAsync();
            return;
        }
        _refreshTimer.Stop();
        _sessionStop?.Dispose();
    }

    private async Task FinishBeforeCloseAsync()
    {
        try { await StopAndFinalizeAsync(); }
        finally { Close(); }
    }
}

public sealed record PassiveMonitorUiRow(
    string UtcText, string Direction, long Sequence, int ByteCount, string Hex);
