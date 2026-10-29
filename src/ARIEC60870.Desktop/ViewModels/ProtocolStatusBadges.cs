// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows.Media;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Lightweight visual projection of a typed protocol flag. It never parses HEX
/// or infers bits from human-readable messages.
/// </summary>
public sealed record ProtocolStatusBadge(string Text, Brush Background, Brush Foreground);

public static class ProtocolStatusBadges
{
    private static readonly Brush GoodBg = Solid(0xE7, 0xF5, 0xEE);
    private static readonly Brush GoodFg = Solid(0x21, 0x68, 0x47);
    private static readonly Brush WarnBg = Solid(0xFF, 0xF3, 0xD6);
    private static readonly Brush WarnFg = Solid(0x81, 0x56, 0x0C);
    private static readonly Brush BadBg = Solid(0xFC, 0xE9, 0xEC);
    private static readonly Brush BadFg = Solid(0xA2, 0x22, 0x36);
    private static readonly Brush QuietBg = Solid(0xEE, 0xF2, 0xF6);
    private static readonly Brush QuietFg = Solid(0x43, 0x54, 0x66);

    private static readonly IReadOnlyList<ProtocolStatusBadge> Good =
        new[] { new ProtocolStatusBadge("Good", GoodBg, GoodFg) };
    private static readonly IReadOnlyList<ProtocolStatusBadge> Unknown =
        new[] { new ProtocolStatusBadge("—", QuietBg, QuietFg) };
    private static readonly IReadOnlyList<ProtocolStatusBadge> Negative =
        new[] { new ProtocolStatusBadge("NEG", BadBg, BadFg) };
    private static readonly IReadOnlyList<ProtocolStatusBadge> Test =
        new[] { new ProtocolStatusBadge("TEST", WarnBg, WarnFg) };
    private static readonly IReadOnlyList<ProtocolStatusBadge> Both =
        new[] { new ProtocolStatusBadge("NEG", BadBg, BadFg), new ProtocolStatusBadge("TEST", WarnBg, WarnFg) };

    public static IReadOnlyList<ProtocolStatusBadge> ForCot(Iec10xCauseDetails? cause) =>
        cause is null ? Array.Empty<ProtocolStatusBadge>() :
        cause.Negative && cause.Test ? Both :
        cause.Negative ? Negative :
        cause.Test ? Test : Array.Empty<ProtocolStatusBadge>();

    public static string CotText(Iec10xCauseDetails? cause, string fallback) =>
        cause is null ? fallback : $"{cause.Code} · {cause.Phase}";

    public static string CotDetail(Iec10xCauseDetails? cause, string fallback) =>
        cause?.Detail ?? fallback;

    public static string QualityDetail(Iec10xQualityDetails? quality, string fallback) =>
        quality?.BitDetails ?? (string.IsNullOrWhiteSpace(fallback) || fallback == "-" ?
            "No decoded quality byte; status is not known." : "Unstructured legacy quality: " + fallback);

    public static IReadOnlyList<ProtocolStatusBadge> ForQuality(Iec10xQualityDetails? quality, string fallback)
    {
        if (quality is null)
        {
            // A missing or unsupported QDS/SIQ/DIQ/BCR byte is NOT Good.
            if (string.IsNullOrWhiteSpace(fallback) || fallback == "-") return Unknown;
            return fallback.Equals("Good", StringComparison.OrdinalIgnoreCase) ? Good :
                new[] { new ProtocolStatusBadge(TrimLegacy(fallback), QuietBg, QuietFg) };
        }

        // Preserve severity ordering and only allocate chips for actual active flags.
        var active = new List<ProtocolStatusBadge>(4);
        if (quality.Invalid) active.Add(new ProtocolStatusBadge("INVALID", BadBg, BadFg));
        if (quality.Overflow == true) active.Add(new ProtocolStatusBadge("OVF", WarnBg, WarnFg));
        if (quality.Carry == true) active.Add(new ProtocolStatusBadge("CARRY", WarnBg, WarnFg));
        if (quality.NotTopical == true) active.Add(new ProtocolStatusBadge("NT", WarnBg, WarnFg));
        if (quality.Blocked == true) active.Add(new ProtocolStatusBadge("BLK", QuietBg, QuietFg));
        if (quality.Substituted == true) active.Add(new ProtocolStatusBadge("SUB", QuietBg, QuietFg));
        if (quality.Adjusted == true) active.Add(new ProtocolStatusBadge("ADJ", QuietBg, QuietFg));
        if (active.Count == 0) return Good;
        if (active.Count <= 3) return active;

        // +N means additional visible UI flags, NOT the IEC QDS OV bit.
        var extra = active.Count - 3;
        return new[]
        {
            active[0], active[1], active[2],
            new ProtocolStatusBadge($"+{extra}", QuietBg, QuietFg)
        };
    }

    private static string TrimLegacy(string value) => value.Length > 16 ? value[..15] + "…" : value;

    private static Brush Solid(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
