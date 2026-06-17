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
    private void ApplyCommandDockLayout()
    {
        if (CommandDockPanel is null || CommandDockColumn is null)
        {
            return;
        }

        CommandDockColumn.Width = _commandDockExpanded ? new GridLength(320) : new GridLength(42);
        CommandDockPanel.Visibility = _commandDockExpanded ? Visibility.Visible : Visibility.Collapsed;
        CommandDockMiniButton.Visibility = _commandDockExpanded ? Visibility.Collapsed : Visibility.Visible;

        if (CommandDockToggleIcon is not null)
        {
            CommandDockToggleIcon.Data = (Geometry)FindResource(_commandDockExpanded ? "LucideCircleChevronRight" : "LucideCircleChevronLeft");
            CommandDockToggleIcon.Stroke = (Brush)FindResource("Ink500Brush");
        }

        if (CommandDockMiniIcon is not null)
        {
            CommandDockMiniIcon.Stroke = (Brush)FindResource("Ink500Brush");
        }
    }

    private void ToggleCommandDock_Click(object sender, RoutedEventArgs e)
        => ToggleCommandDockPanel();

    private void CommandDockHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ToggleCommandDockPanel();
        e.Handled = true;
    }

    private void ToggleCommandDockPanel()
    {
        _commandDockExpanded = !_commandDockExpanded;
        ApplyCommandDockLayout();
        SaveSetupPreferencesFromUi(silent: true);
    }

    private void CommandDock_Gi_Click(object sender, RoutedEventArgs e)
    {
        SeedValueViewerFromIoaProfile(GetSelectedProtocolMode());
        IssuePriorityRuntimeCommand(new Iec60870ControlCommandRequest { Kind = Iec60870ControlCommandKind.GeneralInterrogation, OperatorNote = "Command dock GI" });
    }

    private void CommandDock_ClockSync_Click(object sender, RoutedEventArgs e) => IssuePriorityRuntimeCommand(new Iec60870ControlCommandRequest { Kind = Iec60870ControlCommandKind.ClockSync, OperatorNote = "Command dock clock sync" });

    private void CommandDock_Read_Click(object sender, RoutedEventArgs e)
    {
        var request = new Iec60870ControlCommandRequest
        {
            Kind = Iec60870ControlCommandKind.Read,
            CommonAddress = ReadInt(CommandCaBox, "Command CA", 0, 0xFFFF),
            InformationObjectAddress = ReadInt(CommandIoaBox, "Command IOA", 0, 0xFFFFFF),
            OperatorNote = "Command dock read"
        };

        if (ValidateCommandTargetBeforeIssue(request, isCommandAction: false))
        {
            IssuePriorityRuntimeCommand(request);
        }
    }

    private void CommandTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCommandDockActionButtons();
        AutoFillCommandTargetFromProfile(preferCurrentSelection: true);
        UpdateCommandPreview(CommandSignalComboBox?.SelectedItem as CommandSignalOption);
    }

    private void UpdateCommandDockActionButtons()
    {
        if (CommandSelectOpenButton is null || CommandOperateOpenButton is null)
        {
            return;
        }

        var kind = ResolveCommandKindFromCombo();
        var isSetpoint = kind == Iec60870ControlCommandKind.SetpointNormalizedCommand;
        if (CommandSetpointPanel is not null)
        {
            CommandSetpointPanel.Visibility = isSetpoint ? Visibility.Visible : Visibility.Collapsed;
        }
        if (CommandSetpointLabel is not null)
        {
            CommandSetpointLabel.Text = "Setpoint value";
        }
        if (CommandQualifierHelpText is not null)
        {
            CommandQualifierHelpText.Text = string.Empty;
        }
        CommandSelectCloseButton.Visibility = isSetpoint ? Visibility.Collapsed : Visibility.Visible;
        CommandOperateCloseButton.Visibility = isSetpoint ? Visibility.Collapsed : Visibility.Visible;

        if (kind == Iec60870ControlCommandKind.RegulatingStepCommand)
        {
            CommandSelectOpenButton.Content = "Select Lower";
            CommandOperateOpenButton.Content = "Operate Lower";
            CommandSelectCloseButton.Content = "Select Raise";
            CommandOperateCloseButton.Content = "Operate Raise";
            return;
        }

        if (isSetpoint)
        {
            CommandSelectOpenButton.Content = "Select Setpoint";
            CommandOperateOpenButton.Content = "Operate Setpoint";
            return;
        }

        CommandSelectOpenButton.Content = "Select Open";
        CommandOperateOpenButton.Content = "Operate Open";
        CommandSelectCloseButton.Content = "Select Close";
        CommandOperateCloseButton.Content = "Operate Close";
    }


    private void RefreshCommandSignalOptions()
    {
        if (CommandSignalOptions is null)
        {
            return;
        }

        var previousIoa = CommandSignalComboBox?.SelectedItem is CommandSignalOption selected
            ? selected.InformationObjectAddress
            : (int?)null;

        CommandSignalOptions.Clear();
        foreach (var point in _ioaProfile.Points
                     .Where(IsCommandPoint)
                     .OrderBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.Ioa))
        {
            var typeName = point.TypeId switch
            {
                45 => "Single C_SC_NA_1",
                46 => "Double C_DC_NA_1",
                47 => "Regulating C_RC_NA_1",
                48 => "Setpoint C_SE_NA_1",
                49 => "Setpoint scaled C_SE_NB_1",
                50 => "Setpoint float C_SE_NC_1",
                51 => "Bitstring C_BO_NA_1",
                _ => string.IsNullOrWhiteSpace(point.CommandPolicy) ? "Command" : point.CommandPolicy
            };

            var ca = point.Ca?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*";
            var feedbackPoint = point.FeedbackIoa.HasValue
                ? _ioaProfile.Points.FirstOrDefault(x => x.Ioa == point.FeedbackIoa.Value)
                : null;
            var fb = point.FeedbackIoa.HasValue ? $" · FB IOA {point.FeedbackIoa.Value}" : string.Empty;
            var range = point.EngineeringMin.HasValue || point.EngineeringMax.HasValue
                ? $" · range {point.EngineeringMin?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "-"}..{point.EngineeringMax?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "-"} {point.Unit}".TrimEnd()
                : string.Empty;

            CommandSignalOptions.Add(new CommandSignalOption
            {
                Name = string.IsNullOrWhiteSpace(point.Name) ? $"IOA {point.Ioa}" : point.Name,
                Detail = $"{typeName} · CA {ca} · IOA {point.Ioa}{fb}{range}",
                SearchText = $"{point.Name} {point.Group} IOA {point.Ioa} CA {ca} {typeName} {point.CommandPolicy} {point.Mnemonic}",
                CommonAddress = point.Ca,
                InformationObjectAddress = point.Ioa,
                TypeId = point.TypeId,
                FeedbackIoa = point.FeedbackIoa,
                FeedbackName = feedbackPoint?.Name ?? string.Empty,
                CommandPolicy = point.CommandPolicy,
                EngineeringMin = point.EngineeringMin,
                EngineeringMax = point.EngineeringMax,
                Unit = point.Unit
            });
        }

        if (CommandSignalComboBox is not null)
        {
            CommandSignalComboBox.SelectedItem = previousIoa.HasValue
                ? CommandSignalOptions.FirstOrDefault(x => x.InformationObjectAddress == previousIoa.Value)
                : null;
        }
    }

    private void CommandSignalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSavedSetup || CommandSignalComboBox?.SelectedItem is not CommandSignalOption option)
        {
            return;
        }

        ApplyCommandSignalOption(option);
    }

    private void ApplyCommandSignalOption(CommandSignalOption option)
    {
        if (CommandCaBox is null || CommandIoaBox is null)
        {
            return;
        }

        if (option.CommonAddress.HasValue)
        {
            CommandCaBox.Text = option.CommonAddress.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (!int.TryParse(CommandCaBox.Text, out _))
        {
            CommandCaBox.Text = (_ioaProfile.CommonAddress ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        CommandIoaBox.Text = option.InformationObjectAddress.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SelectCommandTypeByTypeId(option.TypeId);

        if (option.TypeId is 48 or 49 or 50 && option.EngineeringMin.HasValue && option.EngineeringMax.HasValue)
        {
            var mid = (option.EngineeringMin.Value + option.EngineeringMax.Value) / 2.0;
            CommandSetpointBox.Text = mid.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        UpdateCommandPreview(option);
        AppendSessionLog($"Command target selected: {option.Name} ({option.Detail}).");
    }

    private void SelectCommandTypeByTypeId(int? typeId)
    {
        if (CommandTypeComboBox is null || !typeId.HasValue)
        {
            return;
        }

        var targetIndex = typeId.Value switch
        {
            45 => 0,
            46 => 1,
            47 => 2,
            48 or 49 or 50 => 3,
            _ => -1
        };

        if (targetIndex >= 0 && CommandTypeComboBox.SelectedIndex != targetIndex)
        {
            CommandTypeComboBox.SelectedIndex = targetIndex;
        }

        UpdateCommandDockActionButtons();
    }


    private void UpdateCommandPreview(CommandSignalOption? option = null)
    {
        if (CommandPreviewTitleText is null)
        {
            return;
        }

        if (option is null)
        {
            var caText = CommandCaBox?.Text ?? "-";
            var ioaText = CommandIoaBox?.Text ?? "-";
            var kind = ResolveCommandKindFromCombo();
            CommandPreviewTitleText.Text = "Manual command target";
            CommandPreviewAddressText.Text = $"{kind} · CA {caText} · IOA {ioaText}";
            CommandPreviewFeedbackText.Text = "Feedback IOA: not mapped from database";
            CommandPreviewSafetyText.Text = "Manual target is allowed, but the validator cannot prove feedback unless the Signal List maps command feedback.";
            return;
        }

        var kindText = option.TypeId switch
        {
            45 => "Single command",
            46 => "Double command",
            47 => "Regulating step",
            48 => "Setpoint normalized",
            49 => "Setpoint scaled",
            50 => "Setpoint float",
            51 => "Bitstring command",
            _ => "Command"
        };

        var ca = option.CommonAddress?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? (CommandCaBox?.Text ?? "*");
        CommandPreviewTitleText.Text = option.Name;
        CommandPreviewAddressText.Text = $"{kindText} · CA {ca} · IOA {option.InformationObjectAddress} · policy {option.CommandPolicy}";
        CommandPreviewFeedbackText.Text = option.FeedbackIoa.HasValue
            ? $"Feedback IOA {option.FeedbackIoa.Value}: {(string.IsNullOrWhiteSpace(option.FeedbackName) ? "mapped process point" : option.FeedbackName)}"
            : "Feedback IOA: not mapped";
        CommandPreviewSafetyText.Text = option.FeedbackIoa.HasValue
            ? "Validator will look for ACTCON, ACTTERM and mapped feedback value."
            : "Validator can check ACTCON/ACTTERM, but feedback proof needs FeedbackIoa in Signal List.";
    }

    private bool ValidateCommandTargetBeforeIssue(Iec60870ControlCommandRequest request, bool isCommandAction)
    {
        if (request.Kind is Iec60870ControlCommandKind.GeneralInterrogation or Iec60870ControlCommandKind.ClockSync or Iec60870ControlCommandKind.Read)
        {
            return true;
        }

        var selected = CommandSignalComboBox?.SelectedItem as CommandSignalOption;
        if (selected is null)
        {
            AddUiDiagnostic(
                "Info",
                "Command",
                "IEC10X-COMMAND-MANUAL-TARGET",
                "Manual command target is being used",
                $"Command will be sent to CA={request.CommonAddress}, IOA={request.InformationObjectAddress}. No command signal was selected from the database.",
                "Manual IOA is allowed, but selecting a command signal gives feedback mapping and stronger command verdicts.");
            return true;
        }

        if (selected.InformationObjectAddress != request.InformationObjectAddress)
        {
            AddUiDiagnostic(
                "Warning",
                "Command",
                "IEC10X-COMMAND-TARGET-MISMATCH",
                "Selected command signal and IOA field do not match",
                $"Selected '{selected.Name}' is IOA {selected.InformationObjectAddress}, but IOA box contains {request.InformationObjectAddress}.",
                "Either re-select the command signal or clear the dropdown if you intentionally want manual IOA.");
            CommandDockStatusText.Text = "Command blocked: selected signal and IOA field mismatch.";
            return false;
        }

        if (selected.CommonAddress.HasValue && request.CommonAddress.HasValue && selected.CommonAddress.Value != request.CommonAddress.Value)
        {
            AddUiDiagnostic(
                "Warning",
                "Command",
                "IEC10X-COMMAND-CA-MISMATCH",
                "Selected command signal and CA field do not match",
                $"Selected '{selected.Name}' uses CA {selected.CommonAddress.Value}, but CA box contains {request.CommonAddress.Value}.",
                "Use the database CA or intentionally clear the selection for manual target testing.");
            CommandDockStatusText.Text = "Command blocked: selected signal and CA field mismatch.";
            return false;
        }

        if (isCommandAction && !selected.FeedbackIoa.HasValue)
        {
            AddUiDiagnostic(
                "Info",
                "Command",
                "IEC10X-COMMAND-NO-FEEDBACK-MAP",
                "Selected command has no feedback IOA mapping",
                $"'{selected.Name}' can be commanded, but the Signal List does not define FeedbackIoa.",
                "Command validator will still check ACTCON/ACTTERM, but cannot prove physical feedback until FeedbackIoa is mapped.");
        }

        return true;
    }

    private void AutoFillCommandTargetFromProfile(bool preferCurrentSelection = false)
    {
        if (_isApplyingSavedSetup || _ioaProfile.Points.Count == 0 || CommandIoaBox is null)
        {
            return;
        }

        if (preferCurrentSelection && CommandSignalComboBox?.SelectedItem is CommandSignalOption selected)
        {
            ApplyCommandSignalOption(selected);
            return;
        }

        // Do not keep overwriting manual IOA entry. Auto-fill only when the box is empty
        // or still at the old starter value.
        var hasManualIoa = int.TryParse(CommandIoaBox.Text, out var currentIoa) && currentIoa > 0 && currentIoa != 101;
        if (hasManualIoa)
        {
            return;
        }

        var kind = ResolveCommandKindFromCombo();
        var typeId = kind switch
        {
            Iec60870ControlCommandKind.SingleCommand => 45,
            Iec60870ControlCommandKind.DoubleCommand => 46,
            Iec60870ControlCommandKind.RegulatingStepCommand => 47,
            Iec60870ControlCommandKind.SetpointNormalizedCommand => 48,
            _ => 0
        };

        if (typeId == 0)
        {
            return;
        }

        var option = CommandSignalOptions.FirstOrDefault(x => x.TypeId == typeId)
            ?? CommandSignalOptions.FirstOrDefault();

        if (option is null)
        {
            return;
        }

        if (CommandSignalComboBox is not null)
        {
            CommandSignalComboBox.SelectedItem = option;
        }

        ApplyCommandSignalOption(option);
    }

    private void CommandDock_Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var tag = button.Tag?.ToString() ?? string.Empty;
        var select = tag.StartsWith("select", StringComparison.OrdinalIgnoreCase);
        var leftAction = tag.EndsWith("left", StringComparison.OrdinalIgnoreCase);
        var kind = ResolveCommandKindFromCombo();
        var value = BuildCommandValue(kind, leftAction);

        var request = new Iec60870ControlCommandRequest
        {
            Kind = kind,
            CommonAddress = ReadInt(CommandCaBox, "Command CA", 0, 0xFFFF),
            InformationObjectAddress = ReadInt(CommandIoaBox, "Command IOA", 0, 0xFFFFFF),
            Value = value,
            NumericValue = ParseLeadingDouble(CommandSetpointBox.Text, 0),
            Qualifier = ReadInt(CommandQualifierBox, "Command qualifier", 0, 31),
            SelectBeforeOperate = select,
            OperatorNote = select ? "Command dock SELECT" : "Command dock OPERATE"
        };

        if (ValidateCommandTargetBeforeIssue(request, isCommandAction: true))
        {
            IssuePriorityRuntimeCommand(request);
        }
    }

    private Iec60870ControlCommandKind ResolveCommandKindFromCombo()
    {
        var typeText = (CommandTypeComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Double";
        if (typeText.Contains("Setpoint", StringComparison.OrdinalIgnoreCase)) return Iec60870ControlCommandKind.SetpointNormalizedCommand;
        if (typeText.Contains("Regulating", StringComparison.OrdinalIgnoreCase)) return Iec60870ControlCommandKind.RegulatingStepCommand;
        if (typeText.Contains("Double", StringComparison.OrdinalIgnoreCase)) return Iec60870ControlCommandKind.DoubleCommand;
        return Iec60870ControlCommandKind.SingleCommand;
    }

    private static int BuildCommandValue(Iec60870ControlCommandKind kind, bool leftAction)
    {
        return kind switch
        {
            Iec60870ControlCommandKind.SingleCommand => leftAction ? 0 : 1,       // OFF/Open, ON/Close
            Iec60870ControlCommandKind.DoubleCommand => leftAction ? 1 : 2,       // DCS=1 Open/Off, DCS=2 Close/On
            Iec60870ControlCommandKind.RegulatingStepCommand => leftAction ? 1 : 2, // RCS=1 Lower, RCS=2 Raise
            _ => 0
        };
    }

    private static double ParseLeadingDouble(string text, double fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var token = text.Trim().Split(' ', '/', '\t', '\r', '\n').FirstOrDefault() ?? string.Empty;
        return double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private void IssuePriorityRuntimeCommand(Iec60870ControlCommandRequest request)
    {
        if (_activeControlSession is null || !_activeControlSession.SupportsRuntimeControlCommands)
        {
            CommandDockStatusText.Text = "No active IEC-101/104 runtime session. Connect first before issuing a command.";
            AppendSessionLog("Command dock refused: no active runtime control session.");
            return;
        }

        if (GetSelectedProtocolMode() == Iec60870ProtocolMode.Iec103)
        {
            CommandDockStatusText.Text = "IEC-103 control command dock is not enabled. Use IEC-101/104 command ASDUs only in this build.";
            AppendSessionLog("Command dock refused: IEC-103 command workflow is not enabled in this build.");
            return;
        }

        _activeControlSession.QueueControlCommand(request);
        var selectedName = CommandSignalComboBox?.SelectedItem is CommandSignalOption option ? $" · {option.Name}" : string.Empty;
        CommandDockStatusText.Text = "Issued priority command: " + request.Summary + selectedName;
        AppendSessionLog("Command dock issued: " + request.Summary + selectedName);
    }
}
