// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class BoundedRingBufferTailTests
{
    [Fact]
    public void PeekLatestIsConstantTimeAndDoesNotReorderCircularBuffer()
    {
        var ring = new BoundedRingBuffer<string>(3);
        Assert.False(ring.TryPeekLatest(out _));
        ring.Add("one");
        ring.Add("two");
        Assert.True(ring.TryPeekLatest(out var second));
        Assert.Equal("two", second);
        ring.Add("three");
        ring.Add("four"); // wrapping overwrites the oldest entry
        Assert.Equal(new[] { "two", "three", "four" }, ring.Snapshot());
        Assert.True(ring.TryPeekLatest(out var latest));
        Assert.Equal("four", latest);
        ring.Clear();
        Assert.False(ring.TryPeekLatest(out _));
    }
}
