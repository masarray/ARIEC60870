// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO.Ports;
using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class PassiveJournalReplayTests
{
    [Fact]
    public async Task ReplaysBothDirectionsFromFullRawBytesAcrossArbitraryReadSplits()
    {
        var folder = CreateTemp();
        try
        {
            var fixedFrame = new byte[] { 0x10, 0x49, 0x69, 0xB2, 0x16 };
            var payload = new byte[] { 0x08, 0x69, 1, 1, 3, 0, 1, 0, 7, 0, 0, 1 };
            var count = (byte)payload.Length;
            var variableFrame = new byte[] { 0x68, count, count, 0x68 }.Concat(payload)
                .Concat(new byte[] { (byte)(payload.Sum(x => x) & 0xFF), 0x16 }).ToArray();
            var scadaRaw = fixedFrame.Concat(new byte[] { 0xE5 }).Concat(variableFrame).ToArray();
            var rtuRaw = new byte[] { 0xE5 }.Concat(fixedFrame).ToArray();
            var source = new PassiveCaptureOptions(folder,
                new PassiveSerialSource("COM201", "SCADA TX"),
                new PassiveSerialSource("COM202", "RTU TX"), ReadChunkBytes: 128,
                ChannelCapacity: 32);
            var session = new PassiveMonitorSession(source,
                new ReplayTap("COM201", scadaRaw, 2),
                new ReplayTap("COM202", rtuRaw, 3));
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            var result = await session.RunAsync(stop.Token);
            Assert.True(PassiveJournalVerifier.Verify(result.Directory).Complete);

            // Delete all secondary frame indexes. Raw replay must remain authoritative.
            File.Delete(Path.Combine(result.Directory, "frames-00.jsonl"));
            File.Delete(Path.Combine(result.Directory, "frames-01.jsonl"));
            var observed = PassiveJournalReplay.ReadVerifiedFrames(result.Directory).ToArray();
            Assert.Equal(5, observed.Length);
            var scada = observed.Where(x => x.StreamId == 0).OrderBy(x => x.StreamOffset).ToArray();
            var rtu = observed.Where(x => x.StreamId == 1).OrderBy(x => x.StreamOffset).ToArray();
            Assert.Equal(new[] { "Fixed", "Single", "Variable" }, scada.Select(x => x.Format));
            Assert.Equal(new[] { "Single", "Fixed" }, rtu.Select(x => x.Format));
            Assert.Equal(new long[] { 0, fixedFrame.Length, fixedFrame.Length + 1 }, scada.Select(x => x.StreamOffset));
            Assert.All(observed, x => Assert.True(x.ChecksumValid));
            Assert.Equal(variableFrame, scada[2].RawBytes);
            Assert.Equal(fixedFrame, rtu[1].RawBytes);
            Assert.All(observed, x => Assert.Equal(DateTimeKind.Utc, x.EstimatedUtc.Kind));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task LiveWriterTransactionJournalCanBeRecomputedFromFullRawOffline()
    {
        var folder = CreateTemp();
        try
        {
            var request = new byte[] { 0x10, 0x49, 0x69, 0xB2, 0x16 };
            var options = new PassiveCaptureOptions(folder,
                new PassiveSerialSource("COM201", "SCADA TX"),
                new PassiveSerialSource("COM202", "RTU TX"), ReadChunkBytes: 128);
            var session = new PassiveMonitorSession(options,
                new ReplayTap("COM201", request, 1),
                new ReplayTap("COM202", Array.Empty<byte>(), 1));
            // Synchronize with the real archive writer, not a 180 ms wall-clock
            // timer that can expire before Windows CI schedules the RX workers.
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var captureTask = session.RunAsync(stop.Token);
            try
            {
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (session.GetProgress().ScadaBytes < request.Length)
                {
                    if (deadline.Elapsed > TimeSpan.FromSeconds(5))
                        throw new TimeoutException("Capture writer did not commit the expected source bytes.");
                    await Task.Delay(15);
                }
            }
            finally { stop.Cancel(); }
            var capture = await captureTask;
            Assert.True(PassiveJournalVerifier.Verify(capture.Directory).Complete);

            var journal = Path.Combine(capture.Directory, "transactions.jsonl");
            Assert.True(File.Exists(journal));
            var saved = File.ReadAllLines(journal)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => System.Text.Json.JsonSerializer.Deserialize<PassiveTransactionEvent>(x)!)
                .ToArray();
            Assert.Single(saved);
            Assert.Equal(PassiveTransactionKind.PendingAtEnd, saved[0].Kind);
            Assert.Equal(0, saved[0].RequestOffset);

            // The derived transaction file is disposable. Re-run from verified raw.
            File.Delete(journal);
            var rebuilt = new List<PassiveTransactionEvent>();
            var corr = new PassiveIec101TransactionCorrelator();
            foreach (var frame in PassiveJournalReplay.ReadVerifiedFrames(capture.Directory))
                corr.Observe(frame, rebuilt.Add);
            corr.AdvanceTime(capture.Manifest.EndUtc, rebuilt.Add);
            corr.Complete(rebuilt.Add);
            Assert.Equal(saved.Select(x => (x.Kind, x.LinkAddress, x.RequestOffset)),
                rebuilt.Select(x => (x.Kind, x.LinkAddress, x.RequestOffset)));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task TwoOctetRawJournalCanBeReDecodedWithDifferentLinkWidthWithoutChangingEvidence()
    {
        var folder = CreateTemp();
        try
        {
            var src0 = new byte[] { 0x10, 0x49, 0x69, 0x01, 0xB3, 0x16 };
            var src1 = new byte[] { 0x10, 0x0B, 0x69, 0x01, 0x75, 0x16 };
            var options = new PassiveCaptureOptions(folder,
                new PassiveSerialSource("COM201", "SCADA TX"),
                new PassiveSerialSource("COM202", "RTU TX"),
                LinkAddressWidth: 2, ChannelCapacity: 32, ReadChunkBytes: 128);
            var session = new PassiveMonitorSession(options,
                new ReplayTap("COM201", src0, 1),
                new ReplayTap("COM202", src1, 2));
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var work = session.RunAsync(stop.Token);
            try
            {
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (session.GetProgress().ScadaBytes < src0.Length ||
                       session.GetProgress().RtuBytes < src1.Length)
                {
                    if (deadline.Elapsed > TimeSpan.FromSeconds(5))
                        throw new TimeoutException("The two serial journals did not commit the expected bytes.");
                    await Task.Delay(15);
                }
            }
            finally { stop.Cancel(); }
            var capture = await work;
            Assert.True(PassiveJournalVerifier.Verify(capture.Directory).Complete);

            var before = File.ReadAllBytes(Path.Combine(capture.Directory, "manifest.json"));
            var original0 = File.ReadAllBytes(Path.Combine(capture.Directory, "stream-00.bin"));
            var original1 = File.ReadAllBytes(Path.Combine(capture.Directory, "stream-01.bin"));
            var saved = PassiveReplayProfile.FromManifest(capture.Manifest);
            Assert.Equal(2, saved.LinkAddressWidth);

            var correct = PassiveJournalReplay.ReadVerifiedFrames(capture.Directory).ToArray();
            Assert.Equal(2, correct.Length);
            Assert.All(correct, f => Assert.Equal("Fixed", f.Format));
            Assert.All(correct, f => Assert.True(f.ChecksumValid));
            Assert.Equal(src0, Assert.Single(correct, f => f.StreamId == 0).RawBytes);
            Assert.Equal(src1, Assert.Single(correct, f => f.StreamId == 1).RawBytes);

            var alternate = saved with { LinkAddressWidth = 1 };
            var wrong = PassiveJournalReplay.ReadVerifiedFrames(capture.Directory, alternate).ToArray();
            Assert.DoesNotContain(wrong, f => f.Format == "Fixed" && f.ChecksumValid);

            var restored = PassiveJournalReplay.ReadVerifiedFrames(capture.Directory, saved).ToArray();
            Assert.Equal(correct.Select(x => (x.StreamId, x.StreamOffset, x.Format, Convert.ToHexString(x.RawBytes))),
                restored.Select(x => (x.StreamId, x.StreamOffset, x.Format, Convert.ToHexString(x.RawBytes))));

            Assert.Equal(before, File.ReadAllBytes(Path.Combine(capture.Directory, "manifest.json")));
            Assert.Equal(original0, File.ReadAllBytes(Path.Combine(capture.Directory, "stream-00.bin")));
            Assert.Equal(original1, File.ReadAllBytes(Path.Combine(capture.Directory, "stream-01.bin")));
            Assert.True(PassiveJournalVerifier.Verify(capture.Directory).Complete);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void ReplayOverrideValidatesAllIec101InteroperabilityWidths()
    {
        var original = new PassiveReplayProfile(2, 2, 2, 3, 900, "Auto");
        original.Validate();
        Assert.Equal(original, original with { });
        Assert.Throws<ArgumentOutOfRangeException>(() => (original with { LinkAddressWidth = 3 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (original with { CotWidth = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (original with { CommonAddressWidth = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (original with { InformationAddressWidth = 4 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (original with { FrameGapMilliseconds = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (original with { LinkMode = "Unknown" }).Validate());
    }

    [Fact]
    public async Task FullGiEventJournalRebuildsExactlyFromVerifiedRawWithoutDerivedIndexes()
    {
        var folder = CreateTemp();
        try
        {
            var asdu = new byte[] { 100, 1, 6, 0, 1, 0, 0, 0, 0, 20 };
            var payload = new byte[] { 0x53, 0x69 }.Concat(asdu).ToArray();
            var request = new byte[] { 0x68, (byte)payload.Length, (byte)payload.Length, 0x68 }
                .Concat(payload)
                .Concat(new byte[] { (byte)(payload.Sum(x => x) & 0xff), 0x16 }).ToArray();
            var options = new PassiveCaptureOptions(folder,
                new PassiveSerialSource("COM201", "SCADA TX"),
                new PassiveSerialSource("COM202", "RTU TX"), ReadChunkBytes: 128);
            var session = new PassiveMonitorSession(options,
                new ReplayTap("COM201", request, 1),
                new ReplayTap("COM202", Array.Empty<byte>(), 1));
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var writer = session.RunAsync(cancel.Token);
            try
            {
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (session.GetProgress().ScadaBytes < request.Length)
                {
                    if (deadline.Elapsed > TimeSpan.FromSeconds(5))
                        throw new TimeoutException("GI fixture bytes not committed by raw writer.");
                    await Task.Delay(15);
                }
            }
            finally { cancel.Cancel(); }
            var captured = await writer;
            Assert.True(PassiveJournalVerifier.Verify(captured.Directory).Complete);
            var derived = Path.Combine(captured.Directory, "gi-events.jsonl");
            var saved = File.ReadAllLines(derived)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => System.Text.Json.JsonSerializer.Deserialize<PassiveGiEvent>(x)!)
                .ToArray();
            Assert.Equal(new[] { PassiveGiKind.Activation, PassiveGiKind.PendingAtCaptureEnd },
                saved.Select(x => x.Kind));

            // Delete ALL secondary indexes; raw + manifest remain the only source of truth.
            File.Delete(derived);
            File.Delete(Path.Combine(captured.Directory, "frames-00.jsonl"));
            File.Delete(Path.Combine(captured.Directory, "frames-01.jsonl"));
            var tracker = new PassiveIec101GiTracker();
            var rebuilt = new List<PassiveGiEvent>();
            foreach (var frame in PassiveJournalReplay.ReadVerifiedFrames(captured.Directory))
                tracker.Observe(frame, rebuilt.Add);
            tracker.AdvanceTime(captured.Manifest.EndUtc, rebuilt.Add);
            tracker.Complete(rebuilt.Add);
            Assert.Equal(saved.Select(x => (x.Kind, x.StreamId, x.StreamOffset, x.RequestOffset, x.Qualifier)),
                rebuilt.Select(x => (x.Kind, x.StreamId, x.StreamOffset, x.RequestOffset, x.Qualifier)));
            Assert.True(PassiveJournalVerifier.Verify(captured.Directory).Complete);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task RawCorruptionIsRejectedBeforeAnyDecodedFrameIsShown()
    {
        var folder = CreateTemp();
        try
        {
            var result = await RecordAsync(folder);
            var path = Path.Combine(result.Directory, "stream-00.bin");
            using (var handle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                handle.Position = PassiveJournalFormat.HeaderSize;
                var first = handle.ReadByte();
                handle.Position--;
                handle.WriteByte((byte)(first ^ 0x33));
            }
            Assert.Throws<InvalidDataException>(() =>
                PassiveJournalReplay.ReadVerifiedFrames(result.Directory).ToArray());
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task CancelledReplayCannotProjectPartialOrUnverifiedEvidence()
    {
        var folder = CreateTemp();
        try
        {
            var result = await RecordAsync(folder);
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                PassiveJournalReplay.ReadVerifiedFrames(result.Directory, stop.Token).ToArray());
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static async Task<(string Directory, PassiveCaptureManifest Manifest)> RecordAsync(string folder)
    {
        var options = new PassiveCaptureOptions(folder,
            new PassiveSerialSource("COM201", "SCADA TX"),
            new PassiveSerialSource("COM202", "RTU TX"), ReadChunkBytes: 128);
        var session = new PassiveMonitorSession(options,
            new ReplayTap("COM201", new byte[] { 0xE5, 0x10, 0x49, 0x69, 0xB2, 0x16 }, 2),
            new ReplayTap("COM202", new byte[] { 0xE5 }, 1));
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(160));
        return await session.RunAsync(stop.Token);
    }

    private static string CreateTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ariec-replay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed class ReplayTap : IReadOnlySerialTap
    {
        private readonly byte[] _bytes;
        private readonly int _chunk;
        private int _offset;
        private bool _open;

        public ReplayTap(string port, byte[] bytes, int chunk)
        {
            PortName = port;
            _bytes = bytes;
            _chunk = chunk;
        }
        public string PortName { get; }
        public long DriverErrorCount => 0;
        public void Open() => _open = true;
        public int Read(byte[] buffer, int offset, int count)
        {
            if (!_open) throw new IOException("Not opened.");
            if (_offset == _bytes.Length) { Thread.Sleep(5); return 0; }
            var n = Math.Min(Math.Min(_bytes.Length - _offset, _chunk), count);
            Array.Copy(_bytes, _offset, buffer, offset, n);
            _offset += n;
            return n;
        }
        public void Close() => _open = false;
        public void Dispose() => Close();
    }
}
