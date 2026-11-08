// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class FrameByteInspectorProjectionTests
{
    private static EvidenceRow TimedSiq()
    {
        // Real FT1.2 source bytes from the existing production decoder.
        byte[] asdu =
        [
            0x1E, 0x01, 0x14, 0x00, 0x01, 0x00,
            0x6C, 0x00, 0x80, 0x01,
            0x2C, 0x6F, 0x27, 0x14, 0x1C, 0x0A, 0x1A
        ];
        var length = asdu.Length + 2;
        var raw = new byte[length + 6];
        raw[0] = 0x68; raw[1] = (byte)length;
        raw[2] = (byte)length; raw[3] = 0x68;
        raw[4] = 0x28; raw[5] = 0x69;
        asdu.CopyTo(raw, 6);
        raw[length + 4] = (byte)raw.Skip(4).Take(length).Sum(x => x);
        raw[length + 5] = 0x16;
        var frame = new Ft12Parser(1).Decode(raw);
        var decoded = new Iec10xAsduDecoder(2, 2, 3).Decode(asdu);
        Assert.True(frame.IsChecksumValid);
        Assert.True(frame.IsLengthValid);
        Assert.Empty(decoded.Issues);
        return new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            RawHex = string.Join(" ", raw.Select(x => x.ToString("X2"))),
            Frame = frame,
            AsduDetails = decoded
        });
    }

    [Fact]
    public void RealTi30ProvidesSelectableVerifiedOffsetRangesAndOriginalBytes()
    {
        var row = TimedSiq();
        var projection = FrameByteInspectorProjection.Create(row);
        Assert.Equal(25, projection.Bytes.Count);
        Assert.Empty(projection.Warning);
        Assert.Contains(projection.Fields, f => f.Key == "start-1" && f.Offset == 0 && f.Hex == "68");
        Assert.Contains(projection.Fields, f => f.Key == "length-1" && f.Offset == 1 && f.Hex == "13");
        Assert.Contains(projection.Fields, f => f.Key == "asdu-type" && f.Offset == 6 && f.Hex == "1E");
        Assert.Contains(projection.Fields, f => f.Key == "ca" && f.Hex == "01 00");
        Assert.Contains(projection.Fields, f => f.Key == "object-1" && f.Hex == "6C 00 80");
        Assert.Contains(projection.Fields, f => f.Key == "quality-1" && f.Hex == "01");
        var minute = Assert.Single(projection.Fields.Where(f => f.Key == "cp56-minute-1"));
        Assert.Contains("GEN=0 Genuine", minute.Meaning);
        Assert.Equal("27", minute.Hex);
        Assert.Equal(minute, projection.BestFieldAt(minute.Offset));
        Assert.Equal(minute.Count, minute.EndExclusive - minute.Offset);
        Assert.Contains("  27" + Environment.NewLine, projection.ExportSelectedField(minute));
        Assert.Contains("HEX  " + row.RawHex, projection.ExportAllFields());
        Assert.DoesNotContain("└─", projection.ExportAllFields());
    }

    [Fact]
    public void E5FrameDoesNotCreateFakeChecksumOrAsduFields()
    {
        var frame = new Ft12Parser(1).Decode([0xE5]);
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            RawHex = "E5", Frame = frame
        });
        var p = FrameByteInspectorProjection.Create(row);
        Assert.Single(p.Bytes);
        Assert.Contains(p.Fields, f => f.Name == "Short response");
        Assert.DoesNotContain(p.Fields, f => f.Key.Contains("checksum") || f.Key == "asdu-type");
        Assert.DoesNotContain("┴─", p.ExportAllFields());
    }

    [Fact]
    public void SourceByteMismatchWithholdsAllTypedFieldPositions()
    {
        var actual = new Ft12Parser(1).Decode(Convert.FromHexString("105B69C416"));
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            RawHex = "E5", Frame = actual
        });
        var p = FrameByteInspectorProjection.Create(row);
        Assert.Single(p.Bytes);
        Assert.Empty(p.Fields);
        Assert.Contains("differs", p.Warning);
        Assert.Equal("E5", p.ExportAllFields().Split('\n')[0].Replace("HEX  ", "").Trim());
    }

    [Theory]
    [InlineData("68 14 0Z 16")]
    [InlineData("-")]
    [InlineData("68 14 16 0")]
    public void MalformedHexCreatesNoInventedFields(string hex)
    {
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101, RawHex = hex
        });
        var p = FrameByteInspectorProjection.Create(row);
        Assert.Empty(p.Bytes);
        Assert.Empty(p.Fields);
        Assert.Contains("No verified", p.Warning);
    }
}
