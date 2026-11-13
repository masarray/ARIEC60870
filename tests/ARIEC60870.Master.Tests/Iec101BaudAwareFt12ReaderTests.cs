// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>
/// Isolated RX framing tests ported from draft #145. The framer does not own
/// FC/FCB, GI, controls, or event accounting; it measures physical bytes only.
/// </summary>
public sealed class Iec101BaudAwareFt12ReaderTests
{
    [Fact]
    public async Task CompleteVariableFrameAt1200BpsIsNotCutAtFirstByteTimeout()
    {
        var frame = Ft12FrameBuilder.Variable(0x08, 105, new byte[35], 2);
        await using var port = new PacedReceiveTransport(frame, 12);
        var reader = new Ft12StreamReader(port, linkAddressSize: 2, baudRate: 1200);
        var stopwatch = Stopwatch.StartNew();

        var actual = await reader.ReadFrameAsync(250, CancellationToken.None);

        stopwatch.Stop();
        Assert.Equal(frame, actual);
        Assert.Equal(frame.LongLength, reader.ReceivedByteCount);
        Assert.True(stopwatch.ElapsedMilliseconds >= 250,
            "The physical 1200-bps fixture must exceed the original response-start deadline.");
        Assert.Equal(0, reader.ExpiredPartialFrames);
    }

    [Fact]
    public async Task NoPhysicalStartDoesNotExtendResponseStartDeadline()
    {
        await using var port = new PacedReceiveTransport(Array.Empty<byte>(), 10);
        var reader = new Ft12StreamReader(port, linkAddressSize: 2, baudRate: 1200);
        var stopwatch = Stopwatch.StartNew();

        Assert.Null(await reader.ReadFrameAsync(80, CancellationToken.None));

        stopwatch.Stop();
        Assert.InRange(stopwatch.ElapsedMilliseconds, 1, 1500);
        Assert.Equal(0, reader.ReceivedByteCount);
    }

    [Fact]
    public async Task TruncatedFixedReplyUsesBoundedFixedBudgetRatherThanFullVariableBudget()
    {
        await using var port = new PacedReceiveTransport(new byte[] { 0x10 }, 12);
        var reader = new Ft12StreamReader(port, linkAddressSize: 2, baudRate: 1200);
        var stopwatch = Stopwatch.StartNew();

        Assert.Null(await reader.ReadFrameAsync(80, CancellationToken.None));

        stopwatch.Stop();
        Assert.InRange(stopwatch.ElapsedMilliseconds, 1, 1500);
        Assert.Equal(1, reader.ReceivedByteCount);
        Assert.Equal(1, reader.ExpiredPartialFrames);
        Assert.Equal(1, reader.ExpiredPartialBytes);
    }

    [Fact]
    public async Task TimedOutFragmentCannotReappearAsFreshE5Acknowledgement()
    {
        await using var port = new ScriptedReceiveTransport();
        var reader = new Ft12StreamReader(port, linkAddressSize: 1);
        port.Push([0x68, 0x0D]);

        Assert.Null(await reader.ReadFrameAsync(40, CancellationToken.None));
        Assert.Equal(2, reader.ReceivedByteCount);
        Assert.Equal(1, reader.ExpiredPartialFrames);
        Assert.Equal(2, reader.ExpiredPartialBytes);

        port.Push([0xE5]);
        Assert.Null(await reader.ReadFrameAsync(40, CancellationToken.None));
        Assert.Equal(1, reader.AmbiguousShortResponsesSuppressed);

        var addressed = Ft12FrameBuilder.Fixed(0x0B, 105, 1);
        port.Push(addressed);
        Assert.Equal(addressed, await reader.ReadFrameAsync(100, CancellationToken.None));
        port.Push([0xE5]);
        Assert.Equal(new byte[] { 0xE5 }, await reader.ReadFrameAsync(100, CancellationToken.None));
    }

    [Fact]
    public async Task ExternalCancellationExpiresPartialAndPropagates()
    {
        await using var port = new ScriptedReceiveTransport();
        var reader = new Ft12StreamReader(port, linkAddressSize: 1);
        port.Push([0x10, 0x0B]);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await reader.ReadFrameAsync(1000, stop.Token));

        Assert.Equal(1, reader.ExpiredPartialFrames);
        Assert.Equal(2, reader.ExpiredPartialBytes);
        port.Push([0xE5]);
        Assert.Null(await reader.ReadFrameAsync(40, CancellationToken.None));
        Assert.Equal(1, reader.AmbiguousShortResponsesSuppressed);
    }

    private sealed class PacedReceiveTransport : IByteTransport
    {
        private readonly byte[] _bytes;
        private readonly int _delayMs;
        private int _index;
        public bool IsOpen { get; private set; } = true;
        public PacedReceiveTransport(byte[] bytes, int delayMs)
        {
            _bytes = bytes;
            _delayMs = delayMs;
        }
        public ValueTask OpenAsync(CancellationToken token)
        {
            IsOpen = true;
            return ValueTask.CompletedTask;
        }
        public ValueTask CloseAsync(CancellationToken token)
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
            => throw new InvalidOperationException("RX-only fixture cannot TX.");
        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token)
        {
            await Task.Delay(_delayMs, token);
            if (_index >= _bytes.Length) return 0;
            buffer.Span[0] = _bytes[_index++];
            return 1;
        }
        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScriptedReceiveTransport : IByteTransport
    {
        private readonly Queue<byte> _input = new();
        public bool IsOpen { get; private set; } = true;
        public void Push(ReadOnlySpan<byte> bytes)
        {
            foreach (var b in bytes) _input.Enqueue(b);
        }
        public ValueTask OpenAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CloseAsync(CancellationToken token)
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
            => throw new InvalidOperationException("RX-only fixture cannot TX.");
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_input.Count == 0) return ValueTask.FromResult(0);
            buffer.Span[0] = _input.Dequeue();
            return ValueTask.FromResult(1);
        }
        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
