// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec101TraceNarratorTests
{
    private static readonly DateTime PcTime = new(2026, 10, 28, 20, 39, 37, DateTimeKind.Utc);

    private static Iec103MasterEvidenceEvent LinkFrame(string hex, string dataClass)
    {
        var bytes = Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal));
        var frame = new Ft12Parser(1).Decode(bytes);
        Assert.True(frame.IsChecksumValid);
        return new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            TimestampUtc = PcTime,
            Direction = frame.LinkControl?.Prm == true
                ? FrameDirection.MasterToSlave
                : FrameDirection.SlaveToMaster,
            Frame = frame,
            LinkAddress = frame.LinkAddress,
            DataClass = dataClass,
            RawHex = hex
        };
    }

    [Theory]
    [InlineData("10 4B 69 B4 16", "FCB:0")]
    [InlineData("10 6B 69 D4 16", "FCB:1")]
    public void Class2RequestMatchesExactlyTheUsersThreeLinePattern(string hex, string fcb)
    {
        var f = LinkFrame(hex, "Class 2");
        var (title, meaning) = Iec101TraceNarrator.Describe(f);
        Assert.Contains("⇧ [", title);
        Assert.EndsWith("Class 2 request", title);
        // The 4B and 6B control bytes have FCV bit 4 CLEAR (FCV=0).
        // Preserve actual wire bits even if a pasted line-monitor example says FCV=1.
        Assert.Equal($"Data Link: Class 2 (Respond) [PRM:1,FCV:0] [{fcb}] Data Link Addr 105", meaning);
        var row = new EvidenceRow(f);
        Assert.Equal("Hex Frame: " + hex, row.ProtocolTraceRaw);
        Assert.Empty(row.ProtocolTraceMeta);
    }

    [Fact]
    public void Class2NoDataAndAcd1ShowsAvailableClass1WithoutClaimingDeliveredData()
    {
        var f = LinkFrame("10 29 69 92 16", "Class 2");
        var (title, meaning) = Iec101TraceNarrator.Describe(f);
        Assert.Contains("⇩ [", title);
        Assert.EndsWith("Class 2 empty · Class 1 pending", title);
        Assert.Contains("No requested data (FC9, not link NACK)", meaning);
        Assert.Contains("Class 1 pending", meaning);
    }

    [Fact]
    public void Class1RequestDecodesFcbAndNoIncorrectAcd()
    {
        var first = Iec101TraceNarrator.Describe(LinkFrame("10 5A 69 C3 16", "Class 1"));
        var next = Iec101TraceNarrator.Describe(LinkFrame("10 7A 69 E3 16", "Class 1"));
        Assert.EndsWith("Class 1 request", first.Title);
        Assert.Contains("FCB:0", first.Meaning);
        Assert.Contains("FCB:1", next.Meaning);
        Assert.DoesNotContain("ACD:", first.Meaning);
    }

    [Fact]
    public void UnknownPollContextMustNotFabricateClass()
    {
        var title = Iec101TraceNarrator.Describe(LinkFrame("10 29 69 92 16", "-")).Title;
        Assert.Contains("No requested data", title);
        Assert.DoesNotContain("Class 2 empty", title);
    }

    private static byte[] MakeVariableFrame(byte linkControl, byte linkAddress, byte[] asdu)
    {
        var count = asdu.Length + 2;
        var raw = new byte[count + 6];
        raw[0] = 0x68; raw[1] = (byte)count; raw[2] = (byte)count; raw[3] = 0x68;
        raw[4] = linkControl; raw[5] = linkAddress;
        asdu.CopyTo(raw, 6);
        var checksum = 0;
        for (var i = 4; i < count + 4; i++) checksum += raw[i];
        raw[count + 4] = (byte)checksum;
        raw[count + 5] = 0x16;
        return raw;
    }

    [Fact]
    public void SinglePointCp56ResponseShowsAsduCotOriginCaDiQualityTimeAndFullHex()
    {
        // An ACTUAL structurally valid Type 30 telegram, not the truncated
        // / bad-checksum hex in the screen-copy example.
        var asduBytes = new byte[] {
            0x1E, 0x01, 0x14, 0x00, 0x01, 0x00,
            0x6C, 0x00, 0x80, 0x01,
            0x2C, 0x6F, 0x27, 0x14, 0x1C, 0x0A, 0x1A
        };
        var raw = MakeVariableFrame(0x28, 0x69, asduBytes);
        var frame = new Ft12Parser(1).Decode(raw);
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(asduBytes);
        Assert.True(frame.IsChecksumValid);
        Assert.True(frame.IsLengthValid);
        Assert.Empty(asdu.Issues);
        var e = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            TimestampUtc = PcTime,
            Direction = FrameDirection.SlaveToMaster,
            Frame = frame,
            LinkAddress = 105,
            AsduDetails = asdu,
            RawHex = Convert.ToHexString(raw),
            DataClass = "Class 1"
        };
        var row = new EvidenceRow(e);
        Assert.EndsWith("GI data · Single-point with date/time", row.ProtocolTraceTitle);
        Assert.Contains("Data Link: User Data [PRM:0,ACD:1,DFC:0] Length 19 Data Link Addr 105", row.ProtocolTraceMeaning);
        Assert.Contains("ASDU: Type ID 30 <Single-point with date/time> Count 1", row.ProtocolTraceMeaning);
        Assert.Contains("Cause of transmission: 20 (interrogated by station interrogation) | Origin: 0", row.ProtocolTraceMeaning);
        Assert.Contains("Comm Addr 1", row.ProtocolTraceMeaning);
        Assert.Contains("DI 8388716 1 [Valid] 10/28/26 20:39:28.460 [Valid time,Standard time]", row.ProtocolTraceMeaning);
        Assert.StartsWith("Hex Frame: 68 13 13 68 28 69", row.ProtocolTraceRaw);
        Assert.EndsWith(" 16", row.ProtocolTraceRaw);
    }

    [Fact]
    public void AllObjectsInSameAsduArePrintedWithinOneTraceBlock()
    {
        var asdu = new Iec10xAsduDecoder().Decode(new byte[] {
            1, 2, 3, 0, 1, 0,
            10, 0, 0, 1,
            11, 0, 0, 0
        });
        var f = LinkFrame("10 29 69 92 16", "Class 1");
        var e = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            TimestampUtc = PcTime,
            Direction = FrameDirection.SlaveToMaster,
            Frame = new Ft12FrameDecode
            {
                LinkControl = new LinkControlInfo
                {
                    Prm = false,
                    FunctionCode = 8,
                    Acd = true,
                    Dfc = false
                },
                LinkAddress = 105,
                IsChecksumValid = true,
                IsLengthValid = true
            },
            AsduDetails = asdu
        };
        var block = Iec101TraceNarrator.Describe(e).Meaning;
        Assert.Contains("DI 10 1 [Valid]", block);
        Assert.Contains("DI 11 0 [Valid]", block);
    }

    [Fact]
    public void LinkAckAndE5NeverProvePhysicalCommandOperation()
    {
        var ack = LinkFrame("10 00 69 69 16", "Link");
        var title = Iec101TraceNarrator.Describe(ack);
        Assert.Contains("Link ACK response", title.Title);
        Assert.Contains("not proven", title.Meaning);

        var e5 = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            TimestampUtc = PcTime,
            Frame = new Ft12Parser(1).Decode(new byte[] { 0xE5 })
        };
        var e5Text = Iec101TraceNarrator.Describe(e5);
        Assert.Contains("Single-character response (E5)", e5Text.Title);
        Assert.Contains("Match preceding request", e5Text.Meaning);
    }

    [Fact]
    public void PassiveNegativeControlShowsCauseAndSourceInTraceWithoutReparsingHex()
    {
        var asduBytes = new byte[] { 46, 1, 0x47, 0, 1, 0, 123, 0, 0, 0x82 };
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(asduBytes);
        var raw = MakeVariableFrame(0x08, 0x69, asduBytes);
        var frame = new Ft12Parser(1).Decode(raw);
        Assert.True(frame.IsChecksumValid);
        var item = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            TimestampUtc = PcTime,
            PhysicalSource = "Link A",
            Frame = frame,
            LinkAddress = 105,
            AsduDetails = asdu,
            CommandDetails = asdu.CommandDetails,
            TypeId = asdu.TypeId,
            CauseOfTransmission = asdu.CauseOfTransmission,
            CauseDetails = asdu.CauseDetails,
            CommonAddressNumber = asdu.CommonAddress,
            InformationObjectAddress = asdu.FirstObject?.InformationObjectAddress,
            ControlObservation = "RTU NEGATIVE CONFIRMATION (P/N=1, COT=7)"
        };
        var row = new EvidenceRow(item);
        Assert.Contains("Command rejected · negative ACTCON", row.ProtocolTraceTitle);
        Assert.Equal("NEG  ·  SELECT  ·  COT 7  ·  CA 1 / IOA 123", row.ProtocolTraceCompactMeaning);
        Assert.Contains("Cause of transmission: 7", row.ProtocolTraceMeaning);
        Assert.Contains("Control sequence: RTU NEGATIVE CONFIRMATION", row.ProtocolTraceMeaning);
        Assert.Contains("Hex Frame:", row.ProtocolTraceRaw);
        Assert.DoesNotContain("physical operation confirmed", row.ProtocolTraceMeaning);
    }

    [Fact]
    public void MalformedUserSampleMustNotBeFalselyMarkedValid()
    {
        var provided = Convert.FromHexString(
            "6814146808691E01140001006C00800164FCCD4B1E1A1A8516");
        var frame = new Ft12Parser(1).Decode(provided);
        Assert.False(frame.IsLengthValid && frame.IsChecksumValid);
        var title = Iec101TraceNarrator.Describe(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Frame = frame,
            Direction = FrameDirection.SlaveToMaster
        }).Title;
        Assert.Contains("Invalid FT1.2 frame", title);
    }
}
