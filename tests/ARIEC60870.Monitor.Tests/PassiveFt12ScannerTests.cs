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
        Assert.Equal(3, events.Count);
        Assert.Equal("Noise", events[0].Format);
        Assert.Equal(new byte[] { 0x55 }, events[0].RawBytes);
        Assert.Equal(201, events[1].ByteOffset);
        Assert.True(events[1].ChecksumValid);
        Assert.Equal("Fixed", events[1].Format);
        Assert.False(events[2].ChecksumValid);
        Assert.Equal(1, scanner.NoiseBytes);
    }

    [Fact]
    public void ValidTwoOctetLinkCheckIsNeverSilentlyDiscardedWhenSetupIsOneOctet()
    {
        var scanner = new PassiveFt12Scanner(1);
        var frames = new List<PassiveFt12Frame>();
        var wire = new byte[] { 0x10, 0x49, 0x69, 0x01, 0xB3, 0x16 };
        for (var i = 0; i < wire.Length; i++)
            scanner.Feed(wire.AsSpan(i, 1), i + 200, frames.Add);
        var detected = Assert.Single(frames);
        Assert.Equal("LinkWidthMismatch", detected.Format);
        Assert.True(detected.ChecksumValid);
        Assert.Equal(2, detected.DetectedLinkAddressWidth);
        Assert.Equal(200, detected.ByteOffset);
        Assert.Equal(wire, detected.RawBytes);
        Assert.Equal(0, scanner.NoiseBytes);
    }

    [Fact]
    public void NoiseAndMalformedPrefixRemainVisibleBeforeValidLinkStatusAndShortAck()
    {
        var scanner = new PassiveFt12Scanner(1);
        var frames = new List<PassiveFt12Frame>();
        var wire = new byte[] { 0x99, 0x88, 0x68, 0x01, 0x00, 0x10,
                                0x49, 0x69, 0xB2, 0x16, 0xE5 };
        scanner.Feed(wire, 70, frames.Add);
        Assert.Equal(new[] { "Noise", "Fixed", "Single" },
            frames.Select(x => x.Format));
        Assert.Equal(new byte[] { 0x99, 0x88, 0x68, 0x01, 0x00 }, frames[0].RawBytes);
        Assert.Equal(70, frames[0].ByteOffset);
        Assert.Equal(75, frames[1].ByteOffset);
        Assert.Equal(80, frames[2].ByteOffset);
        Assert.True(frames[1].ChecksumValid);
        Assert.True(frames[2].ChecksumValid);
    }


    [Fact]
    public void FieldTwoByteLinkWidthIsConfirmedBeforeDecodingSpontaneousEventFrame()
    {
        // Field sequence: IEC101 105 (69 00), Setup accidentally has width=1.
        // A single foreign-width fixed frame must not rewrite ASDU offsets.
        var scanner = new PassiveFt12Scanner(1);
        var observed = new List<PassiveFt12Frame>();
        byte[] status = [0x10, 0x0B, 0x69, 0x00, 0x74, 0x16];
        byte[] ack = [0x10, 0x00, 0x69, 0x00, 0x69, 0x16];
        byte[] payload =
        [
            0x28, 0x69, 0x00, // secondary user-data control + 2-byte Link Address
            0x1F, 0x01, 0x03, 0x00, 0x69, 0x00, // Type 31, VSQ 1, spontaneous, CA 105
            0xF6, 0x03, 0xFF, 0x02, // 3-byte IOA and DP=ON
            0x2A, 0xAB, 0x04, 0x11, 0x85, 0x0B, 0x1A // CP56 Nov 5 2026
        ];
        byte length = (byte)payload.Length;
        var variable = new byte[] { 0x68, length, length, 0x68 }.Concat(payload)
            .Concat(new byte[] { (byte)(payload.Sum(x => x) & 0xFF), 0x16 })
            .ToArray();
        scanner.Feed(status, 0, observed.Add);
        scanner.Feed(variable, 6, observed.Add);
        Assert.Null(observed[^1].DetectedLinkAddressWidth);
        scanner.Feed(ack, 6 + variable.Length, observed.Add);
        scanner.Feed(variable, 12 + variable.Length, observed.Add);
        Assert.Equal("LinkWidthMismatch", observed[0].Format);
        Assert.Equal("LinkWidthMismatch", observed[2].Format);
        var corrected = observed[^1];
        Assert.Equal("Variable", corrected.Format);
        Assert.True(corrected.ChecksumValid);
        Assert.Equal(2, corrected.DetectedLinkAddressWidth);
        Assert.Equal(variable, corrected.RawBytes);
    }

    [Fact]
    public void CorruptAlternateWidthFixedFrameCannotChangeAsduInterpretation()
    {
        var scanner = new PassiveFt12Scanner(1);
        var frames = new List<PassiveFt12Frame>();
        scanner.Feed([0x10, 0x0B, 0x69, 0x00, 0x75, 0x16], 0, frames.Add);
        scanner.Feed([0x10, 0x0B, 0x69, 0x00, 0x74, 0x16], 6, frames.Add);
        Assert.Single(frames, f => f.DetectedLinkAddressWidth == 2);
        Assert.DoesNotContain(frames, f => f.Format == "Variable");
    }
    [Fact]
    public void NoisyInputIsBoundedIntoFramesInsteadOfOneUiRowPerByte()
    {
        var scanner = new PassiveFt12Scanner(1);
        var frames = new List<PassiveFt12Frame>();
        var garbage = Enumerable.Repeat((byte)0x77, 512).ToArray();
        scanner.Feed(garbage, 0, frames.Add);
        Assert.Equal(4, frames.Count);
        Assert.All(frames, frame =>
        {
            Assert.Equal("Noise", frame.Format);
            Assert.Equal(128, frame.RawBytes.Length);
            Assert.False(frame.ChecksumValid);
        });
        Assert.Equal(512, scanner.NoiseBytes);
    }

    [Fact]
    public void A2IsRetainedAsSingleByteButIsNotAnIec101Reply()
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
