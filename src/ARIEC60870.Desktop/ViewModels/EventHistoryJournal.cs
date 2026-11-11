// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Temporary, append-only session index for every event admitted to the
/// operator Event Log. This is not a substitute for the independently owned
/// UART/raw journal, or proof that the outstation transmitted all SOEs.
/// </summary>
public sealed class EventHistoryJournal : IDisposable
{
    private const int HeaderSize = 36; // JSON length (4), SHA-256 (32)
    private const long DefaultMaxBytes = 1024L * 1024 * 1024;
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _dataPath;
    private readonly string _indexPath;
    private readonly FileStream _data;
    private readonly FileStream _index;
    private readonly long _maxBytes;
    private long _count;
    private long _bytes;
    private bool _disposed;

    public EventHistoryJournal(long maxBytes = DefaultMaxBytes)
    {
        if (maxBytes < HeaderSize + 8 + 64)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _maxBytes = maxBytes;
        _directory = Path.Combine(Path.GetTempPath(), "ArSee", "EventHistory",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _dataPath = Path.Combine(_directory, "events.bin");
        _indexPath = Path.Combine(_directory, "events.idx");
        _data = new FileStream(_dataPath, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete, 65536);
        try
        {
            _index = new FileStream(_indexPath, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete, 65536);
        }
        catch
        {
            _data.Dispose();
            throw;
        }
    }

    public long Count { get { lock (_gate) return _count; } }
    public long BytesWritten { get { lock (_gate) return _bytes; } }

    /// <summary>
    /// Called once for each accepted UI event, before its 420-row preview
    /// rollover. Explicit IOException on capacity/disk failure; no silent loss.
    /// </summary>
    public void Append(RelayEventRow row) => AppendBatch(new[] { row });

    /// <summary>
    /// Serialize and hash outside the journal lock (background writer only).
    /// Commit a contiguous batch under one lock. If an I/O operation fails,
    /// Count remains the last fully indexed prefix; no next write is attempted.
    /// Readers validate every committed record before exposing a page.
    /// </summary>
    public void AppendBatch(IReadOnlyList<RelayEventRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return;
        if (rows.Count > 256) throw new ArgumentOutOfRangeException(nameof(rows));
        var payloads = new byte[rows.Count][];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i] ?? throw new ArgumentException("Null event row.", nameof(rows));
            payloads[i] = JsonSerializer.SerializeToUtf8Bytes(new Entry(
                row.EventNumber, row.SourceEntry,
                EventHistoryOriginalSnapshot.Capture(row.OriginalFrameEvidence, row.SourceEntry.RawHex)));
            if (payloads[i].Length is < 1 or > 1024 * 1024)
                throw new IOException("Event history record outside bounded size.");
        }
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            for (var i = 0; i < rows.Count; i++)
            {
                var payload = payloads[i];
                if (rows[i].EventNumber != _count + 1)
                    throw new InvalidDataException("Event number is not contiguous.");
                if (_bytes + HeaderSize + payload.Length + sizeof(long) > _maxBytes)
                    throw new IOException("Temporary Event History reached its storage limit.");
                var pos = _data.Position;
                Span<byte> header = stackalloc byte[HeaderSize];
                BinaryPrimitives.WriteInt32LittleEndian(header[..4], payload.Length);
                SHA256.HashData(payload).AsSpan().CopyTo(header[4..]);
                _data.Write(header);
                _data.Write(payload);
                Span<byte> offset = stackalloc byte[sizeof(long)];
                BinaryPrimitives.WriteInt64LittleEndian(offset, pos);
                _index.Write(offset);
                _bytes += HeaderSize + payload.Length + sizeof(long);
                _count++;
            }
        }
    }

    /// <summary>Zero-based cursor, bounded page. Execute off the Dispatcher.</summary>
    public IReadOnlyList<Entry> ReadAfter(long alreadyRead, int limit)
    {
        if (alreadyRead < 0 || limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(alreadyRead));
        long until;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _data.Flush();
            _index.Flush();
            until = Math.Min(_count, alreadyRead + limit);
        }
        if (alreadyRead >= until) return Array.Empty<Entry>();
        using var offsets = new FileStream(_indexPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.RandomAccess);
        using var data = new FileStream(_dataPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.RandomAccess);
        var result = new List<Entry>((int)(until - alreadyRead));
        Span<byte> idx = stackalloc byte[8];
        Span<byte> header = stackalloc byte[HeaderSize];
        for (var ordinal = alreadyRead; ordinal < until; ordinal++)
        {
            offsets.Position = ordinal * 8;
            offsets.ReadExactly(idx);
            var position = BinaryPrimitives.ReadInt64LittleEndian(idx);
            if (position < 0 || position > data.Length - HeaderSize)
                throw new InvalidDataException("Event History index points outside data.");
            data.Position = position;
            data.ReadExactly(header);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header[..4]);
            if (length is < 1 or > 1024 * 1024 || length > data.Length - data.Position)
                throw new InvalidDataException("Event History record length is invalid.");
            var payload = new byte[length];
            data.ReadExactly(payload);
            if (!SHA256.HashData(payload).AsSpan().SequenceEqual(header[4..]))
                throw new InvalidDataException("Event History record hash mismatch.");
            var entry = JsonSerializer.Deserialize<Entry>(payload)
                ?? throw new InvalidDataException("Event History record is missing.");
            if (entry.EventNumber != ordinal + 1 || entry.Source is null)
                throw new InvalidDataException("Event History ordinal/source mismatch.");
            result.Add(entry);
        }
        return result;
    }

    public sealed record Entry(long EventNumber, Iec103RelayEventLogEntry Source,
        EventHistoryOriginalSnapshot? Original)
    {
        public RelayEventRow ToRow() => new(Source, Original?.Restore(Source.RawHex), EventNumber);
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
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { /* best-effort temporary cleanup */ }
        catch (UnauthorizedAccessException) { /* OS may still hold a reader */ }
    }
}

/// <summary>
/// Preserve the physical source, exact raw bytes and original sequence needed
/// for source-qualified Event -> Frame navigation after the live ring rolls.
/// Never infer a different source frame by IOA, timestamp or equal HEX.
/// </summary>
public sealed record EventHistoryOriginalSnapshot(
    long Sequence, DateTime TimestampUtc, FrameDirection Direction,
    Iec60870ProtocolMode Protocol, string RawHex, string PhysicalSource,
    int? Stream, long? Offset, long? FrameSequence, int? LinkAddress,
    int LinkAddressWidth, int? TypeId, string TypeName,
    int? Cot, string CauseName, int? Ca, int? Ioa,
    string SignalName, string SignalValue, string RelayTime,
    bool RawHexSharedWithSource)
{
    public static EventHistoryOriginalSnapshot? Capture(
        Iec103MasterEvidenceEvent? source, string? eventRawHex = null)
        => source is null ? null : new(
            source.SequenceNumber, source.TimestampUtc, source.Direction,
            source.ProtocolMode,
            !string.IsNullOrEmpty(source.RawHex) &&
            string.Equals(source.RawHex, eventRawHex, StringComparison.Ordinal)
                ? string.Empty : source.RawHex, source.PhysicalSource,
            source.PhysicalStreamId, source.RawStreamOffset,
            source.PhysicalFrameSequenceNumber, source.LinkAddress,
            source.Frame?.LinkAddressSize ?? 1,
            source.TypeId, source.TypeName, source.CauseOfTransmission,
            source.CauseName, source.CommonAddressNumber,
            source.InformationObjectAddress, source.SignalName,
            source.SignalDisplayValue, source.RelayTimestampText,
            !string.IsNullOrEmpty(source.RawHex) &&
            string.Equals(source.RawHex, eventRawHex, StringComparison.Ordinal));

    public Iec103MasterEvidenceEvent Restore(string? eventRawHex = null)
    {
        // A single on-disk raw HEX copy is sufficient when the event and
        // original physical frame share identical bytes; never infer equality.
        var rawHex = RawHexSharedWithSource ? eventRawHex ?? string.Empty : RawHex;
        Ft12FrameDecode? frame = null;
        if (Protocol is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec103 &&
            LinkAddressWidth is >= 0 and <= 2 && !string.IsNullOrWhiteSpace(rawHex))
        {
            try
            {
                var raw = Convert.FromHexString(rawHex.Replace(" ", "", StringComparison.Ordinal));
                frame = Protocol == Iec60870ProtocolMode.Iec101
                    ? Ft12Parser.ForIec101(LinkAddressWidth).Decode(raw)
                    : Ft12Parser.ForIec103(LinkAddressWidth).Decode(raw);
            }
            catch (FormatException) { /* raw evidence stays verbatim */ }
        }
        return new Iec103MasterEvidenceEvent
        {
            SequenceNumber = Sequence,
            TimestampUtc = TimestampUtc,
            Direction = Direction,
            ProtocolMode = Protocol,
            RawHex = rawHex,
            PhysicalSource = PhysicalSource,
            PhysicalStreamId = Stream,
            RawStreamOffset = Offset,
            PhysicalFrameSequenceNumber = FrameSequence,
            LinkAddress = LinkAddress,
            Frame = frame,
            TypeId = TypeId,
            TypeName = TypeName,
            CauseOfTransmission = Cot,
            CauseName = CauseName,
            CommonAddressNumber = Ca,
            InformationObjectAddress = Ioa,
            SignalName = SignalName,
            SignalDisplayValue = SignalValue,
            RelayTimestampText = RelayTime,
            IsRelayValue = true
        };
    }
}

/// <summary>Pure, stable navigation arithmetic; the live ceiling is pinned
/// while a historical page is selected.</summary>
public static class EventHistoryPagination
{
    public const int PageSize = 100;
    public static long PageCount(long total) => total <= 0 ? 0 :
        (total - 1) / PageSize + 1;
    public static long StartForEvent(long eventNumber)
    {
        if (eventNumber <= 0) throw new ArgumentOutOfRangeException(nameof(eventNumber));
        return (eventNumber - 1) / PageSize * PageSize;
    }
}
