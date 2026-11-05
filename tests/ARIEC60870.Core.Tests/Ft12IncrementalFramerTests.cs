// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using ARIEC60870.Core.Parsing;
using Xunit;
using Xunit.Abstractions;

namespace ARIEC60870.Core.Tests;

public sealed class Ft12IncrementalFramerTests
{
    private readonly ITestOutputHelper _log;
    public Ft12IncrementalFramerTests(ITestOutputHelper log) => _log = log;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SplitEveryByteAndConcatenatedFt12FormatsKeepExactSourceOffsets(int width)
    {
        var framer = new Ft12IncrementalFramer(width, permitAlternateLinkWidths: false);
        var fixedFrame = Fixed(width);
        var variable = Variable(width, [0x1F, 0x01, 0x03, 0x00, 0x69, 0x00]);
        var stream = fixedFrame.Concat(variable).Concat(new byte[] { 0xE5, 0xA2 }).ToArray();
        var observed = new List<Ft12StreamCandidate>();
        for (var i = 0; i < stream.Length; i++)
            framer.Feed(stream.AsSpan(i, 1), 700 + i, observed.Add, flushNoiseAtEnd: false);

        Assert.Equal(new[] { "Fixed", "Variable", "Single", "Single" },
            observed.Select(x => x.Format));
        Assert.All(observed, x => Assert.True(x.ChecksumValid));
        Assert.Equal(new long[] { 700, 700 + fixedFrame.Length,
            700 + fixedFrame.Length + variable.Length, 700 + stream.Length - 1 },
            observed.Select(x => x.ByteOffset));
        Assert.Equal(fixedFrame, observed[0].RawBytes);
        Assert.Equal(variable, observed[1].RawBytes);
        Assert.Equal(0, framer.NoiseBytes);
        Assert.Equal(0, framer.IncompleteBytes);
    }

    [Fact]
    public void CorruptChecksumKeepsCompleteCandidateButNeverPromotesItToVerified()
    {
        var framer = new Ft12IncrementalFramer(1);
        var invalid = Fixed(1);
        invalid[^2] ^= 0x3F;
        var observed = new List<Ft12StreamCandidate>();
        framer.Feed(invalid, 100, observed.Add);
        var f = Assert.Single(observed);
        Assert.Equal("Fixed", f.Format);
        Assert.False(f.ChecksumValid);
        Assert.Equal(invalid, f.RawBytes);
        Assert.False(Ft12WireLayout.TryParse(invalid, 1, out _));
    }

    [Fact]
    public void InvalidVariableHeaderResynchronizesToNextValidFrame()
    {
        var framer = new Ft12IncrementalFramer(1, permitAlternateLinkWidths: false);
        var valid = Fixed(1);
        byte[] corrupt = [0x68, 0x09, 0x06, 0x68, 0x99];
        var observed = new List<Ft12StreamCandidate>();
        framer.Feed(corrupt.Concat(valid).ToArray(), 33, observed.Add);
        Assert.Contains(observed, x => x.Format == "Noise" &&
            x.RawBytes.SequenceEqual(corrupt));
        Assert.Contains(observed, x => x.Format == "Fixed" &&
            x.ChecksumValid && x.ByteOffset == 38);
        Assert.Equal(corrupt.Length, framer.NoiseBytes);
    }

    [Fact]
    public void IdleGapProducesExplicitPartialNotTrustedFrame()
    {
        var framer = new Ft12IncrementalFramer(2);
        framer.Feed([0x68, 0xE0, 0xE0, 0x68], 88, _ => { });
        var partial = Assert.IsType<Ft12StreamCandidate>(framer.ExpireIncomplete());
        Assert.Equal("Partial", partial.Format);
        Assert.False(partial.ChecksumValid);
        Assert.Equal(88, partial.ByteOffset);
        Assert.Equal(4, framer.NoiseBytes);
        Assert.Equal(0, framer.IncompleteBytes);
        var next = new List<Ft12StreamCandidate>();
        framer.Feed([0xE5], 92, next.Add);
        Assert.Equal("Single", Assert.Single(next).Format);
    }

    [Fact]
    public void AlternateLinkWidthNeedsTwoMatchingValidatedFixedFrames()
    {
        var adaptive = new Ft12IncrementalFramer(1);
        var f = Fixed(2);
        var observed = new List<Ft12StreamCandidate>();
        adaptive.Feed(f, 0, observed.Add);
        adaptive.Feed(f, f.Length, observed.Add);
        var variable = Variable(2, [0x1F, 0x01, 0x03, 0x00, 0x69, 0x00]);
        adaptive.Feed(variable, f.Length * 2, observed.Add);
        Assert.Equal("LinkWidthMismatch", observed[0].Format);
        Assert.Equal("LinkWidthMismatch", observed[1].Format);
        Assert.Equal("Variable", observed[2].Format);
        Assert.Equal(2, observed[2].DetectedLinkAddressWidth);
        Assert.True(observed[2].ChecksumValid);
    }

    [Fact]
    public void StrictModeNeverAutomaticallyChangesLinkAddressProfile()
    {
        var strict = new Ft12IncrementalFramer(1, permitAlternateLinkWidths: false);
        var observed = new List<Ft12StreamCandidate>();
        var alternative = Fixed(2);
        strict.Feed(alternative.Concat(new byte[] { 0xE5 }).ToArray(), 0, observed.Add);
        Assert.DoesNotContain(observed, x => x.Format == "LinkWidthMismatch");
        Assert.DoesNotContain(observed, x => x.DetectedLinkAddressWidth == 2);
        Assert.Contains(observed, x => x.Format == "Single" && x.RawBytes[0] == 0xE5);
    }

    [Fact]
    public void CompleteInvalidEndCanBeReturnedToMasterAsUntrustedFrame()
    {
        var master = new Ft12IncrementalFramer(1, false, true);
        var candidate = Fixed(1);
        candidate[^1] = 0x15;
        var output = new List<Ft12StreamCandidate>();
        master.Feed(candidate, 5, output.Add);
        var result = Assert.Single(output);
        Assert.Equal("Malformed", result.Format);
        Assert.False(result.ChecksumValid);
        Assert.Equal(candidate, result.RawBytes);
    }

    [Fact]
    public void FiftyThousandFt12FramesRemainBoundedAndAccountedFor()
    {
        const int frames = 50_000;
        var valid = Fixed(1);
        var bytes = new byte[frames * valid.Length];
        for (var i = 0; i < frames; i++)
            valid.CopyTo(bytes, i * valid.Length);
        var framer = new Ft12IncrementalFramer(1);
        var count = 0;
        var rejected = 0;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        framer.Feed(bytes, 0, frame =>
        {
            if (frame.Format == "Fixed" && frame.ChecksumValid) count++;
            else rejected++;
        });
        sw.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        _log.WriteLine($"P0-C CORE 50k FT1.2 frames: {sw.Elapsed.TotalMilliseconds:F2} ms; " +
            $"thread allocations={allocated} bytes; per-frame≈{allocated/(double)frames:F1} B");
        Assert.Equal(frames, count);
        Assert.Equal(0, rejected);
        Assert.Equal(0, framer.NoiseBytes);
        Assert.InRange(framer.IncompleteBytes, 0L, 261L);
    }

    private static byte[] Fixed(int width)
    {
        var frame = new byte[4 + width];
        frame[0] = 0x10;
        frame[1] = 0x29;
        for (var i = 0; i < width; i++) frame[2 + i] = i == 0 ? (byte)0x69 : (byte)0;
        frame[^2] = (byte)frame.AsSpan(1, 1 + width).ToArray().Sum(x => x);
        frame[^1] = 0x16;
        return frame;
    }

    private static byte[] Variable(int width, byte[] asdu)
    {
        var length = checked((byte)(1 + width + asdu.Length));
        var frame = new byte[length + 6];
        frame[0] = 0x68;
        frame[1] = length;
        frame[2] = length;
        frame[3] = 0x68;
        frame[4] = 0x28;
        if (width > 0) frame[5] = 0x69;
        if (width > 1) frame[6] = 0x00;
        asdu.CopyTo(frame, 5 + width);
        var sum = 0;
        for (var i = 4; i < 4 + length; i++) sum += frame[i];
        frame[^2] = (byte)sum;
        frame[^1] = 0x16;
        return frame;
    }
}
