// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec104GiSourceQualifiedAcceptanceTests
{
    private static Iec103MasterSettings Settings(int ca = 105) => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec104,
        CauseOfTransmissionSize = 2,
        CommonAddressSize = 2,
        InformationObjectAddressSize = 3,
        CommonAddress = ca,
        ResponseTimeoutMs = 20,
        Iec104T1AckTimeoutMs = 130,
        Iec104T3TestIntervalMs = 60000,
        Class2PollIntervalMs = 60000,
        SendClockSyncOnConnect = false,
        SendGeneralInterrogationOnConnect = true,
        MaxRetainedEvidenceEvents = 100
    };

    private static byte[] Gi(int cause, int ca = 105, byte qoi = 20,
        bool negative = false, bool test = false, byte originator = 0)
    {
        var bytes = Iec10xAsduBuilder.GeneralInterrogation(Settings(ca), qoi, cause);
        if (negative) bytes[2] |= 0x40;
        if (test) bytes[2] |= 0x80;
        bytes[3] = originator;
        return bytes;
    }

    private static async Task<Iec103MasterRunResult> Run(params byte[][] replies)
    {
        using var transport = new GiPeerTransport(replies);
        var session = new Iec104ClientSession(Settings(), transport);
        var result = await session.RunForAsync(
            TimeSpan.FromMilliseconds(260), CancellationToken.None);
        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.Equal(1, transport.GiTransmissions);
        Assert.Equal(1, result.Counters.GiCommands);
        Assert.DoesNotContain(result.Findings, x => x.Id == "IEC104-SESSION-FAULT");
        return result;
    }

    [Fact]
    public async Task WrongCaQoiAndCommandActtermCannotShortCircuitActualGiResponseWindow()
    {
        var commandActterm = new byte[]
        {
            46, 1, 10, 0, 105, 0, 20, 0, 0, 2
        };
        var result = await Run(
            commandActterm,
            Gi(10, ca: 106),
            Gi(10, qoi: 21),
            Gi(7),
            Gi(10));

        Assert.Equal(5, result.Counters.UserDataResponses);
        Assert.Equal(1, result.Counters.GiEndResponses);
        Assert.Equal(5, result.Events.Count(e =>
            e.Direction == ARIEC60870.Core.Model.FrameDirection.SlaveToMaster &&
            e.ApciFormat == "I" && e.Category == "RX"));
    }

    [Theory]
    [InlineData(0)] // No ACTCON at all
    [InlineData(1)] // Negative ACTCON
    [InlineData(2)] // TEST-bit ACTCON
    [InlineData(3)] // ACTCON with wrong CA
    [InlineData(4)] // ACTCON with wrong originator
    [InlineData(5)] // ACTCON with wrong QOI
    [InlineData(6)] // Correct ACTCON, wrong-CA ACTTERM
    [InlineData(7)] // Correct ACTCON, negative ACTTERM
    [InlineData(8)] // Correct ACTCON, TEST-bit ACTTERM
    public async Task NonCorrelatedApplicationReplyNeverCompletesGi(int scenario)
    {
        byte[][] responses = scenario switch
        {
            0 => [Gi(10)],
            1 => [Gi(7, negative: true), Gi(10)],
            2 => [Gi(7, test: true), Gi(10)],
            3 => [Gi(7, ca: 106), Gi(10)],
            4 => [Gi(7, originator: 1), Gi(10)],
            5 => [Gi(7, qoi: 21), Gi(10)],
            6 => [Gi(7), Gi(10, ca: 106)],
            7 => [Gi(7), Gi(10, negative: true)],
            8 => [Gi(7), Gi(10, test: true)],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var result = await Run(responses);
        Assert.Equal(responses.Length, result.Counters.UserDataResponses);
        Assert.Equal(0, result.Counters.GiEndResponses);
        Assert.Contains(result.Findings, x => x.Id == "IEC104-GI-NO-ACTTERM");
    }

    [Fact]
    public async Task MatchedActivationAndTerminationEndExactlyOneGi()
    {
        var result = await Run(Gi(7), Gi(10));
        Assert.Equal(2, result.Counters.UserDataResponses);
        Assert.Equal(1, result.Counters.GiEndResponses);
        Assert.DoesNotContain(result.Findings, x => x.Id == "IEC104-GI-NO-ACTTERM");
    }

    [Theory]
    [InlineData(60)]
    [InlineData(500)]
    [InlineData(2000)]
    public void Iec104SharedRollingEvidenceRetainsOnlyNewestAndCountsEvictions(int count)
    {
        var history = new Iec10xBoundedEvidenceHistory(100);
        for (var sequence = 1; sequence <= count; sequence++)
            history.Append(new Iec103MasterEvidenceEvent { SequenceNumber = sequence });

        var snapshot = history.Snapshot();
        Assert.Equal(Math.Min(count, 100), snapshot.Length);
        Assert.Equal(Math.Max(0, count - 100), history.Evicted);
        Assert.Equal(Math.Max(1, count - 99), snapshot[0].SequenceNumber);
        Assert.Equal(count, snapshot[^1].SequenceNumber);
    }

    private sealed class GiPeerTransport : IByteTransport
    {
        private readonly Queue<byte> _bytes = new();
        private readonly byte[][] _replies;
        public bool IsOpen { get; private set; }
        public int GiTransmissions { get; private set; }

        public GiPeerTransport(byte[][] replies) => _replies = replies;

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            IsOpen = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            var bytes = buffer.Span;
            if (bytes.Length >= 6 && bytes[2] == 0x07)
                Push(Iec104FrameBuilder.StartDtConfirmation());
            else if (bytes.Length >= 7 && (bytes[2] & 0x01) == 0 &&
                     bytes[6] == 100 && GiTransmissions++ == 0)
            {
                for (var i = 0; i < _replies.Length; i++)
                    Push(Iec104FrameBuilder.I(i, 1, _replies[i]));
            }
            else if (bytes.Length >= 6 && bytes[2] == 0x43)
                Push(Iec104FrameBuilder.TestFrConfirmation());
            return ValueTask.CompletedTask;
        }

        private void Push(byte[] data)
        {
            foreach (var b in data) _bytes.Enqueue(b);
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_bytes.Count == 0)
                return new ValueTask<int>(AwaitDeadline(cancellationToken));
            var count = 0;
            while (count < buffer.Length && _bytes.Count > 0)
                buffer.Span[count++] = _bytes.Dequeue();
            return ValueTask.FromResult(count);
        }

        private static async Task<int> AwaitDeadline(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
    }
}
