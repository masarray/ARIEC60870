// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec101FrameByteInspectorTests
{
    [Fact]
    public void Fc11RequestMapsEachOnWireByteToItsActualMeaning()
    {
        var raw = Convert.FromHexString("105B6900C416");
        // Two-octet Link Address: 69 00 (the SICAM configuration in the screenshot).
        var frame = new Ft12Parser(2).Decode(raw);
        var fields = Iec101FrameByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Frame = frame
        });

        Assert.Equal("10", fields.Single(x => x.Key == "envelope").Hex);
        Assert.Equal("5B", fields.Single(x => x.Key == "control").Hex);
        Assert.Contains("FC=11", fields.Single(x => x.Key == "control").Meaning);
        Assert.Contains("FCV=1", fields.Single(x => x.Key == "control").Meaning);
        Assert.Equal("69 00", fields.Single(x => x.Key == "address").Hex);
        Assert.Contains("RTU 105", fields.Single(x => x.Key == "address").Meaning);
        Assert.Equal("C4 16", fields.Single(x => x.Key == "check").Hex);
        Assert.Contains("checksum OK", fields.Single(x => x.Key == "check").Meaning);
        Assert.DoesNotContain(fields, x => x.Key == "asdu-type");
    }

    [Fact]
    public void Fc9NoDataDoesNotMisreportClass1PendingAsReceivedData()
    {
        var frame = new Ft12Parser(1).Decode(Convert.FromHexString("1029699216"));
        var control = Iec101FrameByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Frame = frame
        }).Single(x => x.Key == "control");

        Assert.Equal("29", control.Hex);
        Assert.Contains("FC=9", control.Meaning);
        Assert.Contains("ACD=1", control.Meaning);
        Assert.Contains("Class 1 pending", control.Meaning);
        Assert.Contains("No requested data", control.Meaning);
    }

    [Fact]
    public void Type30ShowsExactOffsetsAndValuesWithNoFakeHexAddresses()
    {
        var asduBytes = new byte[] {
            0x1E, 0x01, 0x14, 0x00, 0x01, 0x00,
            0x6C, 0x00, 0x80, 0x01,
            0x2C, 0x6F, 0x27, 0x14, 0x1C, 0x0A, 0x1A
        };
        var l = asduBytes.Length + 2;
        var raw = new byte[l + 6];
        raw[0] = 0x68;
        raw[1] = (byte)l;
        raw[2] = (byte)l;
        raw[3] = 0x68;
        raw[4] = 0x28;
        raw[5] = 0x69;
        asduBytes.CopyTo(raw, 6);
        raw[l + 4] = (byte)raw.Skip(4).Take(l).Sum(b => b);
        raw[l + 5] = 0x16;

        var frame = new Ft12Parser(1).Decode(raw);
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(asduBytes);
        Assert.True(frame.IsChecksumValid);
        Assert.Empty(asdu.Issues);
        var fields = Iec101FrameByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Frame = frame,
            AsduDetails = asdu
        });
        Assert.Equal("68 13 13 68", fields.Single(x => x.Key == "envelope").Hex);
        Assert.Equal("28", fields.Single(x => x.Key == "control").Hex);
        Assert.Equal("1E", fields.Single(x => x.Key == "asdu-type").Hex);
        Assert.Equal("01", fields.Single(x => x.Key == "vsq").Hex);
        Assert.Equal("14 00", fields.Single(x => x.Key == "cot").Hex);
        Assert.Equal("01 00", fields.Single(x => x.Key == "ca").Hex);
        Assert.Equal("6C 00 80", fields.Single(x => x.Key == "object-1").Hex);
        var value = fields.Single(x => x.Key == "value-1");
        Assert.Equal("01 2C 6F 27 14 1C 0A 1A", value.Hex);
        Assert.Contains("IOA 8388716 = SP=ON", value.Meaning);
        Assert.Contains("2026-10-28 20:39:28.460", value.Meaning);
        Assert.All(fields.Where(x => x.Count > 0), f =>
            Assert.Equal(string.Join(" ", raw.Skip(f.Offset).Take(f.Count).Select(b => b.ToString("X2"))), f.Hex));
        Assert.DoesNotContain(fields, x => x.Hex.Contains("CA=", StringComparison.Ordinal));
    }

    [Fact]
    public void MultiObjectAsduPreservesIndividualRawByteGroups()
    {
        var asduBytes = new byte[] {
            0x01, 0x02, 0x03, 0x00, 0x01, 0x00,
            0x0A, 0x00, 0x00, 0x01,
            0x0B, 0x00, 0x00, 0x00
        };
        var l = asduBytes.Length + 2;
        var raw = new byte[l + 6];
        raw[0] = 0x68; raw[1] = (byte)l; raw[2] = (byte)l; raw[3] = 0x68;
        raw[4] = 0x08; raw[5] = 0x69;
        asduBytes.CopyTo(raw, 6);
        raw[l + 4] = (byte)raw.Skip(4).Take(l).Sum(b => b);
        raw[l + 5] = 0x16;

        var fields = Iec101FrameByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Frame = new Ft12Parser(1).Decode(raw),
            AsduDetails = new Iec10xAsduDecoder(2, 2, 3).Decode(asduBytes)
        });
        Assert.Equal("0A 00 00", fields.Single(x => x.Key == "object-1").Hex);
        Assert.Equal("01", fields.Single(x => x.Key == "value-1").Hex);
        Assert.Equal("0B 00 00", fields.Single(x => x.Key == "object-2").Hex);
        Assert.Equal("00", fields.Single(x => x.Key == "value-2").Hex);
    }

    [Fact]
    public void NoFabricatedFieldsForOtherProtocolsOrNoRawFrame()
    {
        Assert.Empty(Iec101FrameByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            Frame = new Ft12Parser(1).Decode(Convert.FromHexString("105B6900C416"))
        }));
        Assert.Empty(Iec101FrameByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101
        }));
    }
}
