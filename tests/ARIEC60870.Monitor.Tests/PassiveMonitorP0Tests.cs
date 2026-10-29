// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.IO.Ports;
using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class PassiveMonitorP0Tests
{
    [Fact]
    public void ReadOnlyTapInterfaceExposesNoTransmitterMethods()
    {
        var methods = typeof(IReadOnlySerialTap).GetMethods().Select(x => x.Name).ToArray();
        Assert.Contains(nameof(IReadOnlySerialTap.Read), methods);
        Assert.DoesNotContain(methods, x => x.Contains("Write", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("Send", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("Break", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("Rts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetupFailsClosedOnDuplicatePortsAndMismatchedFraming()
    {
        var a = new PassiveSerialSource("COM7", "SCADA TX");
        Assert.Throws<ArgumentException>(() => new PassiveCaptureOptions("capture", a,
            new PassiveSerialSource("com7", "RTU TX")).Validate());
        Assert.Throws<ArgumentException>(() => new PassiveCaptureOptions("capture", a,
            new PassiveSerialSource("COM8", "RTU TX", BaudRate: 1200)).Validate());
    }

    [Fact]
    public void FieldUartAndDecodeProfilesAreIndependentAndValidated()
    {
        var uartA = new PassiveSerialSource("COM10", "SCADA TX", 1200, Parity.Even, 8, StopBits.Two);
        var uartB = new PassiveSerialSource("COM11", "RTU TX", 1200, Parity.Even, 8, StopBits.Two);
        var opts = new PassiveCaptureOptions("capture", uartA, uartB,
            LinkAddressWidth: 2, CotWidth: 1, CommonAddressWidth: 1,
            InformationAddressWidth: 2, FrameGapMilliseconds: 1500, LinkMode: "Balanced");
        opts.Validate();
        Assert.Equal(StopBits.Two, opts.RtuTx.StopBits);
        Assert.Equal(1500, opts.FrameGapMilliseconds);
        Assert.Equal("Balanced", opts.LinkMode);
        Assert.Throws<ArgumentOutOfRangeException>(() => (opts with { FrameGapMilliseconds = 99 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (opts with { FrameGapMilliseconds = 10001 }).Validate());
        Assert.Throws<ArgumentException>(() => (opts with { LinkMode = "Unknown" }).Validate());
        Assert.Throws<ArgumentException>(() => (opts with
        {
            ScadaTx = uartA with { DataBits = 7 },
            RtuTx = uartB with { DataBits = 7 }
        }).Validate());
        Assert.Throws<ArgumentException>(() => (opts with
        {
            RtuTx = uartB with { StopBits = StopBits.One }
        }).Validate());
    }

    [Fact]
    public async Task LiveMonitoringReceivesAndDecodesWithoutCreatingAnyCaptureDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ariec-live-monitor-" + Guid.NewGuid().ToString("N"));
        var tx = new byte[] { 0x10, 0x4B, 0x69, 0xB4, 0x16 };
        var rx = new byte[] { 0x10, 0x29, 0x69, 0x92, 0x16 };
        Assert.False(Directory.Exists(root));
        var opts = Options(root) with { RecordFullSession = false };
        opts.Validate();
        var tapA = new ReplayTap("COM7", tx, tx.Length);
        var tapB = new ReplayTap("COM8", rx, rx.Length);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(180));
        var session = new PassiveMonitorSession(opts, tapA, tapB);
        var result = await session.RunAsync(stop.Token);

        Assert.False(Directory.Exists(root));
        Assert.Equal(string.Empty, result.Directory);
        Assert.Equal("MonitorStopped", result.Manifest.Status);
        Assert.Equal("MonitorStopped", session.GetProgress().Status);
        Assert.Equal(tx.Length, session.GetProgress().ScadaBytes);
        Assert.Equal(rx.Length, session.GetProgress().RtuBytes);
        Assert.True(tapA.WasClosed && tapB.WasClosed);
        Assert.Equal(0, tapA.Writes + tapB.Writes);
        var received = new List<PassiveObservedFrame>();
        while (session.TryReadFrame(out var frame)) received.Add(frame);
        Assert.Contains(received, frame => frame.StreamId == 0 && frame.RawBytes.SequenceEqual(tx));
        Assert.Contains(received, frame => frame.StreamId == 1 && frame.RawBytes.SequenceEqual(rx));
        Assert.All(result.Manifest.Streams, stream =>
        {
            Assert.Equal(string.Empty, stream.File);
            Assert.Equal(string.Empty, stream.RawSha256);
        });
    }

    [Fact]
    public void LiveModeDoesNotRequireOrValidateAnArchiveFolder()
    {
        var opts = Options(string.Empty) with { RecordFullSession = false };
        opts.Validate();
        Assert.Throws<ArgumentException>(() => (opts with { RecordFullSession = true }).Validate());
    }

    [Fact]
    public async Task TwoDirectionsAreArchivedByteExactAndShaVerified()
    {
        var folder = CreateTemp();
        var a = Enumerable.Range(0, 20000).Select(i => (byte)(i * 29 + 7)).ToArray();
        var b = Enumerable.Range(0, 18000).Select(i => (byte)(i * 13 + 99)).ToArray();
        try
        {
            var tapA = new ReplayTap("COM7", a, 71);
            var tapB = new ReplayTap("COM8", b, 43);
            // This verifies byte-for-byte archival, not artificial writer-queue
            // saturation: all 701 one-chunk records fit the bounded fixture FIFO.
            // Separate backpressure tests still assert IntegrityUncertain.
            var opts = Options(folder) with { ChannelCapacity = 2048 };
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
            var session = new PassiveMonitorSession(opts, tapA, tapB);
            var result = await session.RunAsync(stop.Token);
            Assert.Equal("NoApplicationLossDetected", result.Manifest.Status);
            Assert.Equal(new long[] { a.Length, b.Length }, result.Manifest.Streams.Select(x => x.Bytes));
            Assert.Equal(9600, result.Manifest.Streams[0].BaudRate);
            Assert.Equal("Even", result.Manifest.Streams[0].Parity);
            Assert.Equal(8, result.Manifest.Streams[1].DataBits);
            Assert.Equal("One", result.Manifest.Streams[1].StopBits);
            Assert.All(result.Manifest.Streams, x => Assert.Equal(64, x.RawSha256.Length));
            Assert.Equal(0, tapA.Writes);
            Assert.Equal(0, tapB.Writes);
            var audit = PassiveJournalVerifier.Verify(result.Directory);
            Assert.True(audit.Complete, string.Join("; ", audit.Problems));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(a)), audit.Streams[0].Sha256);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(b)), audit.Streams[1].Sha256);
            Assert.True(audit.Streams.All(x => x.Records > 2));
            // UI-only preview is bounded and may skip chunks; the disk stream must stay exact.
            Assert.Equal("NoApplicationLossDetected", session.GetProgress().Status);
            Assert.Equal(a.Length, session.GetProgress().ScadaBytes);
            Assert.Equal(b.Length, session.GetProgress().RtuBytes);
            var previewCount = 0;
            while (session.TryReadPreview(out var preview))
            {
                Assert.InRange(preview.StreamId, (byte)0, (byte)1);
                Assert.InRange(preview.FirstBytes.Length, 1, 48);
                Assert.True(preview.ByteCount >= preview.FirstBytes.Length);
                previewCount++;
            }
            Assert.InRange(previewCount, 1, 256);

        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task CorruptOrTruncatedJournalIsNotMistakenForCompleteCapture()
    {
        var folder = CreateTemp();
        try
        {
            var session = new PassiveMonitorSession(Options(folder),
                new ReplayTap("COM7", Enumerable.Repeat((byte)0xA5, 200).ToArray(), 20),
                new ReplayTap("COM8", new byte[] { 0x10, 0x49, 0x69, 0x00, 0xB2, 0x16 }, 6));
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(140));
            var result = await session.RunAsync(stop.Token);
            var file = Path.Combine(result.Directory, "stream-00.bin");
            using (var f = new FileStream(file, FileMode.Open, FileAccess.ReadWrite))
            {
                f.Position = PassiveJournalFormat.HeaderSize;
                var old = f.ReadByte();
                f.Position--;
                f.WriteByte((byte)(old ^ 0xFF));
            }
            var damaged = PassiveJournalVerifier.Verify(result.Directory);
            Assert.False(damaged.Complete);
            Assert.Contains(damaged.Problems, s => s.Contains("CRC32", StringComparison.OrdinalIgnoreCase));
            using (var f = new FileStream(file, FileMode.Open, FileAccess.Write))
                f.SetLength(42);
            Assert.False(PassiveJournalVerifier.Verify(result.Directory).Complete);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task FailureToOpenSecondPortClosesFirstAndMarksSessionIncomplete()
    {
        var folder = CreateTemp();
        var tapA = new ReplayTap("COM7", new byte[] { 0xE5 }, 1);
        var tapB = new ReplayTap("COM8", Array.Empty<byte>(), 1, failOpen: true);
        try
        {
            var session = new PassiveMonitorSession(Options(folder), tapA, tapB);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var result = await session.RunAsync(stop.Token);
            Assert.Equal("Incomplete", result.Manifest.Status);
            Assert.True(tapA.WasClosed);
            Assert.False(PassiveJournalVerifier.Verify(result.Directory).Complete);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task DriverErrorsDegradeIntegrityEvenWhenRawJournalsValidate()
    {
        var folder = CreateTemp();
        try
        {
            var tapA = new ReplayTap("COM7", new byte[] { 0xE5, 0x10 }, 1, errors: 1);
            var tapB = new ReplayTap("COM8", new byte[] { 0x68 }, 1);
            var session = new PassiveMonitorSession(Options(folder), tapA, tapB);
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(130));
            var result = await session.RunAsync(stop.Token);
            Assert.Equal("IntegrityUncertain", result.Manifest.Status);
            Assert.False(PassiveJournalVerifier.Verify(result.Directory).Complete);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void JournalFormatRejectsMismatchedSequenceSourceAndCrc()
    {
        var buf = new byte[PassiveJournalFormat.HeaderSize + 6];
        new byte[] { 0x10, 0x49, 0x69, 0, 0xB2, 0x16 }.CopyTo(buf, 40);
        PassiveJournalFormat.Encode(buf, 0, 1, 123, 0, 6);
        Assert.True(PassiveJournalFormat.TryDecode(buf.AsSpan(0, 40), buf.AsSpan(40),
            0, 1, 0, out _));
        Assert.False(PassiveJournalFormat.TryDecode(buf.AsSpan(0, 40), buf.AsSpan(40),
            1, 1, 0, out _));
        Assert.False(PassiveJournalFormat.TryDecode(buf.AsSpan(0, 40), buf.AsSpan(40),
            0, 2, 0, out _));
        buf[42] ^= 0xFF;
        Assert.False(PassiveJournalFormat.TryDecode(buf.AsSpan(0, 40), buf.AsSpan(40),
            0, 1, 0, out _));
    }

    [Fact]
    public async Task FullJournalRetainsEveryControlEventEvenWhenGuiPreviewDropsOlderFrames()
    {
        var folder = CreateTemp();
        const int count = 620; // more than the 512-frame GUI preview ring
        var master = Enumerable.Range(0, count)
            .SelectMany(i => PassiveIec101ControlIndexerTests.Frame(0, i * 18, 6, 0x82).RawBytes)
            .ToArray();
        try
        {
            var opts = Options(folder) with { ChannelCapacity = 1024, ReadChunkBytes = 256 };
            var session = new PassiveMonitorSession(opts,
                new ReplayTap("COM7", master, 256),
                new ReplayTap("COM8", new byte[] { 0xE5 }, 1));
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
            var result = await session.RunAsync(stop.Token);
            Assert.Equal("NoApplicationLossDetected", result.Manifest.Status);
            Assert.True(PassiveJournalVerifier.Verify(result.Directory).Complete);

            var indexPath = Path.Combine(result.Directory, "controls.jsonl");
            Assert.True(File.Exists(indexPath));
            var stored = File.ReadLines(indexPath).Count(line => line.Contains(
                "\"Explanation\"", StringComparison.Ordinal));
            var physical = File.ReadLines(Path.Combine(result.Directory, "frames-00.jsonl")).Count();
            var scanned = 0;
            var scanner = new PassiveFt12Scanner();
            scanner.Feed(master, 0, frame =>
            {
                if (frame.Format == "Variable" && frame.ChecksumValid) scanned++;
            });
            Assert.True(stored == count,
                $"Expected {count} control stages; index={stored}, physical={physical}, " +
                $"scanner={scanned}, scadaBytes={session.GetProgress().ScadaBytes}, " +
                $"status={session.GetProgress().Status}.");

            var shown = 0;
            while (session.TryReadFrame(out _)) shown++;
            Assert.True(shown <= 512);
            Assert.True(shown < stored); // durability is independent of WPF ring
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static PassiveCaptureOptions Options(string folder) => new(folder,
        new PassiveSerialSource("COM7", "SCADA TX -> RTU"),
        new PassiveSerialSource("COM8", "RTU TX -> SCADA"), ChannelCapacity: 32,
        ReadChunkBytes: 256);

    private static string CreateTemp()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ariec-p0-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private sealed class ReplayTap : IReadOnlySerialTap
    {
        private readonly byte[] _data;
        private readonly int _chunk;
        private readonly bool _failOpen;
        private readonly long _errors;
        private int _position;
        private volatile bool _opened;
        public ReplayTap(string portName, byte[] data, int chunk, bool failOpen = false, long errors = 0)
        { PortName = portName; _data = data; _chunk = chunk; _failOpen = failOpen; _errors = errors; }
        public string PortName { get; }
        public long DriverErrorCount => _errors;
        public int Writes => 0; // no Write method in the read-only interface
        public bool WasClosed { get; private set; }
        public void Open()
        {
            if (_failOpen) throw new IOException("Simulated COM open failure");
            _opened = true;
        }
        public int Read(byte[] buffer, int offset, int count)
        {
            if (!_opened) throw new IOException("Port closed");
            var remaining = _data.Length - _position;
            if (remaining == 0) { Thread.Sleep(5); return 0; }
            var n = Math.Min(remaining, Math.Min(count, _chunk));
            Array.Copy(_data, _position, buffer, offset, n);
            _position += n;
            return n;
        }
        public void Close() { _opened = false; WasClosed = true; }
        public void Dispose() => Close();
    }
}
