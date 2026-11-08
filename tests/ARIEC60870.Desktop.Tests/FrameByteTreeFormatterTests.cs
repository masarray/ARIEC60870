// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class FrameByteTreeFormatterTests
{
    private static EvidenceRow TimedSiq()
    {
        // Source-backed TI30 fixture (not the user's illustrative, potentially
        // inconsistent hex example). Length and checksum are computed from
        // the actual ASDU, and the existing production decoder owns semantics.
        byte[] asduBytes =
        [
            0x1E, 0x01, 0x14, 0x00, 0x01, 0x00,
            0x6C, 0x00, 0x80, 0x01,
            0x2C, 0x6F, 0x27, 0x14, 0x1C, 0x0A, 0x1A
        ];
        var length = asduBytes.Length + 2;
        var raw = new byte[length + 6];
        raw[0] = 0x68; raw[1] = (byte)length;
        raw[2] = (byte)length; raw[3] = 0x68;
        raw[4] = 0x28; raw[5] = 0x69;
        asduBytes.CopyTo(raw, 6);
        raw[length + 4] = (byte)raw.Skip(4).Take(length).Sum(x => x);
        raw[length + 5] = 0x16;
        var frame = new Ft12Parser(1).Decode(raw);
        var decoded = new Iec10xAsduDecoder(2, 2, 3).Decode(asduBytes);
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
    public void RealTi30MapsPhysicalByteOffsetsWithoutFabricatingFieldValues()
    {
        var row = TimedSiq();
        var diagram = FrameByteTreeFormatter.Format(row);
        Assert.Contains("HEX  68 13 13 68 28 69 1E 01", diagram);
        Assert.Contains("Start #1 (0x68)", diagram);
        Assert.Contains("Length #1: 19 bytes", diagram);
        Assert.Contains("Length #2: 19 bytes", diagram);
        Assert.Contains("Type ID: 30", diagram);
        Assert.Contains("Common address: CA 1", diagram);
        Assert.Contains("IOA 8388716", diagram);
        Assert.Contains("SIQ:", diagram);
        Assert.Contains("CP56 minute: 39", diagram);
        Assert.Contains("GEN=0 Genuine", diagram);
        Assert.Contains("CP56 year: 2026", diagram);
        Assert.Contains("Checksum:", diagram);
        Assert.Contains("End character: 0x16", diagram);
        Assert.Contains("└─ ", diagram);
        Assert.DoesNotContain("RTU 0", diagram);
    }

    [Fact]
    public void SingleCharacterE5HasNoInventedAsduOrChecksumOctet()
    {
        var frame = new Ft12Parser(1).Decode([0xE5]);
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            RawHex = "E5",
            Frame = frame
        });
        var diagram = FrameByteTreeFormatter.Format(row);
        Assert.Contains("HEX  E5", diagram);
        Assert.DoesNotContain("Checksum:", diagram);
        Assert.DoesNotContain("Common address:", diagram);
        Assert.DoesNotContain("CP56", diagram);
    }

    [Fact]
    public void MismatchedRenderedHexNeverBorrowsTypedOffsetsFromAnotherFrame()
    {
        var otherFrame = new Ft12Parser(1).Decode(Convert.FromHexString("105B69C416"));
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            RawHex = "E5",
            Frame = otherFrame
        });
        var diagram = FrameByteTreeFormatter.Format(row);
        Assert.Contains("source frame bytes disagree", diagram);
        Assert.DoesNotContain("Link control:", diagram);
        Assert.DoesNotContain("Link address:", diagram);
    }

    [Fact]
    public void MissingAndMalformedHexDoNotCreateAnInventedByteDiagram()
    {
        var malformed = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            RawHex = "68 14 0Z 16"
        });
        var diagram = FrameByteTreeFormatter.Format(malformed);
        Assert.Contains("no verified byte sequence", diagram);
        Assert.DoesNotContain("└─", diagram);
    }
}
