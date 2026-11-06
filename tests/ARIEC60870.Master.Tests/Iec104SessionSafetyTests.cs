// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec104SessionSafetyTests
{
    [Fact]
    public void ModularSequenceWrapCannotTurnOldOrFutureAckIntoValidAcknowledgement()
    {
        Assert.Equal(0, Iec104SequenceWindow.Next(32767));
        Assert.Equal(2, Iec104SequenceWindow.Distance(32767, 1));
        Assert.True(Iec104SequenceWindow.IsAcknowledgementWithinWindow(32767, 1, 0));
        Assert.True(Iec104SequenceWindow.IsAcknowledgementWithinWindow(32767, 1, 1));
        Assert.False(Iec104SequenceWindow.IsAcknowledgementWithinWindow(32767, 1, 2));
        Assert.False(Iec104SequenceWindow.IsAcknowledgementWithinWindow(32767, 1, 32766));
        Assert.False(Iec104SequenceWindow.IsAcknowledgementWithinWindow(0, 12, 32767));
        Assert.False(Iec104SequenceWindow.IsAcknowledgementWithinWindow(-1, 0, 0));
    }

    [Fact]
    public async Task WrongStartDtResponseBlocksAllAsduAndReportsFault()
    {
        using var transport = new DeterministicWrongStartDtTransport();
        var settings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            CauseOfTransmissionSize = 2,
            CommonAddressSize = 2,
            InformationObjectAddressSize = 3,
            ResponseTimeoutMs = 10,
            Iec104T1AckTimeoutMs = 40,
            SendClockSyncOnConnect = true,
            SendGeneralInterrogationOnConnect = true
        };
        var session = new Iec104ClientSession(settings, transport);
        var result = await session.RunForAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.False(result.CompletedNormally);
        Assert.Contains("STARTDT", result.CompletionReason);
        Assert.Single(transport.Sent);
        Assert.Equal(Iec104FrameBuilder.StartDtActivation(), transport.Sent[0]);
        Assert.Contains(result.Findings, f => f.Id == "IEC104-STARTDT-CON");
    }


    [Fact]
    public async Task StartDtConfirmedAfterOrdinaryReplyTimeoutButBeforeT1IsAccepted()
    {
        using var transport = new DelayedStartDtTransport();
        var settings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            CauseOfTransmissionSize = 2,
            CommonAddressSize = 2,
            InformationObjectAddressSize = 3,
            ResponseTimeoutMs = 10,
            Iec104T1AckTimeoutMs = 200,
            SendClockSyncOnConnect = false,
            SendGeneralInterrogationOnConnect = false
        };
        var result = await new Iec104ClientSession(settings, transport)
            .RunForAsync(TimeSpan.FromMilliseconds(170), CancellationToken.None);
        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.DoesNotContain(result.Findings, f => f.Id == "IEC104-STARTDT-CON");
        Assert.Equal(Iec104FrameBuilder.StartDtActivation(), transport.Sent[0]);
        Assert.DoesNotContain(transport.Sent, f => new Iec104ApduParser().Decode(f).Format == "I");
    }


    [Fact]
    public async Task OutOfWindowAckCannotPromoteMultiObjectObservationsButValidRetransmissionCan()
    {
        var settings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            CauseOfTransmissionSize = 2,
            CommonAddressSize = 2,
            InformationObjectAddressSize = 3,
            CommonAddress = 1,
            ResponseTimeoutMs = 20,
            Iec104T1AckTimeoutMs = 150,
            Class2PollIntervalMs = 1,
            SendClockSyncOnConnect = false,
            SendGeneralInterrogationOnConnect = false
        };
        var asdu = Iec10xAsduBuilder.SinglePoint(settings, 101, true, cause: 3);
        asdu[1] = 2; // SQ0: two independently addressable process objects
        asdu = asdu.Concat(new byte[] { 102, 0, 0, 0 }).ToArray();

        // No I-format was sent, so N(R)=1 acknowledges a nonexistent TX.
        // The following valid N(S)=0 retransmission must remain acceptable.
        using var transport = new InvalidAckThenValidMultiObjectTransport(
            Iec104FrameBuilder.S(1),
            Iec104FrameBuilder.I(0, 1, asdu),
            Iec104FrameBuilder.I(0, 0, asdu));
        var session = new Iec104ClientSession(settings, transport);
        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(260), CancellationToken.None);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.Contains(result.Findings, f => f.Id == "IEC104-NR-ACK-INVALID");
        Assert.Equal(2, result.Counters.RejectedFrames); // invalid S and I N(R)
        Assert.Equal(1, result.Counters.AckResponses); // STARTDT con only
        Assert.Equal(1, result.Counters.UserDataResponses); // valid I only

        var rejected = Assert.Single(result.Events.Where(e =>
            e.Direction == ARIEC60870.Core.Model.FrameDirection.SlaveToMaster &&
            e.TypeId == 1 && e.Category == "RX Warning"));
        Assert.False(rejected.IsRelayValue);
        Assert.False(rejected.IsRelayEdgeEvent);
        Assert.False(Iec10xObservationIdentity.HasTrustedDecodedPayload(rejected));
        Assert.DoesNotContain(result.Events, e =>
            e.PhysicalFrameSequenceNumber == rejected.SequenceNumber);

        var accepted = result.Events.Where(e => e.TypeId == 1 && e.IsRelayValue).ToArray();
        Assert.Equal(2, accepted.Length);
        Assert.Equal(new int?[] { 101, 102 },
            accepted.Select(e => e.InformationObjectAddress).ToArray());
        Assert.All(accepted, e => Assert.True(
            Iec10xObservationIdentity.HasTrustedDecodedPayload(e)));
        Assert.Equal(accepted[0].EvidenceReference.Split("|object:")[0],
            accepted[1].EvidenceReference.Split("|object:")[0]);
        Assert.Equal(3, transport.SFrameWrites); // invalid I, valid I and no other I
    }

    private sealed class InvalidAckThenValidMultiObjectTransport : IByteTransport
    {
        private readonly Queue<byte> _rx = new();
        private readonly byte[][] _afterStart;
        public int SFrameWrites { get; private set; }
        public bool IsOpen { get; private set; }

        public InvalidAckThenValidMultiObjectTransport(params byte[][] afterStart)
            => _afterStart = afterStart;

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
            if (buffer.Span.Length >= 6 && buffer.Span[2] == 0x07)
            {
                Push(Iec104FrameBuilder.StartDtConfirmation());
                foreach (var response in _afterStart) Push(response);
            }
            else if (buffer.Span.Length >= 6 && buffer.Span[2] == 0x01)
                SFrameWrites++;
            return ValueTask.CompletedTask;
        }

        private void Push(byte[] bytes)
        {
            foreach (var b in bytes) _rx.Enqueue(b);
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_rx.Count == 0)
                return new ValueTask<int>(WaitForDeadlineAsync(cancellationToken));
            var count = 0;
            while (count < buffer.Length && _rx.Count > 0)
                buffer.Span[count++] = _rx.Dequeue();
            return ValueTask.FromResult(count);
        }

        private static async Task<int> WaitForDeadlineAsync(CancellationToken cancellationToken)
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

    private sealed class DelayedStartDtTransport : IByteTransport
    {
        private readonly Queue<byte> _reply = new();
        private bool _waitOnce = true;
        public List<byte[]> Sent { get; } = new();
        public bool IsOpen { get; private set; }
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
            Sent.Add(buffer.ToArray());
            var reply = buffer.Span[2] == 0x43
                ? Iec104FrameBuilder.TestFrConfirmation()
                : Iec104FrameBuilder.StartDtConfirmation();
            foreach (var octet in reply) _reply.Enqueue(octet);
            return ValueTask.CompletedTask;
        }
        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_waitOnce)
            {
                _waitOnce = false;
                await Task.Delay(65, cancellationToken);
            }
            if (_reply.Count == 0)
            {
                await Task.Delay(10, cancellationToken);
                return 0;
            }
            buffer.Span[0] = _reply.Dequeue();
            return 1;
        }
        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync() { IsOpen = false; return ValueTask.CompletedTask; }
    }

    private sealed class DeterministicWrongStartDtTransport : IByteTransport
    {
        private readonly Queue<byte> _reply = new();
        public List<byte[]> Sent { get; } = new();
        public bool IsOpen { get; private set; }
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
            Sent.Add(buffer.ToArray());
            // The peer sends TESTFR con instead of STARTDT con.
            foreach (var octet in Iec104FrameBuilder.TestFrConfirmation()) _reply.Enqueue(octet);
            return ValueTask.CompletedTask;
        }
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_reply.Count == 0) return ValueTask.FromResult(0);
            buffer.Span[0] = _reply.Dequeue();
            return ValueTask.FromResult(1);
        }
        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync() { IsOpen = false; return ValueTask.CompletedTask; }
    }
}
