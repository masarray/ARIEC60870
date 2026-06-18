// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ARIEC60870.Desktop.ViewModels;

namespace ARIEC60870.Desktop.Reporting;

/// <summary>
/// Single source of truth for ARIEC60870 report pagination.
///
/// The engine produces a device-independent layout plan in PDF points. Both the
/// native PDF writer and the WPF FixedDocument preview consume this same plan so
/// headings, page breaks, table widths, row heights and footer placement remain
/// aligned without hosting a browser-based PDF viewer.
/// </summary>
public static class EvidenceReportLayoutEngine
{
    public const int MaxRowsPerSection = 160;
    public const double PageWidth = 842d;   // A4 landscape in PDF points.
    public const double PageHeight = 595d;
    public const double Margin = 30d;
    public const double HeaderBottom = 500d;
    public const double ContentTop = 484d;
    public const double ContentBottom = 55d;
    public const double ContentWidth = PageWidth - (Margin * 2d);

    private static readonly EvidenceReportColor BrandNavy = Color("0F172A");
    private static readonly EvidenceReportColor BrandBlue = Color("2563EB");
    private static readonly EvidenceReportColor SoftBlue = Color("EFF6FF");
    private static readonly EvidenceReportColor SoftSlate = Color("F8FAFC");
    private static readonly EvidenceReportColor Border = Color("DDE7F3");
    private static readonly EvidenceReportColor Muted = Color("64748B");
    private static readonly EvidenceReportColor Ink = Color("111827");
    private static readonly EvidenceReportColor White = Color("FFFFFF");
    private static readonly EvidenceReportColor Pass = Color("15803D");
    private static readonly EvidenceReportColor Attention = Color("B45309");
    private static readonly EvidenceReportColor Fail = Color("B91C1C");
    private static readonly EvidenceReportColor SoftPass = Color("F0FDF4");
    private static readonly EvidenceReportColor SoftAttention = Color("FFFBEB");
    private static readonly EvidenceReportColor SoftFail = Color("FEF2F2");
    private static readonly EvidenceReportColor SoftLine = Color("EEF2F7");

    public static EvidenceReportLayoutPlan Build(EvidencePdfReportModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var builder = new Builder(model);
        return builder.Render();
    }

    private sealed class Builder
    {
        private readonly EvidencePdfReportModel _model;
        private readonly List<PageBuilder> _pages = new();
        private PageBuilder _page = null!;
        private double _cursorY;

        public Builder(EvidencePdfReportModel model)
        {
            _model = model;
        }

        public EvidenceReportLayoutPlan Render()
        {
            NewPage();
            ExecutiveSummary();
            SmartFindingsSection();
            MetricStrip();
            KeyValueCards();
            KeyValueCard("Communication Setup", _model.CommunicationSetup, ContentWidth);
            EvidenceSection("General Interrogation Evidence", "GI request, activation confirmation, activation termination, and related rows.", _model.GiRows);
            EvidenceSection("Command Evidence", "Select/operate, ACTCON/ACTTERM, feedback, and related command rows.", _model.CommandRows);
            EvidenceSection("SOE / Event Evidence", "Spontaneous indications, digital values, timestamps, and quality-related rows.", _model.SoeRows);
            EvidenceSection("Important Protocol Evidence", "Warnings, negative responses, quality issues, mapped values, and other acceptance-critical rows.", _model.ImportantRows);
            AcceptanceNotes();

            var totalPages = _pages.Count;
            for (var i = 0; i < totalPages; i++)
            {
                DrawPageChrome(_pages[i], i + 1, totalPages);
            }

            var pagePlans = _pages
                .Select((page, index) => new EvidenceReportPagePlan(index + 1, PageWidth, PageHeight, page.Commands))
                .ToArray();
            return new EvidenceReportLayoutPlan(PageWidth, PageHeight, _model.ReportId, _model.CreatedLocal, pagePlans);
        }

        private void NewPage()
        {
            _page = new PageBuilder();
            _pages.Add(_page);
            _cursorY = ContentTop;
        }

        private void Ensure(double requiredHeight)
        {
            if (_cursorY - requiredHeight < ContentBottom)
            {
                NewPage();
            }
        }

        private void Space(double value)
        {
            _cursorY -= value;
        }

        private void DrawPageChrome(PageBuilder page, int pageNumber, int totalPages)
        {
            var verdictColor = ResolveVerdictColor(_model.VerdictTone);
            var verdictBackground = ResolveVerdictBackground(_model.VerdictTone);

            page.Line(Margin, HeaderBottom, PageWidth - Margin, HeaderBottom, Border, 0.8d);
            page.Text(Margin, 562d, 360d, "ARIEC60870 Evidence Analyzer", EvidenceReportFontKind.Bold, 7.4d, Muted);
            page.Text(Margin, 541d, 420d, "IEC 60870 Evidence Report", EvidenceReportFontKind.Bold, 20.5d, BrandNavy);
            page.Text(Margin, 523d, 560d, "Commissioning and FAT/SAT protocol evidence for IEC 60870-5-101 / 103 / 104 communication sessions.", EvidenceReportFontKind.Regular, 7.4d, Muted);

            const double cardWidth = 142d;
            const double cardHeight = 58d;
            var cardX = PageWidth - Margin - cardWidth;
            var cardTop = 566d;
            page.RoundRect(cardX, cardTop, cardWidth, cardHeight, 5d, verdictBackground, verdictColor, 0.8d);
            page.Text(cardX + 10d, cardTop - 15d, cardWidth - 20d, "VERDICT", EvidenceReportFontKind.Bold, 6.4d, Muted);
            page.Text(cardX + 10d, cardTop - 35d, cardWidth - 20d, CleanCell(_model.VerdictStatus), EvidenceReportFontKind.Bold, 17d, verdictColor);
            page.Text(cardX + 10d, cardTop - 49d, cardWidth - 20d, Truncate(_model.ReportId, 26), EvidenceReportFontKind.Regular, 5.8d, Muted);

            page.Line(Margin, 42d, PageWidth - Margin, 42d, Border, 0.6d);
            page.Text(Margin, 24d, 600d, "Generated " + _model.CreatedLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  -  SHA256 " + ShortHash(_model.FramesSha256), EvidenceReportFontKind.Regular, 6.4d, Muted);
            page.Text(PageWidth - Margin - 70d, 24d, 70d, $"Page {pageNumber} / {totalPages}", EvidenceReportFontKind.Regular, 6.4d, Muted);
        }

        private void ExecutiveSummary()
        {
            var lines = WrapText(_model.VerdictSummary, ContentWidth - 26d, 8.2d, 3);
            var height = 58d + (lines.Count * 10d);
            Ensure(height + 10d);

            _page.RoundRect(Margin, _cursorY, ContentWidth, height, 5d, SoftSlate, Border, 0.8d);
            _page.Text(Margin + 13d, _cursorY - 18d, ContentWidth - 26d, "Executive Summary", EvidenceReportFontKind.Bold, 11.4d, BrandNavy);
            var y = _cursorY - 35d;
            foreach (var line in lines)
            {
                _page.Text(Margin + 13d, y, ContentWidth - 26d, line, EvidenceReportFontKind.Regular, 8.1d, Ink);
                y -= 10d;
            }

            _cursorY -= height + 12d;
        }


        private void SmartFindingsSection()
        {
            var findings = (_model.SmartFindings ?? Array.Empty<EvidenceSmartFinding>())
                .Take(6)
                .ToArray();
            if (findings.Length == 0)
            {
                return;
            }

            DrawSectionTitle("Smart Findings & Smart Solution", "Cause, proof, and field action extracted from the selected IEC 60870 evidence.", false);

            foreach (var finding in findings)
            {
                var severityColor = ResolveFindingColor(finding.Severity);
                var severityBackground = ResolveFindingBackground(finding.Severity);
                var problemLines = WrapText(finding.Problem, ContentWidth - 126d, 8.4d, 2);
                var whyLines = WrapText(finding.Why, ContentWidth - 74d, 6.7d, 2);
                var evidenceLines = WrapText(finding.Evidence, ContentWidth - 74d, 6.7d, 2);
                var solutionLines = WrapText(finding.Solution, ContentWidth - 74d, 6.9d, 3);
                var bodyLines = whyLines.Count + evidenceLines.Count + solutionLines.Count;
                var height = Math.Max(86d, 40d + (problemLines.Count * 9.5d) + (bodyLines * 8.4d));
                Ensure(height + 9d);

                var top = _cursorY;
                _page.RoundRect(Margin, top, ContentWidth, height, 5d, White, Border, 0.75d);
                _page.Rect(Margin, top, 4d, height, severityColor, severityColor, 0d);
                _page.RoundRect(Margin + 12d, top - 11d, 54d, 15d, 7d, severityBackground, severityColor, 0.55d);
                _page.Text(Margin + 20d, top - 21d, 44d, finding.Severity.ToString().ToUpperInvariant(), EvidenceReportFontKind.Bold, 5.8d, severityColor);
                _page.Text(Margin + ContentWidth - 94d, top - 19d, 82d, "CONF " + CleanCell(finding.Confidence).ToUpperInvariant(), EvidenceReportFontKind.Bold, 5.7d, Muted);

                var y = top - 19d;
                foreach (var line in problemLines)
                {
                    _page.Text(Margin + 74d, y, ContentWidth - 174d, line, EvidenceReportFontKind.Bold, 8.2d, BrandNavy);
                    y -= 9.5d;
                }

                y -= 3d;
                DrawFindingLine("WHY", whyLines, ref y);
                DrawFindingLine("PROOF", evidenceLines, ref y);
                DrawFindingLine("FIX", solutionLines, ref y, BrandBlue);

                _cursorY -= height + 9d;
            }
        }

        private void DrawFindingLine(string label, IReadOnlyList<string> lines, ref double y, EvidenceReportColor? textColor = null)
        {
            _page.Text(Margin + 18d, y, 42d, label, EvidenceReportFontKind.Bold, 5.6d, Muted);
            var bodyX = Margin + 66d;
            var color = textColor ?? Ink;
            foreach (var line in lines)
            {
                _page.Text(bodyX, y, ContentWidth - 82d, line, EvidenceReportFontKind.Regular, 6.65d, color);
                y -= 8.4d;
            }

            y -= 1.8d;
        }

        private void MetricStrip()
        {
            const double gap = 8d;
            var width = (ContentWidth - (gap * 3d)) / 4d;
            Ensure(46d);

            DrawMetric(Margin, width, "Protocol", _model.ProtocolMode);
            DrawMetric(Margin + width + gap, width, "Rows", _model.TotalRows.ToString(CultureInfo.InvariantCulture));
            DrawMetric(Margin + ((width + gap) * 2d), width, "Sequence", _model.FirstSequence + " -> " + _model.LastSequence);
            DrawMetric(Margin + ((width + gap) * 3d), width, "Source", NormalizeWorkspace(_model.SourceWorkspace));
            _cursorY -= 46d;

            void DrawMetric(double x, double w, string label, string value)
            {
                _page.RoundRect(x, _cursorY, w, 36d, 4d, White, Border, 0.7d);
                _page.Text(x + 9d, _cursorY - 13d, w - 18d, label.ToUpperInvariant(), EvidenceReportFontKind.Bold, 5.7d, Muted);
                _page.Text(x + 9d, _cursorY - 27d, w - 18d, Truncate(CleanCell(value), 34), EvidenceReportFontKind.Bold, 9.5d, BrandNavy);
            }
        }

        private void KeyValueCards()
        {
            const double gap = 12d;
            var width = (ContentWidth - gap) / 2d;
            var leftHeight = EstimateKeyValueCardHeight(_model.ReportInfo);
            var rightHeight = EstimateKeyValueCardHeight(_model.SessionCounters);
            var height = Math.Max(leftHeight, rightHeight);
            Ensure(height + 12d);

            DrawKeyValueCard(Margin, _cursorY, width, height, "Report", _model.ReportInfo);
            DrawKeyValueCard(Margin + width + gap, _cursorY, width, height, "Session Counters", _model.SessionCounters);
            _cursorY -= height + 12d;
        }

        private void KeyValueCard(string title, IReadOnlyList<KeyValuePair<string, string>> rows, double width)
        {
            var height = EstimateKeyValueCardHeight(rows);
            Ensure(height + 12d);
            DrawKeyValueCard(Margin, _cursorY, width, height, title, rows);
            _cursorY -= height + 12d;
        }

        private static double EstimateKeyValueCardHeight(IReadOnlyList<KeyValuePair<string, string>> rows)
            => 31d + (Math.Min(rows.Count, 14) * 15d) + (rows.Count > 14 ? 13d : 0d);

        private void DrawKeyValueCard(double x, double top, double width, double height, string title, IReadOnlyList<KeyValuePair<string, string>> rows)
        {
            _page.RoundRect(x, top, width, height, 5d, White, Border, 0.8d);
            _page.Text(x + 10d, top - 18d, width - 20d, title, EvidenceReportFontKind.Bold, 10d, BrandNavy);
            var y = top - 34d;
            foreach (var item in rows.Take(14))
            {
                _page.Line(x + 10d, y + 4d, x + width - 10d, y + 4d, SoftLine, 0.45d);
                _page.Text(x + 10d, y - 4d, 102d, Truncate(item.Key, 24), EvidenceReportFontKind.Bold, 6.3d, Muted);
                _page.Text(x + 118d, y - 4d, width - 132d, Truncate(CleanCell(item.Value), Math.Max(18, (int)((width - 132d) / 3.8d))), EvidenceReportFontKind.Regular, 6.4d, Ink);
                y -= 15d;
            }

            if (rows.Count > 14)
            {
                _page.Text(x + 10d, y - 2d, width - 20d, $"+ {rows.Count - 14} additional fields", EvidenceReportFontKind.Regular, 6.2d, Muted);
            }
        }

        private void EvidenceSection(string title, string description, IReadOnlyList<EvidenceRow> rows)
        {
            Ensure(66d);
            DrawSectionTitle(title, description, false);

            if (rows.Count == 0)
            {
                Ensure(36d);
                _page.RoundRect(Margin, _cursorY, ContentWidth, 30d, 4d, SoftSlate, Border, 0.6d);
                _page.Text(Margin + 10d, _cursorY - 19d, ContentWidth - 20d, "No matching evidence rows in the selected report scope.", EvidenceReportFontKind.Regular, 7.4d, Muted);
                _cursorY -= 42d;
                return;
            }

            var displayedRows = rows.Take(MaxRowsPerSection).ToArray();
            DrawEvidenceTableHeader();

            foreach (var row in displayedRows)
            {
                var cells = BuildEvidenceCells(row);
                var rowHeight = EstimateTableRowHeight(cells);
                if (_cursorY - rowHeight < ContentBottom)
                {
                    NewPage();
                    DrawSectionTitle(title + " (continued)", description, true);
                    DrawEvidenceTableHeader();
                }

                DrawEvidenceRow(cells, rowHeight);
            }

            if (rows.Count > displayedRows.Length)
            {
                Ensure(20d);
                _page.Text(Margin, _cursorY - 4d, ContentWidth, $"Showing first {displayedRows.Length} rows from {rows.Count} matching rows. Use .ariec capture for full replay evidence.", EvidenceReportFontKind.Regular, 6.4d, Muted);
                _cursorY -= 20d;
            }

            Space(10d);
        }

        private void DrawSectionTitle(string title, string description, bool continued)
        {
            Ensure(48d);
            var top = _cursorY;
            _page.RoundRect(Margin, top, ContentWidth, 44d, 5d, White, Border, 0.7d);
            _page.Rect(Margin, top, 4d, 44d, BrandBlue, BrandBlue, 0d);
            _page.Text(Margin + 13d, top - 17d, ContentWidth - 26d, title, EvidenceReportFontKind.Bold, continued ? 9.6d : 10.5d, BrandNavy);
            _page.Text(Margin + 13d, top - 32d, ContentWidth - 26d, description, EvidenceReportFontKind.Regular, 6.6d, Muted);
            _cursorY -= 52d;
        }

        private void DrawEvidenceTableHeader()
        {
            Ensure(22d);
            var widths = EvidenceColumnWidths();
            var headers = new[] { "#", "Time", "Dir", "Service", "CA / IOA", "Type / COT", "Quality", "Meaning" };
            var x = Margin;
            const double height = 18d;
            for (var i = 0; i < headers.Length; i++)
            {
                _page.Rect(x, _cursorY, widths[i], height, SoftBlue, Border, 0.45d);
                _page.Text(x + 4d, _cursorY - 12d, widths[i] - 8d, headers[i], EvidenceReportFontKind.Bold, 5.8d, BrandBlue);
                x += widths[i];
            }

            _cursorY -= height;
        }

        private void DrawEvidenceRow(IReadOnlyList<EvidenceCell> cells, double rowHeight)
        {
            var widths = EvidenceColumnWidths();
            var x = Margin;
            for (var i = 0; i < cells.Count; i++)
            {
                _page.Rect(x, _cursorY, widths[i], rowHeight, White, SoftLine, 0.35d);
                var lines = WrapText(cells[i].Text, widths[i] - 8d, cells[i].Size, cells[i].MaxLines);
                var y = _cursorY - 9d;
                foreach (var line in lines)
                {
                    _page.Text(x + 4d, y, widths[i] - 8d, line, cells[i].Font, cells[i].Size, cells[i].Color);
                    y -= cells[i].Size + 1.4d;
                }

                x += widths[i];
            }

            _cursorY -= rowHeight;
        }

        private static double[] EvidenceColumnWidths()
            => new[] { 32d, 58d, 32d, 100d, 66d, 88d, 70d, ContentWidth - 446d };

        private static EvidenceCell[] BuildEvidenceCells(EvidenceRow row)
        {
            var direction = CleanCell(row.Direction);
            var directionColor = direction.Equals("TX", StringComparison.OrdinalIgnoreCase) ? Color("1D4ED8")
                : direction.Equals("RX", StringComparison.OrdinalIgnoreCase) ? Color("047857")
                : Muted;

            return new[]
            {
                new EvidenceCell(row.Sequence.ToString(CultureInfo.InvariantCulture), EvidenceReportFontKind.Mono, 6.0d, Ink, 1),
                new EvidenceCell(row.Time, EvidenceReportFontKind.Mono, 6.0d, Ink, 1),
                new EvidenceCell(direction, EvidenceReportFontKind.Bold, 6.1d, directionColor, 1),
                new EvidenceCell(row.ProtocolService, EvidenceReportFontKind.Regular, 6.0d, Ink, 2),
                new EvidenceCell(row.CommonAddress + " / " + row.IoAddress, EvidenceReportFontKind.Mono, 6.0d, Ink, 2),
                new EvidenceCell(row.TypeId + " / " + row.CotDisplay, EvidenceReportFontKind.Regular, 6.0d, Ink, 2),
                new EvidenceCell(row.Quality, EvidenceReportFontKind.Regular, 6.0d, Ink, 2),
                new EvidenceCell(FirstMeaning(row), EvidenceReportFontKind.Regular, 6.0d, Ink, 3),
            };
        }

        private static double EstimateTableRowHeight(IReadOnlyList<EvidenceCell> cells)
        {
            var widths = EvidenceColumnWidths();
            var maxLines = 1;
            for (var i = 0; i < cells.Count; i++)
            {
                maxLines = Math.Max(maxLines, WrapText(cells[i].Text, widths[i] - 8d, cells[i].Size, cells[i].MaxLines).Count);
            }

            return Math.Max(18d, 8d + (maxLines * 7.8d));
        }

        private void AcceptanceNotes()
        {
            var note = "Review the evidence against the approved FAT/SAT procedure, relay manual, gateway interoperability list, and project signal mapping. Keep the .ariec capture file when replayable evidence is required.";
            var lines = WrapText(note, ContentWidth - 22d, 7.3d, 4);
            var height = 34d + (lines.Count * 9.5d);
            Ensure(height + 8d);
            _page.RoundRect(Margin, _cursorY, ContentWidth, height, 5d, SoftSlate, Border, 0.7d);
            _page.Text(Margin + 11d, _cursorY - 17d, ContentWidth - 22d, "Acceptance Notes", EvidenceReportFontKind.Bold, 10d, BrandNavy);
            var y = _cursorY - 33d;
            foreach (var line in lines)
            {
                _page.Text(Margin + 11d, y, ContentWidth - 22d, line, EvidenceReportFontKind.Regular, 7.2d, Ink);
                y -= 9.5d;
            }

            _cursorY -= height + 8d;
        }
    }

    private sealed record EvidenceCell(string Text, EvidenceReportFontKind Font, double Size, EvidenceReportColor Color, int MaxLines);

    private sealed class PageBuilder
    {
        private readonly List<EvidenceReportDrawCommand> _commands = new();
        public IReadOnlyList<EvidenceReportDrawCommand> Commands => _commands;

        public void Text(double x, double baselineY, double width, string text, EvidenceReportFontKind font, double size, EvidenceReportColor color)
        {
            var safe = SanitizeReportText(text);
            if (safe.Length == 0)
            {
                return;
            }

            _commands.Add(new EvidenceReportTextCommand(x, baselineY, width, safe, font, size, color));
        }

        public void Line(double x1, double y1, double x2, double y2, EvidenceReportColor stroke, double width)
            => _commands.Add(new EvidenceReportLineCommand(x1, y1, x2, y2, stroke, width));

        public void Rect(double x, double top, double width, double height, EvidenceReportColor fill, EvidenceReportColor stroke, double lineWidth)
            => _commands.Add(new EvidenceReportRectCommand(x, top, width, height, 0d, fill, stroke, lineWidth));

        public void RoundRect(double x, double top, double width, double height, double radius, EvidenceReportColor fill, EvidenceReportColor stroke, double lineWidth)
            => _commands.Add(new EvidenceReportRectCommand(x, top, width, height, radius, fill, stroke, lineWidth));
    }


    private static EvidenceReportColor ResolveFindingColor(EvidenceSmartFindingSeverity severity)
        => severity switch
        {
            EvidenceSmartFindingSeverity.Error => Fail,
            EvidenceSmartFindingSeverity.Warning => Attention,
            _ => BrandBlue
        };

    private static EvidenceReportColor ResolveFindingBackground(EvidenceSmartFindingSeverity severity)
        => severity switch
        {
            EvidenceSmartFindingSeverity.Error => SoftFail,
            EvidenceSmartFindingSeverity.Warning => SoftAttention,
            _ => SoftBlue
        };

    private static EvidenceReportColor ResolveVerdictColor(string tone)
        => tone.Equals("pass", StringComparison.OrdinalIgnoreCase) ? Pass
            : tone.Equals("fail", StringComparison.OrdinalIgnoreCase) ? Fail
            : Attention;

    private static EvidenceReportColor ResolveVerdictBackground(string tone)
        => tone.Equals("pass", StringComparison.OrdinalIgnoreCase) ? SoftPass
            : tone.Equals("fail", StringComparison.OrdinalIgnoreCase) ? SoftFail
            : SoftAttention;

    private static string NormalizeWorkspace(string value)
        => value switch
        {
            "ProtocolTrace" => "Trace",
            "EvidenceSummary" => "Evidence Ledger",
            "CurrentEvidenceBuffer" => "Current Evidence",
            _ => CleanCell(value)
        };

    private static string FirstMeaning(EvidenceRow row)
    {
        var value = !string.IsNullOrWhiteSpace(row.ProtocolTraceMeaning) ? row.ProtocolTraceMeaning
            : !string.IsNullOrWhiteSpace(row.ReadableMeaning) ? row.ReadableMeaning
            : !string.IsNullOrWhiteSpace(row.Detail) ? row.Detail
            : row.Summary;
        return CleanCell(value);
    }

    private static string CleanCell(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "-" : normalized;
    }

    private static string ShortHash(string value)
    {
        var clean = CleanCell(value);
        return clean.Length <= 16 ? clean : clean[..16];
    }

    private static string Truncate(string? value, int max)
    {
        var clean = CleanCell(value);
        if (max <= 3 || clean.Length <= max)
        {
            return clean;
        }

        return clean[..(max - 3)] + "...";
    }

    private static IReadOnlyList<string> WrapText(string? value, double width, double fontSize, int maxLines)
    {
        var clean = SanitizeReportText(CleanCell(value));
        if (clean == "-")
        {
            return new[] { "-" };
        }

        var charsPerLine = Math.Max(8, (int)Math.Floor(width / Math.Max(2.5d, fontSize * 0.48d)));
        var words = clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in words)
        {
            var piece = word.Length > charsPerLine ? word[..charsPerLine] : word;
            if (current.Length == 0)
            {
                current.Append(piece);
            }
            else if (current.Length + 1 + piece.Length <= charsPerLine)
            {
                current.Append(' ').Append(piece);
            }
            else
            {
                lines.Add(current.ToString());
                current.Clear().Append(piece);
            }

            if (lines.Count == maxLines)
            {
                break;
            }
        }

        if (current.Length > 0 && lines.Count < maxLines)
        {
            lines.Add(current.ToString());
        }

        if (lines.Count == 0)
        {
            lines.Add("-");
        }

        if (words.Length > 0 && lines.Count == maxLines)
        {
            var joinedLength = lines.Sum(line => line.Length);
            if (joinedLength < clean.Length && lines[^1].Length > 3)
            {
                lines[^1] = lines[^1][..Math.Max(0, lines[^1].Length - 3)] + "...";
            }
        }

        return lines;
    }

    private static string SanitizeReportText(string? value)
    {
        var input = CleanCell(value);
        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            builder.Append(ch switch
            {
                '\u2013' or '\u2014' or '\u2212' => '-',
                '\u2192' => '>',
                '\u2190' => '<',
                '\u00A0' => ' ',
                >= ' ' and <= '~' => ch,
                _ => ' '
            });
        }

        return builder.ToString().Trim();
    }

    private static EvidenceReportColor Color(string hex) => EvidenceReportColor.FromHex(hex);
}
