// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec10xCommandCasebookTests
{
    private static long _sequence;
    private static Iec103MasterEvidenceEvent Command(int cot = 6, bool select = true,
        bool rtu = false, bool negative = false, string source = "A", int ca = 5,
        int ioa = 123, int code = 2, int link = 105, byte type = 46)
    {
        var qualifier = (byte)((select ? 0x80 : 0) | code);
        var raw = new byte[] { type, 1, (byte)(cot | (negative ? 0x40 : 0)), 0,
            (byte)ca, (byte)(ca >> 8), (byte)ioa, (byte)(ioa >> 8), (byte)(ioa >> 16), qualifier };
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(raw);
        return new Iec103MasterEvidenceEvent
        {
            SequenceNumber = System.Threading.Interlocked.Increment(ref _sequence),
            TimestampUtc = DateTime.UtcNow,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = rtu ? FrameDirection.SlaveToMaster : FrameDirection.MasterToSlave,
            PhysicalSource = source, LinkAddress = link, CommonAddressNumber = ca,
            InformationObjectAddress = ioa, TypeId = type, CauseDetails = asdu.CauseDetails,
            CommandDetails = asdu.CommandDetails, AsduDetails = asdu,
            RawHex = Convert.ToHexString(raw)
        };
    }

    private static Iec103MasterEvidenceEvent Feedback(int code = 2, int quality = 0,
        string source = "A", int ca = 5, int ioa = 201, byte type = 3)
    {
        var raw = new byte[] { type, 1, 3, 0, (byte)ca, 0,
            (byte)ioa, (byte)(ioa >> 8), (byte)(ioa >> 16), (byte)(code | quality) };
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(raw);
        return new Iec103MasterEvidenceEvent
        {
            SequenceNumber = System.Threading.Interlocked.Increment(ref _sequence),
            TimestampUtc = DateTime.UtcNow,
            ProtocolMode = Iec60870ProtocolMode.Iec101, Direction = FrameDirection.SlaveToMaster,
            PhysicalSource = source, CommonAddressNumber = ca, InformationObjectAddress = ioa,
            TypeId = type, CauseDetails = asdu.CauseDetails, AsduDetails = asdu, IsRelayValue = true,
            RawHex = Convert.ToHexString(raw)
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SourceQualifiedSelectExecuteFeedbackAndActtermHaveOneCase(bool feedbackFirst)
    {
        var b = new Iec10xCommandCasebook();
        Assert.True(b.Observe(Command(), feedbackIoa: 201));
        Assert.True(b.Observe(Command(cot: 7, rtu: true)));
        Assert.True(b.Observe(Command(select: false)));
        Assert.True(b.Observe(Command(cot: 7, select: false, rtu: true)));
        if (feedbackFirst) Assert.True(b.Observe(Feedback()));
        Assert.True(b.Observe(Command(cot: 10, select: false, rtu: true)));
        if (!feedbackFirst) Assert.True(b.Observe(Feedback()));
        var c = Assert.Single(b.Snapshot());
        Assert.Equal("Feedback matched", c.Status);
        Assert.Equal(6, c.Stages.Count);
        Assert.Equal(0, b.ActiveCount);
        Assert.Contains("HEX:", c.ToEvidenceText());
        Assert.Contains("Source: A", c.ToEvidenceText());
    }

    [Fact]
    public void NegativeConfirmationIsAnObservedRejectionNotAssumedInterlock()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command());
        b.Observe(Command(cot: 7, rtu: true, negative: true));
        var c = Assert.Single(b.Snapshot());
        Assert.Equal("RTU rejected", c.Status);
        Assert.Contains("COT=7, P/N=1", c.Stages.Last().Explanation);
        Assert.Contains("not proven", c.Interpretation);
        Assert.Equal(0, b.ActiveCount);
    }

    [Theory]
    [InlineData(44)]
    [InlineData(45)]
    [InlineData(46)]
    [InlineData(47)]
    public void ProtocolErrorsAreRejectionStages(int cot)
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command());
        b.Observe(Command(cot: cot, rtu: true));
        Assert.Equal("RTU rejected", Assert.Single(b.Snapshot()).Status);
    }

    [Fact]
    public void WrongPhysicalSourceNeverConsumesCorrectRequest()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(source: "A"));
        b.Observe(Command(cot: 7, rtu: true, source: "B"));
        Assert.Equal(2, b.Count);
        Assert.Equal(1, b.ActiveCount);
        Assert.Equal("Unmatched response", b.Snapshot()[1].Status);
        b.Observe(Command(cot: 7, rtu: true, source: "A"));
        Assert.Equal("SELECT accepted", b.Snapshot()[0].Status);
    }

    [Fact]
    public void OverlappingRequestsNeverBecomeLastWins()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command());
        b.Observe(Command());
        b.Observe(Command(cot: 7, rtu: true));
        Assert.Equal("Ambiguous", Assert.Single(b.Snapshot()).Status);
        Assert.Equal(1, b.ActiveCount);
    }

    [Fact]
    public void EchoedWrongTargetDoesNotCompleteControl()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command());
        b.Observe(Command(cot: 7, rtu: true, code: 1));
        Assert.Equal("Ambiguous", Assert.Single(b.Snapshot()).Status);
    }

    [Fact]
    public void NoMappingCannotInventPhysicalFeedbackSuccess()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(select: false));
        b.Observe(Command(cot: 7, select: false, rtu: true));
        b.Observe(Feedback());
        b.Observe(Command(cot: 10, select: false, rtu: true));
        Assert.Equal("ACTTERM observed", Assert.Single(b.Snapshot()).Status);
        Assert.DoesNotContain("physical position proved", Assert.Single(b.Snapshot()).Interpretation);
    }

    [Fact]
    public void BadQualityOrWrongFeedbackTargetCannotBeSuccess()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(select: false), feedbackIoa: 201);
        b.Observe(Command(cot: 7, select: false, rtu: true));
        Assert.False(b.Observe(Feedback(quality: 0x80)));
        Assert.True(b.Observe(Feedback(code: 1)));
        Assert.Equal("Feedback mismatch", Assert.Single(b.Snapshot()).Status);
        Assert.True(b.Observe(Feedback()));
        Assert.Equal("Feedback matched · ACTTERM pending", Assert.Single(b.Snapshot()).Status);
    }

    [Fact]
    public void PartialCaptureMustBeIncompleteNotHardwareTimeout()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command());
        b.EndObservation();
        var c = Assert.Single(b.Snapshot());
        Assert.Equal("Observed incomplete", c.Status);
        Assert.Contains("Not an RTU failure", c.Interpretation);
    }

    [Fact]
    public void NonControlLinkFramesAndTestBitNeverCreateCases()
    {
        var b = new Iec10xCommandCasebook();
        var noCommand = new Iec103MasterEvidenceEvent
        { Direction = FrameDirection.SlaveToMaster, ProtocolMode = Iec60870ProtocolMode.Iec101 };
        Assert.False(b.Observe(noCommand));
        Assert.Equal(0, b.Count);
    }

    [Fact]
    public void BoundedHistoryPreservesLatestEvidenceAndResetIsolatesSessions()
    {
        var b = new Iec10xCommandCasebook(capacity: 2);
        b.Observe(Command(ioa: 1));
        b.Observe(Command(ioa: 2));
        b.Observe(Command(ioa: 3));
        Assert.Equal(new[] { 2, 3 }, b.Snapshot().Select(x => x.Ioa));
        b.Reset();
        Assert.Empty(b.Snapshot());
        Assert.Equal(0, b.ActiveCount);
    }
}
