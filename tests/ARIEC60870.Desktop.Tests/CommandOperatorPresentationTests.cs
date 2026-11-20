// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class CommandOperatorPresentationTests
{
    private static Iec10xCommandCasebook.CommandCase Case(string status, string phase) => new(
        7, "IEC-101", "LINK B", 105, 105, 68542, 46,
        "DCS=2 (ON)", status, "Unverified",
        20, 23, DateTime.UtcNow,
        new[] { new Iec10xCommandCasebook.CommandStage(
            23, "fixture:23", DateTime.UtcNow, "RX", phase, 7, "Observed", "68 00") },
        1671);

    [Fact]
    public void OverlappingCommandsShowSpecificHumanNextActionWithoutSuccessClaim()
    {
        var c = Case("Ambiguous", "SELECT overlapping");
        Assert.StartsWith("Needs review", CommandCaseOperatorPresentation.Status(c));
        var guidance = CommandCaseOperatorPresentation.Guidance(c);
        Assert.Contains("Repeated or overlapping", guidance);
        Assert.Contains("Stop repeating commands", guidance);
        Assert.DoesNotContain("successful", guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT accepted", "RTU accepted SELECT", "Execute only")]
    [InlineData("EXECUTE accepted", "RTU accepted EXECUTE", "Await ACTTERM")]
    [InlineData("ACTTERM observed", "Protocol ended", "physical movement is not proven")]
    [InlineData("RTU rejected", "RTU rejected", "interlocks")]
    public void OperatorsCanDistinguishAcceptanceCompletionAndRejection(
        string status, string label, string guidance)
    {
        var c = Case(status, "RTU observation");
        Assert.Contains(label, CommandCaseOperatorPresentation.Status(c));
        Assert.Contains(guidance, CommandCaseOperatorPresentation.Guidance(c),
            StringComparison.OrdinalIgnoreCase);
    }

    private static Iec103MasterEvidenceEvent Frame(byte cot, byte qualifier,
        FrameDirection direction)
    {
        var decode = new Iec10xAsduDecoder(2, 2, 3).Decode(
            new byte[] { 46, 1, cot, 0, 105, 0, 0xBE, 0x0B, 0x01, qualifier });
        Assert.Empty(decode.Issues);
        return new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 12,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = direction, TimestampUtc = DateTime.UtcNow,
            AsduDetails = decode, TypeId = decode.TypeId,
            CommonAddressNumber = decode.CommonAddress,
            InformationObjectAddress = decode.InformationObjectAddress,
            CauseDetails = decode.CauseDetails, RawHex = "68 fixture"
        };
    }

    [Fact]
    public void ShortEventCellsDistinguishSelectExecuteAndCorrelatedRtus()
    {
        var select = Frame(6, 0x82, FrameDirection.MasterToSlave);
        var selectRow = new RelayEventRow(
            Iec10xCommandEventProjection.Project(select, 7, "Feeder CB", "SELECT sent")!);
        Assert.StartsWith("SELECT sent", selectRow.CompactDisplayValue);
        Assert.Contains("ON", selectRow.CompactDisplayValue);

        var accepted = Frame(7, 0x82, FrameDirection.SlaveToMaster);
        var acceptedRow = new RelayEventRow(
            Iec10xCommandEventProjection.Project(accepted, 7, "Feeder CB", "SELECT ACTCON")!);
        Assert.StartsWith("SELECT accepted", acceptedRow.CompactDisplayValue);

        var execute = Frame(6, 0x02, FrameDirection.MasterToSlave);
        var executeRow = new RelayEventRow(
            Iec10xCommandEventProjection.Project(execute, 7, "Feeder CB", "EXECUTE sent")!);
        Assert.StartsWith("EXECUTE sent", executeRow.CompactDisplayValue);

        var executeReply = Frame(7, 0x02, FrameDirection.SlaveToMaster);
        var executeReplyRow = new RelayEventRow(
            Iec10xCommandEventProjection.Project(executeReply, 7, "Feeder CB", "EXECUTE ACTCON")!);
        Assert.StartsWith("EXECUTE accepted", executeReplyRow.CompactDisplayValue);
        Assert.DoesNotContain("CB operated", executeReplyRow.CompactDisplayValue);

        var unpaired = new RelayEventRow(Iec10xCommandEventProjection.Project(accepted)!);
        Assert.Contains("unpaired", unpaired.CompactDisplayValue);
        Assert.DoesNotContain("accepted", unpaired.CompactDisplayValue);
    }

    [Fact]
    public void AmbiguousNegativeAndTestCommandNeverGetPositiveVerdicts()
    {
        var reply = Frame(7, 0x02, FrameDirection.SlaveToMaster);
        var mismatch = new RelayEventRow(
            Iec10xCommandEventProjection.Project(reply, 7, null, "RTU reply ambiguous")!);
        Assert.StartsWith("Reply mismatch", mismatch.CompactDisplayValue);
        Assert.DoesNotContain("accepted", mismatch.CompactDisplayValue);

        var negative = Frame(0x47, 0x82, FrameDirection.SlaveToMaster);
        var rejected = new RelayEventRow(Iec10xCommandEventProjection.Project(negative)!);
        Assert.StartsWith("Command rejected", rejected.CompactDisplayValue);

        var test = Frame(0x87, 0x82, FrameDirection.SlaveToMaster);
        var testEntry = Iec10xCommandEventProjection.Project(test);
        Assert.NotNull(testEntry);
        Assert.Contains("TEST (non-operational)", testEntry!.NewValue);
    }
}
