// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ARIEC60870.Desktop.ViewModels;

namespace ARIEC60870.Desktop.Reporting;

/// <summary>
/// Dependency-free PDF evidence report writer for ARIEC60870.
///
/// The implementation intentionally uses only the small PDF surface required by
/// this application: built-in Type 1 fonts, vector rectangles/lines, paged text,
/// and simple tables. This keeps the desktop app Apache-2.0 clean and avoids a
/// heavyweight HTML-to-PDF or third-party PDF layout dependency for FAT/SAT
/// evidence reports.
/// </summary>
public static class EvidencePdfReportService
{
    private const int MaxRowsPerSection = 160;
    private const float PageWidth = 842f;   // A4 landscape in PDF points.
    private const float PageHeight = 595f;
    private const float Margin = 30f;
    private const float HeaderBottom = 500f;
    private const float ContentTop = 484f;
    private const float ContentBottom = 55f;
    private const float ContentWidth = PageWidth - (Margin * 2f);

    private static readonly PdfColor BrandNavy = PdfColor.FromHex("0F172A");
    private static readonly PdfColor BrandBlue = PdfColor.FromHex("2563EB");
    private static readonly PdfColor SoftBlue = PdfColor.FromHex("EFF6FF");
    private static readonly PdfColor SoftSlate = PdfColor.FromHex("F8FAFC");
    private static readonly PdfColor Border = PdfColor.FromHex("DDE7F3");
    private static readonly PdfColor Muted = PdfColor.FromHex("64748B");
    private static readonly PdfColor Ink = PdfColor.FromHex("111827");
    private static readonly PdfColor White = PdfColor.FromHex("FFFFFF");
    private static readonly PdfColor Pass = PdfColor.FromHex("15803D");
    private static readonly PdfColor Attention = PdfColor.FromHex("B45309");
    private static readonly PdfColor Fail = PdfColor.FromHex("B91C1C");
    private static readonly PdfColor SoftPass = PdfColor.FromHex("F0FDF4");
    private static readonly PdfColor SoftAttention = PdfColor.FromHex("FFFBEB");
    private static readonly PdfColor SoftFail = PdfColor.FromHex("FEF2F2");

    public static void Save(string fileName, EvidencePdfReportModel model)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("PDF output path is required.", nameof(fileName));
        }

        ArgumentNullException.ThrowIfNull(model);

        var pages = new EvidencePdfRenderer(model).Render();
        NativePdfDocument.Write(fileName, pages, model);
    }

    private sealed class EvidencePdfRenderer
    {
        private readonly EvidencePdfReportModel _model;
        private readonly List<PdfPageBuffer> _pages = new();
        private PdfPageBuffer _page = null!;
        private float _cursorY;

        public EvidencePdfRenderer(EvidencePdfReportModel model)
        {
            _model = model;
        }

        public IReadOnlyList<PdfPageBuffer> Render()
        {
            NewPage();
            ExecutiveSummary();
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

            return _pages;
        }

        private void NewPage()
        {
            _page = new PdfPageBuffer(PageWidth, PageHeight);
            _pages.Add(_page);
            _cursorY = ContentTop;
        }

        private void Ensure(float requiredHeight)
        {
            if (_cursorY - requiredHeight < ContentBottom)
            {
                NewPage();
            }
        }

        private void Space(float value)
        {
            _cursorY -= value;
        }

        private void DrawPageChrome(PdfPageBuffer page, int pageNumber, int totalPages)
        {
            var verdictColor = ResolveVerdictColor(_model.VerdictTone);
            var verdictBackground = ResolveVerdictBackground(_model.VerdictTone);

            page.Line(Margin, HeaderBottom, PageWidth - Margin, HeaderBottom, Border, 0.8f);
            page.Text(Margin, 562f, "ARIEC60870 Evidence Analyzer", PdfFont.Bold, 7.4f, Muted);
            page.Text(Margin, 541f, "IEC 60870 Evidence Report", PdfFont.Bold, 20.5f, BrandNavy);
            page.Text(Margin, 523f, "Commissioning and FAT/SAT protocol evidence for IEC 60870-5-101 / 103 / 104 communication sessions.", PdfFont.Regular, 7.4f, Muted);

            const float cardWidth = 142f;
            const float cardHeight = 58f;
            var cardX = PageWidth - Margin - cardWidth;
            var cardTop = 566f;
            page.RoundRect(cardX, cardTop, cardWidth, cardHeight, 5f, verdictBackground, verdictColor, 0.8f);
            page.Text(cardX + 10f, cardTop - 15f, "VERDICT", PdfFont.Bold, 6.4f, Muted);
            page.Text(cardX + 10f, cardTop - 35f, CleanCell(_model.VerdictStatus), PdfFont.Bold, 17f, verdictColor);
            page.Text(cardX + 10f, cardTop - 49f, Truncate(_model.ReportId, 26), PdfFont.Regular, 5.8f, Muted);

            page.Line(Margin, 42f, PageWidth - Margin, 42f, Border, 0.6f);
            page.Text(Margin, 24f, "Generated " + _model.CreatedLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  -  SHA256 " + ShortHash(_model.FramesSha256), PdfFont.Regular, 6.4f, Muted);
            page.Text(PageWidth - Margin - 70f, 24f, $"Page {pageNumber} / {totalPages}", PdfFont.Regular, 6.4f, Muted);
        }

        private void ExecutiveSummary()
        {
            var lines = WrapText(_model.VerdictSummary, ContentWidth - 26f, 8.2f, 3);
            var height = 58f + (lines.Count * 10f);
            Ensure(height + 10f);

            _page.RoundRect(Margin, _cursorY, ContentWidth, height, 5f, SoftSlate, Border, 0.8f);
            _page.Text(Margin + 13f, _cursorY - 18f, "Executive Summary", PdfFont.Bold, 11.4f, BrandNavy);
            var y = _cursorY - 35f;
            foreach (var line in lines)
            {
                _page.Text(Margin + 13f, y, line, PdfFont.Regular, 8.1f, Ink);
                y -= 10f;
            }

            _cursorY -= height + 12f;
        }

        private void MetricStrip()
        {
            const float gap = 8f;
            var width = (ContentWidth - (gap * 3f)) / 4f;
            Ensure(46f);

            DrawMetric(Margin, width, "Protocol", _model.ProtocolMode);
            DrawMetric(Margin + width + gap, width, "Rows", _model.TotalRows.ToString(CultureInfo.InvariantCulture));
            DrawMetric(Margin + ((width + gap) * 2f), width, "Sequence", _model.FirstSequence + " -> " + _model.LastSequence);
            DrawMetric(Margin + ((width + gap) * 3f), width, "Source", NormalizeWorkspace(_model.SourceWorkspace));
            _cursorY -= 46f;

            void DrawMetric(float x, float w, string label, string value)
            {
                _page.RoundRect(x, _cursorY, w, 36f, 4f, White, Border, 0.7f);
                _page.Text(x + 9f, _cursorY - 13f, label.ToUpperInvariant(), PdfFont.Bold, 5.7f, Muted);
                _page.Text(x + 9f, _cursorY - 27f, Truncate(CleanCell(value), 34), PdfFont.Bold, 9.5f, BrandNavy);
            }
        }

        private void KeyValueCards()
        {
            const float gap = 12f;
            var width = (ContentWidth - gap) / 2f;
            var leftHeight = EstimateKeyValueCardHeight(_model.ReportInfo);
            var rightHeight = EstimateKeyValueCardHeight(_model.SessionCounters);
            var height = Math.Max(leftHeight, rightHeight);
            Ensure(height + 12f);

            DrawKeyValueCard(Margin, _cursorY, width, height, "Report", _model.ReportInfo);
            DrawKeyValueCard(Margin + width + gap, _cursorY, width, height, "Session Counters", _model.SessionCounters);
            _cursorY -= height + 12f;
        }

        private void KeyValueCard(string title, IReadOnlyList<KeyValuePair<string, string>> rows, float width)
        {
            var height = EstimateKeyValueCardHeight(rows);
            Ensure(height + 12f);
            DrawKeyValueCard(Margin, _cursorY, width, height, title, rows);
            _cursorY -= height + 12f;
        }

        private static float EstimateKeyValueCardHeight(IReadOnlyList<KeyValuePair<string, string>> rows)
            => 31f + (Math.Min(rows.Count, 14) * 15f) + (rows.Count > 14 ? 13f : 0f);

        private void DrawKeyValueCard(float x, float top, float width, float height, string title, IReadOnlyList<KeyValuePair<string, string>> rows)
        {
            _page.RoundRect(x, top, width, height, 5f, White, Border, 0.8f);
            _page.Text(x + 10f, top - 18f, title, PdfFont.Bold, 10f, BrandNavy);
            var y = top - 34f;
            foreach (var item in rows.Take(14))
            {
                _page.Line(x + 10f, y + 4f, x + width - 10f, y + 4f, PdfColor.FromHex("EEF2F7"), 0.45f);
                _page.Text(x + 10f, y - 4f, Truncate(item.Key, 24), PdfFont.Bold, 6.3f, Muted);
                _page.Text(x + 118f, y - 4f, Truncate(CleanCell(item.Value), Math.Max(18, (int)((width - 132f) / 3.8f))), PdfFont.Regular, 6.4f, Ink);
                y -= 15f;
            }

            if (rows.Count > 14)
            {
                _page.Text(x + 10f, y - 2f, $"+ {rows.Count - 14} additional fields", PdfFont.Regular, 6.2f, Muted);
            }
        }

        private void EvidenceSection(string title, string description, IReadOnlyList<EvidenceRow> rows)
        {
            Ensure(66f);
            DrawSectionTitle(title, description, false);

            if (rows.Count == 0)
            {
                Ensure(36f);
                _page.RoundRect(Margin, _cursorY, ContentWidth, 30f, 4f, SoftSlate, Border, 0.6f);
                _page.Text(Margin + 10f, _cursorY - 19f, "No matching evidence rows in the selected report scope.", PdfFont.Regular, 7.4f, Muted);
                _cursorY -= 42f;
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
                Ensure(20f);
                _page.Text(Margin, _cursorY - 4f, $"Showing first {displayedRows.Length} rows from {rows.Count} matching rows. Use .ariec capture for full replay evidence.", PdfFont.Regular, 6.4f, Muted);
                _cursorY -= 20f;
            }

            Space(10f);
        }

        private void DrawSectionTitle(string title, string description, bool continued)
        {
            Ensure(48f);
            var top = _cursorY;
            _page.RoundRect(Margin, top, ContentWidth, 44f, 5f, White, Border, 0.7f);
            _page.Rect(Margin, top, 4f, 44f, BrandBlue, BrandBlue, 0f);
            _page.Text(Margin + 13f, top - 17f, title, PdfFont.Bold, continued ? 9.6f : 10.5f, BrandNavy);
            _page.Text(Margin + 13f, top - 32f, description, PdfFont.Regular, 6.6f, Muted);
            _cursorY -= 52f;
        }

        private void DrawEvidenceTableHeader()
        {
            Ensure(22f);
            var widths = EvidenceColumnWidths();
            var headers = new[] { "#", "Time", "Dir", "Service", "CA / IOA", "Type / COT", "Quality", "Meaning" };
            var x = Margin;
            const float height = 18f;
            for (var i = 0; i < headers.Length; i++)
            {
                _page.Rect(x, _cursorY, widths[i], height, SoftBlue, Border, 0.45f);
                _page.Text(x + 4f, _cursorY - 12f, headers[i], PdfFont.Bold, 5.8f, BrandBlue);
                x += widths[i];
            }

            _cursorY -= height;
        }

        private void DrawEvidenceRow(EvidenceCell[] cells, float rowHeight)
        {
            var widths = EvidenceColumnWidths();
            var x = Margin;
            for (var i = 0; i < cells.Length; i++)
            {
                _page.Rect(x, _cursorY, widths[i], rowHeight, White, PdfColor.FromHex("EEF2F7"), 0.35f);
                var lines = WrapText(cells[i].Text, widths[i] - 8f, cells[i].Size, cells[i].MaxLines);
                var y = _cursorY - 9f;
                foreach (var line in lines)
                {
                    _page.Text(x + 4f, y, line, cells[i].Font, cells[i].Size, cells[i].Color);
                    y -= cells[i].Size + 1.4f;
                }

                x += widths[i];
            }

            _cursorY -= rowHeight;
        }

        private static float[] EvidenceColumnWidths()
            => new[] { 32f, 58f, 32f, 100f, 66f, 88f, 70f, ContentWidth - 446f };

        private static EvidenceCell[] BuildEvidenceCells(EvidenceRow row)
        {
            var direction = CleanCell(row.Direction);
            var directionColor = direction.Equals("TX", StringComparison.OrdinalIgnoreCase) ? PdfColor.FromHex("1D4ED8")
                : direction.Equals("RX", StringComparison.OrdinalIgnoreCase) ? PdfColor.FromHex("047857")
                : Muted;

            return new[]
            {
                new EvidenceCell(row.Sequence.ToString(CultureInfo.InvariantCulture), PdfFont.Mono, 6.0f, Ink, 1),
                new EvidenceCell(row.Time, PdfFont.Mono, 6.0f, Ink, 1),
                new EvidenceCell(direction, PdfFont.Bold, 6.1f, directionColor, 1),
                new EvidenceCell(row.ProtocolService, PdfFont.Regular, 6.0f, Ink, 2),
                new EvidenceCell(row.CommonAddress + " / " + row.IoAddress, PdfFont.Mono, 6.0f, Ink, 2),
                new EvidenceCell(row.TypeId + " / " + row.CotDisplay, PdfFont.Regular, 6.0f, Ink, 2),
                new EvidenceCell(row.Quality, PdfFont.Regular, 6.0f, Ink, 2),
                new EvidenceCell(FirstMeaning(row), PdfFont.Regular, 6.0f, Ink, 3),
            };
        }

        private static float EstimateTableRowHeight(IReadOnlyList<EvidenceCell> cells)
        {
            var widths = EvidenceColumnWidths();
            var maxLines = 1;
            for (var i = 0; i < cells.Count; i++)
            {
                maxLines = Math.Max(maxLines, WrapText(cells[i].Text, widths[i] - 8f, cells[i].Size, cells[i].MaxLines).Count);
            }

            return Math.Max(18f, 8f + (maxLines * 7.8f));
        }

        private void AcceptanceNotes()
        {
            var note = "Review the evidence against the approved FAT/SAT procedure, relay manual, gateway interoperability list, and project signal mapping. Keep the .ariec capture file when replayable evidence is required.";
            var lines = WrapText(note, ContentWidth - 22f, 7.3f, 4);
            var height = 34f + (lines.Count * 9.5f);
            Ensure(height + 8f);
            _page.RoundRect(Margin, _cursorY, ContentWidth, height, 5f, SoftSlate, Border, 0.7f);
            _page.Text(Margin + 11f, _cursorY - 17f, "Acceptance Notes", PdfFont.Bold, 10f, BrandNavy);
            var y = _cursorY - 33f;
            foreach (var line in lines)
            {
                _page.Text(Margin + 11f, y, line, PdfFont.Regular, 7.2f, Ink);
                y -= 9.5f;
            }

            _cursorY -= height + 8f;
        }
    }

    private sealed record EvidenceCell(string Text, PdfFont Font, float Size, PdfColor Color, int MaxLines);

    private sealed class PdfPageBuffer
    {
        private readonly StringBuilder _operations = new();

        public PdfPageBuffer(float width, float height)
        {
            Width = width;
            Height = height;
        }

        public float Width { get; }
        public float Height { get; }
        public string Content => _operations.ToString();

        public void Text(float x, float baselineY, string text, PdfFont font, float size, PdfColor color)
        {
            var safe = SanitizePdfText(text);
            if (safe.Length == 0)
            {
                return;
            }

            _operations.Append("BT ");
            _operations.Append(color.FillOperation());
            _operations.Append(' ');
            _operations.Append('/').Append(font.ResourceName()).Append(' ').Append(Number(size)).Append(" Tf ");
            _operations.Append("1 0 0 1 ").Append(Number(x)).Append(' ').Append(Number(baselineY)).Append(" Tm ");
            _operations.Append('(').Append(EscapeLiteral(safe)).Append(") Tj ET\n");
        }

        public void Line(float x1, float y1, float x2, float y2, PdfColor stroke, float width)
        {
            _operations.Append(Number(width)).Append(" w ");
            _operations.Append(stroke.StrokeOperation()).Append(' ');
            _operations.Append(Number(x1)).Append(' ').Append(Number(y1)).Append(" m ");
            _operations.Append(Number(x2)).Append(' ').Append(Number(y2)).Append(" l S\n");
        }

        public void Rect(float x, float top, float width, float height, PdfColor fill, PdfColor stroke, float lineWidth)
        {
            var y = top - height;
            if (lineWidth <= 0f || fill.Equals(stroke))
            {
                _operations.Append(fill.FillOperation()).Append(' ');
                _operations.Append(Number(x)).Append(' ').Append(Number(y)).Append(' ').Append(Number(width)).Append(' ').Append(Number(height)).Append(" re f\n");
                return;
            }

            _operations.Append(Number(lineWidth)).Append(" w ");
            _operations.Append(fill.FillOperation()).Append(' ');
            _operations.Append(stroke.StrokeOperation()).Append(' ');
            _operations.Append(Number(x)).Append(' ').Append(Number(y)).Append(' ').Append(Number(width)).Append(' ').Append(Number(height)).Append(" re B\n");
        }

        public void RoundRect(float x, float top, float width, float height, float radius, PdfColor fill, PdfColor stroke, float lineWidth)
        {
            // Lightweight approximation: use a normal rectangle for maximum reader compatibility.
            _ = radius;
            Rect(x, top, width, height, fill, stroke, lineWidth);
        }
    }

    private static class NativePdfDocument
    {
        public static void Write(string fileName, IReadOnlyList<PdfPageBuffer> pages, EvidencePdfReportModel model)
        {
            if (pages.Count == 0)
            {
                throw new InvalidOperationException("At least one PDF page is required.");
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(fileName));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var objects = new List<byte[]>();
            int AddObject(string body)
            {
                objects.Add(Encoding.ASCII.GetBytes(body));
                return objects.Count;
            }

            var catalogId = AddObject("<< /Type /Catalog /Pages 2 0 R >>");
            var pagesId = AddObject("__PAGES__");
            var fontRegularId = AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            var fontBoldId = AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
            var fontMonoId = AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
            var pageIds = new List<int>();

            foreach (var page in pages)
            {
                var contentBytes = Encoding.ASCII.GetBytes(page.Content);
                var contentStream = "<< /Length " + contentBytes.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n" + page.Content + "endstream";
                var contentId = AddObject(contentStream);
                var pageId = AddObject("<< /Type /Page /Parent " + pagesId.ToString(CultureInfo.InvariantCulture) + " 0 R /MediaBox [0 0 " + Number(page.Width) + " " + Number(page.Height) + "] /Resources << /Font << /F1 " + fontRegularId.ToString(CultureInfo.InvariantCulture) + " 0 R /F2 " + fontBoldId.ToString(CultureInfo.InvariantCulture) + " 0 R /F3 " + fontMonoId.ToString(CultureInfo.InvariantCulture) + " 0 R >> >> /Contents " + contentId.ToString(CultureInfo.InvariantCulture) + " 0 R >>");
                pageIds.Add(pageId);
            }

            var infoId = AddObject("<< /Title (" + EscapeLiteral(SanitizePdfText("ARIEC60870 IEC 60870 Evidence Report")) + ") /Author (" + EscapeLiteral(SanitizePdfText("ARIEC60870")) + ") /Creator (" + EscapeLiteral(SanitizePdfText("ARIEC60870 Native PDF Engine")) + ") /Producer (" + EscapeLiteral(SanitizePdfText("ARIEC60870 Native PDF Engine")) + ") /CreationDate (" + PdfDate(model.CreatedLocal) + ") >>");
            objects[pagesId - 1] = Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [" + string.Join(" ", pageIds.Select(id => id.ToString(CultureInfo.InvariantCulture) + " 0 R")) + "] /Count " + pageIds.Count.ToString(CultureInfo.InvariantCulture) + " >>");

            using var stream = new MemoryStream();
            WriteAscii(stream, "%PDF-1.4\n%\u00E2\u00E3\u00CF\u00D3\n");
            var offsets = new long[objects.Count + 1];
            for (var i = 0; i < objects.Count; i++)
            {
                offsets[i + 1] = stream.Position;
                WriteAscii(stream, (i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
                stream.Write(objects[i], 0, objects[i].Length);
                WriteAscii(stream, "\nendobj\n");
            }

            var xrefOffset = stream.Position;
            WriteAscii(stream, "xref\n0 " + (objects.Count + 1).ToString(CultureInfo.InvariantCulture) + "\n");
            WriteAscii(stream, "0000000000 65535 f \n");
            for (var i = 1; i < offsets.Length; i++)
            {
                WriteAscii(stream, offsets[i].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
            }

            WriteAscii(stream, "trailer\n<< /Size " + (objects.Count + 1).ToString(CultureInfo.InvariantCulture) + " /Root " + catalogId.ToString(CultureInfo.InvariantCulture) + " 0 R /Info " + infoId.ToString(CultureInfo.InvariantCulture) + " 0 R >>\nstartxref\n" + xrefOffset.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
            File.WriteAllBytes(fileName, stream.ToArray());
        }

        private static void WriteAscii(Stream stream, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    private readonly struct PdfColor : IEquatable<PdfColor>
    {
        public PdfColor(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public float R { get; }
        public float G { get; }
        public float B { get; }

        public static PdfColor FromHex(string hex)
        {
            if (hex.StartsWith("#", StringComparison.Ordinal))
            {
                hex = hex[1..];
            }

            if (hex.Length != 6)
            {
                throw new ArgumentException("PDF color must be a six-digit RGB hex value.", nameof(hex));
            }

            return new PdfColor(
                Convert.ToInt32(hex[..2], 16) / 255f,
                Convert.ToInt32(hex.Substring(2, 2), 16) / 255f,
                Convert.ToInt32(hex.Substring(4, 2), 16) / 255f);
        }

        public string FillOperation() => Number(R) + " " + Number(G) + " " + Number(B) + " rg";
        public string StrokeOperation() => Number(R) + " " + Number(G) + " " + Number(B) + " RG";

        public bool Equals(PdfColor other)
            => Math.Abs(R - other.R) < 0.0001f && Math.Abs(G - other.G) < 0.0001f && Math.Abs(B - other.B) < 0.0001f;

        public override bool Equals(object? obj) => obj is PdfColor other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(R, G, B);
    }

    private enum PdfFont
    {
        Regular,
        Bold,
        Mono
    }

    private static string ResourceName(this PdfFont font)
        => font switch
        {
            PdfFont.Bold => "F2",
            PdfFont.Mono => "F3",
            _ => "F1"
        };

    private static PdfColor ResolveVerdictColor(string tone)
        => tone.Equals("pass", StringComparison.OrdinalIgnoreCase) ? Pass
            : tone.Equals("fail", StringComparison.OrdinalIgnoreCase) ? Fail
            : Attention;

    private static PdfColor ResolveVerdictBackground(string tone)
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

    private static IReadOnlyList<string> WrapText(string? value, float width, float fontSize, int maxLines)
    {
        var clean = SanitizePdfText(CleanCell(value));
        if (clean == "-")
        {
            return new[] { "-" };
        }

        var charsPerLine = Math.Max(8, (int)Math.Floor(width / Math.Max(2.5f, fontSize * 0.48f)));
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

    private static string SanitizePdfText(string? value)
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

    private static string EscapeLiteral(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static string PdfDate(DateTime value)
        => "D:" + value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private static string Number(float value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);
}
