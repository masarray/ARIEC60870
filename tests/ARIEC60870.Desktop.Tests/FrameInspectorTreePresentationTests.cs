// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class FrameInspectorTreePresentationTests
{
    private static FrameInspectorProperty[] Properties() =>
    [
        new("Observed frame number", "77", "Captured physical sequence."),
        new("IOA", "8388725", "Decoded source address.",
        [
            new("IOA1 (low)", "117 (0x75)"),
            new("IOA2 (mid)", "0 (0x00)"),
            new("IOA3 (high)", "128 (0x80)")
        ]),
        new("Quality", "Good", "SIQ raw 0x00",
        [
            new("Invalid (IV)", "False", "The source marks the value invalid only when set."),
            new("Not topical (NT)", "False", "The source value may not be up to date."),
            new("Blocked (BL)", "False", "Source blocked state."),
            new("Substituted (SB)", "False", "Source substituted state."),
            new("Raw quality", "0x00", "Original quality octet.")
        ]),
        new("Original frame size", "26 bytes", "Full raw frame is elsewhere.")
    ];

    [Fact]
    public void DefaultQualityTreeIsReadableAndIncludesExactSourceFlags()
    {
        var root = Properties();
        var open = FrameInspectorTreePresentation.InitiallyExpanded(root);
        var rows = FrameInspectorTreePresentation.Build(root, open);
        Assert.Contains(rows, x => x.Field == "Quality" && x.Depth == 0 &&
            x.HasChildren && x.IsExpanded && x.Tone == "Good");
        Assert.Contains(rows, x => x.Field == "Quality details" && x.Depth == 1 &&
            x.IsExpanded);
        Assert.Contains(rows, x => x.Field == "Invalid (IV)" && x.Depth == 2 &&
            x.Value == "False");
        Assert.Contains(rows, x => x.Field == "Raw quality" && x.Value == "0x00");
        Assert.DoesNotContain(rows, x => x.Field == "IOA1 (low)");
        Assert.DoesNotContain(rows, x => x.Value.Contains("The source marks", StringComparison.Ordinal));
    }

    [Fact]
    public void ClickDisclosureAddsOnlySelectedChildRowsWithoutChangingOriginalEvidence()
    {
        var source = Properties();
        var original = source[1].Children!.Select(x => x.Value).ToArray();
        var expanded = new HashSet<string>(
            FrameInspectorTreePresentation.InitiallyExpanded(source), StringComparer.Ordinal);
        expanded.Add(FrameInspectorTreePresentation.RootKey(1));
        var open = FrameInspectorTreePresentation.Build(source, expanded);
        Assert.Contains(open, x => x.Field == "IOA1 (low)" && x.Value == "117 (0x75)" && x.Depth == 1);
        expanded.Remove(FrameInspectorTreePresentation.RootKey(2));
        var collapsedQuality = FrameInspectorTreePresentation.Build(source, expanded);
        Assert.DoesNotContain(collapsedQuality, x => x.Field == "Invalid (IV)");
        Assert.Contains(collapsedQuality, x => x.Field == "Quality" && !x.IsExpanded);
        Assert.Equal(original, source[1].Children!.Select(x => x.Value));
        var child = open.Single(x => x.Field == "IOA1 (low)");
        Assert.Equal("IOA1 (low)", child.NameTooltip);
        var qualityBit = FrameInspectorTreePresentation.Build(source,
            FrameInspectorTreePresentation.InitiallyExpanded(source))
            .Single(x => x.Field == "Invalid (IV)");
        Assert.Contains("The source marks the value invalid", qualityBit.NameTooltip);
    }

    [Fact]
    public void MissingDescriptorDoesNotInventQualityFlagsOrAnEmptyQualityGroup()
    {
        var root = new[] { new FrameInspectorProperty("Quality", "Unknown", "No QDS") };
        Assert.Empty(FrameInspectorTreePresentation.InitiallyExpanded(root));
        var visible = FrameInspectorTreePresentation.Build(root, new HashSet<string>());
        Assert.Single(visible);
        Assert.False(visible[0].HasChildren);
    }

    [Fact]
    public void FaultSeverityPreservesDifferenceBetweenNormalAcdAndInvalidQuality()
    {
        var root = new[]
        {
            new FrameInspectorProperty("Access demand (ACD)", "1 · Set", "Class 1 pending"),
            new FrameInspectorProperty("Invalid (IV)", "True", "Invalid source bit"),
            new FrameInspectorProperty("Overflow (OV)", "True", "Overflow source bit"),
            new FrameInspectorProperty("Frame checksum", "Invalid", "Bad FT1.2 frame")
        };
        var rows = FrameInspectorTreePresentation.Build(root, new HashSet<string>());
        Assert.Equal("Warning", rows[0].Tone);
        Assert.Equal("Critical", rows[1].Tone);
        Assert.Equal("Warning", rows[2].Tone);
        Assert.Equal("Critical", rows[3].Tone);
    }

    [Fact]
    public void EveryInformationObjectRetainsOwnExpandableDetailsWithoutTruncation()
    {
        var root = Enumerable.Range(1, 40)
            .Select(i => new FrameInspectorProperty($"Object #{i} · {3000 + i}", i.ToString(),
                "Source object", [new("Byte provenance", $"offset:{i}")]))
            .ToArray();
        var expanded = new HashSet<string>(StringComparer.Ordinal)
        {
            FrameInspectorTreePresentation.RootKey(39)
        };
        var rows = FrameInspectorTreePresentation.Build(root, expanded);
        Assert.Equal(41, rows.Count);
        Assert.Contains(rows, x => x.Field == "Byte provenance" && x.Value == "offset:40");
        Assert.Equal(40, rows.Count(x => x.Depth == 0));
    }
}
