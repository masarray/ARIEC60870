// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;

namespace ARIEC60870.Monitor;

/// <summary>
/// One accepted source-object observation. Original raw and physical identity
/// are retained; session-local ordinal is never mistaken for an IOA or wire SQ.
/// </summary>
public sealed record PassiveProcessEventFact(
    long Sequence, PassiveObservedFrame Frame, int ObjectIndex, bool WasChanged);

/// <summary>
/// Append-only, indexed temporary session history for process event facts.
/// A slow UI never participates in writer backpressure or causes fact eviction.
/// The directory is owned/cleaned by the existing PassiveSessionTraceHistory.
/// This is not a replacement for explicitly enabled permanent raw recording.
/// </summary>
public sealed class PassiveProcessEventHistory : IDisposable
{
    private const int HeaderBytes = 32;
    private const long DefaultMaxBytes = 1024L * 1024 * 1024;
    private readonly object _gate = new();
    private readonly FileStream _data;
    private readonly FileStream _index;
    private readonly long _maxBytes;
    private long _count;
    private long _written;
    private bool _disposed;

    public PassiveProcessEventHistory(string sessionTraceDirectory,
        long maxBytes = DefaultMaxBytes)
    {
        if (string.IsNullOrWhiteSpace(sessionTraceDirectory) ||
            !Directory.Exists(sessionTraceDirectory))
            throw new ArgumentException("An active, owned Trace session directory is required.",
                nameof(sessionTraceDirectory));
        if (maxBytes < HeaderBytes + 8 + 1)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _maxBytes = maxBytes;
        var dataPath = Path.Combine(sessionTraceDirectory, "process-events.bin");
        var indexPath = Path.Combine(sessionTraceDirectory, "process-events.idx");
        _data = new FileStream(dataPath, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete, 65536);
        try
        {
            _index = new FileStream(indexPath, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete, 65536);
        }
        catch
        {
            _data.Dispose();
            throw;
        }
        DataPath = dataPath;
        IndexPath = indexPath;
    }

    private string DataPath { get; }
    private string IndexPath { get; }
    public long Count { get { lock (_gate) return _count; } }
    public long StoredBytes { get { lock (_gate) return _written; } }

    /// <summary>
    /// Called only from the session's single writer. A failed append is NOT
    /// counted as indexed. The caller must report an explicit unindexed fact.
    /// </summary>
    public void Append(PassiveObservedFrame frame, int objectIndex, bool changed = false)
    {
        if (frame.Format != "Variable" || !frame.ChecksumValid ||
            frame.StreamId >= 4 || frame.RawBytes.Length is < 7 or > 261 ||
            objectIndex is < 1 or > 127)
            throw new ArgumentException("Only validated IEC-101 variable frame objects are indexable.");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var bytes = HeaderBytes + frame.RawBytes.Length + sizeof(long);
            if (_written + bytes > _maxBytes)
                throw new IOException("Temporary event history reached its bounded capacity.");
            Span<byte> header = stackalloc byte[HeaderBytes];
            header.Clear();
            header[0] = frame.StreamId;
            header[1] = frame.DetectedLinkAddressWidth is { } width ? (byte)width : (byte)255;
            header[2] = changed ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt64LittleEndian(header[4..], frame.StreamOffset);
            BinaryPrimitives.WriteInt64LittleEndian(header[12..], frame.EstimatedUtc.Ticks);
            BinaryPrimitives.WriteInt32LittleEndian(header[20..], objectIndex);
            BinaryPrimitives.WriteInt32LittleEndian(header[24..], frame.RawBytes.Length);
            var position = _data.Position;
            _data.Write(header);
            _data.Write(frame.RawBytes);
            Span<byte> positionBytes = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(positionBytes, position);
            _index.Write(positionBytes);
            _written += bytes;
            _count++;
        }
    }

    /// <summary>
    /// Read a deterministic, bounded page after a zero-based fact cursor.
    /// Call from a background worker, not the WPF Dispatcher.
    /// The index is append-only and old entries are never overwritten.
    /// </summary>
    public IReadOnlyList<PassiveProcessEventFact> ReadAfter(
        long alreadyRead, int limit, CancellationToken cancellationToken = default)
    {
        if (alreadyRead < 0 || limit is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(alreadyRead));
        long until;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _data.Flush();
            _index.Flush();
            until = Math.Min(_count, alreadyRead + limit);
        }
        if (alreadyRead >= until) return Array.Empty<PassiveProcessEventFact>();
        var facts = new List<PassiveProcessEventFact>((int)(until - alreadyRead));
        using var positions = new FileStream(IndexPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.RandomAccess);
        using var raw = new FileStream(DataPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.RandomAccess);
        Span<byte> header = stackalloc byte[HeaderBytes];
        Span<byte> ix = stackalloc byte[sizeof(long)];
        for (var sequence = alreadyRead; sequence < until; sequence++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            positions.Position = sequence * sizeof(long);
            positions.ReadExactly(ix);
            var position = BinaryPrimitives.ReadInt64LittleEndian(ix);
            if (position < 0 || position > raw.Length - HeaderBytes)
                throw new InvalidDataException("Process event index references invalid data.");
            raw.Position = position;
            raw.ReadExactly(header);
            var sid = header[0];
            var width = header[1];
            var offset = BinaryPrimitives.ReadInt64LittleEndian(header[4..]);
            var ticks = BinaryPrimitives.ReadInt64LittleEndian(header[12..]);
            var objectIndex = BinaryPrimitives.ReadInt32LittleEndian(header[20..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header[24..]);
            if (sid >= 4 || width is not (0 or 1 or 2 or 255) ||
                length is < 7 or > 261 || objectIndex is < 1 or > 127 ||
                ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks ||
                offset < 0 || length > raw.Length - raw.Position)
                throw new InvalidDataException("Process event history record is malformed.");
            var frame = new byte[length];
            raw.ReadExactly(frame);
            facts.Add(new PassiveProcessEventFact(sequence + 1,
                new PassiveObservedFrame(sid, offset,
                    new DateTime(ticks, DateTimeKind.Utc), "Variable", true,
                    frame, width == 255 ? null : width), objectIndex,
                header[2] == 1));
        }
        return facts;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _data.Dispose();
            _index.Dispose();
        }
    }
}
