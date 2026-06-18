// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ARIEC60870.Desktop.Reporting;

/// <summary>
/// Dependency-free PDF evidence report writer for ARIEC60870.
///
/// The implementation intentionally uses only the small PDF surface required by
/// this application: built-in Type 1 fonts, vector rectangles/lines, paged text,
/// and simple tables. Layout is produced by EvidenceReportLayoutEngine so the
/// native WPF preview and exported PDF share the same page plan. This keeps the
/// desktop app Apache-2.0 clean and avoids browser or third-party PDF layout
/// dependencies for FAT/SAT evidence reports.
/// </summary>
public static class EvidencePdfReportService
{

    public static void Save(string fileName, EvidencePdfReportModel model)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("PDF output path is required.", nameof(fileName));
        }

        ArgumentNullException.ThrowIfNull(model);

        var layout = EvidenceReportLayoutEngine.Build(model);
        var pages = RenderLayout(layout);
        NativePdfDocument.Write(fileName, pages, model);
    }

    private static IReadOnlyList<PdfPageBuffer> RenderLayout(EvidenceReportLayoutPlan layout)
    {
        var pages = new List<PdfPageBuffer>(layout.Pages.Count);
        foreach (var pagePlan in layout.Pages)
        {
            var page = new PdfPageBuffer((float)pagePlan.Width, (float)pagePlan.Height);
            foreach (var command in pagePlan.Commands)
            {
                switch (command)
                {
                    case EvidenceReportTextCommand text:
                        page.Text((float)text.X, (float)text.BaselineY, text.Text, ToPdfFont(text.Font), (float)text.FontSize, ToPdfColor(text.Color));
                        break;
                    case EvidenceReportLineCommand line:
                        page.Line((float)line.X1, (float)line.Y1, (float)line.X2, (float)line.Y2, ToPdfColor(line.Stroke), (float)line.StrokeThickness);
                        break;
                    case EvidenceReportRectCommand rect:
                        page.RoundRect((float)rect.X, (float)rect.TopY, (float)rect.Width, (float)rect.Height, (float)rect.Radius, ToPdfColor(rect.Fill), ToPdfColor(rect.Stroke), (float)rect.StrokeThickness);
                        break;
                }
            }

            pages.Add(page);
        }

        return pages;
    }

    private static PdfColor ToPdfColor(EvidenceReportColor color)
        => new(color.R / 255f, color.G / 255f, color.B / 255f);

    private static PdfFont ToPdfFont(EvidenceReportFontKind font)
        => font switch
        {
            EvidenceReportFontKind.Bold => PdfFont.Bold,
            EvidenceReportFontKind.Mono => PdfFont.Mono,
            _ => PdfFont.Regular
        };

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
            if (radius <= 0f)
            {
                Rect(x, top, width, height, fill, stroke, lineWidth);
                return;
            }

            var y = top - height;
            var r = Math.Min(radius, Math.Min(width, height) / 2f);
            var c = r * 0.55228475f;

            if (lineWidth > 0f)
            {
                _operations.Append(Number(lineWidth)).Append(" w ");
            }

            _operations.Append(fill.FillOperation()).Append(' ');
            if (lineWidth > 0f)
            {
                _operations.Append(stroke.StrokeOperation()).Append(' ');
            }

            _operations.Append(Number(x + r)).Append(' ').Append(Number(y)).Append(" m ");
            _operations.Append(Number(x + width - r)).Append(' ').Append(Number(y)).Append(" l ");
            _operations.Append(Number(x + width - r + c)).Append(' ').Append(Number(y)).Append(' ')
                .Append(Number(x + width)).Append(' ').Append(Number(y + r - c)).Append(' ')
                .Append(Number(x + width)).Append(' ').Append(Number(y + r)).Append(" c ");
            _operations.Append(Number(x + width)).Append(' ').Append(Number(y + height - r)).Append(" l ");
            _operations.Append(Number(x + width)).Append(' ').Append(Number(y + height - r + c)).Append(' ')
                .Append(Number(x + width - r + c)).Append(' ').Append(Number(y + height)).Append(' ')
                .Append(Number(x + width - r)).Append(' ').Append(Number(y + height)).Append(" c ");
            _operations.Append(Number(x + r)).Append(' ').Append(Number(y + height)).Append(" l ");
            _operations.Append(Number(x + r - c)).Append(' ').Append(Number(y + height)).Append(' ')
                .Append(Number(x)).Append(' ').Append(Number(y + height - r + c)).Append(' ')
                .Append(Number(x)).Append(' ').Append(Number(y + height - r)).Append(" c ");
            _operations.Append(Number(x)).Append(' ').Append(Number(y + r)).Append(" l ");
            _operations.Append(Number(x)).Append(' ').Append(Number(y + r - c)).Append(' ')
                .Append(Number(x + r - c)).Append(' ').Append(Number(y)).Append(' ')
                .Append(Number(x + r)).Append(' ').Append(Number(y)).Append(lineWidth > 0f ? " c B\n" : " c f\n");
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

    private static string CleanCell(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "-" : normalized;
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
