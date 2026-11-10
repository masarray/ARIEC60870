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
    public async Task E5StatusReplyCannotBootstrapGi()
    {
        var settings = Settings();
        settings.StartupDelayMs = 0;
        settings.ResponseTimeoutMs = 60;
        settings.SendGeneralInterrogationOnConnect = true;
        await using var transport = new E5StatusOnlyTransport();
        var result = await new Iec101MasterSession(settings, transport)
            .RunForAsync(TimeSpan.FromMilliseconds(380), CancellationToken.None);
        Assert.True(transport.StatusRequests > 0);
        Assert.Equal(0, result.Counters.GiCommands);
        Assert.DoesNotContain(result.Events, e => e.Summary == "IEC-101 secondary link verified");
        Assert.Contains(result.Findings, e => e.Id == "IEC101-RX-UNEXPECTED-E5-STATUS");
    }

    [Fact]
    public async Task DualLinkE5StatusCannotProveHealthy()
    {
        var settings = Settings();
        settings.StartupDelayMs = 0;
        settings.ResponseTimeoutMs = 80;
        settings.SendGeneralInterrogationOnConnect = true;
        await using var a = new E5StatusOnlyTransport();
        await using var b = new E5StatusOnlyTransport();
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "A", PortName = "TEST-A", LinkAddress = 105 },
            LinkB = new Iec101DualLinkEndpoint { Name = "B", PortName = "TEST-B", LinkAddress = 105 }
        };
        var session = new Iec101DualLinkRedundancySession(options, a, b);
        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        var snapshot = session.CreateSnapshot();
        Assert.True(a.StatusRequests > 0);
        Assert.True(b.StatusRequests > 0);
        Assert.False(snapshot.LinkA.IsHealthy);
        Assert.False(snapshot.LinkB.IsHealthy);
        Assert.Equal(0, result.Counters.GiCommands);
    }

    [Fact]
    public void SerialWriteTimeoutBudgetCovers1200BpsFullFrames()
    {
        var settings = Settings();
        settings.ResponseTimeoutMs = 120;
        Assert.True(settings.SerialWriteTimeoutMs >= 3620);
        settings.BaudRate = 9600;
        Assert.Equal(2000, settings.SerialWriteTimeoutMs);
        settings.ResponseTimeoutMs = 6500;
        Assert.Equal(6500, settings.SerialWriteTimeoutMs);
    }

    [Fact]
    public async Task IncompleteFixedReplyDoesNotConsumeMaximumVariableFrameWindow()
    {
        await using var port = new PacedReceiveTransport([0x10], 12);
        var reader = new Ft12StreamReader(port, 2, baudRate: 1200);
        var elapsed = Stopwatch.StartNew();

        Assert.Null(await reader.ReadFrameAsync(80, CancellationToken.None));

        Assert.True(elapsed.ElapsedMilliseconds < 1500,
            "A truncated fixed telegram must use its small wire budget, not a 262-byte variable budget.");
    }

    [Fact]
    public async Task ZeroRxFramesEmitsTransportLayerFindingWithoutSendingGi()
    {
        var settings = Settings();
        settings.StartupDelayMs = 0;
        settings.ResponseTimeoutMs = 45;
        settings.SendGeneralInterrogationOnConnect = true;
        await using var port = new NoRxEvidenceTransport();
        var result = await new Iec101MasterSession(settings, port)
            .RunForAsync(TimeSpan.FromMilliseconds(900), CancellationToken.None);

        Assert.True(port.ExchangeObservations >= 2);
        Assert.Equal(0, result.Counters.RxFrames);
        Assert.Equal(0, result.Counters.GiCommands);
        Assert.Contains(result.Findings, f => f.Id == "IEC101-NO-COMPLETE-RX-FRAMES");
        Assert.Contains(result.Events, e => e.PollingReason == "TEST-NO-RX-OUTCOME");
    }

    [Fact]
    public async Task T02SilenceRetriesTheExactOriginalStatusFrameOnlyOncePerExchange()
    {
        var settings = Settings();
        settings.StartupDelayMs = 0;
        settings.ResponseTimeoutMs = 45;
        settings.SendGeneralInterrogationOnConnect = true;
        await using var port = new NoRxEvidenceTransport();
        var result = await new Iec101MasterSession(settings, port)
            .RunForAsync(TimeSpan.FromMilliseconds(520), CancellationToken.None);

        // Initial unbalanced status FC9 is a read: after silence it is
        // retransmitted without recomputing FCB/address/checksum.
        Assert.True(port.SentFrames.Count >= 2, "No read retransmission was transmitted.");
        Assert.Equal(port.SentFrames[0], port.SentFrames[1]);
        Assert.Equal(9, port.SentFrames[0][1] & 0x0F);
        Assert.Contains(result.Events, e =>
            e.Direction == ARIEC60870.Core.Model.FrameDirection.MasterToSlave &&
            e.PollingReason.Contains("bounded unchanged-FCB read retry #1",
                StringComparison.Ordinal));
        Assert.Equal(0, result.Counters.GiCommands); // no blind FC3/GI replay
        Assert.Equal(0, result.Counters.RxFrames);
        Assert.True(result.Counters.Timeouts > 0);
        Assert.True(result.Counters.Timeouts < result.Counters.TxFrames,
            "A two-attempt read must not report two separate terminal timeout failures.");
    }

    [Fact]
    public async Task T02SingleLinkRecoversFirstSilentStatusWithoutFalseTerminalTimeout()
    {
        var settings = Settings();
        settings.StartupDelayMs = 0;
        settings.ResponseTimeoutMs = 60;
        await using var port = new SecondAttemptStatusResponder();
        var result = await new Iec101MasterSession(settings, port)
            .RunForAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        Assert.True(port.StatusWrites >= 2);
        Assert.True(port.Transmissions.Count >= 2);
        Assert.Equal(port.Transmissions[0], port.Transmissions[1]);
        Assert.Equal(9, port.Transmissions[0][1] & 0x0F);
        Assert.True(result.Counters.RxFrames > 0,
            "Second unchanged FC9 request must receive an addressed FC11 status.");
        Assert.Equal(0, result.Counters.Timeouts);
        Assert.Contains(result.Events, e => e.PollingReason.Contains(
            "bounded unchanged-FCB read retry #1", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Events, e => e.Summary.Contains(
            "response timeout", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task T02PartialPhysicalRxSuppressesRetryEvenWithoutCompleteFt12Frame()
    {
        var settings = Settings();
        settings.StartupDelayMs = 0;
        settings.ResponseTimeoutMs = 65;
        await using var port = new IncompleteStatusResponderTransport();
        var result = await new Iec101MasterSession(settings, port)
            .RunForAsync(TimeSpan.FromMilliseconds(470), CancellationToken.None);

        Assert.True(port.ReadOctets > 0, "Fixture did not deliver an incomplete FT1.2 start.");
        Assert.True(port.Transmissions > 0);
        Assert.DoesNotContain(result.Events, e => e.PollingReason.Contains(
            "bounded unchanged-FCB read retry #1", StringComparison.Ordinal));
        Assert.True(result.Counters.Timeouts > 0);
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



    // Delivers a *partial physical response* after every status request. No
    // complete frame exists, but the raw octet must forbid a blind T02 retry.
    private sealed class IncompleteStatusResponderTransport : IByteTransport
    {
        private bool _pendingPartial;
        public int ReadOctets { get; private set; }
        public int Transmissions { get; private set; }
        public bool IsOpen { get; private set; }

        public ValueTask OpenAsync(CancellationToken token) { IsOpen = true; return ValueTask.CompletedTask; }
        public ValueTask CloseAsync(CancellationToken token) { IsOpen = false; return ValueTask.CompletedTask; }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            Transmissions++;
            if (bytes.Length > 1 && bytes.Span[0] == 0x10 && (bytes.Span[1] & 0x0F) == 9)
                _pendingPartial = true;
            return ValueTask.CompletedTask;
        }
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token)
        {
            if (_pendingPartial)
            {
                _pendingPartial = false;
                buffer.Span[0] = 0x10;
                ReadOctets++;
                return ValueTask.FromResult(1);
            }
            return ValueTask.FromResult(0);
        }
        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync() { IsOpen = false; return ValueTask.CompletedTask; }
    }

    // Independent deterministic peer: first link-status FC9 gets no response,
    // byte-identical retry gets addressed FC11; later polls get permitted E5.
    private sealed class SecondAttemptStatusResponder : IByteTransport
    {
        private readonly Queue<byte> _rx = new();
        public List<byte[]> Transmissions { get; } = new();
        public int StatusWrites { get; private set; }
        public bool IsOpen { get; private set; }

        public ValueTask OpenAsync(CancellationToken token)
        {
            IsOpen = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken token)
        {
            IsOpen = false;
            _rx.Clear();
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            var original = bytes.ToArray();
            Transmissions.Add(original);
            if (original.Length > 2 && original[0] == 0x10)
            {
                var functionCode = original[1] & 0x0F;
                if (functionCode == 9)
                {
                    if (++StatusWrites > 1)
                        foreach (var octet in Convert.FromHexString("100B69007416"))
                            _rx.Enqueue(octet);
                }
                else if (functionCode is 10 or 11)
                    _rx.Enqueue(0xE5);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken token)
        {
            var count = 0;
            while (count < destination.Length && _rx.Count > 0)
                destination.Span[count++] = _rx.Dequeue();
            return ValueTask.FromResult(count);
        }

        public void Dispose() { IsOpen = false; _rx.Clear(); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class NoRxEvidenceTransport : IByteTransport, ITransportDiagnosticSource, ITransportExchangeObserver
    {
        private readonly Queue<TransportDiagnostic> _events = new();
        public bool IsOpen { get; private set; }
        public int ExchangeObservations { get; private set; }
        public List<byte[]> SentFrames { get; } = new();
        public ValueTask OpenAsync(CancellationToken ct) { IsOpen = true; return ValueTask.CompletedTask; }
        public ValueTask CloseAsync(CancellationToken ct) { IsOpen = false; return ValueTask.CompletedTask; }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            SentFrames.Add(data.ToArray());
            return ValueTask.CompletedTask;
        }
        public ValueTask<int> ReadAsync(Memory<byte> data, CancellationToken ct) => ValueTask.FromResult(0);
        public void ObserveExchangeOutcome(int functionCode, bool acceptedSecondary, int decodedRxFrames, int rejectedRxFrames)
        {
            ExchangeObservations++;
            Assert.False(acceptedSecondary);
            Assert.Equal(0, decodedRxFrames);
            Assert.Equal(0, rejectedRxFrames);
            _events.Enqueue(new TransportDiagnostic
            {
                Code = "TEST-NO-RX-OUTCOME", Severity = "Info", Source = "Fixture",
                Message = "Zero physical FT1.2 frame", Detail = $"FC={functionCode}"
            });
        }
        public IReadOnlyList<TransportDiagnostic> DrainDiagnostics()
        {
            var list = _events.ToArray();
            _events.Clear();
            return list;
        }
        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync() { IsOpen = false; return ValueTask.CompletedTask; }
    }

    private sealed class E5StatusOnlyTransport : IByteTransport
    {
        private readonly Queue<byte> _rx = new();
        public int StatusRequests { get; private set; }
        public bool IsOpen { get; private set; }
        public ValueTask OpenAsync(CancellationToken token) { IsOpen = true; return ValueTask.CompletedTask; }
        public ValueTask CloseAsync(CancellationToken token) { IsOpen = false; return ValueTask.CompletedTask; }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (bytes.Length >= 2 && bytes.Span[0] == 0x10 && (bytes.Span[1] & 0x0F) == 9)
            {
                StatusRequests++;
                _rx.Enqueue(0xE5);
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask<int> ReadAsync(Memory<byte> bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_rx.Count == 0) return ValueTask.FromResult(0);
            bytes.Span[0] = _rx.Dequeue();
            return ValueTask.FromResult(1);
        }
        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync() { IsOpen = false; return ValueTask.CompletedTask; }
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
