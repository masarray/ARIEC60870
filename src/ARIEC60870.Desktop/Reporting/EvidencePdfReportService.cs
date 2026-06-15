// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARIEC60870.Desktop.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ARIEC60870.Desktop.Reporting;

public static class EvidencePdfReportService
{
    private const int MaxRowsPerSection = 160;
    private const string BrandNavy = "#0F172A";
    private const string BrandBlue = "#2563EB";
    private const string SoftBlue = "#EFF6FF";
    private const string SoftSlate = "#F8FAFC";
    private const string Border = "#DDE7F3";
    private const string Muted = "#64748B";
    private const string Ink = "#111827";
    private const string Pass = "#15803D";
    private const string Attention = "#B45309";
    private const string Fail = "#B91C1C";

    public static void Save(string fileName, EvidencePdfReportModel model)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("PDF output path is required.", nameof(fileName));
        }

        ArgumentNullException.ThrowIfNull(model);
        QuestPDF.Settings.License = LicenseType.Community;
        Document.Create(container => Compose(container, model)).GeneratePdf(fileName);
    }

    private static void Compose(IDocumentContainer container, EvidencePdfReportModel model)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(26);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(style => style.FontFamily("Segoe UI").FontSize(8).FontColor(Ink));

            page.Header().Element(content => ComposeHeader(content, model));
            page.Content().PaddingTop(14).Column(column =>
            {
                column.Spacing(12);
                column.Item().Element(content => ComposeExecutiveSummary(content, model));
                column.Item().Element(content => ComposeKeyValueCards(content, model));
                column.Item().Element(content => ComposeSetupCard(content, model));
                column.Item().Element(content => ComposeEvidenceSection(content, "General Interrogation Evidence", "GI request, activation confirmation, activation termination, and related rows.", model.GiRows));
                column.Item().Element(content => ComposeEvidenceSection(content, "Command Evidence", "Select/operate, ACTCON/ACTTERM, feedback, and related command rows.", model.CommandRows));
                column.Item().Element(content => ComposeEvidenceSection(content, "SOE / Event Evidence", "Spontaneous indications, digital values, timestamps, and quality-related rows.", model.SoeRows));
                column.Item().Element(content => ComposeEvidenceSection(content, "Important Protocol Evidence", "Warnings, negative responses, quality issues, mapped values, and other acceptance-critical rows.", model.ImportantRows));
                column.Item().Element(content => ComposeAcceptanceNotes(content));
            });
            page.Footer().Element(content => ComposeFooter(content, model));
        });
    }

    private static void ComposeHeader(IContainer container, EvidencePdfReportModel model)
    {
        var verdictColor = ResolveVerdictColor(model.VerdictTone);

        container.BorderBottom(1).BorderColor(Border).PaddingBottom(10).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Spacing(2);
                column.Item().Text("ARIEC60870 Protocol Lab").FontSize(8).FontColor(Muted).SemiBold();
                column.Item().Text("IEC 60870 Evidence Report").FontSize(22).Bold().FontColor(BrandNavy);
                column.Item().Text("Commissioning and FAT/SAT protocol evidence for IEC 60870-5-101 / 103 / 104 communication sessions.").FontSize(8).FontColor(Muted);
            });

            row.ConstantItem(138).Border(1).BorderColor(verdictColor).Background(ResolveVerdictBackground(model.VerdictTone)).Padding(10).Column(column =>
            {
                column.Spacing(2);
                column.Item().Text("Verdict").FontSize(7).FontColor(Muted).SemiBold();
                column.Item().Text(model.VerdictStatus).FontSize(18).Bold().FontColor(verdictColor);
                column.Item().Text(model.ReportId).FontSize(6).FontColor(Muted);
            });
        });
    }

    private static void ComposeExecutiveSummary(IContainer container, EvidencePdfReportModel model)
    {
        container.Border(1).BorderColor(Border).Background(SoftSlate).Padding(12).Column(column =>
        {
            column.Spacing(8);
            column.Item().Text("Executive Summary").FontSize(12).Bold().FontColor(BrandNavy);
            column.Item().Text(model.VerdictSummary).FontSize(9).FontColor(Ink);
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(content => ComposeMetric(content, "Protocol", model.ProtocolMode));
                row.RelativeItem().Element(content => ComposeMetric(content, "Rows", model.TotalRows.ToString(CultureInfo.InvariantCulture)));
                row.RelativeItem().Element(content => ComposeMetric(content, "Sequence", $"{model.FirstSequence} → {model.LastSequence}"));
                row.RelativeItem().Element(content => ComposeMetric(content, "Source", NormalizeWorkspace(model.SourceWorkspace)));
            });
        });
    }

    private static void ComposeMetric(IContainer container, string label, string value)
    {
        container.Border(1).BorderColor(Border).Background(Colors.White).Padding(8).Column(column =>
        {
            column.Spacing(1);
            column.Item().Text(label).FontSize(6).FontColor(Muted).SemiBold();
            column.Item().Text(value).FontSize(10).Bold().FontColor(BrandNavy);
        });
    }

    private static void ComposeKeyValueCards(IContainer container, EvidencePdfReportModel model)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(content => ComposeKeyValueCard(content, "Report", model.ReportInfo));
            row.ConstantItem(12);
            row.RelativeItem().Element(content => ComposeKeyValueCard(content, "Session Counters", model.SessionCounters));
        });
    }

    private static void ComposeSetupCard(IContainer container, EvidencePdfReportModel model)
        => ComposeKeyValueCard(container, "Communication Setup", model.CommunicationSetup);

    private static void ComposeKeyValueCard(IContainer container, string title, IReadOnlyList<KeyValuePair<string, string>> rows)
    {
        container.Border(1).BorderColor(Border).Background(Colors.White).Padding(10).Column(column =>
        {
            column.Spacing(6);
            column.Item().Text(title).FontSize(11).Bold().FontColor(BrandNavy);
            foreach (var item in rows)
            {
                column.Item().BorderBottom(1).BorderColor("#EEF2F7").PaddingBottom(4).Row(row =>
                {
                    row.ConstantItem(112).Text(item.Key).FontSize(7).FontColor(Muted).SemiBold();
                    row.RelativeItem().Text(CleanCell(item.Value)).FontSize(7).FontColor(Ink);
                });
            }
        });
    }

    private static void ComposeEvidenceSection(IContainer container, string title, string description, IReadOnlyList<EvidenceRow> rows)
    {
        container.Border(1).BorderColor(Border).Background(Colors.White).Padding(10).Column(column =>
        {
            column.Spacing(7);
            column.Item().Text(title).FontSize(11).Bold().FontColor(BrandNavy);
            column.Item().Text(description).FontSize(7).FontColor(Muted);

            if (rows.Count == 0)
            {
                column.Item().Border(1).BorderColor(Border).Background(SoftSlate).Padding(8).Text("No matching evidence rows in the selected report scope.").FontSize(8).FontColor(Muted);
                return;
            }

            var displayedRows = rows.Take(MaxRowsPerSection).ToArray();
            column.Item().Table(table =>
            {
                void BodyCell(string text, bool mono = false)
                {
                    var descriptor = table.Cell().BorderBottom(1).BorderColor("#EEF2F7").PaddingVertical(4).PaddingHorizontal(4);
                    var value = CleanCell(text);
                    if (mono)
                    {
                        descriptor.Text(value).FontFamily("Consolas").FontSize(7).FontColor(Ink);
                    }
                    else
                    {
                        descriptor.Text(value).FontSize(7).FontColor(Ink);
                    }
                }

                void DirectionCell(string direction)
                {
                    var normalized = CleanCell(direction);
                    var color = normalized.Equals("TX", StringComparison.OrdinalIgnoreCase) ? "#1D4ED8" : normalized.Equals("RX", StringComparison.OrdinalIgnoreCase) ? "#047857" : Muted;
                    table.Cell().BorderBottom(1).BorderColor("#EEF2F7").PaddingVertical(4).PaddingHorizontal(4).Text(normalized).FontSize(7).Bold().FontColor(color);
                }

                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(30);
                    columns.ConstantColumn(58);
                    columns.ConstantColumn(34);
                    columns.RelativeColumn(1.08f);
                    columns.RelativeColumn(1.05f);
                    columns.RelativeColumn(1.15f);
                    columns.RelativeColumn(1.1f);
                    columns.RelativeColumn(2.75f);
                });

                table.Header(header =>
                {
                    void HeaderCell(string text)
                    {
                        header.Cell().Background(SoftBlue).BorderBottom(1).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(4).Text(text).FontSize(6).SemiBold().FontColor(BrandBlue);
                    }

                    HeaderCell("#");
                    HeaderCell("Time");
                    HeaderCell("Dir");
                    HeaderCell("Service");
                    HeaderCell("CA / IOA");
                    HeaderCell("Type / COT");
                    HeaderCell("Quality");
                    HeaderCell("Meaning");
                });

                foreach (var row in displayedRows)
                {
                    BodyCell(row.Sequence.ToString(CultureInfo.InvariantCulture), true);
                    BodyCell(row.Time, true);
                    DirectionCell(row.Direction);
                    BodyCell(row.ProtocolService);
                    BodyCell(row.CommonAddress + " / " + row.IoAddress, true);
                    BodyCell(row.TypeId + " / " + row.CotDisplay);
                    BodyCell(row.Quality);
                    BodyCell(FirstMeaning(row));
                }
            });

            if (rows.Count > displayedRows.Length)
            {
                column.Item().Text($"Showing first {displayedRows.Length} rows from {rows.Count} matching rows. Use .ariec capture for full replay evidence.").FontSize(7).FontColor(Muted);
            }
        });
    }

    private static void ComposeAcceptanceNotes(IContainer container)
    {
        container.Border(1).BorderColor(Border).Background(SoftSlate).Padding(10).Column(column =>
        {
            column.Spacing(5);
            column.Item().Text("Acceptance Notes").FontSize(11).Bold().FontColor(BrandNavy);
            column.Item().Text("Review the evidence against the approved FAT/SAT procedure, relay manual, gateway interoperability list, and project signal mapping. Keep the .ariec capture file when replayable evidence is required.").FontSize(8).FontColor(Ink);
        });
    }

    private static void ComposeFooter(IContainer container, EvidencePdfReportModel model)
    {
        container.BorderTop(1).BorderColor(Border).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(text =>
            {
                text.Span("Generated ").FontSize(7).FontColor(Muted);
                text.Span(model.CreatedLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).FontSize(7).FontColor(Ink);
                text.Span("  •  SHA256 ").FontSize(7).FontColor(Muted);
                text.Span(ShortHash(model.FramesSha256)).FontSize(7).FontColor(Ink);
            });

            row.ConstantItem(90).AlignRight().Text(text =>
            {
                text.Span("Page ").FontSize(7).FontColor(Muted);
                text.CurrentPageNumber().FontSize(7).FontColor(Ink);
                text.Span(" / ").FontSize(7).FontColor(Muted);
                text.TotalPages().FontSize(7).FontColor(Ink);
            });
        });
    }

    private static string ResolveVerdictColor(string tone)
        => tone.Equals("pass", StringComparison.OrdinalIgnoreCase) ? Pass
            : tone.Equals("fail", StringComparison.OrdinalIgnoreCase) ? Fail
            : Attention;

    private static string ResolveVerdictBackground(string tone)
        => tone.Equals("pass", StringComparison.OrdinalIgnoreCase) ? "#F0FDF4"
            : tone.Equals("fail", StringComparison.OrdinalIgnoreCase) ? "#FEF2F2"
            : "#FFFBEB";

    private static string NormalizeWorkspace(string value)
        => value switch
        {
            "ProtocolTrace" => "Protocol Trace",
            "EvidenceSummary" => "Evidence Summary",
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
        var normalized = (value ?? string.Empty).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "-" : normalized;
    }

    private static string ShortHash(string value)
    {
        var clean = CleanCell(value);
        return clean.Length <= 16 ? clean : clean[..16];
    }
}
