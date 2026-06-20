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
    private void ApplyIoaProfileDefaultsToUi(Iec10xPointMappingProfile profile, bool onlyWhenUiLooksDefault)
    {
        var defaults = profile.DefaultSettings;
        if (defaults is null)
        {
            return;
        }

        var uiLooksUntouched = string.IsNullOrWhiteSpace(CommonAddressBox.Text) || CommonAddressBox.Text.Trim() == "1";
        if (onlyWhenUiLooksDefault && !uiLooksUntouched)
        {
            return;
        }

        if (defaults.BaudRate.HasValue)
        {
            SetEditableComboText(BaudComboBox, defaults.BaudRate.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrWhiteSpace(defaults.SerialMode))
        {
            SelectComboContent(SerialModeComboBox, defaults.SerialMode);
        }
        if (defaults.LinkAddress.HasValue)
        {
            LinkAddressBox.Text = defaults.LinkAddress.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (defaults.CommonAddress.HasValue)
        {
            CommonAddressBox.Text = defaults.CommonAddress.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            CommandCaBox.Text = CommonAddressBox.Text;
        }
        if (defaults.LinkAddressSize.HasValue)
        {
            SelectComboContent(LinkAddressSizeComboBox, Math.Clamp(defaults.LinkAddressSize.Value, 0, 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (defaults.CauseOfTransmissionSize.HasValue)
        {
            SelectComboContent(CotSizeComboBox, Math.Clamp(defaults.CauseOfTransmissionSize.Value, 1, 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (defaults.CommonAddressSize.HasValue)
        {
            SelectComboContent(CaSizeComboBox, Math.Clamp(defaults.CommonAddressSize.Value, 1, 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (defaults.InformationObjectAddressSize.HasValue)
        {
            SelectComboContent(IoaSizeComboBox, Math.Clamp(defaults.InformationObjectAddressSize.Value, 1, 3).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrWhiteSpace(defaults.TransmissionMode))
        {
            SelectComboContent(TransmissionModeComboBox, defaults.TransmissionMode);
        }
        if (!string.IsNullOrWhiteSpace(defaults.TcpHost))
        {
            TcpHostBox.Text = defaults.TcpHost;
        }
        if (defaults.TcpPort.HasValue)
        {
            TcpPortBox.Text = defaults.TcpPort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        _defaultIoaSeedSettingsApplied = true;
        AppendSessionLog($"IOA profile defaults applied: CA={CommonAddressBox.Text}, COT size={CotSizeComboBox.Text}, CA size={CaSizeComboBox.Text}, IOA size={IoaSizeComboBox.Text}, serial={BaudComboBox.Text} {SerialModeComboBox.Text}.");
    }

    private void LoadSetupPreferences()
    {
        try
        {
            var path = SetupPreferencesPath;
            if (!File.Exists(path))
            {
                return;
            }

            var prefs = JsonSerializer.Deserialize<SetupPreferences>(File.ReadAllText(path, Encoding.UTF8));
            if (prefs is null)
            {
                return;
            }

            _savedSetupPreferencesLoaded = true;
            _isApplyingSavedSetup = true;
            SelectProtocolMode(prefs.ProtocolMode);
            SelectComboContent(TransportModeComboBox, prefs.UseSimulatedSlave ? "Built-in demo simulation" : "Real device / server");

            if (!string.IsNullOrWhiteSpace(prefs.PortName))
            {
                EnsureComboItem(PortComboBox, prefs.PortName);
                PortComboBox.SelectedItem = prefs.PortName;
            }

            if (!string.IsNullOrWhiteSpace(prefs.BackupPortName))
            {
                EnsureComboItem(BackupPortComboBox, prefs.BackupPortName);
                BackupPortComboBox.SelectedItem = prefs.BackupPortName;
            }

            SetEditableComboText(BaudComboBox, prefs.BaudRate.ToString());
            SelectComboContent(SerialModeComboBox, string.IsNullOrWhiteSpace(prefs.SerialMode) ? "8E1" : prefs.SerialMode);
            TcpHostBox.Text = string.IsNullOrWhiteSpace(prefs.TcpHost) ? "127.0.0.1" : prefs.TcpHost;
            TcpPortBox.Text = prefs.TcpPort <= 0 ? "2404" : prefs.TcpPort.ToString();
            LinkAddressBox.Text = prefs.LinkAddress.ToString();
            BackupLinkAddressBox.Text = prefs.BackupLinkAddress.ToString();
            CommonAddressBox.Text = prefs.CommonAddress.ToString();
            CommandCaBox.Text = prefs.CommonAddress.ToString();
            SelectComboContent(LinkAddressSizeComboBox, Math.Clamp(prefs.LinkAddressSize, 0, 2).ToString());
            SelectComboContent(CotSizeComboBox, Math.Clamp(prefs.CauseOfTransmissionSize, 1, 2).ToString());
            SelectComboContent(CaSizeComboBox, Math.Clamp(prefs.CommonAddressSize, 1, 2).ToString());
            SelectComboContent(IoaSizeComboBox, Math.Clamp(prefs.InformationObjectAddressSize, 1, 3).ToString());
            SelectComboContent(TransmissionModeComboBox, prefs.TransmissionMode?.StartsWith("Balanced", StringComparison.OrdinalIgnoreCase) == true ? "Unbalanced" : "Unbalanced");

            Class2IntervalBox.Text = prefs.Class2PollIntervalMs > 0 ? prefs.Class2PollIntervalMs.ToString() : "500";
            MaxDrainBox.Text = prefs.MaxClass1DrainFrames > 0 ? prefs.MaxClass1DrainFrames.ToString() : "64";
            Iec104T0Box.Text = prefs.Iec104T0TimeoutMs > 0 ? prefs.Iec104T0TimeoutMs.ToString() : "30000";
            Iec104T1Box.Text = prefs.Iec104T1AckTimeoutMs > 0 ? prefs.Iec104T1AckTimeoutMs.ToString() : "15000";
            Iec104T2Box.Text = prefs.Iec104T2AckDelayMs > 0 ? prefs.Iec104T2AckDelayMs.ToString() : "10000";
            Iec104T3Box.Text = prefs.Iec104T3TestIntervalMs > 0 ? prefs.Iec104T3TestIntervalMs.ToString() : "20000";
            Iec104KBox.Text = prefs.Iec104KMaxUnacknowledged > 0 ? prefs.Iec104KMaxUnacknowledged.ToString() : "12";
            Iec104WBox.Text = prefs.Iec104WReceiveWindow > 0 ? prefs.Iec104WReceiveWindow.ToString() : "8";
            TimeoutBox.Text = prefs.ResponseTimeoutMs > 0 ? prefs.ResponseTimeoutMs.ToString() : "1500";
            DurationBox.Text = prefs.DurationSeconds >= 0 ? prefs.DurationSeconds.ToString() : "0";
            ResetRemoteLinkCheckBox.IsChecked = prefs.ResetRemoteLinkOnConnect;
            ResetFcbCheckBox.IsChecked = prefs.ResetFcbOnConnect;
            Class2StartupCheckBox.IsChecked = prefs.RequestClass2ImmediatelyAfterStartup;
            ClockSyncCheckBox.IsChecked = prefs.SendClockSyncOnConnect;
            GiCheckBox.IsChecked = prefs.SendGeneralInterrogationOnConnect;
            var savedMappingPath = SanitizeSavedMappingProfilePath(prefs.MappingProfilePath);
            MappingProfilePathBox.Text = savedMappingPath;
            if (!string.IsNullOrWhiteSpace(savedMappingPath) && File.Exists(savedMappingPath))
            {
                TryLoadMappingProfile(savedMappingPath, showMessage: false);
            }
            _commandDockExpanded = prefs.CommandDockExpanded;
            ApplyCommandDockLayout();
        }
        catch (Exception ex)
        {
            AddUiDiagnostic("Warning", "Setup", "IEC60870-SETUP-PREF-LOAD", "Saved setup could not be loaded", ex.Message, "The app will continue with default setup. Re-enter the settings once and they will be saved again.", ex);
        }
        finally
        {
            _isApplyingSavedSetup = false;
        }
    }

    private void SaveSetupPreferencesFromUi(bool silent)
    {
        if (_isApplyingSavedSetup)
        {
            return;
        }

        try
        {
            var settings = BuildSettingsFromUi();
            var duration = ReadInt(DurationBox, "Session timeout", 0, 86400);
            SaveSetupPreferences(settings, duration, silent);
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                MessageBox.Show(this, ex.Message, "Could not save setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void SaveSetupPreferences(Iec103MasterSettings settings, int durationSeconds, bool silent)
    {
        try
        {
            var prefs = new SetupPreferences
            {
                ProtocolMode = settings.ProtocolMode.ToString(),
                UseSimulatedSlave = settings.UseSimulatedSlave,
                PortName = settings.PortName,
                BaudRate = settings.BaudRate,
                SerialMode = (SerialModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? SerialModeComboBox.Text,
                TcpHost = settings.TcpHost,
                TcpPort = settings.TcpPort,
                LinkAddress = settings.LinkAddress,
                BackupPortName = (BackupPortComboBox.SelectedItem as string)?.Trim() ?? BackupPortComboBox.Text.Trim(),
                BackupLinkAddress = int.TryParse(BackupLinkAddressBox.Text, out var backupLinkAddress) ? backupLinkAddress : settings.LinkAddress,
                CommonAddress = settings.CommonAddress,
                LinkAddressSize = settings.LinkAddressSize,
                CauseOfTransmissionSize = settings.CauseOfTransmissionSize,
                CommonAddressSize = settings.CommonAddressSize,
                InformationObjectAddressSize = settings.InformationObjectAddressSize,
                TransmissionMode = settings.TransmissionMode,
                Iec104T0TimeoutMs = settings.Iec104T0TimeoutMs,
                Iec104T1AckTimeoutMs = settings.Iec104T1AckTimeoutMs,
                Iec104T2AckDelayMs = settings.Iec104T2AckDelayMs,
                Iec104T3TestIntervalMs = settings.Iec104T3TestIntervalMs,
                Iec104KMaxUnacknowledged = settings.Iec104KMaxUnacknowledged,
                Iec104WReceiveWindow = settings.Iec104WReceiveWindow,
                ResponseTimeoutMs = settings.ResponseTimeoutMs,
                Class2PollIntervalMs = settings.Class2PollIntervalMs,
                MaxClass1DrainFrames = settings.MaxClass1DrainFrames,
                ResetRemoteLinkOnConnect = settings.ResetRemoteLinkOnConnect,
                ResetFcbOnConnect = settings.ResetFcbOnConnect,
                RequestClass2ImmediatelyAfterStartup = settings.RequestClass2ImmediatelyAfterStartup,
                SendClockSyncOnConnect = settings.SendClockSyncOnConnect,
                SendGeneralInterrogationOnConnect = settings.SendGeneralInterrogationOnConnect,
                MappingProfilePath = SanitizeSavedMappingProfilePath(settings.MappingProfilePath),
                CommandDockExpanded = _commandDockExpanded,
                DurationSeconds = durationSeconds,
                SavedUtc = DateTime.UtcNow
            };

            var path = SetupPreferencesPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            if (!silent)
            {
                AppendSessionLog("Setup preferences saved for next launch.");
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                MessageBox.Show(this, ex.Message, "Could not save setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void SelectProtocolMode(string? protocolMode)
    {
        var needle = protocolMode?.Contains("104", StringComparison.OrdinalIgnoreCase) == true ? "104"
            : protocolMode?.Contains("101", StringComparison.OrdinalIgnoreCase) == true ? "101"
            : "103";
        for (var i = 0; i < ProtocolModeComboBox.Items.Count; i++)
        {
            if ((ProtocolModeComboBox.Items[i] as ComboBoxItem)?.Content?.ToString()?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true)
            {
                ProtocolModeComboBox.SelectedIndex = i;
                return;
            }
        }
    }

    private static void EnsureComboItem(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items)
        {
            if (string.Equals((item as ComboBoxItem)?.Content?.ToString() ?? item?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        comboBox.Items.Add(value);
    }

    private static void SelectComboContent(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items)
        {
            var text = (item as ComboBoxItem)?.Content?.ToString() ?? item?.ToString();
            if (string.Equals(text, value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        if (comboBox.IsEditable)
        {
            comboBox.Text = value;
        }
    }

    private static void SetEditableComboText(ComboBox comboBox, string value)
    {
        SelectComboContent(comboBox, value);
        if (comboBox.IsEditable)
        {
            comboBox.Text = value;
        }
    }


    private static readonly string[] SensitiveMappingProfileTokens =
    {
        new(new[] { 'P', 'L', 'N' }),
        new(new[] { 'P', 'u', 's', 'e', 'r', 't', 'i', 'f' })
    };

    private static string SanitizeSavedMappingProfilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(path);
        foreach (var token in SensitiveMappingProfileTokens)
        {
            if (fileName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
        }

        return path;
    }

    private sealed class SetupPreferences
    {
        public string ProtocolMode { get; set; } = nameof(Iec60870ProtocolMode.Iec103);
        public bool UseSimulatedSlave { get; set; }
        public string PortName { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public string SerialMode { get; set; } = "8E1";
        public string TcpHost { get; set; } = "127.0.0.1";
        public int TcpPort { get; set; } = 2404;
        public int LinkAddress { get; set; } = 1;
        public string BackupPortName { get; set; } = string.Empty;
        public int BackupLinkAddress { get; set; } = 1;
        public int CommonAddress { get; set; } = 1;
        public int LinkAddressSize { get; set; } = 1;
        public int CauseOfTransmissionSize { get; set; } = 2;
        public int CommonAddressSize { get; set; } = 2;
        public int InformationObjectAddressSize { get; set; } = 3;
        public string TransmissionMode { get; set; } = "Unbalanced";
        public int Iec104T0TimeoutMs { get; set; } = 30000;
        public int Iec104T1AckTimeoutMs { get; set; } = 15000;
        public int Iec104T2AckDelayMs { get; set; } = 10000;
        public int Iec104T3TestIntervalMs { get; set; } = 20000;
        public int Iec104KMaxUnacknowledged { get; set; } = 12;
        public int Iec104WReceiveWindow { get; set; } = 8;
        public int ResponseTimeoutMs { get; set; } = 1500;
        public int Class2PollIntervalMs { get; set; } = 500;
        public int MaxClass1DrainFrames { get; set; } = 64;
        public bool ResetRemoteLinkOnConnect { get; set; }
        public bool ResetFcbOnConnect { get; set; } = false;
        public bool RequestClass2ImmediatelyAfterStartup { get; set; } = true;
        public bool SendClockSyncOnConnect { get; set; }
        public bool SendGeneralInterrogationOnConnect { get; set; } = true;
        public string MappingProfilePath { get; set; } = string.Empty;
        public bool CommandDockExpanded { get; set; } = true;
        public int DurationSeconds { get; set; }
        public DateTime SavedUtc { get; set; }
    }

}
