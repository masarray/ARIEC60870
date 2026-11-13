// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class TraceSignalHighlightTests
{
    [Fact]
    public void EmphasisMarksOnlySourceIoaValueAndDeviceTimeWithoutAlteringEvidence()
    {
        const string source = "IOA 16712689 [ IOA3: 255 · IOA2: 2 · IOA1: 241 ] · TI-31 · " +
            "Value: OFF [01] · 13-11-2026 07:12:15.777\r\n" +
            "  ↳ TI-31 Double Point Signal · Quality: Good · Timestamp Valid · Genuine (GEN=0)";
        var parts = TraceSignalHighlight.Tokenize(source);
        Assert.Equal(source, string.Concat(parts.Select(x => x.Text)));
        var emphasized = parts.Where(x => x.Emphasized).Select(x => x.Text).ToArray();
        Assert.Equal(3, emphasized.Length);
        Assert.StartsWith("IOA 16712689 [", emphasized[0]);
        Assert.Contains("Value: OFF [01]", emphasized[1]);
        Assert.Equal("13-11-2026 07:12:15.777", emphasized[2]);
        Assert.DoesNotContain(parts, x => x.Emphasized && x.Text.Contains("Timestamp Valid"));
    }

    [Fact]
    public void MissingRemoteTimeIsNotColoredAsAnEncodedDeviceTimestamp()
    {
        const string source = "IOA 5 [ IOA1: 5 ] · TI-1 · Value: ON\r\n" +
            "  ↳ TI-1 Single Point Signal · Quality: Good · No Timestamp";
        var parts = TraceSignalHighlight.Tokenize(source);
        Assert.Equal(2, parts.Count(x => x.Emphasized));
        Assert.DoesNotContain(parts, x => x.Emphasized && x.Text.Contains("No Timestamp"));
        Assert.Equal(source, string.Concat(parts.Select(x => x.Text)));
    }

    [Fact]
    public void PartialRtuTimeIsNoticeableButStillMarkedPartial()
    {
        const string source = "IOA 6 · TI-2 · Value: OFF · RTU partial time: 20:34\r\n" +
            "  ↳ TI-2 Single Point Signal · Timestamp Partial (CP56 validity / GEN / DST unknown)";
        var parts = TraceSignalHighlight.Tokenize(source);
        Assert.Contains(parts, x => x.Emphasized && x.Text.StartsWith("RTU partial time: 20:34"));
        Assert.Equal(source, string.Concat(parts.Select(x => x.Text)));
    }
}
