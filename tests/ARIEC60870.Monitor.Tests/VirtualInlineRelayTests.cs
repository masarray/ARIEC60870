// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.IO.Ports;
using System.Security.Cryptography;
using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class VirtualInlineRelayTests
{
    private static readonly PassiveSerialSource Master =
        new("COM12", "Virtual Master -> Slave", 9600, Parity.Even);
    private static readonly PassiveSerialSource Slave =
        new("COM13", "Virtual Slave -> Master", 9600, Parity.Even);

    [Theory]
    [InlineData(2048)]
    [InlineData(5000)]
    [InlineData(50000)]
    public void ConcurrentDuplexPreservesBinaryBytesAndOriginalIngress(int byteCount)
    {
        using var a = new MemoryEndpoint("COM12");
        using var b = new MemoryEndpoint("COM13");
        using var relay = new VirtualInlineRelaySession(Master, Slave, a, b);
        var taps = relay.CreateMonitorTaps();
        taps[0].Open();
        taps[1].Open();

        byte[] outboundMaster = Enumerable.Range(0, byteCount)
            .Select(i => (byte)((i * 71 + 19) & 255)).ToArray();
        byte[] outboundSlave = Enumerable.Range(0, byteCount)
            .Select(i => (byte)((i * 97 + 43) & 255)).ToArray();

        // 00..FF, arbitrary binary, and randomized small/large chunk boundaries.
        PushChunks(a, outboundMaster);
        PushChunks(b, outboundSlave);
        byte[] receivedSlave = TakeChunks(b, outboundMaster.Length);
        byte[] receivedMaster = TakeChunks(a, outboundSlave.Length);
        Assert.Equal(SHA256.HashData(outboundMaster), SHA256.HashData(receivedSlave));
        Assert.Equal(SHA256.HashData(outboundSlave), SHA256.HashData(receivedMaster));
        Assert.Equal(outboundMaster, receivedSlave);
        Assert.Equal(outboundSlave, receivedMaster);

        Assert.Equal(outboundMaster, TakeObserved(taps[0], byteCount));
        Assert.Equal(outboundSlave, TakeObserved(taps[1], byteCount));
        var p = relay.GetProgress();
        Assert.Equal(byteCount, p.MasterToSlave.IngressBytes);
        Assert.Equal(byteCount, p.SlaveToMaster.IngressBytes);
        Assert.Equal(byteCount, p.MasterToSlave.WriteCompletedBytes);
        Assert.Equal(byteCount, p.SlaveToMaster.WriteCompletedBytes);
        Assert.Equal(byteCount, p.MasterToSlave.ConsumedBytes);
        Assert.Equal(byteCount, p.SlaveToMaster.ConsumedBytes);
        Assert.Equal(0, p.MasterToSlave.WriteUncertainBytes + p.SlaveToMaster.WriteUncertainBytes);
        Assert.Equal(0, p.MasterToSlave.CaptureRejectedBytes + p.SlaveToMaster.CaptureRejectedBytes);
        taps[0].Close();
        taps[1].Close();
        Assert.True(a.WasClosed && b.WasClosed);
    }

    [Fact]
    public async Task VirtualIngressUsesProductionMonitorFramerAndOriginalEvidence()
    {
        using var a = new MemoryEndpoint("COM12");
        using var b = new MemoryEndpoint("COM13");
        using var relay = new VirtualInlineRelaySession(Master, Slave, a, b);
        var opts = new PassiveCaptureOptions(string.Empty, Master, Slave,
            RecordFullSession: false, CaptureSource: "Virtual Inline (VSPE)");
        var monitor = new PassiveMonitorSession(opts, relay.CreateMonitorTaps());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var running = monitor.RunAsync(stop.Token);

        await UntilAsync(() => a.IsOpen && b.IsOpen);
        byte[] request = [0x10, 0x5B, 0x69, 0xC4, 0x16];
        byte[] response = [0x10, 0x29, 0x69, 0x92, 0x16];
        a.Inject(request);
        b.Inject(response);
        Assert.Equal(request, b.TakeOutbound());
        Assert.Equal(response, a.TakeOutbound());
        await UntilAsync(() => monitor.GetProgress().ValidFt12Frames >= 2);
        stop.Cancel();
        var result = await running;

        Assert.Equal("Virtual Inline (VSPE)", result.Manifest.CaptureSource);
        Assert.Equal("MonitorStopped", result.Manifest.Status);
        Assert.Equal(request.Length, monitor.GetProgress().ScadaBytes);
        Assert.Equal(response.Length, monitor.GetProgress().RtuBytes);
        Assert.Equal(2, monitor.GetProgress().ValidFt12Frames);
        Assert.Equal(2, monitor.GetProgress().StreamCount);
        var frames = new List<PassiveObservedFrame>();
        while (monitor.TryReadFrame(out var frame)) frames.Add(frame);
        Assert.Contains(frames, f => f.StreamId == 0 && f.ChecksumValid &&
            f.Format == "Fixed" && f.RawBytes.SequenceEqual(request));
        Assert.Contains(frames, f => f.StreamId == 1 && f.ChecksumValid &&
            f.Format == "Fixed" && f.RawBytes.SequenceEqual(response));
        Assert.False(Directory.Exists(result.Directory));
    }

    [Fact]
    public async Task VirtualDriverFailureAutomaticallyEndsMonitorAndBothRelayDirections()
    {
        using var a = new MemoryEndpoint("COM12");
        using var b = new MemoryEndpoint("COM13") { FailOnWrite = true };
        using var relay = new VirtualInlineRelaySession(Master, Slave, a, b);
        var options = new PassiveCaptureOptions(string.Empty, Master, Slave,
            RecordFullSession: false, CaptureSource: "Virtual Inline (VSPE)");
        var monitor = new PassiveMonitorSession(options, relay.CreateMonitorTaps());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var running = monitor.RunAsync(timeout.Token);
        await UntilAsync(() => a.IsOpen && b.IsOpen);
        a.Inject([0x10, 0x5B, 0x69, 0xC4, 0x16]);
        // The relay fault MUST automatically terminate BOTH Monitor readers,
        // without relying on a WPF timer or operator Stop click.
        var completed = await running.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(timeout.IsCancellationRequested);
        Assert.Equal("IntegrityUncertain", completed.Manifest.Status);
        Assert.Contains(completed.Manifest.Warnings, warning =>
            warning.Contains("Duplex relay and both Monitor directions are stopping",
                StringComparison.Ordinal));
        Assert.Equal("Faulted", relay.GetProgress().Status);
        Assert.True(a.WasClosed && b.WasClosed);
        Assert.Equal(1, b.WriteCallCount);
    }

    [Fact]
    public async Task MonitorCancellationStopsActiveVirtualForwardingImmediately()
    {
        using var a = new MemoryEndpoint("COM12");
        using var b = new MemoryEndpoint("COM13");
        using var relay = new VirtualInlineRelaySession(Master, Slave, a, b);
        var options = new PassiveCaptureOptions(string.Empty, Master, Slave,
            RecordFullSession: false, CaptureSource: "Virtual Inline (VSPE)");
        var monitor = new PassiveMonitorSession(options, relay.CreateMonitorTaps());

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var running = monitor.RunAsync(stop.Token);
        await UntilAsync(() => a.IsOpen && b.IsOpen);
        stop.Cancel();
        // The cancellation callback is synchronous. Do not allow forwarding
        // to continue until journal writer completion or a delayed UI tick.
        Assert.True(a.WasClosed && b.WasClosed);
        var completed = await running.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("MonitorStopped", completed.Manifest.Status);
        Assert.Equal("Stopped", relay.GetProgress().Status);
    }

    [Fact]
    public void SecondPortOpenFailureClosesFirstWithoutStartingRelay()
    {
        using var a = new MemoryEndpoint("COM12");
        using var b = new MemoryEndpoint("COM13") { FailOnOpen = true };
        using var relay = new VirtualInlineRelaySession(Master, Slave, a, b);
        Assert.Throws<IOException>(() => relay.CreateMonitorTaps()[0].Open());
        Assert.True(a.WasClosed);
        Assert.Equal("Faulted", relay.GetProgress().Status);
        Assert.Equal(0, relay.GetProgress().MasterToSlave.IngressBytes);
    }

    [Fact]
    public async Task AmbiguousWriteFailureStopsBothSidesWithoutRetry()
    {
        using var a = new MemoryEndpoint("COM12");
        using var b = new MemoryEndpoint("COM13") { FailOnWrite = true };
        using var relay = new VirtualInlineRelaySession(Master, Slave, a, b);
        var taps = relay.CreateMonitorTaps();
        taps[0].Open();
        taps[1].Open();
        a.Inject([0xE5, 0x10, 0x68, 0x00, 0xFF]);
        await UntilAsync(() => relay.GetProgress().Status == "Faulted");
        var p = relay.GetProgress();
        Assert.Equal(5, p.MasterToSlave.IngressBytes);
        Assert.Equal(5, p.MasterToSlave.WriteAttemptedBytes);
        Assert.Equal(0, p.MasterToSlave.WriteCompletedBytes);
        Assert.Equal(5, p.MasterToSlave.WriteUncertainBytes);
        Assert.True(a.WasClosed && b.WasClosed);
        Assert.Equal(1, b.WriteCallCount); // no ambiguous auto-retry
        Assert.NotNull(p.Fault);
    }

    [Fact]
    public void InvalidTopologyAndDuplicateEndpointAreRejected()
    {
        using var a = new MemoryEndpoint("COM12");
        Assert.Throws<ArgumentException>(() =>
            new VirtualInlineRelaySession(Master, Master with { PortName = "com12" }, a, a));
        using var b = new MemoryEndpoint("COM13");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VirtualInlineRelaySession(Master, Slave, a, b, observationCapacity: 0));
    }

    [Fact]
    public void HundredFreshStartStopCyclesCloseBothEndpointHandles()
    {
        for (var i = 0; i < 100; i++)
        {
            using var a = new MemoryEndpoint("COM12");
            using var b = new MemoryEndpoint("COM13");
            using var relay = new VirtualInlineRelaySession(Master, Slave, a, b);
            var taps = relay.CreateMonitorTaps();
            taps[0].Open();
            taps[1].Open();
            taps[0].Close();
            taps[1].Close();
            Assert.True(a.WasClosed && b.WasClosed);
            Assert.Equal("Stopped", relay.GetProgress().Status);
            Assert.Throws<InvalidOperationException>(() => taps[0].Open());
        }
    }

    private static void PushChunks(MemoryEndpoint endpoint, byte[] bytes)
    {
        for (int offset = 0; offset < bytes.Length;)
        {
            int count = Math.Min(128 + ((offset * 17) % 897), bytes.Length - offset);
            endpoint.Inject(bytes.AsSpan(offset, count).ToArray());
            offset += count;
        }
    }

    private static byte[] TakeChunks(MemoryEndpoint endpoint, int bytes)
    {
        var result = new byte[bytes];
        for (int offset = 0; offset < bytes;)
        {
            byte[] chunk = endpoint.TakeOutbound();
            Assert.InRange(chunk.Length, 1, bytes - offset);
            chunk.CopyTo(result, offset);
            offset += chunk.Length;
        }
        return result;
    }

    private static byte[] TakeObserved(IReadOnlySerialTap tap, int bytes)
    {
        var result = new byte[bytes];
        for (int offset = 0; offset < bytes;)
        {
            int n = tap.Read(result, offset, bytes - offset);
            if (n == 0) continue;
            offset += n;
        }
        return result;
    }

    private static async Task UntilAsync(Func<bool> predicate)
    {
        for (int i = 0; i < 150; i++)
        {
            if (predicate()) return;
            await Task.Delay(10);
        }
        Assert.True(predicate(), "Timed out waiting for a real relay or monitor state transition.");
    }

    [Fact]
    public async Task StopWithUnconsumedVirtualIngressCannotClaimCompleteCapture()
    {
        // Deterministic overload/in-flight-stop injection: relay read and tee
        // acceptance occurred, but the canonical Monitor was not given that span.
        var master = new IncompleteIngressTap("COM12", 128);
        var slave = new IncompleteIngressTap("COM13", 0);
        var opts = new PassiveCaptureOptions(string.Empty, Master, Slave,
            RecordFullSession: false, CaptureSource: "Virtual Inline (VSPE)");
        var monitor = new PassiveMonitorSession(opts, master, slave);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(130));
        var result = await monitor.RunAsync(stop.Token);
        Assert.Equal("IntegrityUncertain", result.Manifest.Status);
        Assert.Contains(result.Manifest.Warnings, w =>
            w.Contains("COM12", StringComparison.Ordinal) &&
            w.Contains("Capture/transport is incomplete", StringComparison.Ordinal));
        Assert.Equal("Virtual Inline (VSPE)", result.Manifest.CaptureSource);
    }

    private sealed class IncompleteIngressTap : IReadOnlySerialTap, IVirtualInlineIngressAccounting
    {
        private readonly long _ingress;
        internal IncompleteIngressTap(string port, long ingress)
        {
            PortName = port;
            _ingress = ingress;
        }
        public string PortName { get; }
        public long DriverErrorCount => 0;
        public string? RelayFault => null;
        public void StopRelay() { }
        public VirtualInlineDirectionProgress GetIngressAccounting() =>
            new(PortName, "virtual peer", _ingress, _ingress, _ingress,
                0, _ingress, 0, 0, _ingress > 0 ? 1 : 0, 0);
        public void Open() { }
        public int Read(byte[] buffer, int offset, int count)
        {
            Thread.Sleep(15);
            return 0;
        }
        public void Close() { }
        public void Dispose() { }
    }

    private sealed class MemoryEndpoint : IVirtualInlineEndpoint
    {
        private readonly BlockingCollection<byte[]> _incoming = new(new ConcurrentQueue<byte[]>());
        private readonly BlockingCollection<byte[]> _outgoing = new(new ConcurrentQueue<byte[]>());
        private int _open;
        private int _closed;
        private int _writeCalls;

        internal MemoryEndpoint(string portName) => PortName = portName;
        public string PortName { get; }
        public long DriverErrorCount => 0;
        public bool FailOnOpen { get; init; }
        public bool FailOnWrite { get; init; }
        public bool IsOpen => Volatile.Read(ref _open) != 0;
        public bool WasClosed => Volatile.Read(ref _closed) != 0;
        public int WriteCallCount => Volatile.Read(ref _writeCalls);

        public void Open()
        {
            if (FailOnOpen) throw new IOException("Simulated second COM unavailable.");
            Volatile.Write(ref _open, 1);
        }

        public void Inject(byte[] bytes) => _incoming.Add(bytes);
        public byte[] TakeOutbound()
        {
            Assert.True(_outgoing.TryTake(out var value, 2000), "Peer did not receive forwarded bytes.");
            return value!;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (!IsOpen) throw new IOException("COM closed.");
            if (!_incoming.TryTake(out var data, 30)) return 0;
            if (data.Length > count) throw new IOException("Fixture chunk exceeds configured buffer.");
            Buffer.BlockCopy(data, 0, buffer, offset, data.Length);
            return data.Length;
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            Interlocked.Increment(ref _writeCalls);
            if (!IsOpen) throw new IOException("COM closed.");
            if (FailOnWrite) throw new TimeoutException("Write completion uncertain.");
            _outgoing.Add(buffer.AsSpan(offset, count).ToArray());
        }

        public void Close()
        {
            Volatile.Write(ref _closed, 1);
            Volatile.Write(ref _open, 0);
        }

        public void Dispose()
        {
            Close();
            _incoming.Dispose();
            _outgoing.Dispose();
        }
    }
}
