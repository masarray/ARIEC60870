// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>
/// IEC101-R14 / P02 SOFTWARE-ONLY burst accountability at the full Master
/// receive/decode/evidence path. Fake I/O is NOT UART paced and cannot
/// establish actual RTU FIFO capacity, wire throughput or physical FAT.
/// </summary>
public sealed class Iec101SoeBurstAccountabilityTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(500)]
    [InlineData(2000)]
    public async Task EveryReceivedSpontaneousAsduIsRetainedWithNoHiddenDrop(int burstSize)
    {
        var settings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PortName = "SYNTHETIC-IEC101",
            UseSimulatedSlave = true,
            BaudRate = 1200, // Framing settings only; fake peer is deliberately NOT wire paced.
            LinkAddress = 105,
            LinkAddressSize = 1,
            CommonAddress = 105,
            CauseOfTransmissionSize = 2,
            CommonAddressSize = 2,
            InformationObjectAddressSize = 3,
            ResponseTimeoutMs = 220,
            StartupDelayMs = 0,
            ResetRemoteLinkOnConnect = false,
            ResetFcbOnConnect = false,
            SendGeneralInterrogationOnConnect = false,
            SendClockSyncOnConnect = false,
            RequestClass2ImmediatelyAfterStartup = false,
            Class1DrainDelayMs = 0,
            MaxClass1DrainFrames = 64,
            MaxConsecutiveClass1BeforeClass2 = 0, // Isolate SOE FIFO drain from S02 fairness.
            Class2PollIntervalMs = 100,
            MaxRetainedEvidenceEvents = 10000,
            MaxRetainedRelayEvents = 5000
        };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var peer = new ScriptedSoePeer(settings, burstSize, stop);
        var result = await new Iec101MasterSession(settings, peer).RunAsync(stop.Token);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.True(peer.FinalClass2Observed,
            "The fake peer must be drained and a Class 2 request observed before stopping.");
        Assert.Equal(burstSize, peer.SentSpontaneousAsdus);
        Assert.Equal(burstSize, result.Counters.UserDataResponses);
        Assert.Equal(burstSize, result.Counters.DpiEvents);
        Assert.Equal(burstSize, result.Counters.Class1Requests);
        Assert.Equal(0, result.Counters.RejectedFrames);
        Assert.Equal(0, result.Counters.ChecksumErrors);
        Assert.Equal(0, result.Counters.EvidenceEventsDroppedFromMemory);
        Assert.Equal(burstSize, result.Events.Count(item =>
            item.Direction == FrameDirection.SlaveToMaster &&
            item.TypeId == 1 && item.CauseOfTransmission == 3 &&
            item.IsRelayEdgeEvent));
    }

    private sealed class ScriptedSoePeer : IByteTransport
    {
        private readonly Iec103MasterSettings _settings;
        private readonly Ft12Parser _parser;
        private readonly Queue<byte> _outgoing = new();
        private readonly CancellationTokenSource _stop;
        private readonly int _target;
        private int _remaining;

        public ScriptedSoePeer(Iec103MasterSettings settings, int count, CancellationTokenSource stop)
        {
            _settings = settings;
            _parser = Ft12Parser.ForIec101(settings.LinkAddressSize);
            _remaining = count;
            _target = count;
            _stop = stop;
        }

        public bool IsOpen { get; private set; }
        public int SentSpontaneousAsdus { get; private set; }
        public bool FinalClass2Observed { get; private set; }

        public ValueTask OpenAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            IsOpen = true;
            return ValueTask.CompletedTask;
        }
        public ValueTask CloseAsync(CancellationToken ct)
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsOpen) throw new InvalidOperationException("Synthetic COM closed.");
            var frame = _parser.Decode(buffer.ToArray());
            var fc = frame.LinkControl?.FunctionCode ?? -1;
            byte[] response;
            switch (fc)
            {
                case 9:
                    response = Ft12FrameBuilder.Fixed(0x2B, _settings.LinkAddress,
                        _settings.LinkAddressSize);
                    break;
                case 10 when _remaining > 0:
                    var ordinal = _target - _remaining;
                    _remaining--;
                    SentSpontaneousAsdus++;
                    response = Ft12FrameBuilder.Variable((byte)(_remaining > 0 ? 0x28 : 0x08),
                        _settings.LinkAddress,
                        Iec10xAsduBuilder.SinglePoint(_settings, ioa: 201 + (ordinal % 8),
                            value: (ordinal & 1) == 0, cause: 3),
                        _settings.LinkAddressSize);
                    break;
                case 11 when _remaining == 0:
                    // All 60/500/2000 received frames were already acknowledged
                    // by the Master; cancel only after it naturally reaches Class 2.
                    FinalClass2Observed = true;
                    _stop.Cancel();
                    return ValueTask.CompletedTask;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected IEC-101 FC={fc}; SOE remaining={_remaining}.");
            }
            foreach (var octet in response) _outgoing.Enqueue(octet);
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_outgoing.Count == 0) return ValueTask.FromResult(0);
            buffer.Span[0] = _outgoing.Dequeue();
            return ValueTask.FromResult(1);
        }
        public void Dispose() { IsOpen = false; _outgoing.Clear(); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
