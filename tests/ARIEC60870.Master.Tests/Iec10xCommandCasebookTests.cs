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
        int ioa = 123, int code = 2, int link = 105, byte type = 46,
        int qualifier = 0, int oa = 0, bool test = false,
        Iec60870ProtocolMode protocol = Iec60870ProtocolMode.Iec101,
        string connectionId = "", long? observedSequence = null, DateTime? observedUtc = null)
    {
        var qoc = (byte)((select ? 0x80 : 0) | (code & 3) | ((qualifier & 31) << 2));
        var raw = new byte[] { type, 1, (byte)(cot | (negative ? 0x40 : 0) | (test ? 0x80 : 0)), (byte)oa,
            (byte)ca, (byte)(ca >> 8), (byte)ioa, (byte)(ioa >> 8), (byte)(ioa >> 16), qoc };
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(raw);
        return new Iec103MasterEvidenceEvent
        {
            SequenceNumber = observedSequence ?? System.Threading.Interlocked.Increment(ref _sequence),
            TimestampUtc = observedUtc ?? DateTime.UtcNow,
            ProtocolMode = protocol,
            Direction = rtu ? FrameDirection.SlaveToMaster : FrameDirection.MasterToSlave,
            PhysicalSource = source,
            ConnectionInstanceId = connectionId,
            LinkAddress = protocol == Iec60870ProtocolMode.Iec101 ? link : null,
            CommonAddressNumber = ca,
            InformationObjectAddress = ioa,
            TypeId = type,
            CauseDetails = asdu.CauseDetails,
            CommandDetails = asdu.CommandDetails,
            AsduDetails = asdu,
            RawHex = Convert.ToHexString(raw)
        };
    }

    private static Iec103MasterEvidenceEvent Feedback(int code = 2, int quality = 0,
        string source = "A", int ca = 5, int ioa = 201, byte type = 3, int? link = 105,
        Iec60870ProtocolMode protocol = Iec60870ProtocolMode.Iec101,
        string connectionId = "", long? observedSequence = null, DateTime? observedUtc = null)
    {
        var raw = new byte[] { type, 1, 3, 0, (byte)ca, 0,
            (byte)ioa, (byte)(ioa >> 8), (byte)(ioa >> 16), (byte)(code | quality) };
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(raw);
        return new Iec103MasterEvidenceEvent
        {
            SequenceNumber = observedSequence ?? System.Threading.Interlocked.Increment(ref _sequence),
            TimestampUtc = observedUtc ?? DateTime.UtcNow,
            ProtocolMode = protocol,
            Direction = FrameDirection.SlaveToMaster,
            PhysicalSource = source,
            ConnectionInstanceId = connectionId,
            LinkAddress = protocol == Iec60870ProtocolMode.Iec101 ? link : null,
            CommonAddressNumber = ca,
            InformationObjectAddress = ioa,
            TypeId = type,
            CauseDetails = asdu.CauseDetails,
            AsduDetails = asdu,
            IsRelayValue = true,
            RawHex = Convert.ToHexString(raw)
        };
    }

    private static Iec103MasterEvidenceEvent FeedbackMany(params (int Ioa, int Code)[] values)
    {
        var raw = new List<byte> { 3, (byte)values.Length, 3, 0, 5, 0 };
        foreach (var (ioa, code) in values)
        {
            raw.Add((byte)ioa);
            raw.Add((byte)(ioa >> 8));
            raw.Add((byte)(ioa >> 16));
            raw.Add((byte)code);
        }

        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(raw.ToArray());
        return new Iec103MasterEvidenceEvent
        {
            SequenceNumber = System.Threading.Interlocked.Increment(ref _sequence),
            TimestampUtc = DateTime.UtcNow,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            PhysicalSource = "A",
            LinkAddress = 105,
            CommonAddressNumber = 5,
            TypeId = 3,
            CauseDetails = asdu.CauseDetails,
            AsduDetails = asdu,
            IsRelayValue = true,
            RawHex = Convert.ToHexString(raw.ToArray())
        };
    }

    private static Iec103MasterEvidenceEvent TimedFeedback(byte minuteFlags)
    {
        var raw = new byte[]
        {
            31, 1, 3, 0, 5, 0, 201, 0, 0, 2, // DPI=ON
            0, 0, minuteFlags, 7, 16, 11, 26 // CP56Time2a
        };
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(raw);
        return new Iec103MasterEvidenceEvent
        {
            SequenceNumber = System.Threading.Interlocked.Increment(ref _sequence),
            TimestampUtc = DateTime.UtcNow,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            PhysicalSource = "A",
            LinkAddress = 105,
            CommonAddressNumber = 5,
            TypeId = 31,
            CauseDetails = asdu.CauseDetails,
            AsduDetails = asdu,
            IsRelayValue = true,
            RawHex = Convert.ToHexString(raw)
        };
    }

    [Fact]
    public void TcpReconnectCannotConsumeActconOrFeedbackFromPreviousConnection()
    {
        const Iec60870ProtocolMode protocol = Iec60870ProtocolMode.Iec104;
        var b = new Iec10xCommandCasebook();
        var first = Command(select: false, protocol: protocol, connectionId: "tcp-a");
        Assert.True(b.Observe(first, feedbackIoa: 201));

        Assert.True(b.Observe(Command(cot: 7, select: false, rtu: true,
            protocol: protocol, connectionId: "tcp-b")));
        Assert.Equal("EXECUTE sent", b.Snapshot()[0].Status);
        Assert.Equal("Unmatched response", b.Snapshot()[1].Status);
        Assert.Equal(1, b.ActiveCount);

        Assert.True(b.Observe(Command(cot: 7, select: false, rtu: true,
            protocol: protocol, connectionId: "tcp-a")));
        Assert.False(b.Observe(Feedback(protocol: protocol, connectionId: "tcp-b")));
        Assert.Equal("EXECUTE accepted", b.Snapshot()[0].Status);
        Assert.True(b.Observe(Feedback(protocol: protocol, connectionId: "tcp-a")));
        Assert.Equal("Feedback matched · ACTTERM pending", b.Snapshot()[0].Status);
        Assert.Contains("tcp-a", b.Snapshot()[0].ToEvidenceText());
        Assert.Null(b.FindByEvidence(Command(cot: 7, select: false, rtu: true,
            protocol: protocol, connectionId: "tcp-b")));
    }

    [Fact]
    public void ParallelTcpConnectionEpochsHaveIndependentFeedbackOwners()
    {
        const Iec60870ProtocolMode protocol = Iec60870ProtocolMode.Iec104;
        var b = new Iec10xCommandCasebook();
        foreach (var connection in new[] { "tcp-a", "tcp-b" })
        {
            Assert.True(b.Observe(Command(select: false, protocol: protocol,
                connectionId: connection), feedbackIoa: 201));
            Assert.True(b.Observe(Command(cot: 7, select: false, rtu: true,
                protocol: protocol, connectionId: connection)));
        }

        Assert.True(b.Observe(Feedback(protocol: protocol, connectionId: "tcp-b")));
        Assert.Equal("EXECUTE accepted", b.Snapshot()[0].Status);
        Assert.Equal("Feedback matched · ACTTERM pending", b.Snapshot()[1].Status);
        Assert.Equal(2, b.ActiveCount);
        Assert.True(b.Observe(Feedback(protocol: protocol, connectionId: "tcp-a")));
        Assert.All(b.Snapshot(), c => Assert.Equal("Feedback matched · ACTTERM pending", c.Status));
    }

    [Fact]
    public void ReplayedFeedbackOlderThanActconCannotConfirmCurrentControl()
    {
        var b = new Iec10xCommandCasebook();
        var utc = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
        b.Observe(Command(select: false, observedSequence: 1000, observedUtc: utc), feedbackIoa: 201);
        b.Observe(Command(cot: 7, select: false, rtu: true,
            observedSequence: 1001, observedUtc: utc.AddSeconds(2)));
        Assert.True(b.Observe(Feedback(observedSequence: 1002, observedUtc: utc.AddSeconds(1))));
        Assert.Equal("Feedback unverified", Assert.Single(b.Snapshot()).Status);
        Assert.True(b.Observe(Feedback(observedSequence: 999, observedUtc: utc.AddSeconds(3))));
        Assert.Equal("Feedback unverified", Assert.Single(b.Snapshot()).Status);
        Assert.True(b.Observe(Feedback(observedSequence: 1003, observedUtc: utc.AddSeconds(3))));
        Assert.Equal("Feedback matched · ACTTERM pending", Assert.Single(b.Snapshot()).Status);
    }

    [Fact]
    public void ReplayedActconBeforeCommandTxCannotAuthorizeResponse()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(select: false, observedSequence: 5000));
        Assert.True(b.Observe(Command(cot: 7, select: false, rtu: true,
            observedSequence: 4999)));
        Assert.Equal("Ambiguous", Assert.Single(b.Snapshot()).Status);
        Assert.True(b.Observe(Command(cot: 7, select: false, rtu: true,
            observedSequence: 5001)));
        Assert.NotEqual("EXECUTE accepted", Assert.Single(b.Snapshot()).Status);
    }

    [Theory]
    [InlineData((byte)0x8A)]
    [InlineData((byte)0x4A)]
    public void InvalidOrSubstitutedCp56FeedbackCannotProveExecution(byte minuteFlags)
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(select: false), feedbackIoa: 201);
        b.Observe(Command(cot: 7, select: false, rtu: true));
        Assert.True(b.Observe(TimedFeedback(minuteFlags)));
        Assert.Equal("Feedback unverified", Assert.Single(b.Snapshot()).Status);
        Assert.True(b.Observe(Feedback()));
        Assert.Equal("Feedback matched · ACTTERM pending", Assert.Single(b.Snapshot()).Status);
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
    public void DoubleOpenDcsOneMustNotAcceptSinglePointOnSpiOne()
    {
        var b = new Iec10xCommandCasebook();
        Assert.True(b.Observe(Command(select: false, code: 1), feedbackIoa: 201));
        Assert.True(b.Observe(Command(cot: 7, select: false, code: 1, rtu: true)));
        Assert.True(b.Observe(Feedback(code: 1, type: 1))); // SPI=1 means ON, DCS=1 means OFF.
        Assert.Equal("Feedback mismatch", Assert.Single(b.Snapshot()).Status);
        Assert.True(b.Observe(Feedback(code: 0, type: 1))); // SPI=0 correctly means OFF.
        Assert.Equal("Feedback matched · ACTTERM pending", Assert.Single(b.Snapshot()).Status);
        Assert.True(b.Observe(Command(cot: 10, select: false, code: 1, rtu: true)));
        Assert.Equal("Feedback matched", Assert.Single(b.Snapshot()).Status);
        Assert.Equal(0, b.ActiveCount);
    }

    [Theory]
    [InlineData((byte)1, 1)]
    [InlineData((byte)3, 2)]
    public void DoubleCloseUsesLogicalOnAcrossSpiAndDpiFeedback(byte feedbackType, int feedbackCode)
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(select: false, code: 2), feedbackIoa: 201);
        b.Observe(Command(cot: 7, select: false, code: 2, rtu: true));
        Assert.True(b.Observe(Feedback(code: feedbackCode, type: feedbackType)));
        Assert.Equal("Feedback matched · ACTTERM pending", Assert.Single(b.Snapshot()).Status);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    public void SingleCommandUsesDpiLogicalTargetNotNumericalCode(int commandCode, int dpiCode)
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(select: false, type: 45, code: commandCode), feedbackIoa: 201);
        b.Observe(Command(cot: 7, select: false, type: 45, code: commandCode, rtu: true));
        Assert.True(b.Observe(Feedback(code: dpiCode, type: 3)));
        Assert.Equal("Feedback matched · ACTTERM pending", Assert.Single(b.Snapshot()).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void DpiIntermediateAndIndeterminateNeverConfirmTarget(int dpiCode)
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(select: false), feedbackIoa: 201);
        b.Observe(Command(cot: 7, select: false, rtu: true));
        Assert.True(b.Observe(Feedback(code: dpiCode)));
        Assert.Equal("Feedback indeterminate", Assert.Single(b.Snapshot()).Status);
        Assert.True(b.Observe(Command(cot: 10, select: false, rtu: true)));
        Assert.NotEqual("Feedback matched", Assert.Single(b.Snapshot()).Status);
        Assert.Equal(1, b.ActiveCount);
    }

    [Fact]
    public void QualifierEchoMismatchWithSameTargetCannotProduceAcceptedSelect()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(qualifier: 0));
        Assert.True(b.Observe(Command(cot: 7, rtu: true, qualifier: 3)));
        Assert.Equal("Ambiguous", Assert.Single(b.Snapshot()).Status);
        Assert.True(b.Observe(Command(select: false)));
        Assert.Equal("Ambiguous", Assert.Single(b.Snapshot()).Status);
        Assert.DoesNotContain(b.Snapshot(), c => c.Status == "Feedback matched");
    }

    [Fact]
    public void OneMappedRtuFeedbackCannotCompleteTwoActiveCommands()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(select: false, ioa: 123), feedbackIoa: 201);
        b.Observe(Command(cot: 7, select: false, ioa: 123, rtu: true));
        b.Observe(Command(select: false, ioa: 124), feedbackIoa: 201);
        b.Observe(Command(cot: 7, select: false, ioa: 124, rtu: true));
        Assert.True(b.Observe(Feedback()));
        Assert.Equal(2, b.ActiveCount);
        Assert.All(b.Snapshot(), c => Assert.Equal("Ambiguous", c.Status));
        Assert.All(b.Snapshot(), c => Assert.Contains("multiple", c.Interpretation));
    }

    [Fact]
    public void MultiObjectDpiFeedbackCanResolveIndependentCommandMappings()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(select: false, ioa: 123, code: 2), feedbackIoa: 201);
        b.Observe(Command(cot: 7, select: false, ioa: 123, code: 2, rtu: true));
        b.Observe(Command(select: false, ioa: 124, code: 1), feedbackIoa: 202);
        b.Observe(Command(cot: 7, select: false, ioa: 124, code: 1, rtu: true));
        Assert.True(b.Observe(FeedbackMany((201, 2), (202, 1))));
        Assert.All(b.Snapshot(), c => Assert.Equal("Feedback matched · ACTTERM pending", c.Status));
    }

    [Fact]
    public void MultiObjectFeedbackAmbiguityOnlyAffectsSharedIoaNotIndependentIoa()
    {
        var b = new Iec10xCommandCasebook();
        foreach (var ioa in new[] { 123, 124, 125 })
        {
            b.Observe(Command(select: false, ioa: ioa), feedbackIoa: ioa == 125 ? 202 : 201);
            b.Observe(Command(cot: 7, select: false, ioa: ioa, rtu: true));
        }

        Assert.True(b.Observe(FeedbackMany((201, 2), (202, 2))));
        Assert.Equal("Ambiguous", b.Snapshot()[0].Status);
        Assert.Equal("Ambiguous", b.Snapshot()[1].Status);
        Assert.Equal("Feedback matched · ACTTERM pending", b.Snapshot()[2].Status);
    }

    [Fact]
    public void UnsolicitedDeactconDoesNotCancelWithoutMatchingMasterCotEight()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command());
        b.Observe(Command(cot: 9, rtu: true));
        Assert.Equal("Observed incomplete", Assert.Single(b.Snapshot()).Status);
        Assert.Equal(1, b.ActiveCount);
        b.Observe(Command(cot: 8));
        b.Observe(Command(cot: 9, rtu: true));
        Assert.Equal("Cancelled", Assert.Single(b.Snapshot()).Status);
        Assert.Equal(0, b.ActiveCount);
    }

    [Fact]
    public void FeedbackWithoutKnownMatchingIec101LinkCannotProveControl()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(select: false, link: 105), feedbackIoa: 201);
        b.Observe(Command(cot: 7, select: false, rtu: true, link: 105));
        Assert.False(b.Observe(Feedback(link: null)));
        Assert.Equal("EXECUTE accepted", Assert.Single(b.Snapshot()).Status);
        Assert.True(b.Observe(Feedback(link: 105)));
        Assert.Equal("Feedback matched · ACTTERM pending", Assert.Single(b.Snapshot()).Status);
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
    public void FeedbackOnOtherExplicitIec101LinkMustNotCompleteCommand()
    {
        var b = new Iec10xCommandCasebook();
        var request = Command(select: false, link: 105);
        Assert.True(b.Observe(request, feedbackIoa: 201));
        Assert.True(b.Observe(Command(cot: 7, select: false, rtu: true, link: 105)));
        Assert.False(b.Observe(Feedback(link: 106)));
        Assert.Equal("EXECUTE accepted", Assert.Single(b.Snapshot()).Status);
        Assert.True(b.Observe(Feedback(link: 105)));
        Assert.Equal("Feedback matched · ACTTERM pending", Assert.Single(b.Snapshot()).Status);
        var matched = b.FindByEvidence(request);
        Assert.NotNull(matched);
        Assert.Equal(matched!.CaseId, Assert.Single(b.Snapshot()).CaseId);
        Assert.Null(b.FindByEvidence(Command(ioa: 999)));
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
    [Fact]
    public void SecondaryActivationIsAnAnomalyAndDoesNotConsumePendingSelect()
    {
        var b = new Iec10xCommandCasebook();
        Assert.True(b.Observe(Command(cot: 6, select: true)));
        Assert.True(b.Observe(Command(cot: 6, select: true, rtu: true)));
        Assert.Equal(2, b.Count);
        Assert.Equal("Protocol anomaly", b.Snapshot()[1].Status);
        Assert.Equal(1, b.ActiveCount);
        Assert.Contains("not a standard ACTCON", b.Snapshot()[1].Interpretation);
        Assert.True(b.Observe(Command(cot: 7, select: true, rtu: true)));
        Assert.Equal("SELECT accepted", b.Snapshot()[0].Status);
    }

    [Fact]
    public void CasebookAdmitsBoundedStageTruncationInEvidenceReport()
    {
        var b = new Iec10xCommandCasebook();
        Assert.True(b.Observe(Command()));
        for (var i = 0; i < 30; i++)
            Assert.True(b.Observe(Command(cot: 7, rtu: true)));
        var result = Assert.Single(b.Snapshot());
        Assert.True(result.StagesTruncated);
        Assert.Equal(24, result.Stages.Count);
        Assert.Contains("EVIDENCE LIMITED", result.ToEvidenceText());
    }


    [Theory]
    [InlineData("SINGLE")]
    [InlineData("LINK A")]
    [InlineData("LINK B")]
    public void RepeatedSboAfterPositiveExecuteActconCreatesDistinctCasesWithoutActterm(string source)
    {
        var b = new Iec10xCommandCasebook();
        // Field reproduction: SELECT+ACTCON, EXECUTE+ACTCON, mapped DPI ON;
        // new SELECT OFF arrives with no EXECUTE ACTTERM. A/B and single
        // have the same source-qualified command lifecycle.
        Assert.True(b.Observe(Command(source: source, code: 2), feedbackIoa: 201));
        Assert.True(b.Observe(Command(cot: 7, rtu: true, source: source, code: 2)));
        Assert.True(b.Observe(Command(select: false, source: source, code: 2)));
        Assert.True(b.Observe(Command(cot: 7, select: false, rtu: true, source: source, code: 2)));
        Assert.True(b.Observe(Feedback(source: source, code: 2)));

        Assert.True(b.Observe(Command(source: source, code: 1), feedbackIoa: 201));
        Assert.Equal(2, b.Count);
        Assert.Equal(1, b.ActiveCount);
        Assert.Equal("Feedback matched · ACTTERM not observed", b.Snapshot()[0].Status);
        Assert.Equal("SELECT sent", b.Snapshot()[1].Status);
        Assert.NotEqual(b.Snapshot()[0].CaseId, b.Snapshot()[1].CaseId);

        Assert.True(b.Observe(Command(cot: 7, rtu: true, source: source, code: 1)));
        Assert.True(b.Observe(Command(select: false, source: source, code: 1)));
        Assert.True(b.Observe(Command(cot: 7, select: false, rtu: true, source: source, code: 1)));
        Assert.True(b.Observe(Feedback(source: source, code: 1)));
        Assert.Equal("Feedback matched · ACTTERM pending", b.Snapshot()[1].Status);
        Assert.DoesNotContain(b.Snapshot(), c => c.Status == "Ambiguous");
        Assert.Contains("not independent proof", b.Snapshot()[0].Interpretation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoMappedFeedbackDoesNotMakeLaterConfirmedCommandOverlapForever(bool firstSelect)
    {
        var b = new Iec10xCommandCasebook();
        if (firstSelect)
        {
            b.Observe(Command(code: 2));
            b.Observe(Command(cot: 7, rtu: true, code: 2));
        }
        b.Observe(Command(select: false, code: 2));
        b.Observe(Command(cot: 7, select: false, rtu: true, code: 2));

        b.Observe(Command(code: 1));
        Assert.Equal(2, b.Count);
        Assert.Equal("EXECUTE accepted · ACTTERM not observed", b.Snapshot()[0].Status);
        Assert.Equal("SELECT sent", b.Snapshot()[1].Status);
        b.Observe(Command(cot: 7, rtu: true, code: 1));
        Assert.Equal("SELECT accepted", b.Snapshot()[1].Status);
        Assert.Equal(1, b.ActiveCount);
    }

    [Fact]
    public void DelayedOldExecuteReplyCannotPoisonNewSelectOrConfirmIt()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(code: 2));
        b.Observe(Command(cot: 7, rtu: true, code: 2));
        b.Observe(Command(select: false, code: 2));
        b.Observe(Command(cot: 7, select: false, rtu: true, code: 2));
        b.Observe(Command(code: 1));

        // A late ACTTERM/ACTCON carrying EXECUTE phase cannot be associated
        // with the new SELECT; leave it as explicitly unpaired evidence.
        b.Observe(Command(cot: 10, select: false, code: 2, rtu: true));
        b.Observe(Command(cot: 7, select: false, code: 2, rtu: true));
        Assert.Equal(4, b.Count);
        Assert.Equal("SELECT sent", b.Snapshot()[1].Status);
        Assert.All(b.Snapshot().Skip(2), c => Assert.Equal("Unmatched response", c.Status));
        Assert.Equal(1, b.ActiveCount);

        b.Observe(Command(cot: 7, select: true, code: 1, rtu: true));
        Assert.Equal("SELECT accepted", b.Snapshot()[1].Status);
    }

    [Fact]
    public void UnconfirmedExecuteAndUnresolvedSelectRemainAmbiguousForSafety()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(code: 2));
        b.Observe(Command(cot: 7, rtu: true, code: 2));
        b.Observe(Command(select: false, code: 2));
        // No matching RTU execute ACTCON: a new SELECT may overlap.
        b.Observe(Command(code: 1));
        Assert.Equal(1, b.Count);
        Assert.Equal("Ambiguous", b.Snapshot()[0].Status);
        Assert.Equal(1, b.ActiveCount);
        // Even a late EXECUTE ACTCON must not clear that ambiguity.
        b.Observe(Command(cot: 7, select: false, code: 2, rtu: true));
        Assert.Equal("Ambiguous", b.Snapshot()[0].Status);
    }

    [Fact]
    public void NewCommandOnOtherLinkNeverSupersedesOldActiveOwner()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(source: "LINK A", code: 2), feedbackIoa: 201);
        b.Observe(Command(cot: 7, rtu: true, source: "LINK A", code: 2));
        b.Observe(Command(select: false, source: "LINK A", code: 2));
        b.Observe(Command(cot: 7, select: false, rtu: true, source: "LINK A", code: 2));
        b.Observe(Command(source: "LINK B", code: 1), feedbackIoa: 201);
        Assert.Equal(2, b.ActiveCount);
        Assert.Equal("EXECUTE accepted", b.Snapshot()[0].Status);
        Assert.Equal("SELECT sent", b.Snapshot()[1].Status);
        // Wrong physical source must not confirm B.
        b.Observe(Command(cot: 7, rtu: true, source: "LINK A", code: 1));
        Assert.Equal("SELECT sent", b.Snapshot()[1].Status);
    }


    [Fact]
    public void RepeatedExecuteWithoutTermOrFreshSelectIsNotSilentlyIndependent()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(select: false, code: 2));
        b.Observe(Command(select: false, code: 2, rtu: true, cot: 7));
        b.Observe(Command(select: false, code: 2));
        Assert.Equal(1, b.Count);
        Assert.Equal("Ambiguous", b.Snapshot()[0].Status);
    }

    [Fact]
    public void NewSelectAfterObservedActtermIsIndependentEvenWithMissingFeedback()
    {
        var b = new Iec10xCommandCasebook();
        b.Observe(Command(select: false, code: 2));
        b.Observe(Command(select: false, code: 2, rtu: true, cot: 7));
        b.Observe(Command(select: false, code: 2, rtu: true, cot: 10));
        Assert.Equal("ACTTERM observed", b.Snapshot()[0].Status);
        b.Observe(Command(select: true, code: 1));
        Assert.Equal(2, b.Count);
        Assert.Equal("ACTTERM observed", b.Snapshot()[0].Status);
        Assert.Equal("SELECT sent", b.Snapshot()[1].Status);
    }

}
