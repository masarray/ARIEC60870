// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;

namespace ARIEC60870.Monitor;

/// <summary>
/// A disposable, disk-backed, per-session index for passive physical FT1.2 frames.
/// Unlike the bounded GUI ring this receives all scanner output on the sole writer
/// thread before UI channel drop. This is NOT a signed/full raw evidence journal.
/// </summary>
public sealed class PassiveSessionTraceHistory : IDisposable
{
    private const int IndexBytes = 24; // stream byte offset, UTC ticks, payload file position
    private const int HeaderBytes = 22;
    private const int MaxWireBytes = 4096;
    private const long MaxSessionHistoryBytes = 2L * 1024 * 1024 * 1024;
    private long _storedBytes;
    private readonly object _gate = new();
    private readonly FileStream? _sessionLock;
    private readonly FileStream[] _data;
    private readonly FileStream[] _indices;
    private readonly long[] _counts;
    private bool _disposed;

    public PassiveSessionTraceHistory(int streams, string? parentDirectory = null)
    {
        if (streams is not (2 or 4)) throw new ArgumentOutOfRangeException(nameof(streams));
        var parent = parentDirectory ?? Path.Join(Path.GetTempPath(), "ARIEC60870", "TraceSessions");
        Directory.CreateDirectory(parent);
        // Only owned, stale sessions are scavenged; a running instance's folder is untouched.
        foreach (var old in Directory.EnumerateDirectories(parent))
        {
            try
            {
                var info = new DirectoryInfo(old);
                if ((info.Attributes & FileAttributes.ReparsePoint) == 0 &&
                    info.Name.StartsWith("session-", StringComparison.Ordinal) &&
                    info.CreationTimeUtc < DateTime.UtcNow.AddDays(-1))
                {
                    // Never clean another instance's still-running, multi-day
                    // field session. Its exclusive lock remains open until close.
                    var lockPath = Path.Join(old, "session.lock");
                    using (new FileStream(lockPath, FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None)) { }
                    info.Delete(recursive: true);
                }
            }
            catch (IOException ex)
            {
                System.Diagnostics.Trace.TraceInformation("Old Trace cache locked or unavailable: " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Trace.TraceWarning("Old Trace cache access denied: " + ex.Message);
            }
        }
        DirectoryPath = Path.Join(parent, "session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        _sessionLock = new FileStream(Path.Join(DirectoryPath, "session.lock"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        _data = new FileStream[streams];
        _indices = new FileStream[streams];
        _counts = new long[streams];
        try
        {
            for (var i = 0; i < streams; i++)
            {
                _data[i] = Open(Path.Join(DirectoryPath, $"frames-{i:00}.bin"));
                _indices[i] = Open(Path.Join(DirectoryPath, $"frames-{i:00}.idx"));
            }
        }
        catch
        {
            foreach (var file in _data) file?.Dispose();
            foreach (var file in _indices) file?.Dispose();
            _sessionLock?.Dispose();
            Directory.Delete(DirectoryPath, recursive: true);
            throw;
        }
    }

    public string DirectoryPath { get; }
    public long FrameCount { get { lock (_gate) return _counts.Sum(); } }

    public void Append(PassiveObservedFrame frame)
    {
        if (frame.RawBytes.Length is < 1 or > MaxWireBytes)
            throw new InvalidDataException("Passive frame outside bounded FT1.2 history record.");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (frame.StreamId >= _data.Length) throw new ArgumentOutOfRangeException(nameof(frame));
            if (_storedBytes + HeaderBytes + IndexBytes + frame.RawBytes.Length >
                MaxSessionHistoryBytes)
                throw new IOException("Temporary Trace history reached its 2 GiB session limit; " +
                    "live RX continues but earlier history requires full raw recording.");
            var sid = frame.StreamId;
            var data = _data[sid];
            var index = _indices[sid];
            Span<byte> record = stackalloc byte[HeaderBytes];
            BinaryPrimitives.WriteInt64LittleEndian(record, frame.StreamOffset);
            BinaryPrimitives.WriteInt64LittleEndian(record[8..], frame.EstimatedUtc.Ticks);
            BinaryPrimitives.WriteUInt16LittleEndian(record[16..], (ushort)frame.RawBytes.Length);
            record[18] = frame.Format switch
            {
                "Fixed" => 1, "Variable" => 2, "Single" => 3,
                "Partial" => 4, "Noise" => 5, "LinkWidthMismatch" => 6, _ => 0
            };
            record[19] = frame.ChecksumValid ? (byte)1 : (byte)0;
            record[20] = frame.DetectedLinkAddressWidth is { } width ? (byte)width : (byte)255;
            record[21] = 0;
            var position = data.Position;
            data.Write(record);
            data.Write(frame.RawBytes);
            Span<byte> ix = stackalloc byte[IndexBytes];
            BinaryPrimitives.WriteInt64LittleEndian(ix, frame.StreamOffset);
            BinaryPrimitives.WriteInt64LittleEndian(ix[8..], frame.EstimatedUtc.Ticks);
            BinaryPrimitives.WriteInt64LittleEndian(ix[16..], position);
            index.Write(ix);
            _counts[sid]++;
            _storedBytes += HeaderBytes + IndexBytes + frame.RawBytes.Length;
        }
    }

    /// <summary>
    /// Read a short, source-qualified, chronological window on a background
    /// thread. Per-stream disk indexes are binary searched; no all-frame RAM list.
    /// </summary>
    public IReadOnlyList<PassiveObservedFrame> ReadWindow(
        byte streamId, long streamOffset, int before = 80, int after = 80,
        CancellationToken cancellationToken = default)
    {
        if (before is < 0 or > 250 || after is < 0 or > 250)
            throw new ArgumentOutOfRangeException(nameof(before));
        long[] counts;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (streamId >= _data.Length) return Array.Empty<PassiveObservedFrame>();
            // FileStream buffering must be visible to a separate read handle.
            for (var i = 0; i < _data.Length; i++)
            {
                _data[i].Flush();
                _indices[i].Flush();
            }
            counts = (long[])_counts.Clone();
        }

        using var anchorIndex = ReadIndex(streamId);
        var exact = LowerBound(anchorIndex, counts[streamId], streamOffset, byTime: false);
        if (exact >= counts[streamId] || ReadIndexEntry(anchorIndex, exact).Offset != streamOffset)
            return Array.Empty<PassiveObservedFrame>();

        var target = ReadIndexEntry(anchorIndex, exact);
        var candidate = new List<PassiveObservedFrame>((before + after + 25) * counts.Length);
        // Read a bounded time neighborhood from every source, not just RTU RX.
        // That keeps SELECT/EXECUTE and ACTCON visible together in one window.
        Span<byte> hdr = stackalloc byte[HeaderBytes];
        for (byte sid = 0; sid < counts.Length; sid++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var index = ReadIndex(sid);
            using var data = new FileStream(DataPath(sid), FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.RandomAccess);
            var center = sid == streamId ? exact :
                LowerBound(index, counts[sid], target.UtcTicks, byTime: true);
            var start = Math.Max(0, center - before - 20);
            var end = Math.Min(counts[sid], center + after + 21);
            for (var i = start; i < end; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = ReadIndexEntry(index, i);
                data.Position = entry.Position;
                data.ReadExactly(hdr);
                var len = BinaryPrimitives.ReadUInt16LittleEndian(hdr[16..]);
                if (len is 0 or > MaxWireBytes ||
                    BinaryPrimitives.ReadInt64LittleEndian(hdr) != entry.Offset)
                    throw new InvalidDataException("Session Trace temporary frame index is invalid.");
                var bytes = new byte[len];
                data.ReadExactly(bytes);
                var format = hdr[18] switch
                {
                    1 => "Fixed", 2 => "Variable", 3 => "Single",
                    4 => "Partial", 5 => "Noise", 6 => "LinkWidthMismatch", _ => "Unknown"
                };
                candidate.Add(new PassiveObservedFrame(sid, entry.Offset,
                    new DateTime(entry.UtcTicks, DateTimeKind.Utc), format, hdr[19] == 1,
                    bytes, hdr[20] == 255 ? null : hdr[20]));
            }
        }
        var sorted = candidate.OrderBy(x => x.EstimatedUtc)
            .ThenBy(x => x.StreamId).ThenBy(x => x.StreamOffset).ToArray();
        var hit = Array.FindIndex(sorted, x => x.StreamId == streamId && x.StreamOffset == streamOffset);
        if (hit < 0) return Array.Empty<PassiveObservedFrame>();
        return sorted.Skip(Math.Max(0, hit - before)).Take(before + after + 1).ToArray();
    }

    private FileStream ReadIndex(byte sid) => new(IndexPath(sid), FileMode.Open,
        FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
    private string DataPath(byte sid) => Path.Join(DirectoryPath, $"frames-{sid:00}.bin");
    private string IndexPath(byte sid) => Path.Join(DirectoryPath, $"frames-{sid:00}.idx");
    private static FileStream Open(string path) => new(path, FileMode.CreateNew,
        FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan);
    private static long LowerBound(FileStream index, long count, long value, bool byTime)
    {
        long lo = 0, hi = count;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            var item = ReadIndexEntry(index, mid);
            if ((byTime ? item.UtcTicks : item.Offset) < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
    private static (long Offset, long UtcTicks, long Position) ReadIndexEntry(FileStream index, long slot)
    {
        index.Position = checked(slot * IndexBytes);
        Span<byte> entry = stackalloc byte[IndexBytes];
        index.ReadExactly(entry);
        return (BinaryPrimitives.ReadInt64LittleEndian(entry),
            BinaryPrimitives.ReadInt64LittleEndian(entry[8..]),
            BinaryPrimitives.ReadInt64LittleEndian(entry[16..]));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var file in _data) file?.Dispose();
            foreach (var file in _indices) file?.Dispose();
            _sessionLock?.Dispose();
        }
        // The temp cache never survives the owner session's final disposal.
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
    }
}
