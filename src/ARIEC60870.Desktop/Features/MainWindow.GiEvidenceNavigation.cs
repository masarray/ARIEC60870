// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    public ObservableCollection<Iec10xGiCasebook.GiStage> GiCaseStages { get; } = new();

    private void RefreshGiCasesIfVisible(bool force = false)
    {
        if (MainTabControl?.SelectedIndex != 12 || GiCasesGrid is null || (!_giCasesDirty && !force)) return;
        var selected = (GiCasesGrid.SelectedItem as Iec10xGiCasebook.GiCase)?.CaseId;
        GiCaseRows.ReplaceRange(_giCasebook.Snapshot().Reverse().ToArray());
        _giCasesDirty = false;
        var match = selected.HasValue ? GiCaseRows.FirstOrDefault(x => x.CaseId == selected.Value) : null;
        if (match is not null) GiCasesGrid.SelectedItem = match;
        else if (GiCasesGrid.SelectedItem is null && GiCaseRows.Count > 0)
            GiCasesGrid.SelectedItem = GiCaseRows[0];
    }

    private void GiCases_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        GiCaseStages.Clear();
        if (GiCasesGrid?.SelectedItem is not Iec10xGiCasebook.GiCase c)
        {
            if (GiCaseDetailsBox is not null) GiCaseDetailsBox.Text = "Select a GI conversation.";
            return;
        }
        foreach (var s in c.Stages) GiCaseStages.Add(s);
        if (GiCaseDetailsBox is not null)
            GiCaseDetailsBox.Text = c.Summary + " · " + c.Coverage +
                " · Class1 requests " + c.Class1Requests + ". ACTTERM is not process-point verification.";
    }

    private void GiCaseCopy_Click(object sender, RoutedEventArgs e)
    {
        if (GiCasesGrid?.SelectedItem is not Iec10xGiCasebook.GiCase c) return;
        var sb = new StringBuilder();
        sb.AppendLine(c.Summary);
        sb.AppendLine("Protocol: " + c.Protocol + " | Source: " + c.Source +
            " | Link: " + (c.LinkAddress?.ToString() ?? "N/A") + " | OA: " + c.Originator);
        sb.AppendLine("Coverage: " + c.Coverage);
        sb.AppendLine("Class1 request/response: " + c.Class1Requests + "/" + c.Class1Responses);
        foreach (var s in c.Stages)
        {
            sb.AppendLine($"{s.HostUtc:O} | {s.Direction} | {s.Phase} | seq {s.Sequence} | {s.EvidenceReference}");
            sb.AppendLine("HEX: " + s.RawHex);
        }
        Clipboard.SetText(sb.ToString());
    }

    private void GiCaseJump_Click(object sender, RoutedEventArgs e)
    {
        if (GiCasesGrid?.SelectedItem is not Iec10xGiCasebook.GiCase c) return;
        var stage = GiStagesGrid?.SelectedItem as Iec10xGiCasebook.GiStage ?? c.Stages.LastOrDefault();
        if (stage is null) return;
        var protocol = c.Protocol == "IEC-101" ? Iec60870ProtocolMode.Iec101 : Iec60870ProtocolMode.Iec104;
        NavigateToOriginalFrame(stage.EvidenceReference, stage.Sequence, c.Source, protocol);
    }

    private void EventOpenFrame_Click(object sender, RoutedEventArgs e)
    {
        if (RelayEventGrid?.SelectedItem is not RelayEventRow row) return;
        var protocol = row.Protocol switch
        {
            "101" => Iec60870ProtocolMode.Iec101,
            "104" => Iec60870ProtocolMode.Iec104,
            _ => Iec60870ProtocolMode.Iec103
        };
        NavigateToOriginalFrame(row.EvidenceReference, row.Sequence, row.PhysicalSource, protocol);
    }

    private void EventOpenCase_Click(object sender, RoutedEventArgs e)
    {
        if (RelayEventGrid?.SelectedItem is not RelayEventRow row) return;
        if (!row.TransactionCaseId.HasValue || string.IsNullOrWhiteSpace(row.TransactionKind))
        {
            ModernMessageBox.Show(this, "This is a process event, not a linked GI/Command transaction.",
                "No linked transaction", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (row.TransactionKind == "GI")
        {
            var gi = _giCasebook.Snapshot().FirstOrDefault(x => x.CaseId == row.TransactionCaseId.Value &&
                x.Protocol == "IEC-" + row.Protocol &&
                SourceMatches(x.Source, row.PhysicalSource));
            if (gi is null) { ShowEvictedCase(); return; }
            MainTabControl.SelectedIndex = 12;
            RefreshGiCasesIfVisible(force: true);
            GiCasesGrid.SelectedItem = GiCaseRows.FirstOrDefault(x => x.CaseId == gi.CaseId);
            if (GiCasesGrid.SelectedItem is not null) GiCasesGrid.ScrollIntoView(GiCasesGrid.SelectedItem);
        }
        else if (row.TransactionKind == "Command")
        {
            var cmd = _commandCasebook.Snapshot().FirstOrDefault(x => x.CaseId == row.TransactionCaseId.Value &&
                x.Protocol == "IEC-" + row.Protocol &&
                SourceMatches(x.Source, row.PhysicalSource));
            if (cmd is null) { ShowEvictedCase(); return; }
            MainTabControl.SelectedIndex = 11;
            RefreshControlCasesIfVisible(force: true);
            ControlCasesGrid.SelectedItem = ControlCaseRows.FirstOrDefault(x => x.CaseId == cmd.CaseId);
            if (ControlCasesGrid.SelectedItem is not null) ControlCasesGrid.ScrollIntoView(ControlCasesGrid.SelectedItem);
        }
    }

    private static bool SourceMatches(string? left, string? right) =>
        string.Equals(string.IsNullOrWhiteSpace(left) ? "SINGLE" : left.Trim(),
            string.IsNullOrWhiteSpace(right) ? "SINGLE" : right.Trim(), StringComparison.OrdinalIgnoreCase);

    private void ShowEvictedCase() => ModernMessageBox.Show(this,
        "The linked transaction is no longer in this bounded session index. " +
        "Use the original capture/Trace evidence; no other case was substituted.",
        "Transaction not retained", MessageBoxButton.OK, MessageBoxImage.Information);

    private void NavigateToOriginalFrame(string evidenceRef, long sequence, string physicalSource,
        Iec60870ProtocolMode protocol)
    {
        var frame = _protocolTraceStore.Snapshot().FirstOrDefault(x =>
            Iec10xObservationIdentity.SamePhysicalFrame(x.Source, evidenceRef, physicalSource, protocol, sequence));
        if (frame is null)
        {
            ModernMessageBox.Show(this,
                "This original frame is outside the retained Trace preview or the capture lacks its source identity. " +
                "No unrelated frame was substituted. Consult the complete capture for the original reference: " + evidenceRef,
                "Original frame not retained", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MainTabControl.SelectedIndex = 1;
        EnterLineMonitorHoldForUserSelection();
        RefreshActiveTraceSnapshot();
        var visible = FrameTraceRows.FirstOrDefault(x => ReferenceEquals(x, frame));
        if (visible is null)
        {
            AppendSessionLog("Original source frame expired while opening Trace: " + evidenceRef);
            return;
        }
        FrameTraceGrid.SelectedItems.Clear();
        FrameTraceGrid.SelectedItem = visible;
        FrameTraceGrid.ScrollIntoView(visible);
    }
}
