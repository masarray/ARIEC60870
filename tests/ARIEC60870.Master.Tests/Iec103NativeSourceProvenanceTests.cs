// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>Actual IEC-103 Master RX path, not direct model construction.</summary>
public sealed class Iec103NativeSourceProvenanceTests
{
    [Fact]
    public async Task ValidSpontaneousTypeOnePreservesTheSameSourceFrameInResultValuesAndEvents()
    {
        // IEC-103 Type 1, COT 1 spontaneous, FUN=C0, INF=24, DPI=2,
        // CP32Time2a=14:34:12.345; a real FT1.2 secondary response.
        var asdu = new byte[]
            { 0x01, 0x01, 0x01, 0x01, 0xC0, 0x24, 0x02, 0x39, 0x30, 0x22, 0x0E, 0x00 };
        var result = await ReceiveOneIec103AsduAsync(asdu);
        var original = Assert.Single(result.Events.Where(e =>
            e.Direction == FrameDirection.SlaveToMaster && e.Frame?.Asdu is not null));
        Assert.True(original.IsRelayValue);
        Assert.True(original.IsRelayEdgeEvent);
        var value = Assert.Single(result.ValuePoints);
        var eventFact = Assert.Single(result.EventLog);

        Assert.Equal(original.SequenceNumber, eventFact.EvidenceSequenceNumber);
        Assert.Equal(original.EvidenceReference, value.EvidenceReference);
        Assert.Equal(original.EvidenceReference, eventFact.EvidenceReference);
        Assert.Equal(original.RawHex, value.RawHex);
        Assert.Equal(original.RawHex, eventFact.RawHex);
        Assert.True(Iec10xObservationIdentity.SamePhysicalFrame(original,
            eventFact.EvidenceReference, eventFact.PhysicalSource,
            Iec60870ProtocolMode.Iec103, eventFact.EvidenceSequenceNumber));
    }

    [Fact]
    public async Task TruncatedTypeOneIsRawEvidenceNotAProcessValueOrEvent()
    {
        // Complete IEC-103 header with FUN/INF, but missing the mandatory
        // Type 1 DPI and CP32Time2a. A valid link checksum cannot fix it.
        var result = await ReceiveOneIec103AsduAsync(
            [0x01, 0x01, 0x01, 0x01, 0xC0, 0x24]);
        var received = Assert.Single(result.Events.Where(e =>
            e.Direction == FrameDirection.SlaveToMaster && e.Frame?.Asdu is not null));
        Assert.True(received.Frame!.IsChecksumValid);
        Assert.NotEqual(DecodeStatus.Ok, received.Frame.Asdu!.Status);
        Assert.False(received.IsRelayValue);
        Assert.False(received.IsRelayEdgeEvent);
        Assert.Empty(result.ValuePoints);
        Assert.Empty(result.EventLog);
        Assert.NotEmpty(received.RawHex);
    }

    private static async Task<Iec103MasterRunResult> ReceiveOneIec103AsduAsync(byte[] asdu)
    {
        using var transport = new SingleResponseTransport(SecondaryVariable(asdu));
        var settings = new Iec103MasterSettings
        {
            LinkAddress = 1,
            StartupDelayMs = 0,
            ResetRemoteLinkOnConnect = false,
            ResetFcbOnConnect = false,
            SendClockSyncOnConnect = false,
            SendGeneralInterrogationOnConnect = false,
            RequestClass2ImmediatelyAfterStartup = true,
            ResponseTimeoutMs = 350,
            Class2PollIntervalMs = 1000
        };
        var master = new Iec103MasterSession(settings, transport);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        master.EvidenceReceived += (_, item) =>
        {
            if (item.Direction == FrameDirection.SlaveToMaster &&
                item.Frame?.Asdu is not null)
                stop.Cancel();
        };
        return await master.RunAsync(stop.Token);
    }

    private static byte[] SecondaryVariable(byte[] asdu)
    {
        var length = checked((byte)(2 + asdu.Length));
        var bytes = new byte[length + 6];
        bytes[0] = 0x68; bytes[1] = length; bytes[2] = length; bytes[3] = 0x68;
        bytes[4] = 0x08; // IEC-103 secondary PRM=0, user data FC8
        bytes[5] = 0x01; // link address 1
        asdu.CopyTo(bytes, 6);
        var checksum = 0;
        for (var i = 4; i < 4 + length; i++) checksum += bytes[i];
        bytes[^2] = (byte)checksum;
        bytes[^1] = 0x16;
        return bytes;
    }

    private sealed class SingleResponseTransport(byte[] response) : IByteTransport
    {
        private readonly Queue<byte> _pending = new();
        private bool _open;
        private bool _served;
        public bool IsOpen => _open;
        public ValueTask OpenAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _open = true;
            return ValueTask.CompletedTask;
        }
        public ValueTask CloseAsync(CancellationToken ct)
        {
            _open = false;
            return ValueTask.CompletedTask;
        }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!_open) throw new InvalidOperationException("Transport not open.");
            if (!_served)
            {
                _served = true;
                foreach (var b in response) _pending.Enqueue(b);
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, _pending.Count);
            for (var i = 0; i < count; i++) buffer.Span[i] = _pending.Dequeue();
            return ValueTask.FromResult(count);
        }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
