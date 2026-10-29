// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec10xObservedControlTimelineTests
{
    private static Iec10xAsduDecode Decode(int cot, byte qualifier = 0x82,
        int ca = 5, int ioa = 123, byte type = 46, bool negative = false, bool test = false)
    {
        var asdu = new byte[]
        {
            type, 1, (byte)(cot | (negative ? 0x40 : 0) | (test ? 0x80 : 0)), 0,
            (byte)ca, (byte)(ca >> 8), (byte)ioa, (byte)(ioa >> 8),
            (byte)(ioa >> 16), qualifier
        };
        return new Iec10xAsduDecoder(2, 2, 3).Decode(asdu);
    }

    private static string Master(Iec10xObservedControlTimeline timeline,
        int cot = 6, byte qualifier = 0x82, string source = "Link A", int ca = 5,
        int ioa = 123, byte type = 46)
        => timeline.Observe(FrameDirection.MasterToSlave,
            Decode(cot, qualifier, ca, ioa, type), source, 20, true);

    private static string Rtu(Iec10xObservedControlTimeline timeline,
        int cot = 7, byte qualifier = 0x82, bool negative = false,
        string source = "Link A", int ca = 5, int ioa = 123, byte type = 46)
        => timeline.Observe(FrameDirection.SlaveToMaster,
            Decode(cot, qualifier, ca, ioa, type, negative), source, 60, true);

    [Fact]
    public void NegativeActconIsClearlyIdentifiedButNeverClaimsDeviceRootCause()
    {
        var timeline = new Iec10xObservedControlTimeline();
        Assert.Contains("MASTER SELECT", Master(timeline));
        var result = Rtu(timeline, negative: true);
        Assert.Contains("NEGATIVE CONFIRMATION", result);
        Assert.Contains("P/N=1", result);
        Assert.Contains("COT=7", result);
        Assert.Contains("CA=5, IOA=123", result);
        Assert.Contains("request RX offset=20", result);
        Assert.Contains("root cause", result);
        Assert.DoesNotContain("application accepted", result);
        Assert.Equal(0, timeline.PendingCount);
    }

    [Fact]
    public void SelectExecuteActconActtermRemainProtocolOnlyNotPhysicalProof()
    {
        var timeline = new Iec10xObservedControlTimeline();
        Master(timeline);
        var selectAck = Rtu(timeline);
        Assert.Contains("POSITIVE ACTCON", selectAck);
        Assert.Contains("SELECT", selectAck);
        Assert.Contains("still unverified", selectAck);
        var execute = Master(timeline, qualifier: 0x02);
        Assert.Contains("MASTER EXECUTE", execute);
        var executeAck = Rtu(timeline, qualifier: 0x02);
        Assert.Contains("application accepted", executeAck);
        var end = Rtu(timeline, cot: 10, qualifier: 0x02);
        Assert.Contains("ACTTERM COT=10", end);
        Assert.Contains("Physical position/target feedback remains unverified", end);
        Assert.Equal(0, timeline.PendingCount);
    }

    [Theory]
    [InlineData(44, "Unknown Type ID")]
    [InlineData(45, "Unknown COT")]
    [InlineData(46, "Unknown common address")]
    [InlineData(47, "Unknown information object address")]
    public void ProtocolErrorCauseIsExplainedAsProtocolErrorNotInterlock(int cot, string reason)
    {
        var timeline = new Iec10xObservedControlTimeline();
        Master(timeline);
        var result = Rtu(timeline, cot: cot);
        Assert.Contains("RTU APPLICATION ERROR", result);
        Assert.Contains("COT=" + cot, result);
        Assert.Contains(reason, result);
        // IEC names vary across installations; numeric cause and source are authoritative.
        Assert.Contains("profile", result);
        Assert.DoesNotContain("physical position confirmed", result);
        Assert.Equal(0, timeline.PendingCount);
    }

    [Fact]
    public void AandBAreIsolatedEvenWhenCaIoaTypeMatch()
    {
        var timeline = new Iec10xObservedControlTimeline();
        Master(timeline, source: "Link A");
        var replyB = Rtu(timeline, source: "Link B");
        Assert.Contains("matching Master request not observed", replyB);
        Assert.Equal(1, timeline.PendingCount);
        Assert.Contains("POSITIVE ACTCON", Rtu(timeline, source: "Link A"));
    }

    [Fact]
    public void NoRequestAndOutOfOrderActtermAreIncomplete()
    {
        var timeline = new Iec10xObservedControlTimeline();
        Assert.Contains("matching Master request not observed", Rtu(timeline));
        Master(timeline);
        var end = Rtu(timeline, cot: 10);
        Assert.Contains("ACTCON not observed", end);
        Assert.Equal(1, timeline.PendingCount);
    }

    [Fact]
    public void OverlappingRequestAndSelectEchoMismatchDoNotProduceSuccess()
    {
        var timeline = new Iec10xObservedControlTimeline();
        Master(timeline);
        Assert.Contains("ambiguous", Master(timeline));
        Assert.Contains("ambiguous", Rtu(timeline));
        timeline.Reset();
        Master(timeline);
        Assert.Contains("SELECT/EXECUTE echo differs", Rtu(timeline, qualifier: 0x02));
        Assert.Equal(1, timeline.PendingCount);
    }

    [Fact]
    public void CorruptOrTestFramesCannotConfirmOrChangePendingState()
    {
        var timeline = new Iec10xObservedControlTimeline();
        Master(timeline);
        var rejected = timeline.Observe(FrameDirection.SlaveToMaster, Decode(7), "Link A", 60, false);
        Assert.Contains("UNVERIFIED", rejected);
        Assert.Contains("TEST bit set", timeline.Observe(FrameDirection.SlaveToMaster,
            Decode(7, test: true), "Link A", 80, true));
        Assert.Equal(1, timeline.PendingCount);
        Assert.Contains("POSITIVE ACTCON", Rtu(timeline));
    }

    [Fact]
    public void EchoWithDifferentDcsTargetIsNotAMatchedActcon()
    {
        var timeline = new Iec10xObservedControlTimeline();
        Master(timeline, qualifier: 0x82);
        var wrong = Rtu(timeline, qualifier: 0x81);
        Assert.Contains("target differs", wrong);
        Assert.DoesNotContain("application accepted", wrong);
        Assert.Equal(1, timeline.PendingCount);
        Assert.Contains("POSITIVE ACTCON", Rtu(timeline, qualifier: 0x82));
    }

    [Fact]
    public void ExecuteChangingConfirmedSelectTargetIsAmbiguous()
    {
        var timeline = new Iec10xObservedControlTimeline();
        Master(timeline, qualifier: 0x82);
        Rtu(timeline, qualifier: 0x82);
        var wrong = Master(timeline, qualifier: 0x01);
        Assert.Contains("target differs", wrong);
        Assert.Contains("ambiguous", Rtu(timeline, qualifier: 0x01));
    }

    [Fact]
    public void StandaloneLinkAckAndNonControlDataDoNotInventTransactions()
    {
        var timeline = new Iec10xObservedControlTimeline();
        Assert.Equal(string.Empty, timeline.Observe(FrameDirection.SlaveToMaster,
            null, "Link A", 200, true));
        Assert.Equal(string.Empty, timeline.Observe(FrameDirection.SlaveToMaster,
            new Iec10xAsduDecoder().Decode(new byte[]
            {
                1, 1, 3, 0, 1, 0, 1, 0, 0, 1
            }), "Link A", 220, true));
        Assert.Equal(0, timeline.PendingCount);
    }
}
