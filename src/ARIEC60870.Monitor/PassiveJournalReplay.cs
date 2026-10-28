// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;

namespace ARIEC60870.Monitor;

/// <summary>
/// Read-only, streaming replay of the authoritative two-direction raw journals.
/// Runs without serial ports, WPF, live preview files, or the active Master.
/// Each replay verifies all bytes before projecting protocol frames; derived
/// frames-XX.jsonl indexes are intentionally not trusted as evidence.
/// </summary>
public static class PassiveJournalReplay
{
    public static IEnumerable<PassiveObservedFrame> ReadVerifiedFrames(
        string sessionDirectory, CancellationToken cancellationToken = default) =>
        ReadVerifiedFrames(sessionDirectory, replayProfile: null, cancellationToken);

    /// <summary>
    /// Replay with a deliberate, validated interpretation override. CRC/SHA and
    /// original manifest remain authoritative and untouched. The override is
    /// never written to the capture directory.
    /// </summary>
    public static IEnumerable<PassiveObservedFrame> ReadVerifiedFrames(
        string sessionDirectory, PassiveReplayProfile? replayProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        replayProfile?.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var audit = PassiveJournalVerifier.Verify(sessionDirectory, cancellationToken);
        if (!audit.Complete)
            throw new InvalidDataException("Raw journal integrity check failed: " +
                string.Join(" | ", audit.Problems.Take(5)));

        var path = Path.Combine(sessionDirectory, "manifest.json");
        var manifest = JsonSerializer.Deserialize<PassiveCaptureManifest>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Capture manifest is missing.");
        if (manifest.StopwatchFrequency <= 0 || manifest.StopwatchOriginTicks <= 0 ||
            manifest.LinkAddressWidth is < 0 or > 2 || manifest.FrameGapMilliseconds is < 100 or > 10000 ||
            manifest.StartUtc.Kind != DateTimeKind.Utc)
            throw new InvalidDataException("Capture manifest contains invalid replay timing or link settings.");

        var effective = replayProfile ?? PassiveReplayProfile.FromManifest(manifest);
        effective.Validate();

        // The authoritative journal is 2-direction (legacy) or 4-direction
        // (A/B circuits). Keep each source's order, choosing the next chunk
        // by host read-completion ticks without inventing physical wire timing.
        var count = manifest.Streams.Length;
        var cursors = new StreamCursor[count];
        try
        {
            for (byte i = 0; i < count; i++)
            {
                cursors[i] = new StreamCursor(Path.Combine(sessionDirectory,
                    $"stream-{i:00}.bin"), i);
                cursors[i].MoveNext();
            }
            var scanners = Enumerable.Range(0, count)
                .Select(_ => new PassiveFt12Scanner(effective.LinkAddressWidth)).ToArray();
            var lastTicks = new long[count];
            var lastUtc = Enumerable.Repeat(manifest.StartUtc, count).ToArray();
            // Iterator owns this scratch list; each pass fully yields entries
            // before reuse. No list allocation per UART record.
            var emitted = new List<PassiveObservedFrame>(8);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StreamCursor? chosen = null;
                for (var i = 0; i < count; i++)
                {
                    var cursor = cursors[i];
                    if (!cursor.HasRecord) continue;
                    if (chosen is null || cursor.Ticks < chosen.Ticks)
                        chosen = cursor;
                }
                if (chosen is null) break;

                var sid = chosen.StreamId;
                var ticks = chosen.Ticks;
                var delta = (ticks - manifest.StopwatchOriginTicks) /
                    (double)manifest.StopwatchFrequency;
                if (!double.IsFinite(delta) || delta < -1 ||
                    delta > TimeSpan.FromDays(366).TotalSeconds)
                    throw new InvalidDataException("Raw journal contains out-of-range timestamp.");
                var utc = manifest.StartUtc.AddSeconds(delta);
                emitted.Clear();
                if (lastTicks[sid] > 0 &&
                    (ticks - lastTicks[sid]) / (double)manifest.StopwatchFrequency * 1000.0 >
                    effective.FrameGapMilliseconds)
                {
                    var partial = scanners[sid].ExpireIncomplete();
                    if (partial is not null)
                        emitted.Add(new PassiveObservedFrame(sid, partial.ByteOffset, utc,
                            "Partial", false, partial.RawBytes));
                }
                lastTicks[sid] = ticks;
                lastUtc[sid] = utc;
                scanners[sid].Feed(chosen.Payload.AsSpan(0, chosen.PayloadLength),
                    chosen.Offset, frame =>
                    emitted.Add(new PassiveObservedFrame(sid, frame.ByteOffset, utc,
                        frame.Format, frame.ChecksumValid, frame.RawBytes,
                        frame.DetectedLinkAddressWidth)));
                foreach (var frame in emitted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return frame;
                }
                chosen.MoveNext();
            }

            for (byte sid = 0; sid < count; sid++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tail = scanners[sid].ExpireIncomplete();
                if (tail is not null)
                    yield return new PassiveObservedFrame(sid, tail.ByteOffset, lastUtc[sid],
                        "Partial", false, tail.RawBytes);
            }
        }
        finally
        {
            foreach (var cursor in cursors)
                cursor?.Dispose();
        }
    }

    private sealed class StreamCursor : IDisposable
    {
        private readonly FileStream _file;
        private readonly byte[] _header = new byte[PassiveJournalFormat.HeaderSize];
        private long _sequence;
        private long _offset;
        private long _previousTicks;

        public StreamCursor(string path, byte streamId)
        {
            StreamId = streamId;
            _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 65536, options: FileOptions.SequentialScan);
        }

        public byte StreamId { get; }
        public bool HasRecord { get; private set; }
        public long Ticks { get; private set; }
        public long Offset { get; private set; }
        // A single reusable buffer per physical source avoids creating an
        // array on each record of multi-hour four-COM captures.
        public byte[] Payload { get; } = new byte[PassiveJournalFormat.MaxPayload];
        public int PayloadLength { get; private set; }

        public void MoveNext()
        {
            HasRecord = false;
            var n = ReadExactlyOrEof(_header);
            if (n == 0) return;
            if (n != _header.Length)
                throw new InvalidDataException("Truncated raw journal header.");
            var len = BinaryPrimitives.ReadInt32LittleEndian(_header.AsSpan(32, 4));
            if (len is <= 0 or > PassiveJournalFormat.MaxPayload)
                throw new InvalidDataException("Invalid raw journal payload size.");
            if (ReadExactlyOrEof(Payload.AsSpan(0, len)) != len)
                throw new InvalidDataException("Truncated raw journal payload.");
            if (!PassiveJournalFormat.TryDecode(_header, Payload.AsSpan(0, len), StreamId,
                    _sequence + 1, _offset, out var reason))
                throw new InvalidDataException("Raw journal record invalid: " + reason);
            var ticks = BinaryPrimitives.ReadInt64LittleEndian(_header.AsSpan(16, 8));
            if (_sequence > 0 && ticks < _previousTicks)
                throw new InvalidDataException("Source timestamps are not monotonic.");
            _previousTicks = ticks;
            Ticks = ticks;
            Offset = _offset;
            PayloadLength = len;
            HasRecord = true;
            _sequence++;
            _offset += len;
        }

        private int ReadExactlyOrEof(Span<byte> destination)
        {
            var count = 0;
            while (count < destination.Length)
            {
                var read = _file.Read(destination[count..]);
                if (read == 0) break;
                count += read;
            }
            return count;
        }

        public void Dispose() => _file.Dispose();
    }
}
