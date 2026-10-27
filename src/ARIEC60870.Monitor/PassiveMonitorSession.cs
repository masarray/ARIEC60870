// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace ARIEC60870.Monitor;

/// <summary>
/// Owns a two-port raw recording. Both I/O streams are independent and never transmit.
/// This P0 engine deliberately does not decode or infer the ETERA controller state.
/// </summary>
public sealed class PassiveMonitorSession
{
    private readonly PassiveCaptureOptions _options;
    private readonly IReadOnlySerialTap[] _taps;
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
    private readonly long[] _writtenBytes = new long[2];
    private readonly long[] _writtenChunks = new long[2];
    private readonly string[] _rawHashes = new string[2];
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _warnings = new();
    private int _integrityUncertain;
    private int _fatal;
    private int _started;
    private string _captureDirectory = string.Empty;
    private string _captureStatus = "Idle";
    private DateTime _utcOrigin;
    private long _timestampOrigin;

    public PassiveMonitorSession(PassiveCaptureOptions options, IReadOnlySerialTap scadaTx, IReadOnlySerialTap rtuTx)
    {
        _options = options;
        _taps = new[] { scadaTx, rtuTx };
        _options.Validate();
        if (!string.Equals(scadaTx.PortName, options.ScadaTx.PortName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(rtuTx.PortName, options.RtuTx.PortName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Tap identities must match the declared physical capture roles.");
    }

    /// <summary>Starts immediately; returns only after Stop/cancellation and durable archive finalization.</summary>
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
        Volatile.Read(ref _integrityUncertain) != 0 || Volatile.Read(ref _fatal) != 0);

    /// <summary>UI-only sampled byte chunks; may omit samples under UI pressure.</summary>
    public bool TryReadPreview(out PassiveChunkPreview preview) => _previews.Reader.TryRead(out preview!);

    /// <summary>Decoding feed for Trace/Values/Events. A slow UI cannot block archival.</summary>
    public bool TryReadFrame(out PassiveObservedFrame frame) => _frames.Reader.TryRead(out frame!);

    /// <summary>Derived transaction alerts; full chronological output is in transactions.jsonl.</summary>
    public bool TryReadTransaction(out PassiveTransactionEvent item) =>
        _transactionEvents.Reader.TryRead(out item!);

    public async Task<(string Directory, PassiveCaptureManifest Manifest)> RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("One session cannot be reused.");
        Directory.CreateDirectory(_options.OutputDirectory);
        var id = "ARIEC-PASSIVE-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8];
        var root = Path.Combine(Path.GetFullPath(_options.OutputDirectory), id);
        Directory.CreateDirectory(root);
        var startUtc = DateTime.UtcNow;
        var origin = Stopwatch.GetTimestamp();
        _utcOrigin = startUtc;
        _timestampOrigin = origin;
        Volatile.Write(ref _captureDirectory, root);
        Volatile.Write(ref _captureStatus, "Starting");
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
        var opened = new List<IReadOnlySerialTap>(2);
        try
        {
            // Both monitored directions must be armed before recording; never silently record just one.
            foreach (var tap in _taps) { tap.Open(); opened.Add(tap); }
            Volatile.Write(ref _captureStatus, "Recording");

            writerTask = Task.Run(() => WriteLoopAsync(root, channel, stop));
            var reads = new[]
            {
                Task.Run(() => ReadLoopAsync(0, channel.Writer, stop)),
                Task.Run(() => ReadLoopAsync(1, channel.Writer, stop))
            };
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
                Volatile.Read(ref _integrityUncertain) != 0 ? "IntegrityUncertain" : "NoApplicationLossDetected";
            var completed = Snapshot(id, status, startUtc, DateTime.UtcNow, origin);
            WriteManifest(root, completed);
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
        FileStream[]? files = null;
        FileStream? firstFile = null;
        StreamWriter[]? frameFiles = null;
        StreamWriter? transactionFile = null;
        var correlator = new PassiveIec101TransactionCorrelator(new PassiveTransactionOptions(
            LinkAddressWidth: _options.LinkAddressWidth, LinkMode: _options.LinkMode));
        var scanners = new[]
        {
            new PassiveFt12Scanner(_options.LinkAddressWidth),
            new PassiveFt12Scanner(_options.LinkAddressWidth)
        };
        var lastSeenTicks = new long[2];
        var hashes = new[] { IncrementalHash.CreateHash(HashAlgorithmName.SHA256), IncrementalHash.CreateHash(HashAlgorithmName.SHA256) };
        try
        {
            firstFile = OpenJournal(root, 0);
            var secondFile = OpenJournal(root, 1);
            files = new[] { firstFile, secondFile };
            frameFiles = new[]
            {
                new StreamWriter(new FileStream(Path.Combine(root, "frames-00.jsonl"), FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read, 65536), System.Text.Encoding.UTF8),
                new StreamWriter(new FileStream(Path.Combine(root, "frames-01.jsonl"), FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read, 65536), System.Text.Encoding.UTF8)
            };
            transactionFile = new StreamWriter(new FileStream(
                Path.Combine(root, "transactions.jsonl"), FileMode.CreateNew,
                FileAccess.Write, FileShare.Read, 65536), System.Text.Encoding.UTF8);
            var bytesSinceFlush = 0L;
            var lastFlush = Stopwatch.GetTimestamp();
            await foreach (var chunk in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    await files[chunk.StreamId].WriteAsync(chunk.Buffer.AsMemory(0, chunk.Length)).ConfigureAwait(false);
                    hashes[chunk.StreamId].AppendData(chunk.Buffer, PassiveJournalFormat.HeaderSize, chunk.Length - PassiveJournalFormat.HeaderSize);
                    var rawOffset = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(chunk.Buffer.AsSpan(24, 8));
                    var frameTicks = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(chunk.Buffer.AsSpan(16, 8));
                    var frameUtc = _utcOrigin + Stopwatch.GetElapsedTime(_timestampOrigin, frameTicks);
                    // The journal is authoritative. Frame recognition is a passive projection
                    // of the COMPLETE chunk, not the bounded 48-byte UI preview.
                    var observed = new List<PassiveObservedFrame>();
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
                            frame.ByteOffset, frameUtc, frame.Format, frame.ChecksumValid, frame.RawBytes)));
                    var transactions = new List<PassiveTransactionEvent>();
                    foreach (var frame in observed)
                    {
                        // Interpret the *lossless writer stream*, never the bounded WPF preview.
                        correlator.Observe(frame, transactions.Add);
                        var line = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            frame.StreamId, frame.StreamOffset, frame.EstimatedUtc,
                            frame.Format, frame.ChecksumValid, Length = frame.RawBytes.Length
                        });
                        await frameFiles[chunk.StreamId].WriteLineAsync(line).ConfigureAwait(false);
                        _frames.Writer.TryWrite(frame);
                    }
                    foreach (var entry in transactions)
                    {
                        await transactionFile.WriteLineAsync(JsonSerializer.Serialize(entry))
                            .ConfigureAwait(false);
                        _transactionEvents.Writer.TryWrite(entry);
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
                    if (bytesSinceFlush >= 262144 || Stopwatch.GetElapsedTime(lastFlush) >= TimeSpan.FromSeconds(2))
                    {
                        await files[0].FlushAsync().ConfigureAwait(false);
                        await files[1].FlushAsync().ConfigureAwait(false);
                        await frameFiles[0].FlushAsync().ConfigureAwait(false);
                        await frameFiles[1].FlushAsync().ConfigureAwait(false);
                        await transactionFile.FlushAsync().ConfigureAwait(false);
                        bytesSinceFlush = 0;
                        lastFlush = Stopwatch.GetTimestamp();
                    }
                }
                finally { ArrayPool<byte>.Shared.Return(chunk.Buffer); }
            }
            // Preserve truncated tail candidates as explicit partial-frame observations
            // at EOF rather than silently suppressing them.
            for (byte sid = 0; sid < 2; sid++)
            {
                var partial = scanners[sid].ExpireIncomplete();
                if (partial is null) continue;
                var observedTail = new PassiveObservedFrame(sid, partial.ByteOffset,
                    DateTime.UtcNow, "Partial", false, partial.RawBytes);
                await frameFiles[sid].WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    observedTail.StreamId, observedTail.StreamOffset, observedTail.EstimatedUtc,
                    observedTail.Format, observedTail.ChecksumValid, Length = observedTail.RawBytes.Length
                })).ConfigureAwait(false);
                _frames.Writer.TryWrite(observedTail);
                var tailEvents = new List<PassiveTransactionEvent>();
                correlator.Observe(observedTail, tailEvents.Add);
                foreach (var entry in tailEvents)
                {
                    await transactionFile.WriteLineAsync(JsonSerializer.Serialize(entry)).ConfigureAwait(false);
                    _transactionEvents.Writer.TryWrite(entry);
                }
            }
            var finalEvents = new List<PassiveTransactionEvent>();
            correlator.AdvanceTime(DateTime.UtcNow, finalEvents.Add);
            correlator.Complete(finalEvents.Add);
            foreach (var entry in finalEvents)
            {
                await transactionFile.WriteLineAsync(JsonSerializer.Serialize(entry)).ConfigureAwait(false);
                _transactionEvents.Writer.TryWrite(entry);
            }
            await files[0].FlushAsync().ConfigureAwait(false);
            await files[1].FlushAsync().ConfigureAwait(false);
            await frameFiles[0].FlushAsync().ConfigureAwait(false);
            await frameFiles[1].FlushAsync().ConfigureAwait(false);
            await transactionFile.FlushAsync().ConfigureAwait(false);
            for (var i = 0; i < 2; i++) _rawHashes[i] = Convert.ToHexString(hashes[i].GetHashAndReset());
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _fatal, 1);
            _warnings.Enqueue("Disk journal write failed: " + ex.GetType().Name + ": " + ex.Message);
            channel.Writer.TryComplete(ex);
            stop.Cancel();
        }
        finally
        {
            while (channel.Reader.TryRead(out var remaining)) ArrayPool<byte>.Shared.Return(remaining.Buffer);
            foreach (var hash in hashes) hash.Dispose();
            if (frameFiles is not null) foreach (var index in frameFiles) index.Dispose();
            transactionFile?.Dispose();
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
            Streams = new[]
            {
                new CapturedStreamSummary(0, _options.ScadaTx.PortName,
                    _options.ScadaTx.PhysicalTransmitter, Interlocked.Read(ref _writtenChunks[0]),
                    Interlocked.Read(ref _writtenBytes[0]), _taps[0].DriverErrorCount, "stream-00.bin", _rawHashes[0],
                    _options.ScadaTx.BaudRate, _options.ScadaTx.Parity.ToString(),
                    _options.ScadaTx.DataBits, _options.ScadaTx.StopBits.ToString()),
                new CapturedStreamSummary(1, _options.RtuTx.PortName,
                    _options.RtuTx.PhysicalTransmitter, Interlocked.Read(ref _writtenChunks[1]),
                    Interlocked.Read(ref _writtenBytes[1]), _taps[1].DriverErrorCount, "stream-01.bin", _rawHashes[1],
                    _options.RtuTx.BaudRate, _options.RtuTx.Parity.ToString(),
                    _options.RtuTx.DataBits, _options.RtuTx.StopBits.ToString())
            }
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
    bool IntegrityWarning);

public sealed record PassiveChunkPreview(
    byte StreamId, long Sequence, DateTime EstimatedUtc, int ByteCount, byte[] FirstBytes);
