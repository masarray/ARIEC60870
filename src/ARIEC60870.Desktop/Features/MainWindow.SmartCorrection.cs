// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ARIEC60870.Desktop.Reporting;
using ARIEC60870.Desktop.ViewModels;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private readonly List<FrameworkElement> _smartCorrectionHighlightedElements = new();
    private readonly List<EvidenceSmartFinding> _smartCorrectionFindingTrail = new();

    private void AutoFixConfigFromFindings_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<EvidenceRow> evidence = _protocolTraceStore.Snapshot();
        if (evidence.Count == 0)
        {
            evidence = _evidenceSummaryStore.Snapshot();
        }

        if (evidence.Count == 0)
        {
            ModernMessageBox.Show(this, "No evidence is available yet. Capture traffic first so smart correction can infer the configuration from real frames.", "Auto fix config", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var setupBeforeCorrection = BuildReportCommunicationSetupLines().ToArray();
        var suggestion = Iec10xAutoConfigCorrector.Analyze(evidence, setupBeforeCorrection, GetSelectedProtocolMode());
        if (!suggestion.HasChanges)
        {
            AppendSessionLog("Smart correction reviewed the current evidence and found no higher-confidence configuration change.");
            RefreshFindingsWorkspace(force: true);
            ModernMessageBox.Show(this, suggestion.Summary, "Auto fix config", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CaptureSmartCorrectionFindingTrail(evidence, setupBeforeCorrection, suggestion);
        ApplySmartCorrectionSuggestion(suggestion);
        SaveSetupPreferencesFromUi(silent: true);
        RefreshFindingsWorkspace(force: true);

        var summary = BuildSmartCorrectionSummary(suggestion.Changes);
        AppendSessionLog("Smart correction applied: " + summary + ". The original finding is kept as a corrected finding trail until the next session.");
        AddStatusHistory("Smart correction", "Applied: " + summary);
        UpdateStableHeader("Smart correction applied", summary);
        SetupOverlay.Visibility = Visibility.Visible;

        ModernMessageBox.Show(this, "Smart correction updated these setup fields:\n\n- " + string.Join("\n- ", suggestion.Changes.Select(change => $"{change.Label}: {change.CurrentValue} → {change.ProposedValue}")) + "\n\nThe original finding is kept in Findings/Report as a corrected finding trail. Start the next session with this corrected setup; if the evidence is clean, the finding will disappear naturally.", "Auto fix config", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private IReadOnlyList<EvidenceSmartFinding> BuildSmartFindingsWithCorrectionTrail(
        IReadOnlyList<EvidenceRow> rows,
        IReadOnlyList<FindingRow> existingFindings,
        IReadOnlyList<KeyValuePair<string, string>> communicationSetup)
    {
        var active = EvidenceSmartFindingAnalyzer.Analyze(rows, existingFindings, communicationSetup).ToList();

        // Runtime findings are shown only while they are still present in the current
        // engine store. They must not be copied into a sticky session ledger because
        // many protocol findings, such as GI incomplete, naturally resolve a few
        // frames later when ACTCON/data arrives.
        foreach (var finding in existingFindings.Where(ShouldImportRuntimeFinding))
        {
            AddDistinctFinding(active, ConvertRuntimeFindingToSmartFinding(finding));
        }

        // Only Smart Correction results are sticky. This keeps the audit trail for a
        // corrected wrong configuration without leaving stale active warnings behind.
        foreach (var trail in _smartCorrectionFindingTrail)
        {
            AddDistinctFinding(active, trail);
        }

        return active
            .GroupBy(finding => finding.Code + "@" + finding.Sequence, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(f => f.Severity == EvidenceSmartFindingSeverity.Error ? 3 : f.Severity == EvidenceSmartFindingSeverity.Warning ? 2 : 1).First())
            .OrderByDescending(finding => finding.Severity == EvidenceSmartFindingSeverity.Error ? 3 : finding.Severity == EvidenceSmartFindingSeverity.Warning ? 2 : 1)
            .ThenBy(finding => finding.Code.EndsWith("-CORRECTED", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(finding => finding.Sequence)
            .Take(10)
            .ToArray();
    }

    private static bool ShouldImportRuntimeFinding(FindingRow finding)
    {
        var text = string.Join(" ", finding.Id, finding.Title, finding.Evidence, finding.Impact, finding.Recommendation);

        // Guard the Smart Findings UI from older runtime diagnostics that used the
        // generic word "command" for C_IC_NA_1 General Interrogation. GI refresh
        // after a redundancy switch is a service scan, not an operate command and
        // not command congestion.
        if (text.Contains("class", StringComparison.OrdinalIgnoreCase)
            && text.Contains("congestion", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("general interrogation", StringComparison.OrdinalIgnoreCase)
                || text.Contains("C_IC", StringComparison.OrdinalIgnoreCase)
                || text.Contains("interrogation command", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static EvidenceSmartFinding ConvertRuntimeFindingToSmartFinding(FindingRow finding)
    {
        var code = string.IsNullOrWhiteSpace(finding.Id)
            ? "ARIEC-SMART-RUNTIME-FINDING"
            : "ARIEC-SMART-" + new string(finding.Id.Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '-').ToArray()).Trim('-');
        var severity = finding.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)
            ? EvidenceSmartFindingSeverity.Error
            : finding.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase)
                ? EvidenceSmartFindingSeverity.Warning
                : EvidenceSmartFindingSeverity.Info;

        return new EvidenceSmartFinding(
            severity,
            code,
            string.IsNullOrWhiteSpace(finding.Title) ? "Runtime finding requires review." : finding.Title,
            string.IsNullOrWhiteSpace(finding.Impact) ? "The protocol evidence indicates an issue that should remain visible until a new session proves it is resolved." : finding.Impact,
            string.IsNullOrWhiteSpace(finding.Evidence) ? "Runtime finding from protocol engine." : finding.Evidence,
            string.IsNullOrWhiteSpace(finding.Recommendation) ? "Review the related trace rows, correct setup/mapping if needed, then retest in a new session." : finding.Recommendation,
            "Runtime",
            0);
    }

    private static void AddDistinctFinding(ICollection<EvidenceSmartFinding> target, EvidenceSmartFinding finding)
    {
        if (target.Any(item => item.Code.Equals(finding.Code, StringComparison.OrdinalIgnoreCase) && item.Sequence == finding.Sequence))
        {
            return;
        }

        target.Add(finding);
    }

    private void CaptureSmartCorrectionFindingTrail(
        IReadOnlyList<EvidenceRow> evidence,
        IReadOnlyList<KeyValuePair<string, string>> setupBeforeCorrection,
        Iec10xAutoConfigCorrector.Iec10xAutoConfigSuggestion suggestion)
    {
        var previousFindings = EvidenceSmartFindingAnalyzer.Analyze(evidence, _findingStore.Snapshot(), setupBeforeCorrection)
            .Where(IsConfigCorrectionFinding)
            .ToArray();

        var summary = BuildSmartCorrectionSummaryWithBeforeAfter(suggestion.Changes);
        var correctionNote = "Smart Correction applied setup changes: " + summary + ". This is kept as an audit trail for the wrong configuration observed in this session.";

        if (previousFindings.Length == 0)
        {
            var firstSequence = evidence.Count == 0 ? 0 : evidence.Min(row => row.Sequence);
            previousFindings = new[]
            {
                new EvidenceSmartFinding(
                    EvidenceSmartFindingSeverity.Warning,
                    "ARIEC-SMART-CONFIG-CORRECTION",
                    "Configuration was corrected from captured protocol evidence.",
                    "The previous setup did not match the most likely profile inferred from TX/RX evidence.",
                    suggestion.Summary,
                    correctionNote,
                    suggestion.Confidence,
                    firstSequence)
            };
        }

        foreach (var finding in previousFindings)
        {
            var correctedCode = finding.Code.EndsWith("-CORRECTED", StringComparison.OrdinalIgnoreCase)
                ? finding.Code
                : finding.Code + "-CORRECTED";
            if (_smartCorrectionFindingTrail.Any(item => item.Code.Equals(correctedCode, StringComparison.OrdinalIgnoreCase) && item.Sequence == finding.Sequence))
            {
                continue;
            }

            _smartCorrectionFindingTrail.Add(new EvidenceSmartFinding(
                EvidenceSmartFindingSeverity.Info,
                correctedCode,
                "Corrected by Smart Correction: " + finding.Problem,
                finding.Why,
                finding.Evidence + " | " + correctionNote,
                "Already corrected in Setup. Reconnect or start a new session to validate; if the corrected configuration matches the device, this finding will not be produced naturally in the next session.",
                suggestion.Confidence,
                finding.Sequence));
        }
    }

    private static bool IsConfigCorrectionFinding(EvidenceSmartFinding finding)
    {
        var text = string.Join(" ", finding.Code, finding.Problem, finding.Evidence, finding.Solution);
        return text.Contains("PROFILE-SIZE", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Common address", StringComparison.OrdinalIgnoreCase)
               || text.Contains("UNKNOWN-CA", StringComparison.OrdinalIgnoreCase)
               || text.Contains("COT", StringComparison.OrdinalIgnoreCase)
               || text.Contains("IOA size", StringComparison.OrdinalIgnoreCase)
               || text.Contains("CA size", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Link address", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplySmartCorrectionSuggestion(Iec10xAutoConfigCorrector.Iec10xAutoConfigSuggestion suggestion)
    {
        foreach (var change in suggestion.Changes)
        {
            switch (change.Key)
            {
                case "CommonAddress":
                    CommonAddressBox.Text = change.ProposedValue;
                    CommandCaBox.Text = change.ProposedValue;
                    break;
                case "LinkAddress":
                    LinkAddressBox.Text = change.ProposedValue;
                    break;
                case "LinkAddressSize":
                    SelectComboContent(LinkAddressSizeComboBox, change.ProposedValue);
                    break;
                case "CotSize":
                    SelectComboContent(CotSizeComboBox, change.ProposedValue);
                    break;
                case "CaSize":
                    SelectComboContent(CaSizeComboBox, change.ProposedValue);
                    break;
                case "IoaSize":
                    SelectComboContent(IoaSizeComboBox, change.ProposedValue);
                    break;
                case "Class2Interval":
                    Class2IntervalBox.Text = change.ProposedValue;
                    break;
            }
        }

        HighlightSmartCorrectedConfigFields(suggestion.Changes);
    }

    private void HighlightSmartCorrectedConfigFields(IReadOnlyList<Iec10xAutoConfigCorrector.Iec10xAutoConfigFieldChange> changes)
    {
        ClearSmartCorrectionHighlights();
        ResetSmartCorrectionLabels();

        if (SmartCorrectionSetupBanner is not null && SmartCorrectionSetupBannerText is not null)
        {
            SmartCorrectionSetupBanner.Visibility = Visibility.Visible;
            SmartCorrectionSetupBannerText.Text = "Smart Correction applied: " + BuildSmartCorrectionSummaryWithBeforeAfter(changes) + ". Corrected fields are marked in amber.";
        }

        foreach (var change in changes)
        {
            switch (change.Key)
            {
                case "CommonAddress":
                    MarkCorrectedLabel(CommonAddressLabelText, "Common Address", change);
                    HighlightSmartCorrectionElement(CommonAddressBox, $"Smart correction changed Common address from {change.CurrentValue} to {change.ProposedValue}.");
                    break;
                case "LinkAddress":
                    MarkCorrectedLabel(LinkAddressLabelText, "Link Address", change);
                    HighlightSmartCorrectionElement(LinkAddressBox, $"Smart correction changed Link address from {change.CurrentValue} to {change.ProposedValue}.");
                    break;
                case "LinkAddressSize":
                    MarkCorrectedLabel(LinkAddressSizeLabelText, "Link length", change);
                    HighlightSmartCorrectionElement(LinkAddressSizeComboBox, $"Smart correction changed Link length from {change.CurrentValue} to {change.ProposedValue} octet(s).");
                    break;
                case "CotSize":
                    MarkCorrectedLabel(CotSizeLabelText, "COT size", change);
                    HighlightSmartCorrectionElement(CotSizeComboBox, $"Smart correction changed COT size from {change.CurrentValue} to {change.ProposedValue} octet(s).");
                    break;
                case "CaSize":
                    MarkCorrectedLabel(CaSizeLabelText, "CA size", change);
                    HighlightSmartCorrectionElement(CaSizeComboBox, $"Smart correction changed CA size from {change.CurrentValue} to {change.ProposedValue} octet(s).");
                    break;
                case "IoaSize":
                    MarkCorrectedLabel(IoaSizeLabelText, "IOA size", change);
                    HighlightSmartCorrectionElement(IoaSizeComboBox, $"Smart correction changed IOA size from {change.CurrentValue} to {change.ProposedValue} octet(s).");
                    break;
                case "Class2Interval":
                    MarkCorrectedLabel(Class2IntervalLabelText, "Class 2 interval (ms)", change);
                    HighlightSmartCorrectionElement(Class2IntervalBox, $"Smart correction changed Class 2 interval from {change.CurrentValue} to {change.ProposedValue} ms.");
                    break;
            }
        }
    }

    private void MarkCorrectedLabel(TextBlock? label, string baseText, Iec10xAutoConfigCorrector.Iec10xAutoConfigFieldChange change)
    {
        if (label is null)
        {
            return;
        }

        label.Text = $"{baseText} · corrected {change.CurrentValue} → {change.ProposedValue}";
        HighlightSmartCorrectionElement(label, $"Smart correction changed {change.Label} from {change.CurrentValue} to {change.ProposedValue}.");
    }

    private void HighlightSmartCorrectionElement(FrameworkElement? element, string tooltip)
    {
        if (element is null)
        {
            return;
        }

        switch (element)
        {
            case Control control:
                control.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF7D6"));
                control.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
                control.BorderThickness = new Thickness(2);
                control.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C2D12"));
                break;
            case TextBlock textBlock:
                textBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B45309"));
                textBlock.FontWeight = FontWeights.SemiBold;
                break;
        }

        element.ToolTip = tooltip;
        _smartCorrectionHighlightedElements.Add(element);
    }

    private void ClearSmartCorrectionHighlights()
    {
        foreach (var element in _smartCorrectionHighlightedElements)
        {
            switch (element)
            {
                case Control control:
                    control.ClearValue(Control.BackgroundProperty);
                    control.ClearValue(Control.BorderBrushProperty);
                    control.ClearValue(Control.BorderThicknessProperty);
                    control.ClearValue(Control.ForegroundProperty);
                    break;
                case TextBlock textBlock:
                    textBlock.ClearValue(TextBlock.ForegroundProperty);
                    textBlock.ClearValue(TextBlock.FontWeightProperty);
                    break;
            }
        }

        _smartCorrectionHighlightedElements.Clear();
    }

    private void ResetSmartCorrectionLabels()
    {
        if (LinkAddressLabelText is not null) LinkAddressLabelText.Text = "Link Address";
        if (CommonAddressLabelText is not null) CommonAddressLabelText.Text = "Common Address";
        if (LinkAddressSizeLabelText is not null) LinkAddressSizeLabelText.Text = "Link length";
        if (CotSizeLabelText is not null) CotSizeLabelText.Text = "COT size";
        if (CaSizeLabelText is not null) CaSizeLabelText.Text = "CA size";
        if (IoaSizeLabelText is not null) IoaSizeLabelText.Text = "IOA size";
        if (Class2IntervalLabelText is not null) Class2IntervalLabelText.Text = "Class 2 interval (ms)";
    }

    private void ClearSmartCorrectionSessionTrail()
    {
        _smartCorrectionFindingTrail.Clear();
        ClearSmartCorrectionHighlights();
        ResetSmartCorrectionLabels();
        if (SmartCorrectionSetupBanner is not null)
        {
            SmartCorrectionSetupBanner.Visibility = Visibility.Collapsed;
        }
        if (SmartCorrectionSetupBannerText is not null)
        {
            SmartCorrectionSetupBannerText.Text = string.Empty;
        }
    }

    private static string BuildSmartCorrectionSummary(IReadOnlyList<Iec10xAutoConfigCorrector.Iec10xAutoConfigFieldChange> changes)
        => string.Join(", ", changes.Select(change => $"{change.Label}={change.ProposedValue}"));

    private static string BuildSmartCorrectionSummaryWithBeforeAfter(IReadOnlyList<Iec10xAutoConfigCorrector.Iec10xAutoConfigFieldChange> changes)
        => string.Join(", ", changes.Select(change => $"{change.Label} {change.CurrentValue} → {change.ProposedValue}"));

    private void AddStatusHistory(string status, string detail)
    {
        StatusHistoryRows.Insert(0, new StatusHistoryRow(DateTime.Now.ToString("HH:mm:ss"), status, detail));
        while (StatusHistoryRows.Count > 80)
        {
            StatusHistoryRows.RemoveAt(StatusHistoryRows.Count - 1);
        }
    }
}
