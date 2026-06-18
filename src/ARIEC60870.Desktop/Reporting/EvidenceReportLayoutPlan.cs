// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;

namespace ARIEC60870.Desktop.Reporting;

public sealed record EvidenceReportLayoutPlan(
    double PageWidth,
    double PageHeight,
    string ReportId,
    DateTime CreatedLocal,
    IReadOnlyList<EvidenceReportPagePlan> Pages);

public sealed record EvidenceReportPagePlan(
    int PageNumber,
    double Width,
    double Height,
    IReadOnlyList<EvidenceReportDrawCommand> Commands);

public abstract record EvidenceReportDrawCommand;

public sealed record EvidenceReportTextCommand(
    double X,
    double BaselineY,
    double Width,
    string Text,
    EvidenceReportFontKind Font,
    double FontSize,
    EvidenceReportColor Color) : EvidenceReportDrawCommand;

public sealed record EvidenceReportRectCommand(
    double X,
    double TopY,
    double Width,
    double Height,
    double Radius,
    EvidenceReportColor Fill,
    EvidenceReportColor Stroke,
    double StrokeThickness) : EvidenceReportDrawCommand;

public sealed record EvidenceReportLineCommand(
    double X1,
    double Y1,
    double X2,
    double Y2,
    EvidenceReportColor Stroke,
    double StrokeThickness) : EvidenceReportDrawCommand;

public enum EvidenceReportFontKind
{
    Regular,
    Bold,
    Mono
}

public readonly record struct EvidenceReportColor(byte R, byte G, byte B)
{
    public static EvidenceReportColor FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            throw new ArgumentException("A six-digit RGB hex value is required.", nameof(hex));
        }

        var value = hex.StartsWith("#", StringComparison.Ordinal) ? hex[1..] : hex;
        if (value.Length != 6)
        {
            throw new ArgumentException("A six-digit RGB hex value is required.", nameof(hex));
        }

        return new EvidenceReportColor(
            Convert.ToByte(value[..2], 16),
            Convert.ToByte(value.Substring(2, 2), 16),
            Convert.ToByte(value.Substring(4, 2), 16));
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}
