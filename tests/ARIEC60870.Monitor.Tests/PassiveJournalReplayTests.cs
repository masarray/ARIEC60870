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
