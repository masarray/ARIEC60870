// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>Public synthetic vectors only: no customer/field-capture bytes.</summary>
public sealed class Iec10xCommandForensicsTests
{
    private readonly Iec10xAsduDecoder _decoder = new(cotSize: 2, caSize: 2, ioaSize: 3);

    [Fact]
    public void SelectAndRtuPositiveConfirmationPreserveSCOAndExactQualifierOffset()
    {
        var command = Iec10xAsduBuilder.SingleCommand(Settings(), ioa: 77, value: true, select: true, qualifier: 3);
        var sent = _decoder.Decode(command);

        Assert.NotNull(sent.CommandDetails);
        Assert.Equal(6, sent.CauseDetails!.Code);
        Assert.Equal(0, sent.CauseDetails.Raw & 0xC0);
        Assert.True(sent.CommandDetails!.Select == true);
        Assert.Equal("SELECT", sent.CommandDetails.Operation);
        Assert.Equal(3, sent.CommandDetails.QualifierValue);
        Assert.Equal(9, sent.CommandDetails.QualifierAsduOffset);
        Assert.Equal(9, sent.Objects[0].InformationElementOffset);
        Assert.Null(sent.Objects[0].QualityDetails);
        Assert.Contains("Quality=N/A", sent.Objects[0].ReadableSummary);
        Assert.Equal(6, sent.Objects[0].InformationObjectOffset);
        Assert.Equal((byte?)command[9], sent.CommandDetails.QualifierRaw);

        var positiveReply = (byte[])command.Clone();
        positiveReply[2] = 0x07; // COT 7 ACTCON
        var rt = _decoder.Decode(positiveReply);

        Assert.Equal(7, rt.CauseDetails!.Code);
        Assert.False(rt.CauseDetails.Negative);
        Assert.Contains("Activation confirmed", rt.CauseDetails.Outcome);
        Assert.Contains("physical operation NOT proven", rt.CauseDetails.Outcome);
        Assert.Equal(sent.CommandDetails.QualifierRaw, rt.CommandDetails!.QualifierRaw);
    }

    [Fact]
    public void NegativeCommandAnswerIsDistinctFromSuccessfulActivation()
    {
        var command = Iec10xAsduBuilder.DoubleCommand(Settings(), ioa: 123, dcs: 2, select: false, qualifier: 5);
        command[2] = 0x47; // COT7 with P/N=1 (negative confirmation)
        command[3] = 0x04; // OA=4
        var decoded = _decoder.Decode(command);

        Assert.True(decoded.CauseDetails!.Negative);
        Assert.False(decoded.CauseDetails.Test);
        Assert.Equal(4, decoded.CauseDetails.OriginatorAddress);
        Assert.Equal(7, decoded.CauseDetails.Code);
        Assert.Contains("NEGATIVE", decoded.CauseDetails.Outcome);
        Assert.Equal("EXECUTE", decoded.CommandDetails!.Operation);
        Assert.Equal(5, decoded.CommandDetails.QualifierValue);
        Assert.Contains("DCS=2", decoded.CommandDetails.ValueMeaning);
    }

    [Fact]
    public void TestFlagAndUnknownInformationObjectAddressRemainVisible()
    {
        var command = Iec10xAsduBuilder.SingleCommand(Settings(), ioa: 42, value: false, select: false);
        command[2] = 0xAF; // COT=47, P/N=0, TEST=1
        var decoded = _decoder.Decode(command);

        Assert.Equal(47, decoded.CauseDetails!.Code);
        Assert.True(decoded.CauseDetails.Test);
        Assert.False(decoded.CauseDetails.Negative);
        Assert.Contains("Unknown information object address", decoded.CauseDetails.Phase);
        Assert.Contains("Protocol error", decoded.CauseDetails.Outcome);
    }

    [Fact]
    public void ActivationTerminationIsSeparateFromActconAndProcessFeedback()
    {
        var cmd = Iec10xAsduBuilder.GeneralInterrogation(Settings());
        var decoded = _decoder.Decode(cmd);
        Assert.Equal((byte?)20, decoded.CommandDetails!.QualifierRaw);
        Assert.Equal("Station GI", decoded.CommandDetails.ValueMeaning);

        cmd[2] = 10;
        var terminated = _decoder.Decode(cmd);
        Assert.Equal("Activation termination", terminated.CauseDetails!.Phase);
        Assert.Contains("verify returned process state separately", terminated.CauseDetails.Outcome);
    }

    [Fact]
    public void DiqCannotRaiseOverflowBadgeButQdsCan()
    {
        var diq = Iec10xQualityDetails.Decode(31, new byte[] { 0x01 });
        Assert.NotNull(diq);
        Assert.Null(diq!.Overflow);
        Assert.Equal("Good", diq.DisplayText);
        Assert.Contains("OV=N/A", diq.BitDetails);

        var qds = Iec10xQualityDetails.Decode(13, new byte[] { 0, 0, 0, 0, 0x81 });
        Assert.NotNull(qds);
        Assert.True(qds!.Overflow == true);
        Assert.True(qds.Invalid);
        Assert.Equal(new[] { "INVALID", "OVF" }, qds.ActiveBadges);
    }

    [Fact]
    public void CounterCarryIsNotMeasurementOverflow()
    {
        var bcr = Iec10xQualityDetails.Decode(15, new byte[] { 1, 2, 3, 4, 0xA5 });
        Assert.NotNull(bcr);
        Assert.Null(bcr!.Overflow);
        Assert.True(bcr.Carry == true);
        Assert.Equal(5, bcr.CounterSequence);
        Assert.Contains("CARRY", bcr.ActiveBadges);
        Assert.DoesNotContain("OVF", bcr.ActiveBadges);
    }

    [Fact]
    public void SqSequenceObjectsRetainIndependentAsduOffsets()
    {
        byte[] asdu =
        {
            0x01, 0x82, 0x14, 0x00, 0x01, 0x00, // Type 1, 2 objects, COT20, CA1
            0x64, 0x00, 0x00, 0x01, 0x00       // IOA100 ON, IOA101 OFF
        };
        var d = _decoder.Decode(asdu);
        Assert.Equal(2, d.Objects.Count);
        Assert.Equal(9, d.Objects[0].InformationElementOffset);
        Assert.Equal(10, d.Objects[1].InformationElementOffset);
        Assert.Equal(-1, d.Objects[1].InformationObjectOffset); // SQ1: no additional IOA on wire
    }

    [Fact]
    public void SetpointSelectQualifierAndBitstringCommandRemainDecoded()
    {
        var setpoint = Iec10xAsduBuilder.SetpointNormalizedCommand(Settings(), ioa: 100, normalizedValue: 0.25, select: true, qualifier: 9);
        var decodedSetpoint = _decoder.Decode(setpoint);
        Assert.Equal("SELECT", decodedSetpoint.CommandDetails!.Operation);
        Assert.Equal(9, decodedSetpoint.CommandDetails.QualifierValue);
        Assert.Equal(11, decodedSetpoint.CommandDetails.QualifierAsduOffset);
        Assert.Equal(setpoint[11], decodedSetpoint.CommandDetails.QualifierRaw);

        byte[] bitstring =
        {
            51, 1, 6, 0, 1, 0, 123, 0, 0,
            0x78, 0x56, 0x34, 0x12
        };
        var decodedBits = _decoder.Decode(bitstring);
        Assert.Empty(decodedBits.Issues);
        Assert.NotNull(decodedBits.CommandDetails);
        Assert.Null(decodedBits.CommandDetails!.QualifierRaw);
        Assert.Contains("0x12345678", decodedBits.CommandDetails.ValueMeaning);
        Assert.Null(decodedBits.Objects[0].QualityDetails);
    }

    [Fact]
    public void TimeTaggedBitstringCommandCarriesCp56WithoutFalseQuality()
    {
        byte[] asdu =
        {
            64, 1, 7, 0, 1, 0, 5, 0, 0,
            0x78, 0x56, 0x34, 0x12, // 32-bit command
            0x7B, 0x00, 0x04, 0x0B, 0x1C, 0x0A, 0x1A // CP56Time2a
        };
        var decoded = _decoder.Decode(asdu);
        Assert.Empty(decoded.Issues);
        Assert.NotNull(decoded.CommandDetails);
        Assert.Equal(7, decoded.CauseDetails!.Code);
        Assert.Contains("2026-10-28", decoded.Objects[0].TimestampText);
        Assert.Contains("Quality=N/A", decoded.Objects[0].ReadableSummary);
    }

    private static Iec103MasterSettings Settings() => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec101,
        CauseOfTransmissionSize = 2,
        CommonAddressSize = 2,
        InformationObjectAddressSize = 3,
        CommonAddress = 1
    };
}
