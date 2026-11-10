// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Plain-text copy of precisely the bound Raw Frames row fields. No independent
/// decoder, no evidence reinterpretation, no hidden fourth signal expanded.
/// Full forensic evidence remains available via the separate Copy Full Evidence.
/// </summary>
public static class FrameTraceRowClipboardFormatter
{
    public static string Format(IEnumerable<EvidenceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return string.Join(Environment.NewLine + Environment.NewLine,
            rows.Select(FormatOne));
    }

    public static string FormatOne(EvidenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var lines = new List<string>
        {
            row.Sequence + "  " + row.Time + "  " + row.Direction,
            row.TimelineHex,
            row.TimelineTitle
        };
        if (row.HasTimelineSignalDigest) lines.Add(row.TimelineSignalDigest);
        if (row.HasTimelineSignalOverflow) lines.Add(row.TimelineSignalOverflow);
        if (!string.IsNullOrWhiteSpace(row.TimelineEngineeringIdentity))
            lines.Add(row.TimelineEngineeringIdentity);
        if (!string.IsNullOrWhiteSpace(row.TimelineEngineeringCause))
            lines.Add(row.TimelineEngineeringCause);
        if (row.HasTimelineAcdPending) lines.Add(row.TimelineAcdPending);
        if (row.HasTimelineDiagnosticAlerts) lines.Add(row.TimelineDiagnosticAlerts);
        return string.Join(Environment.NewLine, lines);
    }
}
