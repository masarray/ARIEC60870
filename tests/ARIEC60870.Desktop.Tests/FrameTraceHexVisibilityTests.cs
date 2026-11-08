// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class FrameTraceHexVisibilityTests
{
    private static EvidenceRow Frame(string hex) => new(new Iec103MasterEvidenceEvent
    {
        ProtocolMode = Iec60870ProtocolMode.Iec101,
        Direction = FrameDirection.SlaveToMaster,
        RawHex = hex,
        TimestampUtc = new DateTime(2026, 11, 8, 8, 0, 0, DateTimeKind.Utc)
    });

    [Theory]
    [InlineData("104B69B416", "10 4B 69 B4 16")]
    [InlineData("E5", "E5")]
    [InlineData("68111168", "68 11 11 68")]
    public void ContiguousRawHexDisplaysAsReadableOctetsButSourceRemainsExact(
        string original, string expectedDisplay)
    {
        var row = Frame(original);
        Assert.Equal(expectedDisplay, row.TimelineHex);
        Assert.Equal(original, row.RawHex);
        Assert.Same(row.TimelineHex, row.TimelineHex);
    }

    [Fact]
    public void ExistingSeparatedOctetsAreNotReencoded()
    {
        var original = "10 4B 69 B4 16";
        var row = Frame(original);
        Assert.Equal(original, row.TimelineHex);
        Assert.Equal(original, row.RawHex);
    }

    [Theory]
    [InlineData("68F")]
    [InlineData("68XX")]
    [InlineData("AA|BB")]
    [InlineData("68 11 11 68 16")]
    public void MalformedOrAnnotatedRawIsShownWithoutFabricatedBytes(string original)
    {
        var row = Frame(original);
        Assert.Equal(original, row.TimelineHex);
        Assert.Equal(original, row.RawHex);
    }

    [Fact]
    public void MissingRawShowsClearEvidenceAvailabilityWithoutManufacturingHex()
    {
        var row = Frame("");
        Assert.Equal("-", row.RawHex);
        Assert.Equal("No raw bytes available", row.TimelineHex);
    }

    [Fact]
    public void CompleteLongAsduIsKeptInTimelineHexForInspectorWithoutByteLoss()
    {
        var raw = Convert.ToHexString(Enumerable.Range(0, 250)
            .Select(i => (byte)i).ToArray());
        var row = Frame(raw);
        Assert.Equal(250, row.TimelineHex.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(raw, row.TimelineHex.Replace(" ", "", StringComparison.Ordinal));
        Assert.Equal(raw, row.RawHex);
    }
}
