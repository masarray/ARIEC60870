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
    public void Class2EmptyAndRoutineRequestAreReadableButDeEmphasizedAndNeverRemovedFromFrameModel()
    {
        var poll = Row(Convert.FromHexString("105B6900C416"), true);
        var empty = Row(Convert.FromHexString("100969007216"), false);
        Assert.Equal("Routine", poll.TraceVisualClass);
        Assert.Equal("Routine", empty.TraceVisualClass);
        Assert.Equal("#425D79", poll.TraceInk);
        Assert.Equal("#3F6551", empty.TraceInk);
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
    [Fact]
    public void EveryApplicableProcessQualityAlarmGetsAttentionAndStructuredSearch()
    {
        foreach (var (rawQuality, predicate) in new (byte Quality, string Search)[]
        {
            (0x01, "OV=1"), (0x10, "BL=1"), (0x20, "SB=1"),
            (0x40, "NT=1"), (0x80, "IV=1")
        })
        {
            var raw = (byte[])FloatFrame.Clone();
            raw[^3] = rawQuality;
            raw[^2] = (byte)(raw.Skip(4).Take(raw.Length - 6).Sum(b => b) & 255);
            var row = Row(raw, false, decodeAsdu: true);
            Assert.True(row.Source.Frame!.IsChecksumValid);
            Assert.Empty(row.Source.AsduDetails!.Issues);
            Assert.Equal("Attention", row.TraceVisualClass);
            Assert.True(ForensicTraceLayout.MatchFlag(row, predicate));
            Assert.True(QuickFindMatcher.MatchesTrace(row, "", "Negative / errors"));
            Assert.Contains(predicate, row.TraceForensic.ActiveFlags);
            Assert.Equal(Convert.ToHexString(raw), row.RawHex);
        }
    }

    [Fact]
    public void BcrCarryAndAdjustedAreNotTreatedAsQuietValidMeasurements()
    {
        var payload = new byte[]
        {
            15, 1, 3, 0, 1, 0, 44, 0, 0,
            1, 0, 0, 0, 0xE1 // BCR SQ=1; carry, adjusted and invalid
        };
        var l = payload.Length + 3;
        var raw = new byte[l + 6];
        raw[0] = 0x68; raw[1] = (byte)l; raw[2] = (byte)l;
        raw[3] = 0x68; raw[4] = 0x28; raw[5] = 0x69; raw[6] = 0;
        payload.CopyTo(raw, 7);
        raw[^2] = (byte)(raw.Skip(4).Take(l).Sum(b => b) & 255);
        raw[^1] = 0x16;
        var row = Row(raw, false, decodeAsdu: true);
        Assert.Empty(row.Source.AsduDetails!.Issues);
        Assert.Equal("Attention", row.TraceVisualClass);
        Assert.True(ForensicTraceLayout.MatchFlag(row, "CY=1"));
        Assert.True(ForensicTraceLayout.MatchFlag(row, "ADJ=1"));
        Assert.Contains("#1 CY=1", row.TraceForensic.ActiveFlags);
        Assert.Contains("#1 BCR CA=1", row.TraceForensic.ActiveFlags);
        Assert.Null(ForensicTraceLayout.MatchFlag(row, "CA=1"));
    }

    [Fact]
    public void Cp56CalendarAndReservedBitAnomaliesAreAttentionEvenIfTimeIvBitIsClear()
    {
        var raw = Convert.FromHexString(
            "681414682869001E01140001000604FF016C043814070B1AB716");
        raw[21] |= 0xE0; // DOW=7 (Sunday), but November 7 2026 is Saturday
        raw[22] |= 0x10; // unexpected month reserved bit
        raw[^2] = (byte)(raw.Skip(4).Take(raw.Length - 6).Sum(b => b) & 255);
        var row = Row(raw, false, decodeAsdu: true);
        Assert.Empty(row.Source.AsduDetails!.Issues);
        Assert.Equal("Attention", row.TraceVisualClass);
        Assert.True(ForensicTraceLayout.MatchFlag(row, "TIMEWARN=1"));
        Assert.True(ForensicTraceLayout.MatchFlag(row, "TIV=0"));
        Assert.Contains("TIME DOW mismatch", row.TraceForensic.ActiveFlags);
        Assert.Contains("TIME reserved bits", row.TraceForensic.ActiveFlags);
    }

    [Fact]
    public void COTUnknownAddressErrorIsNotConsideredNormalUserData()
    {
        var raw = (byte[])FloatFrame.Clone();
        raw[9] = 46; // IEC 60870 cause 46: unknown common address
        raw[^2] = (byte)(raw.Skip(4).Take(raw.Length - 6).Sum(b => b) & 255);
        var row = Row(raw, false, decodeAsdu: true);
        Assert.Equal("Attention", row.TraceVisualClass);
        Assert.Contains("COT=46", row.TraceForensic.ActiveFlags);
        Assert.True(QuickFindMatcher.MatchesTrace(row, "", "Negative / errors"));
    }

}
