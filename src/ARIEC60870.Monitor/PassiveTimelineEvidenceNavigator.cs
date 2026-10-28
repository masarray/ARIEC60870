// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;

namespace ARIEC60870.Monitor;

/// <summary>
/// A bounded page request over derived five-second IEC-101 timeline rows.
/// Bounds are UTC, inclusive for overlapping time windows. BeforeOffset is an
/// exclusive file-position cursor for stable backwards navigation.
/// </summary>
public sealed record PassiveTimelineSearch(
    DateTime? FromUtc = null, DateTime? UntilUtc = null,
    int? CircuitId = null, int Limit = 60, long? BeforeOffset = null,
    bool AnomaliesOnly = true)
{
    public void Validate()
    {
        if (FromUtc is { Kind: not DateTimeKind.Utc } ||
            UntilUtc is { Kind: not DateTimeKind.Utc } ||
            FromUtc > UntilUtc || CircuitId is < 0 or > 1 ||
            Limit is < 1 or > 200 || BeforeOffset is < 0)
            throw new ArgumentOutOfRangeException(nameof(PassiveTimelineSearch),
                "Use UTC dates, Link A/B, a 1-200 row page and a non-negative cursor.");
    }
}

public sealed record PassiveTimelineHit(PassiveTimelineWindow Window, long FileOffset)
{
    public int? PhysicalAnomalyStream => Window.FirstAnomalyStream is { } local
        ? Window.CircuitId * 2 + local : null;

    public string Label =>
        Window.StartUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") +
        " · Link " + (Window.CircuitId == 0 ? "A" : "B") +
        " · retry " + Window.Retries +
        " / timeout " + Window.ResponseTimeouts +
        " / invalid " + Window.InvalidFrames +
        " / FC15 " + Window.ServiceNotImplementedReplies;
}

/// <summary>
/// A read-only sparse index over derived timeline-windows.jsonl. Each block
/// covers at most 128 lines: no File.ReadAllLines or in-memory row history.
/// The original raw journal is verified before opening a capture through
/// OpenVerified. Timeline rows are navigation HINTS, not authenticated evidence;
/// ReadVerifiedFrame replays authoritative APM1 data and checks exact source
/// byte offsets before presenting a raw frame.
/// </summary>
public sealed class PassiveTimelineEvidenceNavigator
{
    private const int RowsPerBlock = 128;
    private const int MaxLineBytes = 65536;
    private readonly string _timelinePath;
    private readonly string? _captureDirectory;
    private readonly long _length;
    private readonly DateTime _lastWriteUtc;
    private readonly List<Block> _blocks;

    private sealed record Block(long FileOffset, int RowCount, long MinStartTicks,
        long MaxEndTicks, int CircuitMask, bool HasAnomaly);

    private PassiveTimelineEvidenceNavigator(string path, string? directory,
        long length, DateTime modified, List<Block> blocks)
    {
        _timelinePath = path;
        _captureDirectory = directory;
        _length = length;
        _lastWriteUtc = modified;
        _blocks = blocks;
    }

    public int SparseBlockCount => _blocks.Count;

    public static PassiveTimelineEvidenceNavigator OpenVerified(
        string sessionDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        var audit = PassiveJournalVerifier.Verify(sessionDirectory, cancellationToken);
        if (!audit.Complete)
            throw new InvalidDataException("Cannot navigate unverified raw capture: " +
                string.Join(" | ", audit.Problems.Take(4)));
        return Build(Path.Combine(sessionDirectory, "timeline-windows.jsonl"),
            sessionDirectory, cancellationToken);
    }

    /// <summary>
    /// Developer-only derived-index reader for synthetic fixtures. Does NOT
    /// authenticate evidence and cannot open original frames.
    /// </summary>
    public static PassiveTimelineEvidenceNavigator OpenDerivedIndexOnly(
        string jsonlPath, CancellationToken cancellationToken = default) =>
        Build(jsonlPath, null, cancellationToken);

    private static PassiveTimelineEvidenceNavigator Build(
        string path, string? directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.SequentialScan);
        using var lineBuffer = new MemoryStream(1024);
        var blocks = new List<Block>();
        long firstOffset = 0, min = long.MaxValue, max = long.MinValue;
        int rows = 0, mask = 0;
        bool anomaly = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = source.Position;
            var line = ReadLine(source, lineBuffer);
            if (line is null) break;
            var window = Parse(line);
            if (rows == 0) firstOffset = offset;
            min = Math.Min(min, window.StartUtc.Ticks);
            max = Math.Max(max, window.EndUtc.Ticks);
            mask |= 1 << window.CircuitId;
            anomaly |= window.HasAnomaly;
            rows++;
            if (rows == RowsPerBlock)
            {
                blocks.Add(new Block(firstOffset, rows, min, max, mask, anomaly));
                rows = 0; min = long.MaxValue; max = long.MinValue; mask = 0; anomaly = false;
            }
        }
        if (rows > 0)
            blocks.Add(new Block(firstOffset, rows, min, max, mask, anomaly));
        var length = source.Length;
        var writeUtc = File.GetLastWriteTimeUtc(path);
        if (source.Length != length)
            throw new InvalidDataException("Timeline changed while building its sparse index.");
        return new PassiveTimelineEvidenceNavigator(path, directory, length, writeUtc, blocks);
    }

    public IReadOnlyList<PassiveTimelineHit> Search(
        PassiveTimelineSearch query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        EnsureUnchanged();
        var matches = new List<PassiveTimelineHit>(Math.Min(query.Limit, 200));
        using var file = new FileStream(_timelinePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.RandomAccess);
        using var buffer = new MemoryStream(1024);
        for (var i = _blocks.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = _blocks[i];
            if (query.BeforeOffset is { } cursor && block.FileOffset >= cursor)
                continue;
            if (query.AnomaliesOnly && !block.HasAnomaly)
                continue;
            if (query.CircuitId is { } circuit && (block.CircuitMask & (1 << circuit)) == 0)
                continue;
            if (query.FromUtc is { } start && block.MaxEndTicks < start.Ticks)
                continue;
            if (query.UntilUtc is { } end && block.MinStartTicks > end.Ticks)
                continue;
            file.Position = block.FileOffset;
            var candidates = new List<PassiveTimelineHit>(RowsPerBlock);
            for (var row = 0; row < block.RowCount; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var offset = file.Position;
                var text = ReadLine(file, buffer)
                    ?? throw new InvalidDataException("Timeline shortened after indexing.");
                if (query.BeforeOffset is { } before && offset >= before) continue;
                var window = Parse(text);
                if (query.AnomaliesOnly && !window.HasAnomaly) continue;
                if (query.CircuitId is { } required && window.CircuitId != required) continue;
                if (query.FromUtc is { } from && window.EndUtc < from) continue;
                if (query.UntilUtc is { } until && window.StartUtc > until) continue;
                candidates.Add(new PassiveTimelineHit(window, offset));
            }
            for (var n = candidates.Count - 1; n >= 0; n--)
            {
                matches.Add(candidates[n]);
                if (matches.Count >= query.Limit) return matches;
            }
        }
        return matches;
    }

    /// <summary>
    /// The selected anchor is re-found in SHA/CRC-verified raw replay, NEVER
    /// fabricated from JSONL. It is exact but may scan a large archive; run
    /// this method off the WPF Dispatcher and provide a cancellation token.
    /// </summary>
    public PassiveObservedFrame? ReadVerifiedFrame(
        PassiveTimelineHit hit, PassiveReplayProfile? replayProfile = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (_captureDirectory is null)
            throw new InvalidOperationException("A derived index is not authenticated raw evidence.");
        var stream = hit.PhysicalAnomalyStream;
        var offset = hit.Window.FirstAnomalyOffset;
        if (stream is null || offset is null) return null;
        if (stream is < 0 or > 3 || offset < 0)
            throw new InvalidDataException("Timeline references an impossible source offset.");
        EnsureUnchanged();
        foreach (var observed in PassiveJournalReplay.ReadVerifiedFrames(
                     _captureDirectory, replayProfile, cancellationToken))
        {
            if (observed.StreamId != stream) continue;
            if (observed.StreamOffset == offset) return observed;
            if (observed.StreamOffset > offset) break;
        }
        return null;
    }

    private void EnsureUnchanged()
    {
        var info = new FileInfo(_timelinePath);
        if (!info.Exists || info.Length != _length || info.LastWriteTimeUtc != _lastWriteUtc)
            throw new InvalidDataException("Timeline was changed after indexing. Reopen the capture.");
    }

    private static PassiveTimelineWindow Parse(string line)
    {
        PassiveTimelineWindow? window;
        try { window = JsonSerializer.Deserialize<PassiveTimelineWindow>(line); }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Invalid derived timeline JSONL entry.", ex);
        }
        if (window is null || window.CircuitId is < 0 or > 1 ||
            window.StartUtc.Kind != DateTimeKind.Utc ||
            window.EndUtc.Kind != DateTimeKind.Utc || window.EndUtc <= window.StartUtc ||
            window.FirstAnomalyStream is < 0 or > 1 ||
            window.FirstAnomalyOffset is < 0)
            throw new InvalidDataException("Timeline has invalid time, circuit or raw anchor.");
        return window;
    }

    private static string? ReadLine(FileStream file, MemoryStream buffer)
    {
        buffer.SetLength(0);
        var readAny = false;
        while (true)
        {
            var next = file.ReadByte();
            if (next < 0) break;
            readAny = true;
            if (next == '\n') break;
            if (buffer.Length >= MaxLineBytes)
                throw new InvalidDataException("Derived timeline row exceeds the 64 KiB safety bound.");
            buffer.WriteByte((byte)next);
        }
        if (!readAny && buffer.Length == 0) return null;
        var bytes = buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length));
        return Encoding.UTF8.GetString(bytes).TrimEnd('\r').TrimStart('\uFEFF');
    }
}
