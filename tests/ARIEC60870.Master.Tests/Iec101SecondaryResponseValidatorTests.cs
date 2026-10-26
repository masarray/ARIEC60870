// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Protocol;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101SecondaryResponseValidatorTests
{
    private static readonly Ft12Parser Parser = new(2);

    [Fact]
    public void SicamEvidencePrimaryLinkStatusCannotProveOutstationHealth()
    {
        // RX evidence from 2026-10-26: 10 49 69 00 B2 16 (PRM=1).
        var received = Convert.FromHexString("10496900B216");
        var check = Iec101SecondaryResponseValidator.Validate(
            Parser.Decode(received), 105, received);

        Assert.False(check.IsValid);
        Assert.False(check.AdvancesFcb);
        Assert.Equal("IEC101-RX-TX-ECHO", check.Code);
    }

    [Fact]
    public void SicamCrossChannelPrimaryLink105CannotProveLink1Healthy()
    {
        var previousTxLink1 = Convert.FromHexString("104901004A16");
        var check = Iec101SecondaryResponseValidator.Validate(
            Parser.Decode(Convert.FromHexString("10496900B216")), 1, previousTxLink1);

        Assert.False(check.IsValid);
        Assert.False(check.AdvancesFcb);
        Assert.Equal("IEC101-RX-FOREIGN-PRIMARY", check.Code);
        Assert.Contains("expected Link=1", check.Explanation);
    }

    [Fact]
    public void PrimaryFromSameAddressButNotMatchingLastTxIsRejected()
    {
        var giTx = Convert.FromHexString("680D0D6853690064010600690000000014A416");
        var check = Iec101SecondaryResponseValidator.Validate(
            Parser.Decode(Convert.FromHexString("10496900B216")), 105, giTx);

        Assert.False(check.IsValid);
        Assert.Equal("IEC101-RX-UNEXPECTED-PRIMARY", check.Code);
    }

    [Fact]
    public void SecondaryWrongLinkIsRejectedEvenWithCorrectChecksum()
    {
        var check = Iec101SecondaryResponseValidator.Validate(
            Parser.Decode(Convert.FromHexString("100B69007416")), 1, Array.Empty<byte>());

        Assert.False(check.IsValid);
        Assert.Equal("IEC101-RX-LINK-ADDRESS-MISMATCH", check.Code);
    }

    [Fact]
    public void ValidSicamSecondaryStatusIsAccepted()
    {
        var frame = Parser.Decode(Convert.FromHexString("100B69007416"));
        var check = Iec101SecondaryResponseValidator.Validate(frame, 105, Array.Empty<byte>());

        Assert.True(check.IsValid);
        Assert.True(check.AdvancesFcb);
        Assert.True(Iec101SecondaryResponseValidator.CanAdvanceFcb(frame));
    }

    [Fact]
    public void CorruptChecksumNeverAdvancesFcb()
    {
        var frame = Parser.Decode(Convert.FromHexString("100B69007516"));
        var check = Iec101SecondaryResponseValidator.Validate(frame, 105, Array.Empty<byte>());

        Assert.False(check.IsValid);
        Assert.Equal("IEC101-RX-INTEGRITY", check.Code);
    }

    [Theory]
    [InlineData("E5", true)]
    [InlineData("A2", false)]
    [InlineData("100169006A16", false)] // Secondary NACK function 1.
    [InlineData("102B69009416", false)] // Secondary ACK with DFC=1 (0x2B).
    public void AckNackAndBusyPreserveFcbSemantics(string hex, bool advances)
    {
        var frame = Parser.Decode(Convert.FromHexString(hex));
        var check = Iec101SecondaryResponseValidator.Validate(frame, 105, Array.Empty<byte>());

        Assert.True(check.IsValid);
        Assert.Equal(advances, check.AdvancesFcb);
        Assert.Equal(advances, Iec101SecondaryResponseValidator.CanAdvanceFcb(frame));
    }
}

// Integration regression: the exact primary-only RX pattern observed on
// SICAM A8000 must not produce any validated responses, nor healthy standby.
public sealed class Iec101RejectedRxSessionTests
{
    [Fact]
    public async Task SingleLinkPrimaryOnlyRxIsNeverCountedAsTimedResponse()
    {
        var settings = CreateSettings(105);
        var transport = new PrimaryOnlyTransport();
        var session = new ARIEC60870.Master.Iec101MasterSession(settings, transport);

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(220), CancellationToken.None);

        Assert.True(result.Counters.RxFrames > 0);
        Assert.Equal(0, result.Counters.TimedResponses);
        Assert.Contains(result.Events, e => e.Summary.Contains("RX rejected", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Findings, f => f.Id == "IEC101-RX-TX-ECHO" || f.Id == "IEC101-RX-UNEXPECTED-PRIMARY");
    }

    [Fact]
    public async Task DualLinkForeignPrimaryRxCannotPromoteOrCertifyStandby()
    {
        var settings = CreateSettings(105);
        var options = new ARIEC60870.Master.Iec101.Redundancy.Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new() { Name = "A", PortName = "FAKE-A", LinkAddress = 105 },
            LinkB = new() { Name = "B", PortName = "FAKE-B", LinkAddress = 1 },
            ActiveFailureThreshold = 1,
            StandbyFailureThreshold = 1,
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(50)
        };
        var session = new ARIEC60870.Master.Iec101.Redundancy.Iec101DualLinkRedundancySession(
            options, new PrimaryOnlyTransport(), new PrimaryOnlyTransport());

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(290), CancellationToken.None);
        var snapshot = session.CreateSnapshot();

        Assert.Equal(0, snapshot.FailoverCount);
        Assert.False(snapshot.LinkA.IsHealthy);
        Assert.False(snapshot.LinkB.IsHealthy);
        Assert.Contains(result.Events, e => e.Summary.Contains("IEC101-RX-FOREIGN-PRIMARY", StringComparison.OrdinalIgnoreCase));
    }

    private static ARIEC60870.Master.Model.Iec103MasterSettings CreateSettings(int link)
        => new()
        {
            ProtocolMode = ARIEC60870.Master.Model.Iec60870ProtocolMode.Iec101,
            PortName = "FAKE-A",
            LinkAddress = link,
            LinkAddressSize = 2,
            CommonAddress = 105,
            ResponseTimeoutMs = 45,
            Class2PollIntervalMs = 25,
            Class1DrainDelayMs = 1,
            ResetRemoteLinkOnConnect = false,
            ResetFcbOnConnect = false,
            SendGeneralInterrogationOnConnect = false,
            SendClockSyncOnConnect = false,
            RequestClass2ImmediatelyAfterStartup = false
        };

    // Deterministic transport: any master request produces the same unsolicited
    // PRM=1 link-status frame, including on a channel expecting LA=1.
    private sealed class PrimaryOnlyTransport : ARIEC60870.Master.Transport.IByteTransport
    {
        private static readonly byte[] Primary = Convert.FromHexString("10496900B216");
        private readonly Queue<byte> _incoming = new();

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
            if (!IsOpen) throw new InvalidOperationException("Closed test transport.");
            foreach (var value in Primary) _incoming.Enqueue(value);
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (buffer.Length == 0 || _incoming.Count == 0) return ValueTask.FromResult(0);
            buffer.Span[0] = _incoming.Dequeue();
            return ValueTask.FromResult(1);
        }

        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
    }
}
