// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class PassiveProcessEventLedgerTests
{
    [Fact]
    public void WriterClassifierRejectsMasterTxCorruptionAndBadAsduWithoutFabricatingFacts()
    {
        var classifier = new PassiveIec101ProcessEventClassifier(1, 2, 2, 3);
        var accepted = new List<PassiveProcessEventCandidate>();
        var raw = MakeFrame(0);
        var host = DateTime.SpecifyKind(new DateTime(2026, 11, 5), DateTimeKind.Utc);
        classifier.Observe(new PassiveObservedFrame(1, 0, host,
            "Variable", true, raw), accepted);
        Assert.Equal(4, accepted.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, accepted.Select(x => x.ObjectIndex));
        Assert.All(accepted, x => {
            Assert.Equal(31, x.TypeId);
            Assert.Equal(3, x.Cot);
            Assert.Equal(105, x.CommonAddress);
            Assert.False(x.Changed);
        });
        accepted.Clear();
        // PRM=1: same valid ASDU is Master TX and never an RTU event.
        var tx = MakeFrame(0, master: true);
        classifier.Observe(new PassiveObservedFrame(0, 0, host,
            "Variable", true, tx), accepted);
        Assert.Empty(accepted);
        var tampered = raw.ToArray();
        tampered[15] ^= 0x40;
        classifier.Observe(new PassiveObservedFrame(1, 60, host,
            "Variable", true, tampered), accepted);
        Assert.Empty(accepted);
        var truncated = raw.Take(raw.Length - 3).ToArray();
        classifier.Observe(new PassiveObservedFrame(1, 120, host,
            "Variable", true, truncated), accepted);
        Assert.Empty(accepted);
        // The fact store's own byte check prevents a previously flagged-good
        // source from being promoted through a tampered ASDU.
        Assert.True(new Iec10xAsduDecoder().Decode(raw.AsSpan(6, raw.Length - 8)
            .ToArray()).Objects.Count == 4);
    }

    [Fact]
    public void IndexedHistoryRetainsAll2560ObjectsWithBoundedCursorPagesAndNoPreviewDependency()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "ariec-event-ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var journal = new PassiveProcessEventHistory(dir);
            var classifier = new PassiveIec101ProcessEventClassifier(1, 2, 2, 3);
            var candidates = new List<PassiveProcessEventCandidate>(4);
            var utc = DateTime.SpecifyKind(new DateTime(2026, 11, 5), DateTimeKind.Utc);
            for (var i = 0; i < 640; i++)
            {
                var frame = new PassiveObservedFrame(1, i * 58L, utc.AddMilliseconds(i),
                    "Variable", true, MakeFrame(i));
                candidates.Clear();
                classifier.Observe(frame, candidates);
                Assert.Equal(4, candidates.Count);
                foreach (var obj in candidates)
                    journal.Append(frame, obj.ObjectIndex, obj.Changed);
            }
            Assert.Equal(2560L, journal.Count);
            Assert.InRange(journal.StoredBytes, 1, 2_000_000);
            var first = journal.ReadAfter(0, 120);
            var last = journal.ReadAfter(2500, 120);
            Assert.Equal(120, first.Count);
            Assert.Equal(60, last.Count);
            Assert.Equal(1L, first[0].Sequence);
            Assert.Equal(2560L, last[^1].Sequence);
            Assert.Equal((byte)1, first[0].Frame.StreamId);
            Assert.Equal(0, first[0].Frame.StreamOffset);
            Assert.Equal(3, first[2].ObjectIndex);
            Assert.Equal(639 * 58L, last[^1].Frame.StreamOffset);
            Assert.Equal(4, last[^1].ObjectIndex);
            Assert.All(last, x => Assert.Equal("Variable", x.Frame.Format));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task FullRxSessionCountsAndIndexesAll2560SoesEvenAfter512FramePreviewRollover()
    {
        var output = Path.Combine(Path.GetTempPath(),
            "ariec-p0b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var wire = Enumerable.Range(0, 640).SelectMany(i => MakeFrame(i)).ToArray();
        var options = new PassiveCaptureOptions(output,
            new PassiveSerialSource("COM71", "SCADA TX"),
            new PassiveSerialSource("COM72", "RTU TX"),
            ChannelCapacity: 1024, ReadChunkBytes: 256, RecordFullSession: true);
        var session = new PassiveMonitorSession(options,
            new TestTap("COM71", Array.Empty<byte>()), new TestTap("COM72", wire));
        try
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(1600));
            var result = await session.RunAsync(stop.Token);
            Assert.Equal("NoApplicationLossDetected", result.Manifest.Status);
            Assert.Equal(wire.Length, session.GetProgress().RtuBytes);
            var progress = session.GetProcessEventProgress();
            Assert.Equal(2560, progress.AcceptedObjects);
            Assert.Equal(2560, progress.IndexedObjects);
            Assert.Equal(0, progress.UnindexedObjects);
            Assert.True(progress.TemporaryIndexAvailable);
            Assert.True(progress.PreviewFramesDropped >= 128);
            Assert.Equal(2560, result.Manifest.AcceptedProcessEventObjects);
            Assert.Equal(2560, File.ReadLines(
                Path.Combine(result.Directory, "process-events.jsonl")).Count());
            var history = Assert.IsType<PassiveProcessEventHistory>(session.ProcessEventHistory);
            var recent = history.ReadAfter(2450, 150);
            Assert.Equal(110, recent.Count);
            Assert.Equal(2560L, recent[^1].Sequence);
            Assert.True(PassiveJournalVerifier.Verify(result.Directory).Complete);
            Assert.Equal(0, session.GetProgress().RtuDriverErrors);
        }
        finally
        {
            session.ProcessEventHistory?.Dispose();
            session.TraceHistory?.Dispose();
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void IndexCapacityLimitIsExplicitNotSilentlyEvictingOldFacts()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "ariec-event-bounded-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var journal = new PassiveProcessEventHistory(dir, maxBytes: 120);
            var frame = new PassiveObservedFrame(1, 0, DateTime.UtcNow, "Variable",
                true, MakeFrame(0));
            journal.Append(frame, 1);
            Assert.Equal(1, journal.Count);
            Assert.Throws<IOException>(() => journal.Append(frame, 2));
            Assert.Equal(1, journal.Count);
            Assert.Equal(1, Assert.Single(journal.ReadAfter(0, 5)).ObjectIndex);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static byte[] MakeFrame(int frameNumber, bool master = false)
    {
        var body = new List<byte> { (byte)(master ? 0x68 : 0x28), 0x69,
            31, 4, 3, 0, 105, 0 };
        for (var i = 0; i < 4; i++)
        {
            var ioa = frameNumber * 4 + i + 1;
            body.Add((byte)ioa);
            body.Add((byte)(ioa >> 8));
            body.Add((byte)(ioa >> 16));
            body.Add(2); // IEC101 DIQ double point ON, quality valid
            body.Add(0x2A); body.Add(0xAB); // CP56 milliseconds
            body.Add(4); body.Add(17); body.Add(5); body.Add(11); body.Add(26);
        }
        var length = (byte)body.Count;
        return new byte[] { 0x68, length, length, 0x68 }.Concat(body)
            .Concat(new byte[] { (byte)(body.Sum(x => x) & 255), 0x16 }).ToArray();
    }

    private sealed class TestTap : IReadOnlySerialTap
    {
        private readonly byte[] _bytes;
        private int _position;
        private bool _opened;
        public TestTap(string portName, byte[] bytes) { PortName = portName; _bytes = bytes; }
        public string PortName { get; }
        public long DriverErrorCount => 0;
        public void Open() => _opened = true;
        public int Read(byte[] buffer, int offset, int count)
        {
            if (!_opened) throw new IOException("Read from a closed tap.");
            var n = Math.Min(count, Math.Min(256, _bytes.Length - _position));
            if (n <= 0) { Thread.Sleep(5); return 0; }
            Array.Copy(_bytes, _position, buffer, offset, n);
            _position += n;
            return n;
        }
        public void Close() => _opened = false;
        public void Dispose() => Close();
    }
}
