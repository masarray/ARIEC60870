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
    private void ObserveIecProtocolTriggerWatch(Iec103MasterEvidenceEvent item, EvidenceRow row)
    {
        CompleteActiveProtocolTriggerWindows(row);

        if (IsSmartCaptureRuleEnabled())
        {
            var trigger = DetectUserDefinedIecCaptureRuleMatch(item, row);
            if (trigger is not null && ShouldStartProtocolTrigger(trigger, row))
            {
                StartProtocolTriggerCapture(trigger, row);
            }
        }

        AddTriggerPreCaptureRow(row);
    }

    private void CompleteActiveProtocolTriggerWindows(EvidenceRow row)
    {
        if (_activeProtocolTriggerCaptures.Count == 0)
        {
            return;
        }

        for (var i = _activeProtocolTriggerCaptures.Count - 1; i >= 0; i--)
        {
            var capture = _activeProtocolTriggerCaptures[i];
            capture.Rows.Add(row);
            capture.PostRowsRemaining--;

            if (capture.PostRowsRemaining <= 0)
            {
                FinishProtocolTriggerCapture(capture);
                _activeProtocolTriggerCaptures.RemoveAt(i);
            }
        }
    }

    private void AddTriggerPreCaptureRow(EvidenceRow row)
    {
        _triggerPreCaptureBuffer.Enqueue(row);
        while (_triggerPreCaptureBuffer.Count > MaxTriggerPreBufferRows)
        {
            _triggerPreCaptureBuffer.Dequeue();
        }
    }

    private bool ShouldStartProtocolTrigger(ProtocolTriggerCandidate trigger, EvidenceRow row)
    {
        var key = $"{trigger.Code}|{row.ProtocolMode}|CA={row.CommonAddress}|IOA={row.IoAddress}|TYPE={row.TypeId}|COT={row.CotCode}";
        var now = DateTime.UtcNow;

        if (_lastProtocolTriggerUtcByKey.TryGetValue(key, out var lastUtc) && (now - lastUtc).TotalSeconds < 5)
        {
            return false;
        }

        _lastProtocolTriggerUtcByKey[key] = now;
        return true;
    }

    private void StartProtocolTriggerCapture(ProtocolTriggerCandidate trigger, EvidenceRow triggerRow)
    {
        var maxCaptures = ReadSmartCaptureInt(SmartCaptureMaxCapturesBox, 20, 1, 500);
        if (_protocolTriggerCompletedCount >= maxCaptures)
        {
            SmartCaptureRuleStatusText.Text = $"Capture rule reached max captures ({maxCaptures}). Disable or increase Max cap to continue.";
            return;
        }

        if (_activeProtocolTriggerCaptures.Count >= MaxConcurrentTriggerCaptures)
        {
            AddUiDiagnostic(
                "Warning",
                "CaptureRule",
                "ARIEC-RULE-CAPTURE-SKIPPED",
                "IEC capture rule skipped because too many capture windows are already open",
                $"{trigger.Code}: {trigger.Title}",
                "Reduce rule rate, wait for active windows to complete, or narrow the rule condition.");
            return;
        }

        var preRows = ReadSmartCaptureInt(SmartCapturePreRowsBox, TriggerPreCaptureRows, 0, MaxTriggerPreBufferRows);
        var postRows = ReadSmartCaptureInt(SmartCapturePostRowsBox, TriggerPostCaptureRows, 0, 250);

        var rows = _triggerPreCaptureBuffer
            .Reverse()
            .Take(preRows)
            .Reverse()
            .ToList();

        rows.Add(triggerRow);

        var sequence = System.Threading.Interlocked.Increment(ref _protocolTriggerCaptureSequence);
        var capture = new ProtocolTriggerCapture(
            $"RULE-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{sequence:000}",
            trigger,
            triggerRow,
            rows,
            postRows);

        _activeProtocolTriggerCaptures.Add(capture);
        _protocolTriggerStartedCount++;

        SmartCaptureRuleStatusText.Text = $"Recording rule match: {trigger.Code}, row #{triggerRow.Sequence}, pre {Math.Max(0, rows.Count - 1)}, post {postRows}.";

        AddUiDiagnostic(
            trigger.Severity,
            "CaptureRule",
            "ARIEC-RULE-CAPTURE-STARTED",
            trigger.Title,
            $"{trigger.Detail}{Environment.NewLine}Trigger row #{triggerRow.Sequence}. Pre rows: {Math.Max(0, rows.Count - 1)}. Post rows target: {postRows}.",
            "ARIEC is collecting pre/post evidence because the user-enabled capture rule matched.");
    }

    private void FinishProtocolTriggerCapture(ProtocolTriggerCapture capture)
    {
        try
        {
            var folder = GetProtocolTriggerCaptureFolder();
            Directory.CreateDirectory(folder);

            var safeCode = string.Concat(capture.Trigger.Code.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-'));
            var fileName = Path.Combine(folder, $"{capture.CaptureId}-{safeCode}.ariec");

            WriteSelectedEvidenceCapture(fileName, capture.Rows, $"ProtocolTrigger-{capture.Trigger.Code}");
            _protocolTriggerCompletedCount++;
            AddTriggerCaptureDashboardRow(capture, fileName);

            AddUiDiagnostic(
                capture.Trigger.Severity,
                "Trigger",
                "ARIEC-IEC-TRIGGER-CAPTURE-SAVED",
                "IEC trigger pre/post capture saved",
                $"{capture.Trigger.Title}{Environment.NewLine}Rows: {capture.Rows.Count}. File: {fileName}",
                "Open the .ariec file from the trigger evidence folder to review the exact pre/post window around the IEC event.");
            AppendSessionLog($"IEC trigger capture saved: {capture.Trigger.Code}, rows={capture.Rows.Count}, file={fileName}");
        }
        catch (Exception ex)
        {
            AddUiDiagnostic(
                "Error",
                "Trigger",
                "ARIEC-IEC-TRIGGER-CAPTURE-FAILED",
                "IEC trigger capture could not be saved",
                ex.Message,
                "Check local app data write permission and available disk space.",
                ex);
        }
    }


    private void AddTriggerCaptureDashboardRow(ProtocolTriggerCapture capture, string fileName)
    {
        var row = new TriggerCaptureRow(
            capture.CaptureId,
            DateTime.UtcNow.ToLocalTime().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
            capture.Trigger.Severity,
            capture.Trigger.Code,
            capture.Trigger.Title,
            capture.Trigger.Detail,
            capture.Rows.Count,
            capture.TriggerRow.Sequence,
            fileName);

        TriggerCaptureRows.Insert(0, row);
        while (TriggerCaptureRows.Count > 240)
        {
            TriggerCaptureRows.RemoveAt(TriggerCaptureRows.Count - 1);
        }
    }

    private void TriggerCaptureGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TriggerCaptureGrid.SelectedItem is not TriggerCaptureRow row)
        {
            TriggerCaptureDetailBox.Text = "Select a trigger capture row to view complete detail.";
            return;
        }

        TriggerCaptureDetailBox.Text = row.ToDetailText();
    }

    private void CopySelectedTriggerCapturePath_Click(object sender, RoutedEventArgs e)
    {
        if (TriggerCaptureGrid.SelectedItem is not TriggerCaptureRow row || string.IsNullOrWhiteSpace(row.FilePath))
        {
            MessageBox.Show(this, "Select a trigger capture row first.", "Trigger capture", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(row.FilePath);
        AppendSessionLog("Trigger capture path copied to clipboard.");
    }

    private void OpenTriggerCaptureFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = GetProtocolTriggerCaptureFolder();
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AddUiDiagnostic(
                "Error",
                "Trigger",
                "ARIEC-TRIGGER-FOLDER-OPEN-FAILED",
                "Trigger capture folder could not be opened",
                ex.Message,
                "Open the local app data ARIEC60870 trigger-captures folder manually.",
                ex);
            MessageBox.Show(this, "Failed to open trigger capture folder: " + ex.Message, "Trigger capture", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string GetProtocolTriggerCaptureFolder()
        => ARIEC60870.Desktop.Services.LocalWorkspacePaths.TriggerCaptureFolder;


    private ProtocolTriggerCandidate? DetectUserDefinedIecCaptureRuleMatch(Iec103MasterEvidenceEvent item, EvidenceRow row)
    {
        var preset = GetComboBoxText(SmartCapturePresetComboBox, "Any Matching Rule");
        var direction = GetComboBoxText(SmartCaptureDirectionComboBox, "Any");

        if (!direction.Equals("Any", StringComparison.OrdinalIgnoreCase) &&
            !row.Direction.Equals(direction, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!FieldMatches(SmartCaptureCaBox?.Text, row.CommonAddress))
        {
            return null;
        }

        if (!FieldMatches(SmartCaptureIoaBox?.Text, row.IoAddress))
        {
            return null;
        }

        if (!FieldMatches(SmartCaptureTypeIdBox?.Text, row.TypeId))
        {
            return null;
        }

        if (!FieldMatches(SmartCaptureCotBox?.Text, row.CotCode, row.Cot, row.CotDisplay))
        {
            return null;
        }

        var combined = BuildCaptureRuleSearchText(item, row);
        if (!FieldContains(SmartCaptureTextBox?.Text, combined))
        {
            return null;
        }

        if (!FieldContains(SmartCaptureRawHexBox?.Text, row.RawHex, row.ProtocolTraceRaw))
        {
            return null;
        }

        if (!SmartCapturePresetMatches(preset, item, row, combined))
        {
            return null;
        }

        var code = "USER-RULE-" + MakeSafeTriggerCode(preset);
        var title = preset.Equals("Any Matching Rule", StringComparison.OrdinalIgnoreCase)
            ? "User-defined IEC capture rule matched"
            : $"User-defined IEC capture rule matched: {preset}";

        var severity = preset switch
        {
            "Negative / NACK" => "Warning",
            "Quality Issue" => "Warning",
            "Timeout / Error" => "Error",
            "ACD / DFC" => "Warning",
            _ => "Info"
        };

        return new ProtocolTriggerCandidate(
            code,
            title,
            CompactTriggerDetail(row, combined),
            severity);
    }

    private bool IsSmartCaptureRuleEnabled()
        => SmartCaptureRuleEnabledCheckBox?.IsChecked == true;

    private void ApplySmartCaptureRule_Click(object sender, RoutedEventArgs e)
    {
        UpdateSmartCaptureRuleStatus();
    }

    private void DisableSmartCaptureRule_Click(object sender, RoutedEventArgs e)
    {
        if (SmartCaptureRuleEnabledCheckBox is not null)
        {
            SmartCaptureRuleEnabledCheckBox.IsChecked = false;
        }

        _activeProtocolTriggerCaptures.Clear();
        UpdateSmartCaptureRuleStatus();
    }

    private void UpdateSmartCaptureRuleStatus()
    {
        if (SmartCaptureRuleStatusText is null)
        {
            return;
        }

        if (!IsSmartCaptureRuleEnabled())
        {
            SmartCaptureRuleStatusText.Text = "Capture rule OFF. No automatic recording.";
            return;
        }

        var preset = GetComboBoxText(SmartCapturePresetComboBox, "Any Matching Rule");
        var direction = GetComboBoxText(SmartCaptureDirectionComboBox, "Any");
        var pre = ReadSmartCaptureInt(SmartCapturePreRowsBox, TriggerPreCaptureRows, 0, MaxTriggerPreBufferRows);
        var post = ReadSmartCaptureInt(SmartCapturePostRowsBox, TriggerPostCaptureRows, 0, 250);
        var max = ReadSmartCaptureInt(SmartCaptureMaxCapturesBox, 20, 1, 500);
        SmartCaptureRuleStatusText.Text = $"Capture rule ON: {preset}, dir {direction}, pre {pre}, post {post}, max {max}.";
    }

    private static string GetComboBoxText(ComboBox? comboBox, string fallback)
    {
        if (comboBox?.SelectedItem is ComboBoxItem item && item.Content is not null)
        {
            return item.Content.ToString() ?? fallback;
        }

        return fallback;
    }

    private static bool SmartCapturePresetMatches(string preset, Iec103MasterEvidenceEvent item, EvidenceRow row, string combined)
    {
        if (preset.Equals("Any Matching Rule", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return preset switch
        {
            "Negative / NACK" => ContainsAny(combined, "negative", "nack", "not topical", "blocked", "invalid activation", "ACTCON negative"),
            "GI Milestone" => ContainsAny(combined, "general interrogation", "interrogation", "GI ", "ACTCON", "ACTTERM"),
            "Command Lifecycle" => ContainsAny(combined, "command", "select", "operate", "activation confirmation", "activation termination", "feedback", "C_SC", "C_DC", "C_SE"),
            "Digital / Event" => item.IsRelayEdgeEvent || ContainsAny(combined, "spontaneous", "digital", "single-point", "double-point", "M_SP", "M_DP", "event"),
            "Quality Issue" => ContainsAny(combined, "invalid", "not topical", "substituted", "blocked", "overflow", "bad quality", "quality issue"),
            "Timeout / Error" => ContainsAny(combined, "timeout", "no response", "failed", "error", "communication error"),
            "ACD / DFC" => item.Acd == true || item.Dfc == true || ContainsAny(combined, "ACD=1", "DFC=1", "access demand", "data flow control", "busy"),
            _ => true
        };
    }

    private static string BuildCaptureRuleSearchText(Iec103MasterEvidenceEvent item, EvidenceRow row)
        => string.Join(" ",
            item.ProtocolMode,
            item.Category,
            item.State,
            item.Summary,
            item.Detail,
            item.OperatorMessage,
            item.OperatorAction,
            item.ProtocolMeaning,
            item.CauseName,
            item.Cot,
            item.QualityText,
            row.Direction,
            row.ProtocolName,
            row.ProtocolService,
            row.ProtocolAddress,
            row.CommonAddress,
            row.IoAddress,
            row.TypeId,
            row.TypeIdName,
            row.CotCode,
            row.Cot,
            row.CotDisplay,
            row.Quality,
            row.ProtocolTraceTitle,
            row.ProtocolTraceMeaning,
            row.Detail,
            row.RawHex,
            row.ProtocolTraceRaw,
            row.SemanticLabel,
            row.SemanticState);

    private static bool FieldMatches(string? expected, params string[] actuals)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        var needle = expected.Trim();
        return actuals.Any(actual => string.Equals((actual ?? string.Empty).Trim(), needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool FieldContains(string? expected, params string[] actuals)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        var needle = expected.Trim();
        return actuals.Any(actual => (actual ?? string.Empty).Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static int ReadSmartCaptureInt(TextBox? textBox, int fallback, int min, int max)
    {
        if (textBox is null || !int.TryParse(textBox.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static string MakeSafeTriggerCode(string value)
    {
        var safe = new string((value ?? string.Empty)
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '-')
            .ToArray());

        safe = safe.Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "CUSTOM" : safe;
    }

    private static string CompactTriggerDetail(EvidenceRow row, string combined)
    {
        var detail = $"{row.ProtocolTraceTitle} | {row.ProtocolTraceMeaning}";
        if (!string.IsNullOrWhiteSpace(row.RawHex) && row.RawHex != "-")
        {
            detail += $" | RAW {row.RawHex}";
        }

        const int max = 420;
        return detail.Length <= max ? detail : detail[..max] + "…";
    }



    private sealed record ProtocolTriggerCandidate(
        string Code,
        string Title,
        string Detail,
        string Severity);

    private sealed class ProtocolTriggerCapture
    {
        public ProtocolTriggerCapture(
            string captureId,
            ProtocolTriggerCandidate trigger,
            EvidenceRow triggerRow,
            List<EvidenceRow> rows,
            int postRowsRemaining)
        {
            CaptureId = captureId;
            Trigger = trigger;
            TriggerRow = triggerRow;
            Rows = rows;
            PostRowsRemaining = postRowsRemaining;
            StartedUtc = DateTime.UtcNow;
        }

        public string CaptureId { get; }
        public ProtocolTriggerCandidate Trigger { get; }
        public EvidenceRow TriggerRow { get; }
        public List<EvidenceRow> Rows { get; }
        public int PostRowsRemaining { get; set; }
        public DateTime StartedUtc { get; }
    }

}
