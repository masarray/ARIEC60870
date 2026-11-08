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

    [Fact]
    public void InvalidQualityTestAndNegativeObservationsCannotPoisonGiTransitionBaseline()
    {
        var classifier = new PassiveIec101ProcessEventClassifier(1, 2, 2, 3);
        var accepted = new List<PassiveProcessEventCandidate>();
        var utc = new DateTime(2026, 11, 5, 1, 0, 0, DateTimeKind.Utc);
        var offset = 0L;
        void Observe(byte stream, byte quality, byte cot)
        {
            accepted.Clear();
            var raw = MakeFrame(0);
            raw[8] = cot;  // COT, two-octet profile
            raw[15] = quality; // first object's DIQ
            raw[^2] = (byte)(raw.AsSpan(4, raw.Length - 6).ToArray().Sum(x => x) & 0xFF);
            classifier.Observe(new PassiveObservedFrame(stream, offset++, utc,
                "Variable", true, raw), accepted);
        }

        Observe(1, 2, 20); // valid GI baseline, no previous state
        Assert.Empty(accepted); // not an SOE; still a Values snapshot
        var initial = classifier.ReadLatestPointsAfter(0);
        Assert.Equal(4, initial.PointCount);
        Assert.Equal(4, initial.Points.Count);
        Assert.All(initial.Points, x => Assert.Equal(20, x.Frame.RawBytes[8]));
        Observe(1, 0x83, 20); // IV=1: must NOT change baseline
        Assert.Empty(accepted);
        Observe(1, 0x13, 20); // BL=1: not a credible physical edge
        Assert.Empty(accepted);
        Observe(1, 3, 0x94); // TEST with COT 20
        Assert.Empty(accepted);
        Observe(1, 3, 0x54); // NEG with COT 20
        Assert.Empty(accepted);
        var afterNegative = classifier.ReadLatestPointsAfter(0);
        Assert.Equal(4, afterNegative.PointCount);
        Assert.DoesNotContain(afterNegative.Points, x => x.Frame.RawBytes[8] is 0x94 or 0x54);
        Observe(3, 3, 20); // independent Link B has no baseline
        Assert.Empty(accepted);
        Assert.Equal(8, classifier.ReadLatestPointsAfter(0).PointCount);
        Observe(1, 3, 20); // Link A credible 2 -> 3 transition
        var change = Assert.Single(accepted);
        Assert.Equal(1, change.ObjectIndex);
        Assert.True(change.Changed);
        Assert.True(change.HadBaseline);
        var changedPoints = classifier.ReadLatestPointsAfter(afterNegative.LatestRevision);
        Assert.Equal(8, changedPoints.PointCount);
        Assert.Contains(changedPoints.Points, x => x.Frame.StreamId == 1 && x.WasChanged);
        Observe(1, 0x83, 3); // spontaneous invalid still retained, not state-trusted
        Assert.Equal(4, accepted.Count);
        Assert.False(accepted[0].Changed);
        var counters = classifier.GetCounters();
        Assert.True(counters.FramesBalance && counters.AsdusBalance && counters.ObjectsBalance);
        Assert.Equal(8, counters.SecondaryFrames);
        Assert.Equal(32, counters.ObjectsDecoded);
        Assert.Equal(5, counters.ObjectsAccepted); // GI transition 1 + spontaneous 4
    }

    [Fact]
    public void CorruptedTemporaryLedgerFactFailsClosedInsteadOfReturningTrustedRaw()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "ariec-c3-crc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var journal = new PassiveProcessEventHistory(dir);
            journal.Append(new PassiveObservedFrame(1, 0, DateTime.UtcNow,
                "Variable", true, MakeFrame(0)), 1);
            Assert.Single(journal.ReadAfter(0, 1)); // flush existing writer buffers
            using (var disk = new FileStream(Path.Combine(dir, "process-events.bin"),
                FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            {
                disk.Position = 32 + 15; // first DIQ byte of the original frame
                var original = disk.ReadByte();
                Assert.True(original >= 0);
                disk.Position = 32 + 15;
                disk.WriteByte((byte)(original ^ 0x01));
                disk.Flush();
            }
            Assert.Throws<InvalidDataException>(() => journal.ReadAfter(0, 1));
            Assert.Equal(1, journal.Count); // corruption must not invent a second fact
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Mixed50000ObjectClassifierSoakKeepsExactSourceAndRejectionAccounting()
    {
        var classifier = new PassiveIec101ProcessEventClassifier(1, 2, 2, 3);
        var accepted = new List<PassiveProcessEventCandidate>(4);
        var identities = new HashSet<(byte Stream, long Offset, int Object)>();
        var utc = new DateTime(2026, 11, 5, 1, 0, 0, DateTimeKind.Utc);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 12500; i++)
        {
            var raw = MakeFrame(i);
            var cot = i % 5 is 0 ? (byte)20 : i % 5 is 1 ? (byte)2 : (byte)3;
            raw[8] = cot;
            raw[^2] = (byte)(raw.AsSpan(4, raw.Length - 6).ToArray().Sum(x => x) & 0xFF);
            var stream = (byte)(i % 2 == 0 ? 1 : 3);
            var offset = i * 58L;
            accepted.Clear();
            classifier.Observe(new PassiveObservedFrame(stream, offset, utc,
                "Variable", true, raw), accepted);
            Assert.Equal(cot == 3 ? 4 : 0, accepted.Count);
            foreach (var fact in accepted)
                Assert.True(identities.Add((stream, offset, fact.ObjectIndex)));
            if (i % 1000 == 0)
            {
                var broken = raw.ToArray();
                broken[15] ^= 0x01; // corruption with stale checksum
                accepted.Clear();
                classifier.Observe(new PassiveObservedFrame(stream, offset + 1, utc,
                    "Variable", true, broken), accepted);
                Assert.Empty(accepted);
            }
            if (i % 500 == 0)
            {
                accepted.Clear();
                classifier.Observe(new PassiveObservedFrame(0, offset, utc,
                    "Variable", true, MakeFrame(i, master: true)), accepted);
                Assert.Empty(accepted);
            }
        }
        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var stats = classifier.GetCounters();
        Assert.True(stats.FramesBalance && stats.AsdusBalance && stats.ObjectsBalance);
        Assert.Equal(12538, stats.FramesObserved);
        Assert.Equal(13, stats.InvalidVariableFrames);
        Assert.Equal(25, stats.PrimaryFrames);
        Assert.Equal(12500, stats.SecondaryFrames);
        Assert.Equal(12500, stats.AsdusDecoded);
        Assert.Equal(50000, stats.ObjectsDecoded);
        Assert.Equal(30000, stats.ObjectsAccepted);
        Assert.Equal(20000, stats.ObjectsExcluded);
        Assert.Equal(30000, identities.Count);
        // Values state includes 50k accepted process identities even when only
        // 30k of them qualified as Event facts (SOE) in this workload.
        var latest = classifier.ReadLatestPointsAfter(0, 120);
        Assert.Equal(50000, latest.PointCount);
        Assert.Equal(120, latest.Points.Count);
        Assert.Equal(0, latest.CapacityRejected);
        Assert.True(latest.ThroughRevision < latest.LatestRevision);
        Assert.InRange(allocated, 1, long.MaxValue);
        Assert.True(watch.Elapsed > TimeSpan.Zero);
        // Measured on the running CI machine only. No unmeasured p95 GUI claim.
        Console.WriteLine($"C3 classifier 50k objects: {watch.Elapsed.TotalMilliseconds:F1} ms, {allocated:N0} thread-allocated bytes");
    }

    [Fact]
    public async Task DualCircuitRxOnly5000ObjectIntegrationRetainsIndexBeyondUiPreview()
    {
        var output = Path.Combine(Path.GetTempPath(),
            "ariec-c3-dual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var wireA = Enumerable.Range(0, 625).SelectMany(i => MakeFrame(i)).ToArray();
        var wireB = Enumerable.Range(625, 625).SelectMany(i => MakeFrame(i)).ToArray();
        var options = new PassiveCaptureOptions(output,
            new PassiveSerialSource("COM81", "SCADA A"),
            new PassiveSerialSource("COM82", "RTU A"),
            ChannelCapacity: 1024, ReadChunkBytes: 256,
            LinkBScadaTx: new PassiveSerialSource("COM83", "SCADA B"),
            LinkBRtuTx: new PassiveSerialSource("COM84", "RTU B"),
            RecordFullSession: false);
        var session = new PassiveMonitorSession(options,
            new TestTap("COM81", Array.Empty<byte>()), new TestTap("COM82", wireA),
            new TestTap("COM83", Array.Empty<byte>()), new TestTap("COM84", wireB));
        try
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var result = await session.RunAsync(stop.Token);
            var progress = session.GetProcessEventProgress();
            var accounting = Assert.IsType<PassiveProcessDecodeCounters>(progress.DecodeAccounting);
            Assert.Equal("MonitorStopped", result.Manifest.Status);
            Assert.Equal(5000, progress.AcceptedObjects);
            Assert.Equal(5000, progress.IndexedObjects);
            Assert.Equal(0, progress.UnindexedObjects);
            Assert.True(progress.PreviewFramesDropped > 0);
            Assert.True(accounting.FramesBalance && accounting.AsdusBalance && accounting.ObjectsBalance);
            Assert.Equal(1250, accounting.AsdusDecoded);
            Assert.Equal(5000, accounting.ObjectsDecoded);
            Assert.Equal(5000, accounting.ObjectsAccepted);
            Assert.Equal(0, accounting.ObjectsExcluded);
            Assert.Equal(5000, result.Manifest.ProcessDecodeAccounting!.ObjectsAccepted);
            var index = Assert.IsType<PassiveProcessEventHistory>(session.ProcessEventHistory);
            var counts = new Dictionary<byte, int>();
            for (long cursor = 0; cursor < index.Count; cursor += 1024)
            {
                var page = index.ReadAfter(cursor, 1024);
                foreach (var fact in page)
                {
                    counts.TryGetValue(fact.Frame.StreamId, out var count);
                    counts[fact.Frame.StreamId] = count + 1;
                    Assert.InRange(fact.ObjectIndex, 1, 4);
                }
            }
            Assert.Equal(2500, counts[1]);
            Assert.Equal(2500, counts[3]);
            Assert.Empty(Directory.GetDirectories(output)); // live-only means no permanent archive
        }
        finally
        {
            session.ProcessEventHistory?.Dispose();
            session.TraceHistory?.Dispose();
            Directory.Delete(output, recursive: true);
        }
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
