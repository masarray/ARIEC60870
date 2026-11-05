// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

/// <summary>
/// The public Monitor adapter is a projection of the Core framing authority,
/// not a second implementation of FT1.2 checksum/offset/resynchronization.
/// </summary>
public sealed class PassiveFramerParityTests
{
    [Fact]
    public void SplitChunksAndNoiseHaveExactCoreMonitorFrameParity()
    {
        var core = new Ft12IncrementalFramer(1);
        var monitor = new PassiveFt12Scanner(1);
        var byteStream = new byte[]
        {
            0x99, 0x10, 0x49, 0x69, 0xB2, 0x16, 0xE5,
            0x68, 0x01, 0x03, 0x68, 0x00, // bad header/noise
            0x10, 0x29, 0x69, 0x92, 0x16, 0xA2, 0x68, 0x21
        };
        var a = new List<Ft12StreamCandidate>();
        var b = new List<PassiveFt12Frame>();
        long baseOffset = 360;
        for (var cursor = 0; cursor < byteStream.Length;)
        {
            var n = Math.Min(1 + cursor % 3, byteStream.Length - cursor);
            var slice = byteStream.AsSpan(cursor, n);
            core.Feed(slice, baseOffset + cursor, a.Add);
            monitor.Feed(slice, baseOffset + cursor, b.Add);
            cursor += n;
        }
        var tailA = core.ExpireIncomplete();
        var tailB = monitor.ExpireIncomplete();
        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].ByteOffset, b[i].ByteOffset);
            Assert.Equal(a[i].Format, b[i].Format);
            Assert.Equal(a[i].ChecksumValid, b[i].ChecksumValid);
            Assert.Equal(a[i].RawBytes, b[i].RawBytes);
            Assert.Equal(a[i].DetectedLinkAddressWidth, b[i].DetectedLinkAddressWidth);
        }
        Assert.NotNull(tailA);
        Assert.NotNull(tailB);
        Assert.Equal(tailA!.ByteOffset, tailB!.ByteOffset);
        Assert.Equal(tailA.RawBytes, tailB.RawBytes);
        Assert.Equal(core.NoiseBytes, monitor.NoiseBytes);
        Assert.Equal(core.IncompleteBytes, monitor.IncompleteBytes);
    }
}
