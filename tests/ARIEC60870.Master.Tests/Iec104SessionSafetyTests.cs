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
            SendClockSyncOnConnect = true,
            SendGeneralInterrogationOnConnect = true
        };
        var session = new Iec104ClientSession(settings, transport);
        var result = await session.RunForAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.False(result.CompletedNormally);
        Assert.Contains("STARTDT", result.CompletionReason);
        Assert.Single(transport.Sent);
        Assert.Equal(Iec104FrameBuilder.StartDtActivation(), transport.Sent[0]);
        Assert.Contains(result.Findings, f => f.RuleId == "IEC104-STARTDT-CON");
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
