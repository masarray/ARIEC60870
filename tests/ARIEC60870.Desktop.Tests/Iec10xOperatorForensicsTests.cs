// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec10xOperatorForensicsTests
{
    private static Iec10xAsduDecode Asdu(byte type, int cot, bool negative = false) => new()
    {
        TypeId = type, TypeName = type == 100 ? "C_IC_NA_1" : "C_DC_NA_1",
        CauseOfTransmission = cot,
        CauseName = cot == 7 ? "activation confirmation" : cot == 10 ? "activation termination" : "activation",
        IsNegativeConfirm = negative, CommonAddress = 1, InformationObjectAddress = type == 100 ? 0 : 68550,
        CauseDetails = new Iec10xCauseDetails((byte)(cot | (negative ? 0x40 : 0)), cot, negative, false, 0)
    };

    [Fact]
    public void GiAndInterrogatedDataUseDistinctApplicationMeaning()
    {
        Assert.Equal("Send General Interrogation", Iec101TraceNarrator.ApplicationTitle(Asdu(100, 6), FrameDirection.MasterToSlave));
        Assert.Equal("GI accepted · ACTCON", Iec101TraceNarrator.ApplicationTitle(Asdu(100, 7), FrameDirection.SlaveToMaster));
        Assert.Equal("GI rejected · negative ACTCON", Iec101TraceNarrator.ApplicationTitle(Asdu(100, 7, true), FrameDirection.SlaveToMaster));
        Assert.Equal("GI terminated · ACTTERM", Iec101TraceNarrator.ApplicationTitle(Asdu(100, 10), FrameDirection.SlaveToMaster));
        var measured = new Iec10xAsduDecode { TypeId = 13, TypeName = "M_ME_NC_1", CauseOfTransmission = 20 };
        Assert.Equal("GI data · Floating measurement", Iec101TraceNarrator.ApplicationTitle(measured, FrameDirection.SlaveToMaster));
    }

    [Fact]
    public void EveryObservedApplicationCommandStageAppearsAsEventWithoutClaimingOperation()
    {
        var e = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 11,
            TimestampUtc = new DateTime(2026, 10, 30, 0, 35, 57, DateTimeKind.Utc),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            AsduDetails = Asdu(46, 7, true),
            RawHex = "68 0D"
        };
        var projected = Iec10xCommandEventProjection.Project(e);
        Assert.NotNull(projected);
        Assert.Equal(11, projected!.EvidenceSequenceNumber);
        Assert.Contains("Command rejected", projected.NewValue);
        Assert.Equal("Command", projected.SignalType);
        Assert.Contains("PC ", projected.RelayTimeText);
        Assert.Contains("P/N", projected.CauseDetails!.Detail);
        Assert.DoesNotContain("physical", projected.NewValue, StringComparison.OrdinalIgnoreCase);
        var noAsdu = new Iec103MasterEvidenceEvent { ProtocolMode = Iec60870ProtocolMode.Iec101 };
        Assert.Null(Iec10xCommandEventProjection.Project(noAsdu));
    }
}
