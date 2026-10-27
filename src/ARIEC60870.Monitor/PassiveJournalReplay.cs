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
        string sessionDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
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

        using var scada = new StreamCursor(Path.Combine(sessionDirectory, "stream-00.bin"), 0);
        using var rtu = new StreamCursor(Path.Combine(sessionDirectory, "stream-01.bin"), 1);
        var cursors = new[] { scada, rtu };
        var scanners = new[]
        {
            new PassiveFt12Scanner(manifest.LinkAddressWidth),
            new PassiveFt12Scanner(manifest.LinkAddressWidth)
        };
        var lastTicks = new long[2];
        var lastUtc = new[] { manifest.StartUtc, manifest.StartUtc };

        scada.MoveNext();
        rtu.MoveNext();
        while (scada.HasRecord || rtu.HasRecord)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Cross-adapter timestamp order is only a host read-completion estimate.
            // Per-direction journal order is exact; never claim bit-level sequencing.
            var chosen = !rtu.HasRecord || (scada.HasRecord && scada.Ticks <= rtu.Ticks)
                ? scada : rtu;
            var sid = chosen.StreamId;
            var ticks = chosen.Ticks;
            var delta = (ticks - manifest.StopwatchOriginTicks) / (double)manifest.StopwatchFrequency;
            if (!double.IsFinite(delta) || delta < -1 || delta > TimeSpan.FromDays(366).TotalSeconds)
                throw new InvalidDataException("Raw journal contains out-of-range timestamp.");
            var utc = manifest.StartUtc.AddSeconds(delta);
            var emitted = new List<PassiveObservedFrame>();

            if (lastTicks[sid] > 0 && 
                (ticks - lastTicks[sid]) / (double)manifest.StopwatchFrequency * 1000.0 >
                    manifest.FrameGapMilliseconds)
            {
                var partial = scanners[sid].ExpireIncomplete();
                if (partial is not null)
                    emitted.Add(new PassiveObservedFrame(sid, partial.ByteOffset, utc,
                        "Partial", false, partial.RawBytes));
            }
            lastTicks[sid] = ticks;
            lastUtc[sid] = utc;
            scanners[sid].Feed(chosen.Payload, chosen.Offset, frame =>
                emitted.Add(new PassiveObservedFrame(sid, frame.ByteOffset, utc,
                    frame.Format, frame.ChecksumValid, frame.RawBytes)));
            foreach (var frame in emitted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return frame;
            }
            chosen.MoveNext();
        }

        for (byte sid = 0; sid < 2; sid++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tail = scanners[sid].ExpireIncomplete();
            if (tail is not null)
                yield return new PassiveObservedFrame(sid, tail.ByteOffset, lastUtc[sid],
                    "Partial", false, tail.RawBytes);
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
        public byte[] Payload { get; private set; } = Array.Empty<byte>();

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
            var payload = new byte[len];
            if (ReadExactlyOrEof(payload) != len)
                throw new InvalidDataException("Truncated raw journal payload.");
            if (!PassiveJournalFormat.TryDecode(_header, payload, StreamId,
                    _sequence + 1, _offset, out var reason))
                throw new InvalidDataException("Raw journal record invalid: " + reason);
            var ticks = BinaryPrimitives.ReadInt64LittleEndian(_header.AsSpan(16, 8));
            if (_sequence > 0 && ticks < _previousTicks)
                throw new InvalidDataException("Source timestamps are not monotonic.");
            _previousTicks = ticks;
            Ticks = ticks;
            Offset = _offset;
            Payload = payload;
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
