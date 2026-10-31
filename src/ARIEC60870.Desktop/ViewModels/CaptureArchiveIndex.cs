// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// A single verified, read-only capture snapshot. The original ZIP is never
/// modified. Both the decompressed JSONL and fixed-width match offsets live in
/// OS-managed delete-on-close files, not in a growing collection of WPF rows.
/// All pages of a query read the SAME verified snapshot.
/// </summary>
public sealed class CaptureArchiveIndex : IDisposable
{
    private const int MaxLineBytes = 128 * 1024;
    private readonly FileStream _frames;
    private readonly CaptureArchiveReader.Result _verified;
    private readonly string _sourcePath;
    private readonly long _sourceLength;
    private readonly DateTime _sourceModifiedUtc;
    private FileStream? _matches;
    private CaptureArchiveQuery? _activeQuery;
    private long _matchingRows;
    private bool _disposed;

    internal CaptureArchiveIndex(FileStream frames, CaptureArchiveReader.Result verified,
        string sourcePath, long sourceLength, DateTime sourceModifiedUtc)
    {
        _frames = frames;
        _verified = verified;
        _sourcePath = Path.GetFullPath(sourcePath);
        _sourceLength = sourceLength;
        _sourceModifiedUtc = sourceModifiedUtc;
    }

    public bool IntegrityVerified => _verified.IntegrityVerified;
    public long TotalRows => _verified.TotalRows;

    /// <summary>A changed source must be explicitly reopened and reverified;
    /// a previously verified session never silently reads replacement ZIP bytes.</summary>
    public bool IsCurrentSource(string path)
    {
        if (_disposed || !string.Equals(Path.GetFullPath(path), _sourcePath,
                StringComparison.OrdinalIgnoreCase)) return false;
        var info = new FileInfo(path);
        return info.Exists && info.Length == _sourceLength &&
            info.LastWriteTimeUtc == _sourceModifiedUtc;
    }

    /// <summary>
    /// A new query scans the verified local spool once and writes only matching
    /// byte offsets to a bounded-on-disk index. Subsequent pages do zero ZIP
    /// decompression and no all-history scan. A canceled query leaves the
    /// previous complete index available until a new query commits.
    /// </summary>
    public CaptureArchiveReader.Result Search(CaptureArchiveQuery query,
        long skipMatching = 0, int pageSize = 150, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(query);
        if (skipMatching < 0 || skipMatching > 4_000_000)
            throw new ArgumentOutOfRangeException(nameof(skipMatching));
        if (pageSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        cancellationToken.ThrowIfCancellationRequested();
        if (_matches is null || _activeQuery != query)
            BuildMatches(query, cancellationToken);

        var rows = new List<CaptureFrameSnapshot>(pageSize);
        if (skipMatching < _matchingRows)
        {
            _matches!.Position = checked(skipMatching * sizeof(long));
            var offsets = new long[Math.Min(pageSize, checked((int)Math.Min(
                _matchingRows - skipMatching, pageSize)))];
            Span<byte> offsetBytes = stackalloc byte[sizeof(long)];
            for (var i = 0; i < offsets.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _matches.ReadExactly(offsetBytes);
                offsets[i] = BinaryPrimitives.ReadInt64LittleEndian(offsetBytes);
            }

            var scratch = ArrayPool<byte>.Shared.Rent(8192);
            var line = new ArrayBufferWriter<byte>(8192);
            try
            {
                foreach (var offset in offsets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (offset < 0 || offset >= _frames.Length)
                        throw new InvalidDataException("Invalid verified capture row offset.");
                    _frames.Position = offset;
                    line.Clear();
                    while (true)
                    {
                        var count = _frames.Read(scratch);
                        if (count == 0)
                        {
                            if (line.WrittenCount == 0)
                                throw new InvalidDataException("Indexed capture row is missing.");
                            break;
                        }
                        var bytes = scratch.AsSpan(0, count);
                        var end = bytes.IndexOf((byte)'\n');
                        var part = end < 0 ? bytes : bytes[..end];
                        if (line.WrittenCount + part.Length > MaxLineBytes)
                            throw new InvalidDataException("Indexed capture row exceeds safety limit.");
                        line.Write(part);
                        if (end >= 0) break;
                    }
                    rows.Add(JsonSerializer.Deserialize<CaptureFrameSnapshot>(line.WrittenSpan)
                        ?? throw new InvalidDataException("Indexed capture row is null."));
                }
            }
            finally { ArrayPool<byte>.Shared.Return(scratch); }
        }

        return new CaptureArchiveReader.Result(rows.OrderBy(row => row.Sequence).ToArray(),
            _verified.TotalRows, _verified.IntegrityVerified, _verified.TrustDescription)
        {
            MatchingRows = _matchingRows,
            CapturedProtocolMode = _verified.CapturedProtocolMode,
            CapturedProfileId = _verified.CapturedProfileId,
            CapturedProfileEdition = _verified.CapturedProfileEdition,
            CapturedProfileFingerprint = _verified.CapturedProfileFingerprint
        };
    }

    private void BuildMatches(CaptureArchiveQuery query, CancellationToken cancellationToken)
    {
        using var next = CaptureArchiveReader.CreateTemporaryIndexStream();
        _frames.Position = 0;
        var rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
        var pending = new ArrayBufferWriter<byte>(4096);
        long lineStart = 0;
        long bytesRead = 0;
        long matches = 0;
        long parsed = 0;
        try
        {
            int count;
            while ((count = _frames.Read(rented)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cursor = 0;
                while (cursor < count)
                {
                    var span = rented.AsSpan(cursor, count - cursor);
                    var end = span.IndexOf((byte)'\n');
                    var piece = end < 0 ? span : span[..end];
                    if (pending.WrittenCount + piece.Length > MaxLineBytes)
                        throw new InvalidDataException("Indexed capture contains an oversized row.");
                    pending.Write(piece);
                    if (end < 0) break;
                    Accept(pending.WrittenSpan, lineStart);
                    pending.Clear();
                    cursor += end + 1;
                    lineStart = bytesRead + cursor;
                }
                bytesRead += count;
            }
            if (pending.WrittenCount > 0) Accept(pending.WrittenSpan, lineStart);
            if (parsed != _verified.TotalRows)
                throw new InvalidDataException("Indexed capture row count changed.");
            next.Flush();
            next.Position = 0;

            // Commit only after full successful scan, leaving prior index intact on cancel.
            _matches?.Dispose();
            _matches = CaptureArchiveReader.CreateTemporaryIndexStream();
            next.CopyTo(_matches);
            _matches.Position = 0;
            _activeQuery = query;
            _matchingRows = matches;
        }
        finally { ArrayPool<byte>.Shared.Return(rented); }

        void Accept(ReadOnlySpan<byte> row, long offset)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (parsed == 0 && row.Length >= 3 &&
                row[0] == 0xEF && row[1] == 0xBB && row[2] == 0xBF)
                row = row[3..];
            if (row.IsEmpty || (row.Length == 1 && row[0] == '\r')) return;
            var snapshot = JsonSerializer.Deserialize<CaptureFrameSnapshot>(row)
                ?? throw new InvalidDataException("Null indexed capture row.");
            parsed++;
            if (!query.Matches(snapshot)) return;
            Span<byte> encoded = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(encoded, offset);
            next.Write(encoded);
            matches++;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _matches?.Dispose();
        _frames.Dispose();
    }
}
