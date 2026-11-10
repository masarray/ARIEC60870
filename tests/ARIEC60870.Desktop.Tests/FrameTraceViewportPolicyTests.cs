// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class FrameTraceViewportPolicyTests
{
    [Theory]
    [InlineData(1550, "Paired")]
    [InlineData(900, "Paired")]
    [InlineData(840, "Paired")]
    [InlineData(839.9, "Stacked")]
    [InlineData(780, "Stacked")]
    [InlineData(700, "Stacked")]
    [InlineData(420, "Stacked")]
    [InlineData(300, "Stacked")]
    public void MeasuredTraceWidthDeterminesReadableArrangement(double width, string expected)
    {
        Assert.Equal(expected, FrameTraceViewportPolicy.RowLayout(width));
    }

    [Fact]
    public void UnmeasuredLayoutKeepsOriginalPairedProjectionWithoutFictionalWidth()
    {
        Assert.Equal("Paired", FrameTraceViewportPolicy.RowLayout(0));
        Assert.Equal("Paired", FrameTraceViewportPolicy.RowLayout(double.NaN));
        Assert.Equal("Paired", FrameTraceViewportPolicy.RowLayout(double.PositiveInfinity));
    }

    [Fact]
    public void WideningAndNarrowingAlwaysReversesPresentationWithoutChangingSource()
    {
        Assert.Equal("Paired", FrameTraceViewportPolicy.RowLayout(1180));
        Assert.Equal("Stacked", FrameTraceViewportPolicy.RowLayout(620));
        Assert.Equal("Paired", FrameTraceViewportPolicy.RowLayout(1180));
    }
}
