// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
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
    public async Task TwoDirectionsAreArchivedByteExactAndShaVerified()
    {
        var folder = CreateTemp();
        var a = Enumerable.Range(0, 20000).Select(i => (byte)(i * 29 + 7)).ToArray();
        var b = Enumerable.Range(0, 18000).Select(i => (byte)(i * 13 + 99)).ToArray();
        try
        {
            var tapA = new ReplayTap("COM7", a, 71);
            var tapB = new ReplayTap("COM8", b, 43);
            var opts = Options(folder);
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
