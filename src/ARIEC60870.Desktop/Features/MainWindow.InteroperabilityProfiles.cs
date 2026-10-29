// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ARIEC60870.Master.Model;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private Iec10xInteroperabilityProfile? _projectInteroperabilityProfile;
    private string _evidenceProtocolMode = string.Empty;
    private string _evidenceProfileId = string.Empty;
    private string _evidenceProfileEdition = string.Empty;
    private string _evidenceProfileFingerprint = string.Empty;
    private string _evidenceProfileSource = "Not recorded";

    private Iec10xInteroperabilityProfile ResolveInteroperabilityProfile(Iec103MasterSettings settings)
    {
        var selection = InteroperabilityConventionComboBox?.SelectedIndex ?? 0;
        if (selection == 2)
        {
            var project = _projectInteroperabilityProfile
                ?? throw new InvalidOperationException("Import a project profile before connecting.");
            if (project.Protocol != settings.ProtocolMode)
                throw new InvalidOperationException(
                    $"Project profile is {project.Protocol}; current Setup uses {settings.ProtocolMode}.");
            return project;
        }
        if (selection == 1 && settings.ProtocolMode == Iec60870ProtocolMode.Iec103)
            throw new InvalidOperationException("Historical NUC reference is defined for IEC-101/104 only.");
        var template = selection == 1
            ? settings.ProtocolMode == Iec60870ProtocolMode.Iec101
                ? Iec10xInteroperabilityProfile.HistoricalNuc101()
                : Iec10xInteroperabilityProfile.HistoricalNuc104()
            : Iec10xInteroperabilityProfile.Generic(settings.ProtocolMode);
        return template with
        {
            LinkWidth = settings.ProtocolMode == Iec60870ProtocolMode.Iec104 ? 0 : settings.LinkAddressSize,
            CotWidth = settings.CauseOfTransmissionSize,
            CaWidth = settings.CommonAddressSize,
            IoaWidth = settings.InformationObjectAddressSize,
            LinkMode = settings.ProtocolMode == Iec60870ProtocolMode.Iec104 ? "Tcp" : settings.TransmissionMode
        };
    }

    private void InteroperabilityConventionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateInteroperabilityProfilePreview();

    private void InteroperabilityWidth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateInteroperabilityProfilePreview();

    private void UpdateInteroperabilityProfilePreview()
    {
        if (InteroperabilityProfilePreviewText is null || InteroperabilityConventionComboBox is null ||
            LinkAddressSizeComboBox is null || CotSizeComboBox is null || CaSizeComboBox is null ||
            IoaSizeComboBox is null)
            return;
        var selection = InteroperabilityConventionComboBox.SelectedIndex;
        if (selection == 2 && _projectInteroperabilityProfile is null)
        {
            InteroperabilityProfilePreviewText.Text = "No project profile imported · Connect blocked.";
            return;
        }
        var protocol = GetSelectedProtocolMode();
        var current = Iec10xInteroperabilityProfile.Generic(protocol);
        if (protocol == Iec60870ProtocolMode.Iec101)
            current = current with { LinkWidth = WidthValue(LinkAddressSizeComboBox, 1),
                CotWidth = WidthValue(CotSizeComboBox, 2), CaWidth = WidthValue(CaSizeComboBox, 2),
                IoaWidth = WidthValue(IoaSizeComboBox, 3) };
        else if (protocol == Iec60870ProtocolMode.Iec104)
            current = current with { CotWidth = WidthValue(CotSizeComboBox, 2),
                CaWidth = WidthValue(CaSizeComboBox, 2), IoaWidth = WidthValue(IoaSizeComboBox, 3) };
        var proposed = selection == 2 ? _projectInteroperabilityProfile! :
            selection == 1 && protocol == Iec60870ProtocolMode.Iec101
                ? Iec10xInteroperabilityProfile.HistoricalNuc101() with
                { LinkWidth = current.LinkWidth, CotWidth = current.CotWidth,
                  CaWidth = current.CaWidth, IoaWidth = current.IoaWidth }
                : selection == 1 ? Iec10xInteroperabilityProfile.HistoricalNuc104() : current;
        var diff = proposed.DifferencesFrom(current);
        InteroperabilityProfilePreviewText.Text = proposed.Id + " @ " + proposed.Edition +
            (diff.Count == 0 ? " · fields match Setup" : " · Setup → profile: " + string.Join("; ", diff)) +
            (proposed.Protocol != protocol ? " · PROTOCOL MISMATCH — TX BLOCKED" : "") +
            (selection == 2 ? " · manual reconciliation required; nothing auto-applied" : "");
    }

    private static int WidthValue(ComboBox control, int fallback)
    {
        var text = (control.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? control.Text;
        return int.TryParse(text, out var width) ? width : fallback;
    }

    private void ImportInteroperabilityProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionCancellation is not null || _passiveMonitorSession is not null)
        {
            ModernMessageBox.Show(this, "Stop monitoring before changing the profile.",
                "Import profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dialog = new OpenFileDialog { Title = "Import project interoperability profile",
            Filter = "IEC-10x profile JSON (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 65536)
                throw new InvalidDataException("Profile JSON exceeds 64 KiB.");
            var profile = Iec10xInteroperabilityProfile.ImportProject(
                File.ReadAllText(dialog.FileName, Encoding.UTF8));
            _projectInteroperabilityProfile = profile;
            InteroperabilityConventionComboBox.SelectedIndex = 2;
            UpdateInteroperabilityProfilePreview();
            AppendSessionLog($"Project profile imported: {profile.Id}@{profile.Edition}; Setup widths unchanged.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ModernMessageBox.Show(this, ex.Message, "Profile import rejected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportInteroperabilityProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Iec10xInteroperabilityProfile profile;
            if (InteroperabilityConventionComboBox.SelectedIndex == 2)
                profile = _projectInteroperabilityProfile
                    ?? throw new InvalidOperationException("Import a project profile before exporting it.");
            else
                profile = ResolveInteroperabilityProfile(BuildSettingsFromUi());
            profile.ValidateDefinition();
            var dialog = new SaveFileDialog { Title = "Export interoperability profile",
                Filter = "IEC-10x profile JSON (*.json)|*.json", AddExtension = true,
                FileName = "iec10x-profile.json" };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName, profile.Export(), new UTF8Encoding(false));
            AppendSessionLog($"Profile exported: {profile.Id}@{profile.Edition}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            ModernMessageBox.Show(this, ex.Message, "Cannot export profile",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
