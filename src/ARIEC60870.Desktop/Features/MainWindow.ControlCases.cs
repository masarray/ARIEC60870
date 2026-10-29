// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    public ObservableCollection<Iec10xCommandCasebook.CommandStage> ControlCaseStages { get; } = new();

    private void RefreshControlCasesIfVisible(bool force = false)
    {
        if (MainTabControl?.SelectedIndex != 11 || ControlCasesGrid is null || (!_commandCasesDirty && !force)) return;

        var id = (ControlCasesGrid.SelectedItem as Iec10xCommandCasebook.CommandCase)?.CaseId;
        ControlCaseRows.ReplaceRange(_commandCasebook.Snapshot().Reverse().ToArray());
        _commandCasesDirty = false;

        // Preserve the operator's selection while the bounded case index updates.
        var match = id.HasValue ? ControlCaseRows.FirstOrDefault(c => c.CaseId == id.Value) : null;
        if (match is not null) ControlCasesGrid.SelectedItem = match;
        else if (ControlCasesGrid.SelectedItem is null && ControlCaseRows.Count > 0)
            ControlCasesGrid.SelectedItem = ControlCaseRows[0];
    }

    private void ControlCases_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ControlCaseStages.Clear();
        if (ControlCasesGrid?.SelectedItem is not Iec10xCommandCasebook.CommandCase c)
        {
            if (ControlCaseDetailsBox is not null) ControlCaseDetailsBox.Text = "Select a command case.";
            return;
        }
        foreach (var stage in c.Stages) ControlCaseStages.Add(stage);
        if (ControlCaseDetailsBox is not null)
            ControlCaseDetailsBox.Text = c.Interpretation +
                "  ·  Observation means protocol evidence, not independently verified physical operation.";
    }

    private void ControlCaseCopy_Click(object sender, RoutedEventArgs e)
    {
        if (ControlCasesGrid?.SelectedItem is not Iec10xCommandCasebook.CommandCase c) return;
        Clipboard.SetText(c.ToEvidenceText());
        AppendSessionLog($"Copied source-qualified control case #{c.CaseId} ({c.Stages.Count} observed stages) to clipboard.");
    }

    private void ControlCaseJump_Click(object sender, RoutedEventArgs e)
    {
        if (ControlCasesGrid?.SelectedItem is not Iec10xCommandCasebook.CommandCase c) return;
        var stage = ControlCaseStagesGrid?.SelectedItem as Iec10xCommandCasebook.CommandStage
                    ?? c.Stages.LastOrDefault();
        if (stage is null) return;

        // The source qualified case reference is authoritative; the bounded Trace
        // viewport may have evicted it. Never navigate to an unrelated same-IOA row.
        var source = _protocolTraceStore.Snapshot().FirstOrDefault(r =>
            r.Sequence == stage.Sequence &&
            string.Equals(r.Source.PhysicalSource?.Trim(), c.Source, StringComparison.OrdinalIgnoreCase) &&
            r.ProtocolName == c.Protocol);
        if (source is null)
        {
            ModernMessageBox.Show(this,
                "The referenced frame is outside the current bounded Trace preview. " +
                "Its original evidence reference remains in Copy Case; open the full raw journal or a saved capture to inspect it.",
                "Source frame not in preview", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MainTabControl.SelectedIndex = 1;
        EnterLineMonitorHoldForUserSelection();
        RefreshActiveTraceSnapshot();
        FrameTraceGrid.SelectedItems.Clear();
        FrameTraceGrid.SelectedItem = FrameTraceRows.FirstOrDefault(r => ReferenceEquals(r, source));
        if (FrameTraceGrid.SelectedItem is not null) FrameTraceGrid.ScrollIntoView(FrameTraceGrid.SelectedItem);
        else
            AppendSessionLog("Control case source frame is no longer visible in the held Trace window.");
    }
}
