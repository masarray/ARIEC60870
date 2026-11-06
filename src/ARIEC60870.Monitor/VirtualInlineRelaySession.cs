// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Ports;

namespace ARIEC60870.Monitor;

/// <summary>
/// Active serial endpoint used ONLY by the explicitly selected virtual laboratory relay.
/// The physical IReadOnlySerialTap contract is never given a Write method.
/// </summary>
public interface IVirtualInlineEndpoint : IDisposable
{
    string PortName { get; }
    long DriverErrorCount { get; }
    void Open();
    int Read(byte[] buffer, int offset, int count);
    void Write(byte[] buffer, int offset, int count);
    void Close();
}

/// <summary>Binary serial endpoint. A completed driver Write does not prove peer reception.</summary>
public sealed class WindowsVirtualInlineEndpoint : IVirtualInlineEndpoint
{
    private readonly PassiveSerialSource _source;
    private SerialPort? _serial;
    private long _errors;

    public WindowsVirtualInlineEndpoint(PassiveSerialSource source) => _source = source;
    public string PortName => _source.PortName;
    public long DriverErrorCount => Interlocked.Read(ref _errors);

    public void Open()
    {
        if (_serial is not null) throw new InvalidOperationException("Relay endpoint is already open.");
        var port = new SerialPort(_source.PortName, _source.BaudRate, _source.Parity,
            _source.DataBits, _source.StopBits)
        {
            Handshake = Handshake.None,
            RtsEnable = false,
            DtrEnable = false,
            ReadTimeout = 200,
            WriteTimeout = 1000,
            ReadBufferSize = 32768,
            WriteBufferSize = 32768,
            ParityReplace = 0
        };
        port.ErrorReceived += OnError;
        try
        {
            port.Open();
            _serial = port;
        }
        catch
        {
            port.ErrorReceived -= OnError;
            port.Dispose();
            throw;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var port = _serial ?? throw new IOException("Virtual serial endpoint closed.");
        try { return port.Read(buffer, offset, count); }
        catch (TimeoutException) { return 0; }
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        var port = _serial ?? throw new IOException("Virtual serial endpoint closed.");
        port.Write(buffer, offset, count);
    }

    private void OnError(object sender, SerialErrorReceivedEventArgs e) =>
        Interlocked.Increment(ref _errors);

    public void Close()
    {
        var port = Interlocked.Exchange(ref _serial, null);
        if (port is null) return;
        port.ErrorReceived -= OnError;
        port.Dispose();
    }

    public void Dispose() => Close();
}

public sealed record VirtualInlineDirectionProgress(
    string IngressPort,
    string EgressPort,
    long IngressBytes,
    long WriteAttemptedBytes,
    long WriteCompletedBytes,
    long WriteUncertainBytes,
    long CapturedBytes,
    long ConsumedBytes,
    long CaptureRejectedBytes,
    int PendingChunks,
    long DriverErrors);

public sealed record VirtualInlineRelayProgress(
    string Status,
    string? Fault,
    VirtualInlineDirectionProgress MasterToSlave,
    VirtualInlineDirectionProgress SlaveToMaster);

/// <summary>
/// Read-only source accounting exported into the canonical Monitor terminal audit.
/// Unlike physical RX taps, a relay owns a bounded ingress tee whose delivery
/// must be reconciled before calling an observed session complete.
/// </summary>
public interface IVirtualInlineIngressAccounting
{
    VirtualInlineDirectionProgress GetIngressAccounting();
    string? RelayFault { get; }
    /// <summary>Immediately stop active forwarding when Monitor cancellation/fault begins.</summary>
    void StopRelay();
}

/// <summary>
/// Byte-transparent, two-pump virtual inline relay. The only connection to the existing
/// IEC-101 Monitor is an observation-only read adapter. No FT1.2 framing, IEC command,
/// polling or GUI work occurs on either forwarding worker.
/// </summary>
public sealed class VirtualInlineRelaySession : IDisposable
{
    internal sealed record Observation(byte[] Buffer, int Length);

    private readonly IVirtualInlineEndpoint[] _ports;
    private readonly BlockingCollection<Observation>[] _observations;
    private readonly VirtualIngressTap[] _taps;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _openGate = new();
    private readonly bool[] _tapOpen = new bool[2];
    private readonly int _readChunkBytes;
    private readonly long[] _ingress = new long[2];
    private readonly long[] _attempted = new long[2];
    private readonly long[] _completed = new long[2];
    private readonly long[] _uncertain = new long[2];
    private readonly long[] _captured = new long[2];
    private readonly long[] _consumed = new long[2];
    private readonly long[] _rejected = new long[2];
    private Task[] _pumps = Array.Empty<Task>();
    private string _status = "Idle";
    private string? _fault;
    private int _stopped;
    private int _disposed;

    public VirtualInlineRelaySession(PassiveSerialSource masterSide, PassiveSerialSource slaveSide)
        : this(masterSide, slaveSide,
            new WindowsVirtualInlineEndpoint(masterSide),
            new WindowsVirtualInlineEndpoint(slaveSide))
    {
    }

    /// <summary>Inject independent mock endpoints for deterministic byte/transport tests.</summary>
    public VirtualInlineRelaySession(
        PassiveSerialSource masterSide, PassiveSerialSource slaveSide,
        IVirtualInlineEndpoint masterEndpoint, IVirtualInlineEndpoint slaveEndpoint,
        int observationCapacity = 256, int readChunkBytes = 4096)
    {
        new PassiveCaptureOptions(string.Empty, masterSide, slaveSide,
            RecordFullSession: false).Validate();
        if (masterEndpoint is null || slaveEndpoint is null ||
            ReferenceEquals(masterEndpoint, slaveEndpoint) ||
            !string.Equals(masterEndpoint.PortName, masterSide.PortName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(slaveEndpoint.PortName, slaveSide.PortName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Provide two independent endpoints matching the selected virtual COM ports.");
        if (observationCapacity is < 16 or > 8192 || readChunkBytes is < 128 or > 16384)
            throw new ArgumentOutOfRangeException(nameof(observationCapacity), "Bounded relay limits exceeded.");

        _ports = [masterEndpoint, slaveEndpoint];
        _readChunkBytes = readChunkBytes;
        _observations =
        [
            new BlockingCollection<Observation>(new ConcurrentQueue<Observation>(), observationCapacity),
            new BlockingCollection<Observation>(new ConcurrentQueue<Observation>(), observationCapacity)
        ];
        _taps = [new VirtualIngressTap(this, 0), new VirtualIngressTap(this, 1)];
    }

    /// <summary>
    /// Read-only ingestion adapters: they DO NOT open or transmit on physical RX taps.
    /// The active relay owns the virtual COM handles and exposes immutable ingress copies.
    /// One adapter per source, one source frame per actual ingress, no egress echo.
    /// </summary>
    public IReadOnlySerialTap[] CreateMonitorTaps() => [_taps[0], _taps[1]];

    public VirtualInlineRelayProgress GetProgress()
    {
        var fault = Volatile.Read(ref _fault);
        var status = Volatile.Read(ref _stopped) != 0
            ? (fault is null ? "Stopped" : "Faulted")
            : Volatile.Read(ref _status);
        return new(status, fault, Snapshot(0), Snapshot(1));
    }

    private VirtualInlineDirectionProgress Snapshot(int index) => new(
        _ports[index].PortName, _ports[1 - index].PortName,
        Interlocked.Read(ref _ingress[index]),
        Interlocked.Read(ref _attempted[index]),
        Interlocked.Read(ref _completed[index]),
        Interlocked.Read(ref _uncertain[index]),
        Interlocked.Read(ref _captured[index]),
        Interlocked.Read(ref _consumed[index]),
        Interlocked.Read(ref _rejected[index]),
        _observations[index].Count,
        _ports[index].DriverErrorCount);

    private void OpenTap(int index)
    {
        lock (_openGate)
        {
            if (Volatile.Read(ref _stopped) != 0)
                throw new InvalidOperationException("A relay session cannot be restarted; create a fresh epoch.");
            if (_tapOpen[index]) throw new InvalidOperationException("Monitor source already opened.");
            if (_status == "Idle")
            {
                Volatile.Write(ref _status, "Opening");
                try
                {
                    // All-or-nothing COM ownership; never start a one-sided relay.
                    _ports[0].Open();
                    _ports[1].Open();
                    Volatile.Write(ref _status, "Ready");
                }
                catch (Exception ex)
                {
                    Volatile.Write(ref _fault, "Opening: " + ex.GetType().Name + ": " + ex.Message);
                    Stop();
                    throw;
                }
            }
            _tapOpen[index] = true;
            if (_tapOpen[0] && _tapOpen[1])
            {
                _pumps =
                [
                    Task.Factory.StartNew(() => Pump(0), CancellationToken.None,
                        TaskCreationOptions.LongRunning, TaskScheduler.Default),
                    Task.Factory.StartNew(() => Pump(1), CancellationToken.None,
                        TaskCreationOptions.LongRunning, TaskScheduler.Default)
                ];
                Volatile.Write(ref _status, "Relay ready · peer receipt not verified");
            }
        }
    }

    private void Pump(int direction)
    {
        var buffer = new byte[_readChunkBytes];
        var input = _ports[direction];
        var output = _ports[1 - direction];
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var length = input.Read(buffer, 0, buffer.Length);
                if (length == 0) continue;
                if (length < 0 || length > buffer.Length)
                    throw new IOException("Virtual endpoint returned an invalid read count.");
                Interlocked.Add(ref _ingress[direction], length);

                // Immutable copy BEFORE buffer reuse. A saturated observer is fatal
                // rather than silently dropping forensic bytes while claiming complete capture.
                var owned = ArrayPool<byte>.Shared.Rent(length);
                var transferred = false;
                try
                {
                    Buffer.BlockCopy(buffer, 0, owned, 0, length);
                    transferred = _observations[direction].TryAdd(new Observation(owned, length));
                    if (!transferred)
                    {
                        Interlocked.Add(ref _rejected[direction], length);
                        throw new IOException("Bounded ingress observation saturated; capture integrity cannot be preserved.");
                    }
                    Interlocked.Add(ref _captured[direction], length);
                }
                finally
                {
                    if (!transferred) ArrayPool<byte>.Shared.Return(owned);
                }

                // Only driver-write accounting. No auto-retry after an uncertain write:
                // the remote peer may have received any subset of these bytes.
                Interlocked.Add(ref _attempted[direction], length);
                try
                {
                    output.Write(buffer, 0, length);
                    Interlocked.Add(ref _completed[direction], length);
                }
                catch
                {
                    Interlocked.Add(ref _uncertain[direction], length);
                    throw;
                }
                Volatile.Write(ref _status, "Traffic observed · peer receipt not verified");
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Fail(direction, ex);
                break;
            }
        }
    }

    private void Fail(int direction, Exception ex)
    {
        var message = (direction == 0 ? "Master → Slave" : "Slave → Master") +
            ": " + ex.GetType().Name + ": " + ex.Message;
        Interlocked.CompareExchange(ref _fault, message, null);
        Stop(); // Fail closed BOTH directions, with no reconnect or retransmit.
    }

    private int ReadObserved(int index, byte[] destination, int offset, int count, VirtualIngressTap tap)
    {
        if (!_tapOpen[index]) throw new InvalidOperationException("Virtual monitor source is not open.");
        if (count <= 0) return 0;
        if (tap.Pending is null && _observations[index].TryTake(out var next, 200))
            tap.Pending = next;

        if (tap.Pending is null)
        {
            if (Volatile.Read(ref _fault) is { } fault)
                throw new IOException("Virtual relay stopped: " + fault);
            return 0;
        }

        var chunk = tap.Pending;
        var take = Math.Min(count, chunk.Length - tap.Position);
        Buffer.BlockCopy(chunk.Buffer, tap.Position, destination, offset, take);
        tap.Position += take;
        Interlocked.Add(ref _consumed[index], take);
        if (tap.Position == chunk.Length)
        {
            ArrayPool<byte>.Shared.Return(chunk.Buffer);
            tap.Pending = null;
            tap.Position = 0;
        }
        return take;
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        Volatile.Write(ref _status, "Stopping");
        _stop.Cancel();
        foreach (var port in _ports)
        {
            try { port.Close(); }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref _fault, "Close: " + ex.Message, null);
            }
        }
        foreach (var observations in _observations) observations.CompleteAdding();
        Volatile.Write(ref _status, Volatile.Read(ref _fault) is null ? "Stopped" : "Faulted");
    }

    private void AwaitQuiescence()
    {
        try
        {
            if (_pumps.Length != 0 && !Task.WhenAll(_pumps).Wait(TimeSpan.FromSeconds(3)))
                Interlocked.CompareExchange(ref _fault,
                    "A serial driver worker did not stop within the bounded shutdown interval.", null);
        }
        catch (AggregateException ex)
        {
            Interlocked.CompareExchange(ref _fault,
                "Relay worker failed during shutdown: " + ex.GetBaseException().Message, null);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Stop();
        AwaitQuiescence();
        // A driver that refuses to unblock may still own its worker.
        // Do not return pooled buffers while such a worker could publish them.
        if (_pumps.All(task => task.IsCompleted))
        {
            foreach (var tap in _taps)
            {
                if (tap.Pending is { } pending) ArrayPool<byte>.Shared.Return(pending.Buffer);
                tap.Pending = null;
            }
            foreach (var queue in _observations)
            {
                while (queue.TryTake(out var item)) ArrayPool<byte>.Shared.Return(item.Buffer);
                queue.Dispose();
            }
            foreach (var port in _ports) port.Dispose();
            _stop.Dispose();
        }
    }

    private sealed class VirtualIngressTap : IReadOnlySerialTap, IVirtualInlineIngressAccounting
    {
        private readonly VirtualInlineRelaySession _owner;
        private readonly int _index;
        internal Observation? Pending;
        internal int Position;

        internal VirtualIngressTap(VirtualInlineRelaySession owner, int index)
        {
            _owner = owner;
            _index = index;
        }

        public string PortName => _owner._ports[_index].PortName;
        public long DriverErrorCount => _owner._ports[_index].DriverErrorCount;
        public VirtualInlineDirectionProgress GetIngressAccounting() => _owner.Snapshot(_index);
        public string? RelayFault => Volatile.Read(ref _owner._fault);
        public void StopRelay() => _owner.Stop();
        public void Open() => _owner.OpenTap(_index);
        public int Read(byte[] buffer, int offset, int count) =>
            _owner.ReadObserved(_index, buffer, offset, count, this);
        public void Close()
        {
            _owner.Stop();
            _owner.AwaitQuiescence();
        }
        public void Dispose() => Close();
    }
}
