// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace ARIEC60870.Monitor;

/// <summary>
/// Owns receive-only serial monitoring, with an optional full raw journal.
/// Both I/O streams are independent and never transmit.
/// </summary>
public sealed class PassiveMonitorSession
{
    private readonly PassiveCaptureOptions _options;
    private readonly IReadOnlySerialTap[] _taps;
    private readonly PassiveSerialSource[] _sources;
    private readonly int _streamCount;
    // UI-only feed: bounded; loss here never changes raw journal contents.
    private readonly Channel<PassiveChunkPreview> _previews =
        Channel.CreateBounded<PassiveChunkPreview>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
    // Bounded presentation feed; frame index files and primary binary journals remain durable.
    private readonly Channel<PassiveObservedFrame> _frames =
        Channel.CreateBounded<PassiveObservedFrame>(new BoundedChannelOptions(512)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
    // Lightweight UI-only transaction summary. Complete derived evidence is on disk.
    private readonly Channel<PassiveTransactionEvent> _transactionEvents =
        Channel.CreateBounded<PassiveTransactionEvent>(new BoundedChannelOptions(512)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
    // GUI observations are bounded. All GI lifecycle records persist in gi-events.jsonl.
    private readonly Channel<PassiveGiEvent> _giEvents =
        Channel.CreateBounded<PassiveGiEvent>(new BoundedChannelOptions(256)
        {
            SingleReader = true, SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
    // Aggregated 5-second forensic windows; the full timeline stays on disk.
    private readonly Channel<PassiveTimelineWindow> _timelineWindows =
        Channel.CreateBounded<PassiveTimelineWindow>(new BoundedChannelOptions(64)
        {
            SingleReader = true, SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
    // Derived before WPF preview can drop frames; full history is controls.jsonl
    // when recording. This channel alone is intentionally bounded.
    private readonly Channel<PassiveControlEvidence> _controlEvents =
        Channel.CreateBounded<PassiveControlEvidence>(new BoundedChannelOptions(512)
        {
            SingleReader = true, SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
    private readonly long[] _writtenBytes;
    private readonly long[] _writtenChunks;
    private readonly string[] _rawHashes;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _warnings = new();
    private int _integrityUncertain;
    private int _fatal;
    private int _started;
    private string _captureDirectory = string.Empty;
    private string _captureStatus = "Idle";
    private DateTime _utcOrigin;
    private long _timestampOrigin;

    public PassiveMonitorSession(PassiveCaptureOptions options, params IReadOnlySerialTap[] taps)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(taps);
        options.Validate();
        _options = options;
        _sources = options.Sources;
        _streamCount = _sources.Length;
        if (taps.Length != _streamCount ||
            taps.Where((tap, i) => tap is null ||
                !string.Equals(tap.PortName, _sources[i].PortName, StringComparison.OrdinalIgnoreCase)).Any())
            throw new ArgumentException("Provide exactly one RX-only tap per configured physical source in order.");
        _taps = taps;
        _writtenBytes = new long[_streamCount];
        _writtenChunks = new long[_streamCount];
        _rawHashes = Enumerable.Repeat(string.Empty, _streamCount).ToArray();
    }

    /// <summary>Starts RX-only monitoring; optional journal is finalized at Stop.</summary>
    /// <summary>Thread-safe UI polling; never accesses COM drivers from WPF.</summary>
    public PassiveMonitorProgress GetProgress() => new(
        Volatile.Read(ref _captureStatus),
        Volatile.Read(ref _captureDirectory),
        Interlocked.Read(ref _writtenBytes[0]),
        Interlocked.Read(ref _writtenBytes[1]),
        Interlocked.Read(ref _writtenChunks[0]),
        Interlocked.Read(ref _writtenChunks[1]),
        _taps[0].DriverErrorCount,
        _taps[1].DriverErrorCount,
        Volatile.Read(ref _integrityUncertain) != 0 || Volatile.Read(ref _fatal) != 0,
        _streamCount == 4 ? Interlocked.Read(ref _writtenBytes[2]) : 0,
        _streamCount == 4 ? Interlocked.Read(ref _writtenBytes[3]) : 0,
        _streamCount);

    /// <summary>UI-only sampled byte chunks; may omit samples under UI pressure.</summary>
    public bool TryReadPreview(out PassiveChunkPreview preview) => _previews.Reader.TryRead(out preview!);

    /// <summary>Decoding feed for Trace/Values/Events. A slow UI cannot block archival.</summary>
    public bool TryReadFrame(out PassiveObservedFrame frame) => _frames.Reader.TryRead(out frame!);

    /// <summary>Derived transaction alerts; full chronological output is in transactions.jsonl.</summary>
    public bool TryReadTransaction(out PassiveTransactionEvent item) =>
        _transactionEvents.Reader.TryRead(out item!);

    /// <summary>Source-qualified command notes, generated before GUI frame sampling.</summary>
    public bool TryReadControl(out PassiveControlEvidence item) =>
        _controlEvents.Reader.TryRead(out item!);

    public bool TryReadGiEvent(out PassiveGiEvent item) => _giEvents.Reader.TryRead(out item!);

    public bool TryReadTimelineWindow(out PassiveTimelineWindow item) =>
        _timelineWindows.Reader.TryRead(out item!);

    public async Task<(string Directory, PassiveCaptureManifest Manifest)> RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("One session cannot be reused.");
        if (_options.RecordFullSession) Directory.CreateDirectory(_options.OutputDirectory);
        var id = "ARIEC-PASSIVE-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8];
        var root = _options.RecordFullSession
            ? Path.Combine(Path.GetFullPath(_options.OutputDirectory), id) : string.Empty;
        if (_options.RecordFullSession) Directory.CreateDirectory(root);
        var startUtc = DateTime.UtcNow;
        var origin = Stopwatch.GetTimestamp();
        _utcOrigin = startUtc;
        _timestampOrigin = origin;
        Volatile.Write(ref _captureDirectory, root);
        Volatile.Write(ref _captureStatus, "Starting");
        if (_options.RecordFullSession)
            WriteManifest(root, Snapshot(id, "Recording", startUtc, startUtc, origin));

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<OwnedCaptureRecord>(new BoundedChannelOptions(_options.ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        Task? writerTask = null;
        var opened = new List<IReadOnlySerialTap>(_streamCount);
        try
        {
            // Every configured direction must be armed before streaming; never monitor only one.
            foreach (var tap in _taps) { tap.Open(); opened.Add(tap); }
            Volatile.Write(ref _captureStatus, _options.RecordFullSession ? "Recording" : "Monitoring");

            writerTask = Task.Run(() => WriteLoopAsync(root, channel, stop));
            var reads = Enumerable.Range(0, _streamCount)
                .Select(i => Task.Run(() => ReadLoopAsync((byte)i, channel.Writer, stop)))
                .ToArray();
            await Task.WhenAll(reads).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _fatal, 1);
            _warnings.Enqueue("Capture session failure: " + ex.GetType().Name + ": " + ex.Message);
            stop.Cancel();
        }
        finally
        {
            channel.Writer.TryComplete();
            if (writerTask is not null)
            {
                try { await writerTask.ConfigureAwait(false); }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _fatal, 1);
                    _warnings.Enqueue("Writer failure: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            foreach (var tap in opened)
            {
                try { tap.Close(); }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _integrityUncertain, 1);
                    _warnings.Enqueue("Serial close failure on " + tap.PortName + ": " + ex.Message);
                }
            }
            foreach (var tap in _taps)
            {
                if (tap.DriverErrorCount == 0) continue;
                Interlocked.Exchange(ref _integrityUncertain, 1);
                _warnings.Enqueue(tap.PortName + " reported " + tap.DriverErrorCount + " UART driver error notification(s).");
            }
            var status = Volatile.Read(ref _fatal) != 0 ? "Incomplete" :
                Volatile.Read(ref _integrityUncertain) != 0 ? "IntegrityUncertain" :
                _options.RecordFullSession ? "NoApplicationLossDetected" : "MonitorStopped";
            var completed = Snapshot(id, status, startUtc, DateTime.UtcNow, origin);
            if (_options.RecordFullSession) WriteManifest(root, completed);
            Volatile.Write(ref _captureStatus, status);
            Result = (root, completed);
        }

        return Result!.Value;
    }

    public (string Directory, PassiveCaptureManifest Manifest)? Result { get; private set; }

    private async Task ReadLoopAsync(byte streamId, ChannelWriter<OwnedCaptureRecord> output, CancellationTokenSource stop)
    {
        var cancellationToken = stop.Token;
        long sequence = 0;
        long byteOffset = 0;
        var tap = _taps[streamId];

        while (!cancellationToken.IsCancellationRequested)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(PassiveJournalFormat.HeaderSize + _options.ReadChunkBytes);
            var transferred = false;
            try
            {
                // Each reader is a dedicated worker. Real SerialPort.Read uses 200-ms timeout.
                // No Task.Run per byte and no thread-affine WPF dispatch.
                var n = tap.Read(buffer, PassiveJournalFormat.HeaderSize, _options.ReadChunkBytes);
                if (n == 0) continue;
                if (n < 0 || n > _options.ReadChunkBytes)
                    throw new IOException("Serial tap returned an invalid byte count.");
                PassiveJournalFormat.Encode(buffer.AsSpan(0, PassiveJournalFormat.HeaderSize + n),
                    streamId, ++sequence, Stopwatch.GetTimestamp(), byteOffset, n);
                byteOffset += n;

                var queuedAt = Stopwatch.GetTimestamp();
                // Once read from UART, commit this chunk even if Stop is requested during queue handoff.
                // Disk failure completes the channel to release any waiting producer.
                await output.WriteAsync(new OwnedCaptureRecord(streamId, buffer,
                    PassiveJournalFormat.HeaderSize + n)).ConfigureAwait(false);
                transferred = true; // writer now owns pool buffer
                if (Stopwatch.GetElapsedTime(queuedAt) > TimeSpan.FromMilliseconds(250))
                {
                    Interlocked.Exchange(ref _integrityUncertain, 1);
                    _warnings.Enqueue("Archive backpressure exceeded 250 ms on " + tap.PortName +
                        "; USB/driver FIFO continuity cannot be proven.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (ChannelClosedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _fatal, 1);
                _warnings.Enqueue("Read failed on " + tap.PortName + ": " + ex.GetType().Name + ": " + ex.Message);
                stop.Cancel();
                break;
            }
            finally
            {
                if (!transferred) ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private async Task WriteLoopAsync(string root, Channel<OwnedCaptureRecord> channel, CancellationTokenSource stop)
    {
        Stream[]? files = null;
        FileStream? firstFile = null;
        StreamWriter[]? frameFiles = null;
        StreamWriter? transactionFile = null;
        StreamWriter? giFile = null;
        StreamWriter? timelineFile = null;
        StreamWriter? controlFile = null;
        var timelines = Enumerable.Range(0, _streamCount / 2)
            .Select(_ => new PassiveIec101FailureTimeline(_utcOrigin)).ToArray();
        var giTracker = new PassiveIec101GiTracker(new PassiveGiOptions(
            LinkAddressWidth: _options.LinkAddressWidth, CotWidth: _options.CotWidth,
            CommonAddressWidth: _options.CommonAddressWidth,
            InformationAddressWidth: _options.InformationAddressWidth));
        var controlIndexer = new PassiveIec101ControlIndexer(
            _options.LinkAddressWidth, _options.CotWidth,
            _options.CommonAddressWidth, _options.InformationAddressWidth);
        var correlator = new PassiveIec101TransactionCorrelator(new PassiveTransactionOptions(
            LinkAddressWidth: _options.LinkAddressWidth, LinkMode: _options.LinkMode));
        var scanners = Enumerable.Range(0, _streamCount)
            .Select(_ => new PassiveFt12Scanner(_options.LinkAddressWidth)).ToArray();
        var lastSeenTicks = new long[_streamCount];
        var hashes = Enumerable.Range(0, _streamCount)
            .Select(_ => IncrementalHash.CreateHash(HashAlgorithmName.SHA256)).ToArray();
        try
        {
            if (_options.RecordFullSession) firstFile = OpenJournal(root, 0);
            files = new Stream[_streamCount];
            files[0] = firstFile ?? Stream.Null;
            for (byte i = 1; i < _streamCount; i++)
                files[i] = _options.RecordFullSession ? OpenJournal(root, i) : Stream.Null;
            frameFiles = Enumerable.Range(0, _streamCount)
                .Select(i => new StreamWriter(_options.RecordFullSession
                    ? new FileStream(Path.Combine(root, $"frames-{i:00}.jsonl"), FileMode.CreateNew,
                        FileAccess.Write, FileShare.Read, 65536) : Stream.Null, System.Text.Encoding.UTF8))
                .ToArray();
            transactionFile = new StreamWriter(_options.RecordFullSession
                ? new FileStream(Path.Combine(root, "transactions.jsonl"), FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read, 65536) : Stream.Null, System.Text.Encoding.UTF8);
            giFile = new StreamWriter(_options.RecordFullSession
                ? new FileStream(Path.Combine(root, "gi-events.jsonl"), FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read, 65536) : Stream.Null, System.Text.Encoding.UTF8);
            timelineFile = new StreamWriter(_options.RecordFullSession
                ? new FileStream(Path.Combine(root, "timeline-windows.jsonl"), FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read, 65536) : Stream.Null, System.Text.Encoding.UTF8);
            controlFile = new StreamWriter(_options.RecordFullSession
                ? new FileStream(Path.Combine(root, "controls.jsonl"), FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read, 65536) : Stream.Null, System.Text.Encoding.UTF8);
            var bytesSinceFlush = 0L;
            var lastFlush = Stopwatch.GetTimestamp();
            // One writer owns these scratch buffers; reuse them across serial
            // reads to avoid four managed allocations on every read chunk.
            var observed = new List<PassiveObservedFrame>(8);
            var transactions = new List<PassiveTransactionEvent>(8);
            var giEvents = new List<PassiveGiEvent>(4);
            var timelineWindows = new List<PassiveTimelineWindow>(4);
            await foreach (var chunk in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    if (_options.RecordFullSession)
                        await files[chunk.StreamId].WriteAsync(chunk.Buffer.AsMemory(0, chunk.Length)).ConfigureAwait(false);
                    if (_options.RecordFullSession)
                        hashes[chunk.StreamId].AppendData(chunk.Buffer, PassiveJournalFormat.HeaderSize,
                            chunk.Length - PassiveJournalFormat.HeaderSize);
                    var rawOffset = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(chunk.Buffer.AsSpan(24, 8));
                    var frameTicks = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(chunk.Buffer.AsSpan(16, 8));
                    var frameUtc = _utcOrigin + Stopwatch.GetElapsedTime(_timestampOrigin, frameTicks);
                    // The journal is authoritative. Frame recognition is a passive projection
                    // of the COMPLETE chunk, not the bounded 48-byte UI preview.
                    observed.Clear();
                    transactions.Clear();
                    giEvents.Clear();
                    timelineWindows.Clear();
                    if (lastSeenTicks[chunk.StreamId] > 0 &&
                        Stopwatch.GetElapsedTime(lastSeenTicks[chunk.StreamId], frameTicks) > TimeSpan.FromMilliseconds(_options.FrameGapMilliseconds))
                    {
                        var partial = scanners[chunk.StreamId].ExpireIncomplete();
                        if (partial is not null)
                            observed.Add(new PassiveObservedFrame(chunk.StreamId, partial.ByteOffset,
                                frameUtc, partial.Format, false, partial.RawBytes));
                    }
                    lastSeenTicks[chunk.StreamId] = frameTicks;
                    scanners[chunk.StreamId].Feed(chunk.Buffer.AsSpan(PassiveJournalFormat.HeaderSize,
                        chunk.Length - PassiveJournalFormat.HeaderSize), rawOffset,
                        frame => observed.Add(new PassiveObservedFrame(chunk.StreamId,
                            frame.ByteOffset, frameUtc, frame.Format, frame.ChecksumValid,
                            frame.RawBytes, frame.DetectedLinkAddressWidth)));
                    foreach (var frame in observed)
                    {
                        timelines[frame.StreamId / 2].ObserveFrame(frame with
                        {
                            StreamId = (byte)(frame.StreamId % 2)
                        }, window => timelineWindows.Add(window with
                        {
                            CircuitId = frame.StreamId / 2
                        }));
                        // Interpret the *lossless writer stream*, never the bounded WPF preview.
                        correlator.Observe(frame, item =>
                        {
                            transactions.Add(item);
                            timelines[item.ObservedStream / 2].ObserveTransaction(item with
                            {
                                ObservedStream = (byte)(item.ObservedStream % 2)
                            });
                        });
                        giTracker.Observe(frame, item =>
                        {
                            giEvents.Add(item);
                            timelines[item.StreamId / 2].ObserveGi(item with
                            {
                                StreamId = (byte)(item.StreamId % 2)
                            });
                        });
                        if (_options.RecordFullSession)
                        {
                            var line = JsonSerializer.Serialize(new
                            {
                                frame.StreamId, frame.StreamOffset, frame.EstimatedUtc,
                                frame.Format, frame.ChecksumValid, Length = frame.RawBytes.Length
                            });
                            await frameFiles[chunk.StreamId].WriteLineAsync(line).ConfigureAwait(false);
                        }
                        // The complete writer sequence, not GUI's 512-frame ring,
                        // owns control stage correlation and optional durable index.
                        if (controlIndexer.TryObserve(frame, out var control) && control is not null)
                        {
                            if (_options.RecordFullSession)
                                await controlFile.WriteLineAsync(JsonSerializer.Serialize(control))
                                    .ConfigureAwait(false);
                            _controlEvents.Writer.TryWrite(control);
                        }
                        _frames.Writer.TryWrite(frame);
                    }
                    foreach (var entry in transactions)
                    {
                        if (_options.RecordFullSession)
                            await transactionFile.WriteLineAsync(JsonSerializer.Serialize(entry))
                                .ConfigureAwait(false);
                        _transactionEvents.Writer.TryWrite(entry);
                    }
                    foreach (var gi in giEvents)
                    {
                        if (_options.RecordFullSession)
                            await giFile.WriteLineAsync(JsonSerializer.Serialize(gi))
                                .ConfigureAwait(false);
                        _giEvents.Writer.TryWrite(gi);
                    }
                    foreach (var window in timelineWindows)
                    {
                        if (_options.RecordFullSession)
                            await timelineFile.WriteLineAsync(JsonSerializer.Serialize(window))
                                .ConfigureAwait(false);
                        _timelineWindows.Writer.TryWrite(window);
                    }
                    // Small capped byte sample, copied while the writer still owns the pooled buffer.
                    var byteCount = chunk.Length - PassiveJournalFormat.HeaderSize;
                    var first = new byte[Math.Min(byteCount, 48)];
                    chunk.Buffer.AsSpan(PassiveJournalFormat.HeaderSize, first.Length).CopyTo(first);
                    var sequence = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(chunk.Buffer.AsSpan(8, 8));
                    var ticks = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(chunk.Buffer.AsSpan(16, 8));
                    var utcEstimate = _utcOrigin + Stopwatch.GetElapsedTime(_timestampOrigin, ticks);
                    _previews.Writer.TryWrite(new PassiveChunkPreview(
                        chunk.StreamId, sequence, utcEstimate, byteCount, first));
                    Interlocked.Increment(ref _writtenChunks[chunk.StreamId]);
                    Interlocked.Add(ref _writtenBytes[chunk.StreamId], chunk.Length - PassiveJournalFormat.HeaderSize);
                    bytesSinceFlush += chunk.Length;
                    if (_options.RecordFullSession &&
                        (bytesSinceFlush >= 262144 || Stopwatch.GetElapsedTime(lastFlush) >= TimeSpan.FromSeconds(2)))
                    {
                        foreach (var file in files) await file.FlushAsync().ConfigureAwait(false);
                        foreach (var index in frameFiles) await index.FlushAsync().ConfigureAwait(false);
                        await transactionFile.FlushAsync().ConfigureAwait(false);
                        await giFile.FlushAsync().ConfigureAwait(false);
                        await timelineFile.FlushAsync().ConfigureAwait(false);
                await controlFile.FlushAsync().ConfigureAwait(false);
                        await controlFile.FlushAsync().ConfigureAwait(false);
                        bytesSinceFlush = 0;
                        lastFlush = Stopwatch.GetTimestamp();
                    }
                }
                finally { ArrayPool<byte>.Shared.Return(chunk.Buffer); }
            }
            // Preserve truncated tail candidates as explicit partial-frame observations
            // at EOF rather than silently suppressing them.
            for (byte sid = 0; sid < _streamCount; sid++)
            {
                var partial = scanners[sid].ExpireIncomplete();
                if (partial is null) continue;
                var observedTail = new PassiveObservedFrame(sid, partial.ByteOffset,
                    DateTime.UtcNow, "Partial", false, partial.RawBytes);
                if (_options.RecordFullSession)
                    await frameFiles[sid].WriteLineAsync(JsonSerializer.Serialize(new
                    {
                        observedTail.StreamId, observedTail.StreamOffset, observedTail.EstimatedUtc,
                        observedTail.Format, observedTail.ChecksumValid, Length = observedTail.RawBytes.Length
                    })).ConfigureAwait(false);
                _frames.Writer.TryWrite(observedTail);
                var tailEvents = new List<PassiveTransactionEvent>();
                correlator.Observe(observedTail, tailEvents.Add);
                // A truncated frame cannot advance GI, but still advances the
                // original per-source offset for the deterministic replay path.
                giTracker.Observe(observedTail, _ => { });
                foreach (var entry in tailEvents)
                {
                    await transactionFile.WriteLineAsync(JsonSerializer.Serialize(entry)).ConfigureAwait(false);
                    _transactionEvents.Writer.TryWrite(entry);
                }
            }
            var finalGi = new List<PassiveGiEvent>();
            giTracker.AdvanceTime(DateTime.UtcNow, finalGi.Add);
            giTracker.Complete(finalGi.Add);
            foreach (var gi in finalGi)
            {
                await giFile.WriteLineAsync(JsonSerializer.Serialize(gi)).ConfigureAwait(false);
                _giEvents.Writer.TryWrite(gi);
                timelines[gi.StreamId / 2].ObserveGi(gi with
                {
                    StreamId = (byte)(gi.StreamId % 2)
                });
            }
            var finalEvents = new List<PassiveTransactionEvent>();
            correlator.AdvanceTime(DateTime.UtcNow, finalEvents.Add);
            correlator.Complete(finalEvents.Add);
            foreach (var entry in finalEvents)
            {
                await transactionFile.WriteLineAsync(JsonSerializer.Serialize(entry)).ConfigureAwait(false);
                _transactionEvents.Writer.TryWrite(entry);
                timelines[entry.ObservedStream / 2].ObserveTransaction(entry with
                {
                    ObservedStream = (byte)(entry.ObservedStream % 2)
                });
            }
            var finalWindows = new List<PassiveTimelineWindow>();
            for (var circuit = 0; circuit < timelines.Length; circuit++)
            {
                var id = circuit;
                timelines[circuit].AdvanceTime(DateTime.UtcNow,
                    x => finalWindows.Add(x with { CircuitId = id }));
                timelines[circuit].Complete(x => finalWindows.Add(x with { CircuitId = id }));
            }
            foreach (var window in finalWindows)
            {
                await timelineFile.WriteLineAsync(JsonSerializer.Serialize(window))
                    .ConfigureAwait(false);
                _timelineWindows.Writer.TryWrite(window);
            }
            if (_options.RecordFullSession)
            {
                foreach (var file in files) await file.FlushAsync().ConfigureAwait(false);
                foreach (var index in frameFiles) await index.FlushAsync().ConfigureAwait(false);
                await transactionFile.FlushAsync().ConfigureAwait(false);
                await giFile.FlushAsync().ConfigureAwait(false);
                await timelineFile.FlushAsync().ConfigureAwait(false);
            }
            if (_options.RecordFullSession)
                for (var i = 0; i < _streamCount; i++)
                    _rawHashes[i] = Convert.ToHexString(hashes[i].GetHashAndReset());
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _fatal, 1);
            _warnings.Enqueue((_options.RecordFullSession ? "Disk journal" : "Live monitor pipeline") +
                " failed: " + ex.GetType().Name + ": " + ex.Message);
            channel.Writer.TryComplete(ex);
            stop.Cancel();
        }
        finally
        {
            while (channel.Reader.TryRead(out var remaining)) ArrayPool<byte>.Shared.Return(remaining.Buffer);
            foreach (var hash in hashes) hash.Dispose();
            if (frameFiles is not null) foreach (var index in frameFiles) index.Dispose();
            transactionFile?.Dispose();
            giFile?.Dispose();
            timelineFile?.Dispose();
            controlFile?.Dispose();
            if (files is null) firstFile?.Dispose();
            if (files is not null)
                foreach (var file in files)
                    try { file.Dispose(); }
                    catch (Exception ex)
                    {
                        Interlocked.Exchange(ref _fatal, 1);
                        _warnings.Enqueue("Disk finalization error: " + ex.Message);
                    }
        }
    }

    private static FileStream OpenJournal(string root, byte id) =>
        new(Path.Combine(root, $"stream-{id:00}.bin"), FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            bufferSize: 65536, options: FileOptions.Asynchronous | FileOptions.SequentialScan);

    private PassiveCaptureManifest Snapshot(string id, string status, DateTime started, DateTime ended, long origin) =>
        new()
        {
            SessionId = id,
            Status = status,
            Warnings = _warnings.ToArray(),
            StartUtc = started,
            EndUtc = ended,
            StopwatchOriginTicks = origin,
            StopwatchFrequency = Stopwatch.Frequency,
            LinkAddressWidth = _options.LinkAddressWidth,
            CotWidth = _options.CotWidth,
            CommonAddressWidth = _options.CommonAddressWidth,
            InformationAddressWidth = _options.InformationAddressWidth,
            FrameGapMilliseconds = _options.FrameGapMilliseconds,
            LinkMode = _options.LinkMode,
            CircuitNames = _streamCount == 4 ? new[] { "Link A", "Link B" } : new[] { "Link A" },
            Streams = _sources.Select((source, i) => new CapturedStreamSummary(
                (byte)i, source.PortName, source.PhysicalTransmitter,
                Interlocked.Read(ref _writtenChunks[i]), Interlocked.Read(ref _writtenBytes[i]),
                _taps[i].DriverErrorCount,
                _options.RecordFullSession ? $"stream-{i:00}.bin" : string.Empty, _rawHashes[i],
                source.BaudRate, source.Parity.ToString(), source.DataBits,
                source.StopBits.ToString())).ToArray()
        };

    private static void WriteManifest(string root, PassiveCaptureManifest manifest)
    {
        var name = Path.Combine(root, "manifest.json");
        var tmp = Path.Combine(root, "manifest.pending");
        File.WriteAllText(tmp, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, name, overwrite: true);
    }

    private sealed record OwnedCaptureRecord(byte StreamId, byte[] Buffer, int Length);
}

// These immutable snapshots are presentation-only, independent of serial write capabilities.
public sealed record PassiveMonitorProgress(
    string Status, string CaptureDirectory, long ScadaBytes, long RtuBytes,
    long ScadaChunks, long RtuChunks, long ScadaDriverErrors, long RtuDriverErrors,
    bool IntegrityWarning,
    long LinkBScadaBytes = 0,
    long LinkBRtuBytes = 0,
    int StreamCount = 2);

public sealed record PassiveChunkPreview(
    byte StreamId, long Sequence, DateTime EstimatedUtc, int ByteCount, byte[] FirstBytes);
