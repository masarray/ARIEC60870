// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Read-only visual emphasis for the bounded, source-decoded Frame Trace preview.
/// The decoder's exact text, delimiters, quality and time validity remain intact.
/// Only IOA identity, decoded value and the actually encoded RTU time are emphasized.
/// </summary>
public static class TraceSignalHighlight
{
    public readonly record struct Segment(string Text, bool Emphasized);

    private static readonly Regex FieldPattern = new(
        @"\bIOA\s+\d+(?:\s+\[[^\]\r\n]+\])?|Value:\s*[^·\r\n]+|\b\d{2}-\d{2}-\d{4}\s+\d{2}:\d{2}:\d{2}\.\d{3}|RTU partial time:\s*[^·\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Brush EmphasisInk = CreateEmphasisInk();

    private static Brush CreateEmphasisInk()
    {
        var brush = new SolidColorBrush(Color.FromRgb(22, 99, 63));
        brush.Freeze();
        return brush;
    }

    public static readonly DependencyProperty RichTextProperty =
        DependencyProperty.RegisterAttached("RichText", typeof(string),
            typeof(TraceSignalHighlight), new PropertyMetadata(string.Empty, OnRichTextChanged));

    public static void SetRichText(DependencyObject owner, string? value) =>
        owner.SetValue(RichTextProperty, value ?? string.Empty);

    public static string GetRichText(DependencyObject owner) =>
        (string?)owner.GetValue(RichTextProperty) ?? string.Empty;

    public static IReadOnlyList<Segment> Tokenize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<Segment>();
        var segments = new List<Segment>();
        var offset = 0;
        foreach (Match match in FieldPattern.Matches(text))
        {
            if (match.Index > offset)
                segments.Add(new Segment(text[offset..match.Index], false));
            segments.Add(new Segment(match.Value, true));
            offset = match.Index + match.Length;
        }
        if (offset < text.Length)
            segments.Add(new Segment(text[offset..], false));
        return segments;
    }

    private static void OnRichTextChanged(DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        if (sender is not TextBlock block) return;
        block.Inlines.Clear();
        foreach (var segment in Tokenize(args.NewValue as string))
        {
            var run = new Run(segment.Text);
            if (segment.Emphasized)
            {
                run.FontWeight = FontWeights.SemiBold;
                run.Foreground = EmphasisInk;
            }
            block.Inlines.Add(run);
        }
    }
}
