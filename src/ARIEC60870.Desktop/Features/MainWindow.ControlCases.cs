// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Controls;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    public ARIEC60870.Desktop.ViewModels.ObservableRangeCollection<Iec10xCommandCasebook.CommandStage> ControlCaseStages { get; } = new();
    private bool _restoringControlCaseSelection;

    private void RefreshControlCasesIfVisible(bool force = false)
    {
        if (MainTabControl?.SelectedIndex != 11 || ControlCasesGrid is null || (!_commandCasesDirty && !force)) return;

        var id = (ControlCasesGrid.SelectedItem as Iec10xCommandCasebook.CommandCase)?.CaseId;
        _restoringControlCaseSelection = true;
        try
        {
            ControlCaseRows.ReconcileByKey(_commandCasebook.Snapshot().Reverse().ToArray(),
                row => (row.Protocol, row.Source, row.CaseId));
            _commandCasesDirty = false;
            var match = id.HasValue ? ControlCaseRows.FirstOrDefault(c => c.CaseId == id.Value) : null;
            if (match is not null) ControlCasesGrid.SelectedItem = match;
            else if (ControlCasesGrid.SelectedItem is null && ControlCaseRows.Count > 0)
                ControlCasesGrid.SelectedItem = ControlCaseRows[0];
        }
        finally { _restoringControlCaseSelection = false; }
        RefreshSelectedControlCaseStages();
    }

    private void ControlCases_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringControlCaseSelection) return;
        RefreshSelectedControlCaseStages();
    }

    private void RefreshSelectedControlCaseStages()
    {
        if (ControlCasesGrid?.SelectedItem is not Iec10xCommandCasebook.CommandCase c)
        {
            if (ControlCaseStages.Count > 0) ControlCaseStages.Clear();
            if (ControlCaseDetailsBox is not null) ControlCaseDetailsBox.Text = "Select a command case.";
            return;
        }
        ControlCaseStages.ReconcileByKey(c.Stages, stage => (stage.Sequence, stage.EvidenceReference));
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

        // Share the same strict reference matcher as Event/GI navigation. Never
        // substitute a same-IOA row or merely the newest frame.
        var protocol = c.Protocol == "IEC-101"
            ? ARIEC60870.Master.Model.Iec60870ProtocolMode.Iec101
            : ARIEC60870.Master.Model.Iec60870ProtocolMode.Iec104;
        NavigateToOriginalFrame(stage.EvidenceReference, stage.Sequence, c.Source, protocol);
    }
}
