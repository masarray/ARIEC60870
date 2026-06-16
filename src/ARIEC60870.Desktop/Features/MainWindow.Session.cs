// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ARIEC60870.Core.Mapping;
using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Reporting;
using ARIEC60870.Master.Transport;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private void ConnectToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionCancellation is null)
        {
            Start_Click(sender, e);
        }
        else
        {
            Stop_Click(sender, e);
        }
    }

    private void UpdateConnectToggleVisual(bool isRunning)
    {
        if (StartButton is null)
        {
            return;
        }

        if (ConnectToggleCaption is not null)
        {
            ConnectToggleCaption.Text = isRunning ? "Disconnect" : "Connect";
        }

        if (ConnectIconOn is not null)
        {
            ConnectIconOn.Visibility = isRunning ? Visibility.Collapsed : Visibility.Visible;
        }

        if (ConnectIconOff is not null)
        {
            ConnectIconOff.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        }

        StartButton.Background = (Brush)new BrushConverter().ConvertFromString("#F4F8FF")!;
        StartButton.BorderBrush = Brushes.Transparent;
        StartButton.Foreground = (Brush)new BrushConverter().ConvertFromString(isRunning ? "#B91C1C" : "#166534")!;
        StartButton.ToolTip = isRunning ? "Disconnect and close transport" : "Connect and monitor continuously";
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionCancellation != null)
        {
            return;
        }

        Iec103MasterSettings settings;
        int durationSeconds;
        try
        {
            settings = BuildSettingsFromUi();
            durationSeconds = ReadInt(DurationBox, "Session timeout", 0, 86400);
            SaveSetupPreferences(settings, durationSeconds, silent: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearSessionView(clearLog: false);
        SeedValueViewerFromIoaProfile(settings.ProtocolMode);
        _stopRequested = false;
        SetRunUiState(isRunning: true);
        _lastResult = null;
        _sessionCancellation = new CancellationTokenSource();
        SessionSubtitleText.Text = settings.SerialSummary;
        UpdateStableHeader("Monitoring", settings.UseSimulatedSlave
            ? "Demo mode active. Monitoring continuously until Stop."
            : (settings.ProtocolMode == Iec60870ProtocolMode.Iec104
                ? "TCP client session active. Monitoring continuously until Stop."
                : "Serial master session active. Monitoring continuously until Stop."));
        AppendSessionLog("Starting master session: " + settings.SerialSummary);
        if (settings.ProtocolMode != Iec60870ProtocolMode.Iec104)
        {
            var estimatedClass2CycleMs = EstimatePracticalSerialCycleMs(settings);
            AppendSessionLog($"Class 2 scan feasibility: configured={settings.Class2PollIntervalMs} ms, estimated physical minimum≈{estimatedClass2CycleMs} ms at {settings.BaudRate} bps.");
            if (settings.BaudRate <= 1200)
            {
                AppendSessionLog("Low-baud serial timing guard active: timeout/poll/backoff widened for 1200 bps field channels; 100 ms polling cannot be treated as a guaranteed measurement refresh at this speed.");
            }
        }

        AppendSessionLog("Target mode: " + (settings.UseSimulatedSlave ? settings.TargetProfile + " simulation" : settings.TargetProfile));
        AppendSessionLog(settings.ProtocolMode == Iec60870ProtocolMode.Iec104
            ? "IEC-104 profile: STARTDT, optional clock sync/GI, I/S/U frame evidence, and TESTFR health check."
            : "Polling profile: Class 2 normal cycle; Class 1 only when ACD=1 or bounded GI follow-up.");
        AppendSessionLog(settings.ProtocolMode == Iec60870ProtocolMode.Iec103
            ? (_mappingProfile.HasSignals ? $"Mapping profile loaded: {_mappingProfile.ProfileName} ({_mappingProfile.Signals.Count} signals)." : "No mapping profile loaded. Value/Event views will show raw FUN/INF names.")
            : (_ioaProfile.HasPoints ? $"IOA mapping profile loaded: {_ioaProfile.ProfileName} ({_ioaProfile.Points.Count} points)." : "IEC-101/104 uses raw IOA labels. Load or edit an IOA mapping profile for project names."));

        try
        {
            await using var transport = CreateTransport(settings);
            _activeTransport = transport;
            var session = CreateSession(settings, transport);
            _activeControlSession = session as IProtocolControlCommandSession;
            session.EvidenceReceived += OnEvidenceReceived;
            session.FindingRaised += OnFindingRaised;

            var result = durationSeconds <= 0
                ? await session.RunAsync(_sessionCancellation.Token).ConfigureAwait(false)
                : await session.RunForAsync(TimeSpan.FromSeconds(durationSeconds), _sessionCancellation.Token).ConfigureAwait(false);
            _lastResult = result;

            await Dispatcher.InvokeAsync(() =>
            {
                ApplyFinalResult(result);
                AppendSessionLog("Monitor session completed: " + result.CompletionReason);
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateStableHeader("Stopped", "Session stopped by user.");
                AppendSessionLog("Session stopped by user.");
            });
        }
        catch (Exception ex) when (_stopRequested || _sessionCancellation?.IsCancellationRequested == true)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateStableHeader("Stopped", "Session stopped and transport was closed safely.");
                AppendSessionLog("Session stopped while transport was closing: " + ex.Message);
                AddUiDiagnostic("Warning", "Desktop", "IEC103-DESKTOP-STOP-CLOSE", "Session stopped while transport was closing", ex.Message, "Usually safe during Stop/Force Close. If repeated, check USB/serial driver stability.", ex);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateStableHeader("Faulted", ex.Message);
                AppendSessionLog("Fault captured in Diagnostics: " + ex.Message);
                AddUiDiagnostic("Error", "Desktop", "IEC103-DESKTOP-SESSION-FAULT", "Master session fault", ex.Message, "Select this diagnostic row and copy detail if escalation/debugging is needed.", ex);
            });
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _activeControlSession = null;
                _activeTransport = null;
                _stopRequested = false;
                _sessionCancellation?.Dispose();
                _sessionCancellation = null;
                SetRunUiState(isRunning: false);
            });
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionCancellation is null)
        {
            SetRunUiState(isRunning: false);
            return;
        }

        _stopRequested = true;
        _sessionCancellation.Cancel();
        StopButton.IsEnabled = true;
        StopButton.ToolTip = "Force close transport";
        UpdateStableHeader("Stopping", "Closing active transport safely.");
        AppendSessionLog("Stop requested by user. Active transport close requested.");

        await TryCloseActiveTransportAsync("Stop request");
    }

    private void SignalList_Click(object sender, RoutedEventArgs e)
    {
        EditSignalList_Click(sender, e);
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => ClearSessionView(clearLog: true);


    private Iec103MasterSettings BuildSettingsFromUi()
    {
        var port = (PortComboBox.SelectedItem as string)?.Trim();

        var settings = Iec103MasterSettings.CreateDefault();
        settings.UseSimulatedSlave = IsDemoModeSelected();
        settings.ProtocolMode = GetSelectedProtocolMode();
        if (settings.ProtocolMode != Iec60870ProtocolMode.Iec104 && string.IsNullOrWhiteSpace(port))
        {
            throw new InvalidOperationException("COM port is required for IEC-101/103 serial mode.");
        }
        settings.TargetProfile = settings.ProtocolMode switch
        {
            Iec60870ProtocolMode.Iec101 => settings.UseSimulatedSlave ? "IEC-101 demo outstation" : "IEC-101 RTU/outstation",
            Iec60870ProtocolMode.Iec104 => settings.UseSimulatedSlave ? "IEC-104 demo server" : "IEC-104 server",
            _ => settings.UseSimulatedSlave ? "generic relay demo slave" : "IEC-103 protection relay"
        };
        settings.PortName = port ?? string.Empty;
        settings.BaudRate = ReadComboInt(BaudComboBox, "Baudrate");
        if (settings.BaudRate < 300 || settings.BaudRate > 921600)
        {
            throw new InvalidOperationException("Baudrate must be between 300 and 921600 bps.");
        }

        settings.TcpHost = TcpHostBox.Text.Trim();
        settings.TcpPort = ReadInt(TcpPortBox, "IEC-104 TCP Port", 1, 65535);

        if (settings.ProtocolMode == Iec60870ProtocolMode.Iec103)
        {
            settings.LinkAddressSize = 1;
            settings.CauseOfTransmissionSize = 1;
            settings.CommonAddressSize = 1;
            settings.InformationObjectAddressSize = 1;
            settings.LinkAddress = ReadInt(LinkAddressBox, "IEC-103 Link Address", 0, 255);
            settings.CommonAddress = ReadInt(CommonAddressBox, "IEC-103 Common Address", 0, 255);
        }
        else
        {
            settings.LinkAddressSize = settings.ProtocolMode == Iec60870ProtocolMode.Iec101 ? ReadComboInt(LinkAddressSizeComboBox, "Link address size") : 1;
            settings.CauseOfTransmissionSize = ReadComboInt(CotSizeComboBox, "Cause of transmission size");
            settings.CommonAddressSize = ReadComboInt(CaSizeComboBox, "Common address size");
            settings.InformationObjectAddressSize = ReadComboInt(IoaSizeComboBox, "Information object address size");
            settings.TransmissionMode = (TransmissionModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Unbalanced";

            if (settings.ProtocolMode == Iec60870ProtocolMode.Iec101 && settings.TransmissionMode.StartsWith("Balanced", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("IEC-101 Balanced mode is not active in this build. Use Unbalanced for master polling, or treat Balanced as a roadmap item until the balanced link-layer engine is implemented.");
            }

            if (settings.ProtocolMode == Iec60870ProtocolMode.Iec101 && settings.LinkAddressSize == 0)
            {
                throw new InvalidOperationException("IEC-101 link address size 0 is a valid profile case only for specific balanced/monitor links. This build implements unbalanced master polling, so use 1 or 2 octets for field validation.");
            }

            var linkMax = settings.LinkAddressSize == 0 ? 0 : settings.LinkAddressSize == 1 ? 255 : 65535;
            var caMax = settings.CommonAddressSize == 1 ? 255 : 65535;
            settings.LinkAddress = settings.ProtocolMode == Iec60870ProtocolMode.Iec101 ? ReadInt(LinkAddressBox, "IEC-101 Link Address", 0, linkMax) : 0;
            settings.CommonAddress = ReadInt(CommonAddressBox, "Common Address", 0, caMax);
        }

        settings.Iec104T0TimeoutMs = ReadInt(Iec104T0Box, "IEC-104 t0", 1000, 120000);
        settings.Iec104T1AckTimeoutMs = ReadInt(Iec104T1Box, "IEC-104 t1", 1000, 120000);
        settings.Iec104T2AckDelayMs = ReadInt(Iec104T2Box, "IEC-104 t2", 1000, 120000);
        settings.Iec104T3TestIntervalMs = ReadInt(Iec104T3Box, "IEC-104 t3", 1000, 300000);
        settings.Iec104KMaxUnacknowledged = ReadInt(Iec104KBox, "IEC-104 k", 1, 32767);
        settings.Iec104WReceiveWindow = ReadInt(Iec104WBox, "IEC-104 w", 1, 32767);
        settings.ResponseTimeoutMs = ReadInt(TimeoutBox, "Timeout", 100, 60000);
        settings.Class2PollIntervalMs = ReadInt(Class2IntervalBox, "Class 2 interval", 50, 60000);
        settings.MaxClass1DrainFrames = ReadInt(MaxDrainBox, "Max Class 1 drain", 1, 512);
        settings.ResetRemoteLinkOnConnect = ResetRemoteLinkCheckBox.IsChecked == true;
        settings.ResetFcbOnConnect = settings.ProtocolMode == Iec60870ProtocolMode.Iec101
            ? false
            : ResetFcbCheckBox.IsChecked == true;
        settings.SendClockSyncOnConnect = ClockSyncCheckBox.IsChecked == true;
        settings.SendGeneralInterrogationOnConnect = GiCheckBox.IsChecked == true;
        settings.RequestClass2ImmediatelyAfterStartup = Class2StartupCheckBox.IsChecked == true;
        settings.MappingProfilePath = MappingProfilePathBox.Text.Trim();
        ApplyLowBaudSerialTimingGuard(settings);

        var serialMode = (SerialModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "8E1";
        settings.DataBits = 8;
        settings.StopBits = StopBits.One;
        settings.Parity = serialMode switch
        {
            "8N1" => Parity.None,
            "8O1" => Parity.Odd,
            _ => Parity.Even
        };

        return settings;
    }

    private static int EstimatePracticalSerialCycleMs(Iec103MasterSettings settings)
    {
        var bitsPerByte = 1 + settings.DataBits + (settings.Parity == Parity.None ? 0 : 1) + (settings.StopBits == StopBits.Two ? 2 : 1);
        var requestBytes = 4 + Math.Max(0, settings.LinkAddressSize);
        var typicalResponseBytes = 16 + Math.Max(0, settings.LinkAddressSize) + settings.CommonAddressSize + settings.CauseOfTransmissionSize + settings.InformationObjectAddressSize + 12;
        var baud = Math.Max(300, settings.BaudRate);
        var wireMs = (int)Math.Ceiling((requestBytes + typicalResponseBytes) * bitsPerByte * 1000.0 / baud);
        var turnaroundMs = baud <= 1200 ? 220 : baud <= 2400 ? 140 : 70;
        return Math.Max(50, wireMs + turnaroundMs + settings.Class1DrainDelayMs);
    }

    private static void ApplyLowBaudSerialTimingGuard(Iec103MasterSettings settings)
    {
        if (settings.ProtocolMode == Iec60870ProtocolMode.Iec104 || settings.BaudRate > 1200)
        {
            return;
        }

        // Low-speed IEC-101/103 channels are common in legacy utility links. A large ASDU,
        // modem/RS-485 turnaround time, or Class 1 drain cycle can exceed aggressive bench
        // timing. Guard the session so 1200 bps does not fail simply because the analyzer
        // was tuned for 9600/19200 bps lab links.
        settings.ResponseTimeoutMs = Math.Max(settings.ResponseTimeoutMs, 5000);
        settings.Class2PollIntervalMs = Math.Max(settings.Class2PollIntervalMs, 1000);
        settings.BusyBackoffMs = Math.Max(settings.BusyBackoffMs, 500);
        settings.TimeoutRecoveryBackoffMs = Math.Max(settings.TimeoutRecoveryBackoffMs, 500);
    }

    private async Task TryCloseActiveTransportAsync(string reason)
    {
        var transport = _activeTransport;
        if (transport is null)
        {
            return;
        }

        try
        {
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() => AppendSessionLog($"Transport closed: {reason}."));
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                AppendSessionLog($"Transport close warning: {ex.Message}");
                AddUiDiagnostic("Warning", "Transport", "IEC103-TRANSPORT-CLOSE", "Transport close warning", ex.Message, "Stop/Force Close requested. If COM port remains locked, unplug/replug the USB converter or restart the app.", ex);
            });
        }
    }

    private IByteTransport CreateTransport(Iec103MasterSettings settings)
    {
        return settings.ProtocolMode switch
        {
            Iec60870ProtocolMode.Iec104 => settings.UseSimulatedSlave
                ? new SimulatedIec104ServerTransport(settings)
                : new TcpClientByteTransport(settings),
            Iec60870ProtocolMode.Iec101 => settings.UseSimulatedSlave
                ? new SimulatedIec101Transport(settings)
                : new SerialByteTransport(settings),
            _ => settings.UseSimulatedSlave
                ? new SimulatedRelayTransport(settings)
                : new SerialByteTransport(settings)
        };
    }

    private IProtocolMasterSession CreateSession(Iec103MasterSettings settings, IByteTransport transport)
    {
        return settings.ProtocolMode switch
        {
            Iec60870ProtocolMode.Iec104 => new Iec104ClientSession(settings, transport),
            Iec60870ProtocolMode.Iec101 => new Iec101MasterSession(settings, transport),
            _ => new Iec103MasterSession(settings, transport, _mappingProfile)
        };
    }


    private void ProtocolModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ApplyProtocolUxProfile(GetSelectedProtocolMode());
    }

    private void ApplyProtocolUxProfile(Iec60870ProtocolMode mode)
    {
        var is103 = mode == Iec60870ProtocolMode.Iec103;
        var is101 = mode == Iec60870ProtocolMode.Iec101;
        var is104 = mode == Iec60870ProtocolMode.Iec104;
        var serialVisibility = is104 ? Visibility.Collapsed : Visibility.Visible;
        var tcpVisibility = is104 ? Visibility.Visible : Visibility.Collapsed;
        var funInfVisibility = is103 ? Visibility.Visible : Visibility.Collapsed;
        var ioaVisibility = is103 ? Visibility.Collapsed : Visibility.Visible;
        var apciVisibility = is104 ? Visibility.Visible : Visibility.Collapsed;
        var classVisibility = is104 ? Visibility.Collapsed : Visibility.Visible;

        ProductTitleText.Text = "ARIEC60870 Evidence Analyzer";
        ApplyProtocolLogo(mode);
        ClassPollLabelText.Text = is104 ? "GI/I/S " : "GI/C1/C2 ";
        EventChipLabelText.Text = is104 ? "ASDU " : "EVENT ";
        CommandDockStatusText.Text = is103
            ? "IEC-103 selected. Command Dock is active for IEC-101/104 control ASDUs only in this build."
            : "Ready. Connect first, then queue GI, read, clock sync, or safe test commands.";

        SetupTitleText.Text = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "IEC-101 telecontrol serial setup",
            Iec60870ProtocolMode.Iec104 => "IEC-104 telecontrol TCP/IP setup",
            _ => "IEC-103 protection relay setup"
        };
        SetupSubtitleText.Text = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "Serial telecontrol interface: link address, CA, IOA, COT, General Interrogation and Class 1/Class 2 polling.",
            Iec60870ProtocolMode.Iec104 => "TCP/IP telecontrol interface: server endpoint, STARTDT, APCI I/S/U frames, CA, IOA and ASDU decode.",
            _ => "Serial protection interface: link address, Class 1/Class 2 policy, FUN/INF mapping."
        };
        ProtocolSetupBadgeText.Text = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "IEC-101 · FT1.2 serial telecontrol",
            Iec60870ProtocolMode.Iec104 => "IEC-104 · TCP/IP telecontrol",
            _ => "IEC-103 · FT1.2 serial protection"
        };
        ProtocolSetupDescriptionText.Text = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "Use this profile for serial RTU/outstation tests. Main addressing is CA + IOA; Type ID and COT explain what data is returned and why.",
            Iec60870ProtocolMode.Iec104 => "Use this profile for IEC-104 server tests over TCP. The frame trace exposes APCI format, sequence numbers, STARTDT/TESTFR control and ASDU payload.",
            _ => "Use this profile for protection IED IEC-103 tests. Main addressing is FUN/INF; Class 1 carries events, Class 2 carries background data."
        };
        SerialConnectionTitleText.Text = is101 ? "IEC-101 SERIAL CONNECTION" : "IEC-103 SERIAL CONNECTION";
        PollingPolicyTitleText.Text = is101 ? "IEC-101 CLASS POLLING" : "IEC-103 CLASS POLLING";
        Class2IntervalLabelText.Text = is101 ? "Class 2 scan interval (ms)" : "Class 2 interval (ms)";
        MaxDrainLabelText.Text = is101 ? "Max Class 1 event drain" : "Max Class 1 drain";
        LinkAddressLabelText.Text = is101 ? "Link Address" : "Link Address";
        CommonAddressLabelText.Text = is104 ? "Common Address (CA)" : is101 ? "Common Address (CA)" : "Common Address";
        if (string.IsNullOrWhiteSpace(CommandCaBox.Text)) CommandCaBox.Text = string.IsNullOrWhiteSpace(CommonAddressBox.Text) ? "1" : CommonAddressBox.Text;
        Iec10xProfileTitleText.Text = is104 ? "IEC-104 INTEROPERABILITY PROFILE" : "IEC-101 INTEROPERABILITY PROFILE";
        if (is103)
        {
            LinkAddressSizeComboBox.SelectedIndex = 1;
            CotSizeComboBox.SelectedIndex = 0;
            CaSizeComboBox.SelectedIndex = 0;
            IoaSizeComboBox.SelectedIndex = 0;
        }
        else
        {
            if (CotSizeComboBox.SelectedIndex < 0) CotSizeComboBox.SelectedIndex = 1;
            if (CaSizeComboBox.SelectedIndex < 0) CaSizeComboBox.SelectedIndex = 1;
            if (IoaSizeComboBox.SelectedIndex < 0) IoaSizeComboBox.SelectedIndex = 2;
        }
        MappingProfileTitleText.Text = is103 ? "IEC-103 FUN/INF MAPPING PROFILE" : "IEC-101/104 IOA POINT PROFILE";
        if (!is103)
        {
            if (_ioaProfile.HasPoints && string.IsNullOrWhiteSpace(MappingProfilePathBox.Text))
            {
                var candidate = File.Exists(BundledUtilityFatProfilePath) ? BundledUtilityFatProfilePath : Path.GetFullPath(SourceTreeUtilityFatProfilePath);
                if (File.Exists(candidate)) MappingProfilePathBox.Text = candidate;
            }
            if (_ioaProfile.HasPoints && !_defaultIoaSeedSettingsApplied)
            {
                ApplyIoaProfileDefaultsToUi(_ioaProfile, onlyWhenUiLooksDefault: !_savedSetupPreferencesLoaded);
            }
            var scenarioText = _ioaProfile.TestScenarios.Count > 0 ? $", {_ioaProfile.TestScenarios.Count} example test scenarios" : string.Empty;
            MappingProfileStatusText.Text = _ioaProfile.HasPoints
                ? $"Loaded: {_ioaProfile.ProfileName} ({_ioaProfile.Points.Count} IOA points{scenarioText}). User-editable JSON; copy the example profile and adapt it for the project."
                : "No IOA profile loaded. Raw IOA, Type ID, COT and CA will be shown.";
        }
        else if (string.IsNullOrWhiteSpace(MappingProfilePathBox.Text))
        {
            MappingProfileStatusText.Text = "No mapping profile loaded. Raw FUN/INF will be shown.";
        }

        SerialConnectionPanel.Visibility = serialVisibility;
        TcpConnectionPanel.Visibility = tcpVisibility;
        SerialPollingPanel.Visibility = is104 ? Visibility.Collapsed : Visibility.Visible;
        Iec10xProfilePanel.Visibility = is103 ? Visibility.Collapsed : Visibility.Visible;
        LinkAddressSizePanel.Visibility = is101 ? Visibility.Visible : Visibility.Collapsed;
        TransmissionModeComboBox.IsEnabled = is101;
        Iec104RuntimePanel.Visibility = tcpVisibility;
        Iec103OptionsPanel.Visibility = is104 ? Visibility.Collapsed : Visibility.Visible;
        LinkAddressPanel.Visibility = is104 ? Visibility.Collapsed : Visibility.Visible;
        MappingProfilePanel.Visibility = Visibility.Visible;

        // Evidence Summary is a distilled human-readable proof view. Keep protocol-heavy columns in Protocol Trace and the selected-row inspector.
        SetColumnVisibility(EvidenceClassColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceApciColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceTypeColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceCotColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceCaColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceIoaColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceFunInfColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceQualityColumn, Visibility.Collapsed);
        EvidenceSignalColumn.Header = is103 ? "Signal" : "Signal";

        // Protocol Trace is now a lightweight line monitor, not a protocol column grid.
        // Protocol-specific fields are rendered inside the line text and decoded in the interpreter.

        // Value/Event main grids also keep one compact Address column; raw CA/IOA/FUN/INF/TypeID columns stay in Protocol Trace.
        SetColumnVisibility(ValueCaColumn, Visibility.Collapsed);
        SetColumnVisibility(ValueIoaColumn, Visibility.Collapsed);
        SetColumnVisibility(ValueFunInfColumn, Visibility.Collapsed);
        SetColumnVisibility(ValueTypeIdColumn, Visibility.Collapsed);
        SetColumnVisibility(ValueQualityColumn, Visibility.Collapsed);

        SetColumnVisibility(EventCaColumn, Visibility.Collapsed);
        SetColumnVisibility(EventIoaColumn, Visibility.Collapsed);
        SetColumnVisibility(EventFunInfColumn, Visibility.Collapsed);
        SetColumnVisibility(EventTypeIdColumn, Visibility.Collapsed);
        SetColumnVisibility(EventQualityColumn, Visibility.Collapsed);

        RawFrameGroupingHintText.Text = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "grouped by IEC-101 FT1.2 + ASDU fields",
            Iec60870ProtocolMode.Iec104 => "grouped by IEC-104 APCI/APDU fields",
            _ => "grouped by IEC-103 FT1.2 + FUN/INF fields"
        };

        SessionSubtitleText.Text = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "IEC-101 selected: serial FT1.2, ACD/DFC, Class 1/Class 2, Type ID/COT/CA/IOA views.",
            Iec60870ProtocolMode.Iec104 => "IEC-104 selected: TCP/IP, APCI I/S/U trace, sequence numbers, Type ID/COT/CA/IOA views.",
            _ => "IEC-103 selected: serial protection relay, ACD/DFC, Class 1/Class 2, FUN/INF views."
        };
    }

    private static void SetColumnVisibility(DataGridColumn column, Visibility visibility)
    {
        column.Visibility = visibility;
    }

    private void ApplyProtocolLogo(Iec60870ProtocolMode mode)
    {
        var iconFile = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "iec101-icon.png",
            Iec60870ProtocolMode.Iec104 => "iec104-icon.png",
            _ => "iec103-icon.png"
        };

        try
        {
            var source = new BitmapImage(new Uri($"pack://application:,,,/Assets/Icons/{iconFile}", UriKind.Absolute));
            ProtocolLogoImage.Source = source;
            Icon = source;
        }
        catch
        {
            // Keep the default app icon if a resource is unavailable in a developer build.
        }
    }

    private Iec60870ProtocolMode GetSelectedProtocolMode()
    {
        var protocol = (ProtocolModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
        if (protocol.Contains("104", StringComparison.OrdinalIgnoreCase)) return Iec60870ProtocolMode.Iec104;
        if (protocol.Contains("101", StringComparison.OrdinalIgnoreCase)) return Iec60870ProtocolMode.Iec101;
        return Iec60870ProtocolMode.Iec103;
    }

    private bool IsDemoModeSelected()
    {
        var mode = (TransportModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
        return mode.Contains("demo", StringComparison.OrdinalIgnoreCase) || mode.Contains("simulated", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadComboInt(ComboBox comboBox, string label)
    {
        var value = (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = comboBox.Text;
        }

        if (!int.TryParse(value?.Trim(), out var number))
        {
            throw new InvalidOperationException(label + " is invalid.");
        }

        return number;
    }

    private static int ReadInt(TextBox textBox, string label, int min, int max)
    {
        if (!int.TryParse(textBox.Text.Trim(), out var number))
        {
            throw new InvalidOperationException(label + " must be a number.");
        }

        if (number < min || number > max)
        {
            throw new InvalidOperationException($"{label} must be between {min} and {max}.");
        }

        return number;
    }
}
