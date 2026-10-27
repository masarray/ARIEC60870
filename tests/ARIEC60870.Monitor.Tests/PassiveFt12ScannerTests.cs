// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class PassiveFt12ScannerTests
{
    [Fact]
    public void FixedFrameCanBeSplitIntoIndividualReads()
    {
        var found = new List<PassiveFt12Frame>();
        var scanner = new PassiveFt12Scanner(1);
        var bytes = new byte[] { 0x10, 0x49, 0x69, 0xB2, 0x16, 0xE5 };
        for (var i = 0; i < bytes.Length; i++)
            scanner.Feed(bytes.AsSpan(i, 1), i, found.Add);
        Assert.Equal(2, found.Count);
        Assert.Equal("Fixed", found[0].Format);
        Assert.True(found[0].ChecksumValid);
        Assert.Equal(bytes[..5], found[0].RawBytes);
        Assert.Equal("Single", found[1].Format);
        Assert.True(found[1].ChecksumValid);
    }

    [Fact]
    public void VariableAsduIsReassembledAcrossReadBoundaries()
    {
        // Control 08 + link address 69 + ASDU Type 1, VSQ 1, COT 3, CA 1, IOA 7, SIQ=1
        var payload = new byte[] { 0x08, 0x69, 1, 1, 3, 0, 1, 0, 7, 0, 0, 1 };
        var l = (byte)payload.Length;
        var frame = new byte[] { 0x68, l, l, 0x68 }.Concat(payload)
            .Concat(new byte[] { (byte)(payload.Sum(b => b) & 0xFF), 0x16 }).ToArray();
        var scan = new PassiveFt12Scanner();
        var output = new List<PassiveFt12Frame>();
        scan.Feed(frame.AsSpan(0, 5), 100, output.Add);
        Assert.Empty(output);
        scan.Feed(frame.AsSpan(5, 4), 105, output.Add);
        Assert.Empty(output);
        scan.Feed(frame.AsSpan(9), 109, output.Add);
        Assert.Single(output);
        Assert.Equal(100, output[0].ByteOffset);
        Assert.Equal(frame, output[0].RawBytes);
        Assert.Equal("Variable", output[0].Format);
        Assert.True(output[0].ChecksumValid);
        Assert.Equal(0, scan.IncompleteBytes);
    }

    [Fact]
    public void TwoByteAddressAndCorruptionAreNotSilentlyValid()
    {
        var scanner = new PassiveFt12Scanner(2);
        var events = new List<PassiveFt12Frame>();
        var source = new byte[] { 0x55, 0x10, 0x49, 0x69, 0x00, 0xB2, 0x16,
                                  0x10, 0x49, 0x69, 0x00, 0x00, 0x16 };
        scanner.Feed(source, 200, events.Add);
        Assert.Equal(2, events.Count);
        Assert.Equal(201, events[0].ByteOffset);
        Assert.True(events[0].ChecksumValid);
        Assert.False(events[1].ChecksumValid);
        Assert.Equal(1, scanner.NoiseBytes);
    }

    [Fact]
    public void A2SingleCharacterIsValidNegativeReplyNotChecksumFailure()
    {
        var observed = new List<PassiveFt12Frame>();
        new PassiveFt12Scanner().Feed(new byte[] { 0xA2 }, 33, observed.Add);
        var reply = Assert.Single(observed);
        Assert.Equal(33, reply.ByteOffset);
        Assert.True(reply.ChecksumValid);
        Assert.Equal(new byte[] { 0xA2 }, reply.RawBytes);
    }

    [Fact]
    public void BadLengthHeaderResynchronizesWithoutUnboundedBuffer()
    {
        var scanner = new PassiveFt12Scanner();
        var output = new List<PassiveFt12Frame>();
        for (var i = 0; i < 5000; i++)
            scanner.Feed(new byte[] { 0x68, 0xFF, 0x10, 0x10, 0x00 }, i * 5, output.Add);
        // The raw stream may legitimately end with an incomplete candidate; an
        // independently observed idle gap/EOF is the boundary that releases it.
        scanner.ExpireIncomplete();
        scanner.Feed(new byte[] { 0xE5 }, 25000, output.Add);
        Assert.Contains(output, f => f.Format == "Single" && f.ChecksumValid);
        Assert.InRange(scanner.IncompleteBytes, 0, 260);
    }
}
