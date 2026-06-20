// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using ARIEC60870.Desktop.Reporting;
using ARIEC60870.Desktop.ViewModels;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private string? _lastFindingWorkspaceSignature;

    private bool IsFindingsWorkspaceTabActive()
        => MainTabControl?.SelectedIndex == 4;

    private void RefreshFindingsWorkspace_Click(object sender, RoutedEventArgs e)
        => RefreshFindingsWorkspace(force: true);

    private void RefreshFindingsWorkspace(bool force = false)
    {
        if (FindingWorkspaceRows is null)
        {
            return;
        }

        var evidence = _protocolTraceStore.Snapshot();
        if (evidence.Count == 0)
        {
            evidence = _evidenceSummaryStore.Snapshot();
        }

        var setup = BuildReportCommunicationSetupLines().ToArray();
        var signature = BuildFindingsWorkspaceSignature(evidence, setup);
        if (!force && string.Equals(_lastFindingWorkspaceSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        var smartFindings = BuildSmartFindingsWithCorrectionTrail(evidence, _findingStore.Snapshot(), setup);
        var rows = smartFindings
            .Select(finding => BuildFindingWorkspaceRow(finding, evidence))
            .ToArray();

        FindingWorkspaceRows.ReplaceRange(rows);
        _lastFindingWorkspaceSignature = signature;
    }

    private string BuildFindingsWorkspaceSignature(IReadOnlyList<EvidenceRow> evidence, IReadOnlyList<KeyValuePair<string, string>> setup)
    {
        var last = evidence.Count == 0 ? 0 : evidence[^1].Sequence;
        var setupKey = string.Join("|", setup.Select(pair => pair.Key + "=" + pair.Value));
        return string.Join(";", evidence.Count.ToString(CultureInfo.InvariantCulture), last.ToString(CultureInfo.InvariantCulture), FindingRows.Count.ToString(CultureInfo.InvariantCulture), _smartCorrectionFindingTrail.Count.ToString(CultureInfo.InvariantCulture), setupKey.GetHashCode(StringComparison.Ordinal).ToString(CultureInfo.InvariantCulture));
    }

    private FindingWorkspaceRow BuildFindingWorkspaceRow(EvidenceSmartFinding finding, IReadOnlyList<EvidenceRow> evidence)
    {
        var frames = SelectFindingFrames(finding, evidence)
            .Select(BuildFindingFrameRow)
            .ToArray();

        return new FindingWorkspaceRow(
            finding.Severity.ToString(),
            finding.Code,
            CleanFindingText(finding.Problem),
            CleanFindingText(finding.Why),
            CleanFindingText(finding.Evidence),
            CleanFindingText(finding.Solution),
            finding.Confidence,
            finding.Sequence,
            BuildFindingInterpretation(finding, frames),
            frames);
    }

    private IReadOnlyList<EvidenceRow> SelectFindingFrames(EvidenceSmartFinding finding, IReadOnlyList<EvidenceRow> evidence)
    {
        if (evidence.Count == 0)
        {
            return Array.Empty<EvidenceRow>();
        }

        var pivot = finding.Sequence > 0
            ? evidence.OrderBy(row => Math.Abs(row.Sequence - finding.Sequence)).FirstOrDefault()
            : null;

        if (pivot is null)
        {
            var text = CleanFindingText(finding.Problem + " " + finding.Evidence);
            pivot = evidence.FirstOrDefault(row => ContainsFindingToken(row, text));
        }

        pivot ??= evidence.Last();
        var pivotSequence = pivot.Sequence;

        var window = evidence
            .Where(row => row.Sequence >= pivotSequence - 4 && row.Sequence <= pivotSequence + 8)
            .OrderBy(row => row.Sequence)
            .ToList();

        if (window.Count < 3)
        {
            window = evidence
                .OrderBy(row => Math.Abs(row.Sequence - pivotSequence))
                .ThenBy(row => row.Sequence)
                .Take(5)
                .OrderBy(row => row.Sequence)
                .ToList();
        }

        return window
            .Where(IsUsefulFindingFrame)
            .Take(5)
            .ToArray();
    }

    private static bool ContainsFindingToken(EvidenceRow row, string findingText)
    {
        var rowText = string.Join(" ", row.ProtocolTraceTitle, row.ProtocolTraceMeaning, row.ProtocolTraceRaw, row.ProtocolService, row.ProtocolAddress, row.CotDisplay);
        foreach (var token in new[] { "CA", "IOA", "GI", "ACTCON", "ACTTERM", "command", "Class 1", "spontaneous", "quality", "unknown" })
        {
            if (findingText.Contains(token, StringComparison.OrdinalIgnoreCase) && rowText.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUsefulFindingFrame(EvidenceRow row)
    {
        var text = string.Join(" ", row.ProtocolTraceTitle, row.ProtocolTraceMeaning, row.ProtocolTraceRaw, row.ProtocolService, row.ProtocolAddress, row.CotDisplay, row.Quality);
        return row.Direction.Equals("TX", StringComparison.OrdinalIgnoreCase)
               || row.Direction.Equals("RX", StringComparison.OrdinalIgnoreCase)
               || text.Contains("unknown", StringComparison.OrdinalIgnoreCase)
               || text.Contains("negative", StringComparison.OrdinalIgnoreCase)
               || text.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || text.Contains("quality", StringComparison.OrdinalIgnoreCase);
    }

    private static FindingWorkspaceFrameRow BuildFindingFrameRow(EvidenceRow row)
    {
        var address = row.ProtocolMode is "101" or "104"
            ? $"CA {CleanDash(row.CommonAddress)} / IOA {CleanDash(row.IoAddress)}"
            : CleanDash(row.ProtocolAddress);

        return new FindingWorkspaceFrameRow(
            CleanDash(row.Direction),
            "#" + row.Sequence.ToString(CultureInfo.InvariantCulture),
            CleanDash(row.Time),
            CleanDash(row.ProtocolService),
            address,
            CleanFindingText(row.ProtocolTraceTitle, 180),
            CleanFindingText(row.ProtocolTraceMeaning, 230),
            CleanFindingText(row.ProtocolTraceRaw, 180),
            CleanDash(row.TrafficTone));
    }

    private static string BuildFindingInterpretation(EvidenceSmartFinding finding, IReadOnlyList<FindingWorkspaceFrameRow> frames)
    {
        var problem = finding.Problem;
        if (problem.Contains("common address", StringComparison.OrdinalIgnoreCase) || finding.Code.Contains("CA", StringComparison.OrdinalIgnoreCase))
        {
            return "Read the TX CA and the RX CA first. If they differ, the line may look alive but the application request is addressed to the wrong station.";
        }

        if (problem.Contains("Class 1", StringComparison.OrdinalIgnoreCase) || problem.Contains("spontaneous", StringComparison.OrdinalIgnoreCase))
        {
            return "Check the rows after the command. If analog or cyclic values dominate Class 1, command confirmation must wait behind noisy traffic.";
        }

        if (problem.Contains("GI", StringComparison.OrdinalIgnoreCase) || finding.Code.Contains("GI", StringComparison.OrdinalIgnoreCase))
        {
            return "GI is complete only when request, ACTCON, data, and ACTTERM are all visible. Missing one stage means the baseline scan is not proven.";
        }

        if (problem.Contains("IOA", StringComparison.OrdinalIgnoreCase))
        {
            return "Compare command IOA with the status feedback IOA. Many RTUs separate operate points and indication points.";
        }

        if (problem.Contains("quality", StringComparison.OrdinalIgnoreCase))
        {
            return "Do not accept a point only because it has a value. Quality flags decide whether the value is usable evidence.";
        }

        if (frames.Count > 0)
        {
            return "Read the frame block top to bottom: request, response, then status. The proof and fix point to the failing protocol layer.";
        }

        return "No frame snippet is available yet. Capture GI, command, and response rows around the symptom to improve the finding.";
    }

    private static string CleanFindingText(string? value, int max = 260)
    {
        var clean = (value ?? string.Empty).ReplaceLineEndings(" ").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = "-";
        }

        return clean.Length <= max || max < 4 ? clean : clean[..(max - 3)] + "...";
    }

    private static string CleanDash(string? value)
    {
        var clean = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "-" : clean;
    }
}
