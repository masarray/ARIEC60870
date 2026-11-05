// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class CanonicalFt12StreamReaderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FragmentedSerialResponsesUseCoreFramerWithoutAnyTransmit(int width)
    {
        using var transport = new ByteScriptTransport();
        var first = Fixed(width);
        var second = Variable(width);
        transport.Push(new byte[] { 0x99, 0x00, 0xFF }.Concat(first).Concat(second).ToArray());
        var reader = new Ft12StreamReader(transport, width);
        Assert.Equal(first, await reader.ReadFrameAsync(400, CancellationToken.None));
        Assert.Equal(second, await reader.ReadFrameAsync(400, CancellationToken.None));
        Assert.Equal(0, transport.Writes);
        Assert.True(Ft12WireLayout.TryParse(first, width, out _));
        Assert.True(Ft12WireLayout.TryParse(second, width, out _));
    }

    [Fact]
    public async Task MalformedVariableHeaderDoesNotConsumeFollowingValidResponse()
    {
        using var transport = new ByteScriptTransport();
        var valid = Fixed(1);
        transport.Push(new byte[] { 0x68, 0x10, 0x11, 0x68, 0x99 }
            .Concat(valid).ToArray());
        var reader = new Ft12StreamReader(transport);
        Assert.Equal(valid, await reader.ReadFrameAsync(400, CancellationToken.None));
    }

    [Fact]
    public async Task BrokenChecksumAndEndReachExistingSecondaryValidatorAsUntrustedRaw()
    {
        using var transport = new ByteScriptTransport();
        var checksum = Fixed(1);
        checksum[^2] ^= 0x45;
        var end = Fixed(1);
        end[^1] = 0x44;
        transport.Push(checksum.Concat(end).ToArray());
        var reader = new Ft12StreamReader(transport);
        Assert.Equal(checksum, await reader.ReadFrameAsync(400, CancellationToken.None));
        Assert.Equal(end, await reader.ReadFrameAsync(400, CancellationToken.None));
        Assert.False(Ft12Parser.ForIec101(1).Decode(checksum).IsChecksumValid);
        Assert.False(Ft12Parser.ForIec101(1).Decode(end).IsLengthValid);
    }

    [Fact]
    public async Task TimeoutDropsStalePartialBeforeNextMasterExchange()
    {
        using var transport = new ByteScriptTransport();
        transport.Push([0x68, 0x80, 0x80, 0x68, 0x00]);
        var reader = new Ft12StreamReader(transport, 1);
        Assert.Null(await reader.ReadFrameAsync(35, CancellationToken.None));
        var valid = Fixed(1);
        transport.Push(valid);
        Assert.Equal(valid, await reader.ReadFrameAsync(400, CancellationToken.None));
    }

    [Fact]
    public async Task EmbeddedE5WithinDamagedFrameCannotMasqueradeAsMasterLinkAck()
    {
        using var transport = new ByteScriptTransport();
        var valid = Fixed(1);
        // A bad duplicate-length header followed by a data byte E5 must
        // not advance the active IEC101 link merely because resync saw E5.
        transport.Push(new byte[] { 0x68, 0x01, 0x03, 0x68, 0xE5 }
            .Concat(valid).ToArray());
        var reader = new Ft12StreamReader(transport);
        Assert.Equal(valid, await reader.ReadFrameAsync(400, CancellationToken.None));
        Assert.Equal(1, reader.AmbiguousShortResponsesSuppressed);
    }

    [Fact]
    public async Task BenignConverterNoiseDoesNotSuppressAnActualSingleE5()
    {
        using var transport = new ByteScriptTransport();
        transport.Push([0x55, 0x99, 0xE5]);
        var reader = new Ft12StreamReader(transport);
        Assert.Equal(new byte[] { 0xE5 },
            await reader.ReadFrameAsync(400, CancellationToken.None));
        Assert.Equal(0, reader.AmbiguousShortResponsesSuppressed);
    }

    [Fact]
    public async Task CallerCancellationPropagatesInsteadOfReportingOrdinaryTimeout()
    {
        using var transport = new ByteScriptTransport();
        var reader = new Ft12StreamReader(transport);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await reader.ReadFrameAsync(500, stop.Token));
    }

    private static byte[] Fixed(int width)
    {
        var bytes = new byte[4 + width];
        bytes[0] = 0x10;
        bytes[1] = 0x29;
        if (width > 0) bytes[2] = 0x69;
        if (width > 1) bytes[3] = 0;
        var sum = 0;
        for (var i = 1; i <= 1 + width; i++) sum += bytes[i];
        bytes[^2] = (byte)sum;
        bytes[^1] = 0x16;
        return bytes;
    }

    private static byte[] Variable(int width)
    {
        var asdu = new byte[] { 0x1F, 1, 3, 0, 0x69, 0, 0xF6, 3, 0xFF, 2 };
        var length = checked((byte)(1 + width + asdu.Length));
        var bytes = new byte[length + 6];
        bytes[0] = 0x68; bytes[1] = length; bytes[2] = length; bytes[3] = 0x68;
        bytes[4] = 0x28;
        if (width > 0) bytes[5] = 0x69;
        asdu.CopyTo(bytes, 5 + width);
        var sum = 0;
        for (var i = 4; i < 4 + length; i++) sum += bytes[i];
        bytes[^2] = (byte)sum;
        bytes[^1] = 0x16;
        return bytes;
    }

    private sealed class ByteScriptTransport : IByteTransport
    {
        private readonly Queue<byte> _pending = new();
        public int Writes { get; private set; }
        public bool IsOpen => true;
        public void Push(byte[] bytes)
        {
            foreach (var b in bytes) _pending.Enqueue(b);
        }
        public ValueTask OpenAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CloseAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            Writes++;
            throw new InvalidOperationException("RX-only test must not transmit.");
        }
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_pending.Count == 0) return ValueTask.FromResult(0);
            buffer.Span[0] = _pending.Dequeue();
            return ValueTask.FromResult(1);
        }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
