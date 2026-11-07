// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class ForensicTraceLayoutTests
{
    private static readonly byte[] FloatFrame = Convert.FromHexString(
        "681111682869000D0114000100AE0F0C9A994941003A16");

    private static EvidenceRow Row(byte[] raw, Iec10xAsduDecode? asdu = null)
    {
        var frame = new Ft12Parser(2).Decode(raw);
        return new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            TimestampUtc = new DateTime(2026, 11, 8, 5, 4, 3, DateTimeKind.Utc),
            Direction = FrameDirection.SlaveToMaster,
            Frame = frame,
            LinkAddress = 105,
            AsduDetails = asdu,
            RawHex = Convert.ToHexString(raw),
            DataClass = "Class 1"
        });
    }

    [Fact]
    public void OriginalFloatFrameShowsAcdAndEveryApplicableQualityBit()
    {
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(
            FloatFrame.Skip(7).Take(FloatFrame.Length - 9).ToArray());
        Assert.Empty(asdu.Issues);
        var view = ForensicTraceLayout.Create(Row(FloatFrame, asdu));
        Assert.Contains("ACD          : 1", view.LinkLayer);
        Assert.Contains("Type ID      : 13", view.AsduHeader);
        Assert.Contains("IOA          : 790446", view.InformationObjects);
        Assert.Contains("OV           : 0", view.InformationObjects);
        Assert.Contains("IV           : 0", view.InformationObjects);
        Assert.Contains("Checksum test: PASS", view.Validation);
        Assert.Contains("Calculated   : 0x3A", view.Validation);
    }

    [Fact]
    public void CorruptedChecksumNeverPromotesUntrustedPayload()
    {
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(
            FloatFrame.Skip(7).Take(FloatFrame.Length - 9).ToArray());
        var corrupted = (byte[])FloatFrame.Clone();
        corrupted[^2] ^= 0x01;
        var view = ForensicTraceLayout.Create(Row(corrupted, asdu));
        Assert.Contains("Checksum test: FAIL", view.Validation);
        Assert.Contains("Unverified", view.AsduHeader);
        Assert.DoesNotContain("790446", view.InformationObjects);
    }


    [Fact]
    public void StructuredFlagQueryFindsSecondPhysicalObject()
    {
        // Two real IEC-101 objects; only the second contains OV=1 and IV=1.
        var payload = new byte[]
        {
            0x0D, 0x02, 0x14, 0x00, 0x01, 0x00,
            0x0A, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F, 0x00,
            0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x81
        };
        var l = payload.Length + 3;
        var bytes = new byte[l + 6];
        bytes[0] = 0x68; bytes[1] = (byte)l; bytes[2] = (byte)l;
        bytes[3] = 0x68; bytes[4] = 0x28;
        bytes[5] = 0x69; bytes[6] = 0;
        payload.CopyTo(bytes, 7);
        bytes[^2] = (byte)(bytes.Skip(4).Take(l).Sum(b => b) & 0xFF);
        bytes[^1] = 0x16;
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(payload);
        Assert.Empty(asdu.Issues);
        var row = Row(bytes, asdu);
        var view = ForensicTraceLayout.Create(row);
        Assert.Contains("OBJECT #2", view.InformationObjects);
        Assert.Contains("IOA raw      : 0A 00 00", view.InformationObjects);
        Assert.Contains("IOA raw      : 0B 00 00", view.InformationObjects);
        Assert.Contains("#2 OV=1", view.ActiveFlags);
        Assert.True(ForensicTraceLayout.MatchFlag(row, "OV=1") == true);
        Assert.True(ForensicTraceLayout.MatchFlag(row, "OV=0") == true);
        Assert.True(ForensicTraceLayout.MatchFlag(row, "IV=1") == true);
        Assert.Null(ForensicTraceLayout.MatchFlag(row, "unrecognized"));
    }

    [Fact]
    public void NegativeCotFlagMatchesActualSource()
    {
        var bytes = (byte[])FloatFrame.Clone();
        bytes[9] = 0x54; // P/N=1, COT=20, TEST=0
        bytes[^2] = (byte)(bytes.Skip(4).Take(bytes.Length - 6).Sum(b => b) & 0xFF);
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(
            bytes.Skip(7).Take(bytes.Length - 9).ToArray());
        var row = Row(bytes, asdu);
        Assert.True(ForensicTraceLayout.MatchFlag(row, "NEG=1") == true);
        Assert.Contains("NEG          : 1", ForensicTraceLayout.Create(row).AsduHeader);
    }

    [Fact]
    public void NoQualityDescriptorIsNeverFabricatedAsGood()
    {
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 100, TypeName = "C_IC_NA_1", ObjectCount = 1,
            Objects = new[] {new Iec10xInformationObject
            {
                Index = 1, InformationObjectAddress = 0, ValueText = "QOI 20"
            }}
        };
        var row = Row(FloatFrame, asdu);
        Assert.False(ForensicTraceLayout.MatchFlag(row, "IV=0") == true);
        Assert.False(ForensicTraceLayout.MatchFlag(row, "OV=0") == true);
    }
}
