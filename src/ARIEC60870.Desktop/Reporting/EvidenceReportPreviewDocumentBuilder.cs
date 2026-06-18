// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ARIEC60870.Desktop.Reporting;

/// <summary>
/// Native WPF preview renderer for ARIEC60870 evidence reports.
///
/// The preview intentionally consumes the same EvidenceReportLayoutPlan as the
/// PDF exporter. It therefore shares page size, page breaks, table row heights,
/// column widths, title positions and footer numbering with the final PDF output.
/// </summary>
public static class EvidenceReportPreviewDocumentBuilder
{
    private const double DipPerPdfPoint = 96d / 72d;

    private static readonly FontFamily ReportFont = new("Arial, Segoe UI");
    private static readonly FontFamily MonoFont = new("Consolas, Cascadia Mono");

    public static FixedDocument Build(EvidencePdfReportModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Render(EvidenceReportLayoutEngine.Build(model));
    }

    public static FixedDocument Render(EvidenceReportLayoutPlan layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var document = new FixedDocument();
        foreach (var pagePlan in layout.Pages)
        {
            var fixedPage = new FixedPage
            {
                Width = pagePlan.Width * DipPerPdfPoint,
                Height = pagePlan.Height * DipPerPdfPoint,
                Background = Brushes.White,
                SnapsToDevicePixels = true
            };

            fixedPage.SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Ideal);
            fixedPage.SetValue(TextOptions.TextRenderingModeProperty, TextRenderingMode.ClearType);

            foreach (var command in pagePlan.Commands)
            {
                switch (command)
                {
                    case EvidenceReportRectCommand rect:
                        AddRectangle(fixedPage, pagePlan.Height, rect);
                        break;
                    case EvidenceReportLineCommand line:
                        AddLine(fixedPage, pagePlan.Height, line);
                        break;
                    case EvidenceReportTextCommand text:
                        AddText(fixedPage, pagePlan.Height, text);
                        break;
                }
            }

            var content = new PageContent();
            ((IAddChild)content).AddChild(fixedPage);
            document.Pages.Add(content);
        }

        return document;
    }

    private static void AddRectangle(FixedPage page, double pageHeight, EvidenceReportRectCommand command)
    {
        var border = new Border
        {
            Background = ToBrush(command.Fill),
            BorderBrush = ToBrush(command.Stroke),
            BorderThickness = command.StrokeThickness <= 0d ? new Thickness(0) : new Thickness(Math.Max(0.5d, command.StrokeThickness * DipPerPdfPoint)),
            CornerRadius = new CornerRadius(Math.Max(0d, command.Radius * DipPerPdfPoint)),
            SnapsToDevicePixels = true
        };

        Add(page,
            border,
            command.X * DipPerPdfPoint,
            (pageHeight - command.TopY) * DipPerPdfPoint,
            command.Width * DipPerPdfPoint,
            command.Height * DipPerPdfPoint);
    }

    private static void AddLine(FixedPage page, double pageHeight, EvidenceReportLineCommand command)
    {
        var x1 = command.X1 * DipPerPdfPoint;
        var y1 = (pageHeight - command.Y1) * DipPerPdfPoint;
        var x2 = command.X2 * DipPerPdfPoint;
        var y2 = (pageHeight - command.Y2) * DipPerPdfPoint;
        var left = Math.Min(x1, x2);
        var top = Math.Min(y1, y2);
        var line = new Line
        {
            X1 = x1 - left,
            Y1 = y1 - top,
            X2 = x2 - left,
            Y2 = y2 - top,
            Stroke = ToBrush(command.Stroke),
            StrokeThickness = Math.Max(0.5d, command.StrokeThickness * DipPerPdfPoint),
            SnapsToDevicePixels = true
        };

        Add(page, line, left, top, Math.Max(1d, Math.Abs(x2 - x1) + 1d), Math.Max(1d, Math.Abs(y2 - y1) + 1d));
    }

    private static void AddText(FixedPage page, double pageHeight, EvidenceReportTextCommand command)
    {
        var fontSize = Math.Max(1d, command.FontSize * DipPerPdfPoint);
        var top = (pageHeight - command.BaselineY - (command.FontSize * 0.82d)) * DipPerPdfPoint;
        var block = new TextBlock
        {
            Text = command.Text,
            FontFamily = command.Font == EvidenceReportFontKind.Mono ? MonoFont : ReportFont,
            FontSize = fontSize,
            FontWeight = command.Font == EvidenceReportFontKind.Bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = ToBrush(command.Color),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            LineHeight = Math.Max(fontSize + 1.5d, fontSize * 1.18d),
            SnapsToDevicePixels = true
        };

        block.SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Ideal);
        block.SetValue(TextOptions.TextRenderingModeProperty, TextRenderingMode.ClearType);

        Add(page,
            block,
            command.X * DipPerPdfPoint,
            top,
            Math.Max(4d, command.Width * DipPerPdfPoint),
            Math.Max(fontSize + 3d, fontSize * 1.25d));
    }

    private static void Add(FixedPage page, UIElement element, double x, double y, double width, double height)
    {
        element.SetValue(FrameworkElement.WidthProperty, width);
        element.SetValue(FrameworkElement.HeightProperty, height);
        FixedPage.SetLeft(element, x);
        FixedPage.SetTop(element, y);
        page.Children.Add(element);
    }

    private static Brush ToBrush(EvidenceReportColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
