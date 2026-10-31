// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class TraceDragEdgeScrollTests
{
    [Theory]
    [InlineData(-20, 600, -61.6)]
    [InlineData(10, 600, -28.6)]
    [InlineData(300, 600, 0)]
    [InlineData(590, 600, 28.6)]
    [InlineData(660, 600, 64)]
    [InlineData(20, 50, 0)]
    public void EdgeScrollingIsBoundedAndOnlyActiveNearViewportEdge(
        double pointerY, double height, double expected) =>
        Assert.Equal(expected, TraceDragEdgeScroll.ScrollDelta(pointerY, height), 3);
}
