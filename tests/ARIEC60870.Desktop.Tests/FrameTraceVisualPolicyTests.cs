// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class FrameTraceVisualPolicyTests
{
    private static readonly byte[] FloatFrame = Convert.FromHexString(
        "681111682869000D0114000100AE0F0C9A994941003A16");

    private static EvidenceRow Row(byte[] raw, bool isTx, bool decodeAsdu = false)
    {
        var frame = new Ft12Parser(2).Decode(raw);
        var asdu = decodeAsdu
            ? new Iec10xAsduDecoder(2, 2, 3).Decode(raw.Skip(7).Take(raw.Length - 9).ToArray())
            : null;
        return new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = isTx ? FrameDirection.MasterToSlave : FrameDirection.SlaveToMaster,
            RawHex = Convert.ToHexString(raw),
            TimestampUtc = DateTime.UtcNow,
            Frame = frame,
            AsduDetails = asdu,
            LinkAddress = 105
        });
    }

    [Fact]
    public void Class2EmptyAndRoutineRequestAreMutedButNeverRemovedFromFrameModel()
    {
        var poll = Row(Convert.FromHexString("105B6900C416"), true);
        var empty = Row(Convert.FromHexString("100969007216"), false);
        Assert.Equal("Routine", poll.TraceVisualClass);
        Assert.Equal("Routine", empty.TraceVisualClass);
        Assert.Equal("#8497A8", poll.TraceInk);
        Assert.Equal("#849D8E", empty.TraceInk);
        Assert.NotEmpty(poll.RawHex);
        Assert.NotEmpty(empty.RawHex);
    }

    [Fact]
    public void ACDPendingIsAttentionEvenWhenClass2ResponseIsEmpty()
    {
        var row = Row(Convert.FromHexString("102969009216"), false);
        Assert.Equal("Attention", row.TraceVisualClass);
        Assert.Equal("#18583B", row.TraceInk);
    }

    [Fact]
    public void GiMeasurementAndMalformedPhysicalFrameKeepFullDirectionAndRawBytes()
    {
        var gi = Row(FloatFrame, false, decodeAsdu: true);
        Assert.Equal("GI", gi.TraceVisualClass);
        Assert.Equal("#18583B", gi.TraceInk);
        Assert.Contains("9A994941", gi.RawHex);

        var corrupted = (byte[])FloatFrame.Clone();
        corrupted[^2] ^= 1;
        var invalid = Row(corrupted, false);
        Assert.Equal("Attention", invalid.TraceVisualClass);
        Assert.Equal("#18583B", invalid.TraceInk);
        Assert.Equal(Convert.ToHexString(corrupted), invalid.RawHex);
    }

    [Fact]
    public void UnknownUnparsedWireIsNeverClassifiedAsRoutineOrHidden()
    {
        var invalid = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            TimestampUtc = DateTime.UtcNow,
            RawHex = "681100"
        });
        Assert.Equal("Unknown", invalid.TraceVisualClass);
        Assert.Equal("#18583B", invalid.TraceInk);
        Assert.Equal("681100", invalid.RawHex);
    }
}
