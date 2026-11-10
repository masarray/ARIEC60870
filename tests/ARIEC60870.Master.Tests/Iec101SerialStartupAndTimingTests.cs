// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using ARIEC60870.Master;
using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101SerialStartupAndTimingTests
{
    private static Iec103MasterSettings Settings() => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec101,
        PortName = "TEST-COM-A",
        BaudRate = 1200,
        LinkAddress = 105,
        LinkAddressSize = 2,
        CommonAddress = 105,
        ResponseTimeoutMs = 80,
        StartupDelayMs = 220,
        ResetRemoteLinkOnConnect = false,
        ResetFcbOnConnect = false,
        SendClockSyncOnConnect = false,
        SendGeneralInterrogationOnConnect = false
    };

    [Fact]
    public async Task SingleLinkHonorsConfiguredComSettlingBeforeFirstTransmit()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var port = new FirstWriteProbeTransport(() => stop.Cancel());
        var session = new Iec101MasterSession(Settings(), port);

        await session.RunAsync(stop.Token);

        Assert.True(port.FirstWriteUtc.HasValue, "The status request was never transmitted.");
        Assert.True(port.FirstWriteUtc.Value - port.OpenedUtc >= TimeSpan.FromMilliseconds(175),
            "IEC-101 sent a status request before the configured 220 ms COM settling interval.");
    }

    [Fact]
    public async Task DualLinkHonorsSettlingAfterBothPortsOpenedBeforeFirstStatusProbe()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var settings = Settings();
        await using var linkA = new FirstWriteProbeTransport(() => stop.Cancel());
        await using var linkB = new FirstWriteProbeTransport(() => stop.Cancel());
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "A", PortName = "TEST-COM-A", LinkAddress = 105 },
            LinkB = new Iec101DualLinkEndpoint { Name = "B", PortName = "TEST-COM-B", LinkAddress = 105 }
        };
        var session = new Iec101DualLinkRedundancySession(options, linkA, linkB);

        await session.RunAsync(stop.Token);

        var firstWrite = new[] { linkA.FirstWriteUtc, linkB.FirstWriteUtc }
            .Where(value => value.HasValue).Select(value => value!.Value).OrderBy(value => value).FirstOrDefault();
        Assert.NotEqual(default, firstWrite);
        Assert.True(firstWrite - linkB.OpenedUtc >= TimeSpan.FromMilliseconds(175),
            "Dual-link status probe began before the second COM had settled.");
    }

    [Fact]
    public async Task Slow1200BpsVariableReplyCanFinishPastResponseStartTimeout()
    {
        var reply = Ft12FrameBuilder.Variable(0x08, 105, new byte[35], 2);
        await using var port = new PacedReceiveTransport(reply, 12);
        var reader = new Ft12StreamReader(port, 2, baudRate: 1200);
        var elapsed = Stopwatch.StartNew();

        var actual = await reader.ReadFrameAsync(250, CancellationToken.None);

        elapsed.Stop();
        Assert.Equal(reply, actual);
        Assert.True(elapsed.ElapsedMilliseconds >= 250,
            "Fixture must exceed the old whole-frame 250 ms deadline.");
    }

    [Fact]
    public async Task SerialSilenceStillObeysOriginalDeadlineAt1200Bps()
    {
        await using var port = new PacedReceiveTransport(Array.Empty<byte>(), 10);
        var reader = new Ft12StreamReader(port, 2, baudRate: 1200);
        var elapsed = Stopwatch.StartNew();

        Assert.Null(await reader.ReadFrameAsync(80, CancellationToken.None));

        Assert.True(elapsed.ElapsedMilliseconds < 1500,
            "No physical start byte means no extended frame-completion deadline.");
    }

    private sealed class FirstWriteProbeTransport : IByteTransport
    {
        private readonly Action _onFirstWrite;
        public FirstWriteProbeTransport(Action onFirstWrite) => _onFirstWrite = onFirstWrite;
        public DateTimeOffset OpenedUtc { get; private set; }
        public DateTimeOffset? FirstWriteUtc { get; private set; }
        public bool IsOpen { get; private set; }
        public ValueTask OpenAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            OpenedUtc = DateTimeOffset.UtcNow;
            IsOpen = true;
            return ValueTask.CompletedTask;
        }
        public ValueTask CloseAsync(CancellationToken token)
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!FirstWriteUtc.HasValue)
            {
                FirstWriteUtc = DateTimeOffset.UtcNow;
                _onFirstWrite();
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask<int> ReadAsync(Memory<byte> bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }
        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync() { IsOpen = false; return ValueTask.CompletedTask; }
    }

    private sealed class PacedReceiveTransport : IByteTransport
    {
        private readonly byte[] _bytes;
        private readonly int _spacingMs;
        private int _offset;
        public PacedReceiveTransport(byte[] bytes, int spacingMs)
        {
            _bytes = bytes;
            _spacingMs = spacingMs;
        }
        public bool IsOpen => true;
        public ValueTask OpenAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CloseAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
            => throw new InvalidOperationException("RX-only fixture must never transmit.");
        public async ValueTask<int> ReadAsync(Memory<byte> bytes, CancellationToken token)
        {
            await Task.Delay(_spacingMs, token).ConfigureAwait(false);
            if (_offset >= _bytes.Length) return 0;
            bytes.Span[0] = _bytes[_offset++];
            return 1;
        }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
