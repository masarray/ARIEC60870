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
        var original = _relayEventStore.Snapshot()
            .FirstOrDefault(ev => ev.OriginalFrameEvidence is not null &&
                Iec10xObservationIdentity.SamePhysicalFrame(ev.OriginalFrameEvidence,
                    stage.EvidenceReference, c.Source, protocol, stage.Sequence))
            ?.OriginalFrameEvidence;
        NavigateToOriginalFrame(stage.EvidenceReference, stage.Sequence, c.Source, protocol, original);
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
        NavigateToOriginalFrame(row.EvidenceReference, row.Sequence, row.PhysicalSource, protocol, row.OriginalFrameEvidence);
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

    private async void NavigateToOriginalFrame(string evidenceRef, long sequence, string physicalSource,
        Iec60870ProtocolMode protocol, Iec103MasterEvidenceEvent? retainedOriginal = null)
    {
        // Direct navigation must reveal the original despite a previous quick filter.
        ClearTraceQuickFindForNavigation();
        // Prefer the session-wide disk index, which is fed before the lossy
        // GUI preview. This works for Controls/GI stages without an Event row.
        if (await TryOpenHistoryWindowAsync(evidenceRef, physicalSource, protocol))
            return;

        // For active IEC-101/104 and legacy non-indexed sources, use the exact
        // physical-frame matcher. No same-IOA/timestamp substitution.
        var frame = _protocolTraceStore.Snapshot().FirstOrDefault(x =>
            Iec10xObservationIdentity.SamePhysicalFrame(x.Source, evidenceRef, physicalSource, protocol, sequence));
        var fromEvent = false;
        if (frame is null)
        {
            var original = retainedOriginal ?? _relayEventStore.Snapshot()
                .FirstOrDefault(ev => ev.OriginalFrameEvidence is not null &&
                    Iec10xObservationIdentity.SamePhysicalFrame(ev.OriginalFrameEvidence,
                        evidenceRef, physicalSource, protocol, sequence))
                ?.OriginalFrameEvidence;
            if (original is not null && original.RawHex.Length > 0 &&
                Iec10xObservationIdentity.SamePhysicalFrame(original,
                    evidenceRef, physicalSource, protocol, sequence))
            {
                frame = new EvidenceRow(original, ResolveIoaPoint(original));
                fromEvent = true;
            }
        }
        if (frame is null)
        {
            ModernMessageBox.Show(this,
                "The exact original frame is not available in this session. " +
                "If the temporary history was already cleaned, a full raw " +
                "recording is required for older frames. Reference: " + evidenceRef,
                "Original frame unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MainTabControl.SelectedIndex = 1;
        EnterLineMonitorHoldForUserSelection();
        if (fromEvent)
        {
            // Legacy in-memory fallback: retain the nearby source-qualified
            // Event frames instead of silently substituting unrelated frames.
            var ordered = _relayEventStore.Snapshot()
                .Where(ev => ev.OriginalFrameEvidence is { RawHex.Length: > 0 } &&
                    ev.OriginalFrameEvidence.ProtocolMode == protocol)
                .Select(ev => ev.OriginalFrameEvidence!)
                .DistinctBy(ev => Iec10xObservationIdentity.EvidenceReference(ev).Split("|object:")[0])
                .OrderBy(ev => ev.TimestampUtc).ToArray();
            var idx = Array.FindIndex(ordered, ev =>
                Iec10xObservationIdentity.SamePhysicalFrame(ev, evidenceRef, physicalSource, protocol, sequence));
            var range = idx >= 0
                ? ordered.Skip(Math.Max(0, idx - 40)).Take(81)
                    .Select(ev => new EvidenceRow(ev, ResolveIoaPoint(ev))).ToArray()
                : new[] { frame };
            FrameTraceRows.ReplaceRange(range);
            frame = FrameTraceRows.FirstOrDefault(x =>
                Iec10xObservationIdentity.SamePhysicalFrame(x.Source, evidenceRef, physicalSource, protocol, sequence))
                ?? frame;
            _historicalTraceViewActive = true;
            FrameTimelineCaption.Text = $"Retained Event frames · {range.Length} (partial)";
        }
        else
        {
            _historicalTraceViewActive = false;
            FrameTraceRows.ReplaceRange(_protocolTraceStore.Snapshot());
            FrameTimelineCaption.Text = "Frame timeline · original selected";
        }
        TraceOlderButton.Visibility = Visibility.Collapsed;
        TraceNewerButton.Visibility = Visibility.Collapsed;
        FrameTraceGrid.SelectedItems.Clear();
        FrameTraceGrid.SelectedItem = frame;
        FrameTraceGrid.ScrollIntoView(frame);
        ApplySelectedEvidenceRowToInspector(frame);
    }
}
