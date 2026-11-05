// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Core.Tests;

/// <summary>Cross-family parsing exercised from Core.Tests without a Master dependency.</summary>
public sealed class CanonicalWireCoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Ft12ValidatesEveryLegalLinkWidthUsingOneEnvelopeParser(int width)
    {
        var raw = new List<byte> { 0x10, 0x49 };
        for (var i = 0; i < width; i++) raw.Add((byte)(i == 0 ? 0x69 : 0x01));
        raw.Add((byte)(raw.Skip(1).Sum(x => x) & 0xFF));
        raw.Add(0x16);
        var bytes = raw.ToArray();
        Assert.True(Ft12WireLayout.TryParse(bytes, width, out var layout));
        Assert.Equal(Ft12WireKind.Fixed, layout.Kind);
        Assert.Equal(0x49, layout.Control);
        Assert.Equal(width == 0 ? null : width == 1 ? 105 : 361, layout.LinkAddress);
        var decoded = Ft12Parser.ForIec101(width).Decode(bytes);
        Assert.True(decoded.IsChecksumValid);
        Assert.Equal(Ft12FrameFormat.FixedLength, decoded.Format);
        Assert.Equal(bytes, decoded.RawBytes);
        bytes[^2] ^= 1;
        Assert.False(Ft12WireLayout.TryParse(bytes, width, out _));
    }

    [Fact]
    public void Iec101AndIec104ShareOneAsduInterpretationWhileIec103RemainsDistinct()
    {
        // One M_DP_TB_1 CP56 object, spontaneous, CA=105, IOA=100.
        byte[] asdu = [31, 1, 3, 0, 105, 0, 100, 0, 0, 2,
            0x2A, 0xAB, 0x04, 0x11, 0x85, 0x0B, 0x1A];
        var payload = new byte[] { 0x08, 0x69, 0x00 }.Concat(asdu).ToArray();
        var serial = new byte[] { 0x68, (byte)payload.Length, (byte)payload.Length, 0x68 }
            .Concat(payload).Concat([(byte)(payload.Sum(x => x) & 255), 0x16]).ToArray();
        Assert.True(Ft12WireLayout.TryParse(serial, 2, out var wire));
        Assert.Equal(7, wire.AsduOffset);
        var parsed = Ft12Parser.ForIec101(2).Decode(serial);
        Assert.True(parsed.IsChecksumValid);
        Assert.Null(parsed.Asdu); // Never decode IEC101 ASDU as IEC103 FUN/INF.
        Assert.Equal(asdu, parsed.AsduBytes);
        var from101 = new Iec10xAsduDecoder().Decode(parsed.AsduBytes);
        var tcp = new byte[] { 0x68, (byte)(4 + asdu.Length), 0, 0, 0, 0 }
            .Concat(asdu).ToArray();
        var from104 = new Iec104ApduParser().Decode(tcp);
        Assert.True(from104.IsValid);
        Assert.NotNull(from104.Asdu);
        Assert.Equal(from101.TypeId, from104.Asdu!.TypeId);
        Assert.Equal(from101.CauseOfTransmission, from104.Asdu.CauseOfTransmission);
        Assert.Equal(from101.CommonAddress, from104.Asdu.CommonAddress);
        Assert.Equal(from101.Objects[0].InformationObjectAddress,
            from104.Asdu.Objects[0].InformationObjectAddress);
        Assert.Equal(from101.Objects[0].ValueText, from104.Asdu.Objects[0].ValueText);
        Assert.Equal(from101.Objects[0].TimestampText, from104.Asdu.Objects[0].TimestampText);
        Assert.Empty(from101.Issues);
        Assert.Empty(from104.Asdu.Issues);

        var iec103Asdu = new byte[] { 8, 1, 10, 1, 0, 0, 0 };
        var body103 = new byte[] { 8, 1 }.Concat(iec103Asdu).ToArray();
        var wire103 = new byte[] { 0x68, (byte)body103.Length, (byte)body103.Length, 0x68 }
            .Concat(body103).Concat([(byte)(body103.Sum(x => x) & 255), 0x16]).ToArray();
        var native103 = Ft12Parser.ForIec103(1).Decode(wire103);
        Assert.NotNull(native103.Asdu);
        Assert.Equal(8, native103.Asdu!.TypeId);
        Assert.Equal("General Interrogation End", native103.Asdu.TypeName);
        Assert.Null(Ft12Parser.ForIec101(1).Decode(wire103).Asdu);
    }

    [Fact]
    public void CommonHeaderPreservesNegativeTestOriginatorAndRawCot()
    {
        byte[] raw = [31, 1, 0xC3, 17, 0x34, 0x12, 1, 0, 0,
            2, 0x2A, 0xAB, 0x04, 0x11, 0x85, 0x0B, 0x1A];
        Assert.True(Iec10xAsduHeader.TryParse(raw, 2, 2, out var header));
        Assert.Equal(31, header.TypeId);
        Assert.Equal(3, header.Cause);
        Assert.True(header.IsNegative);
        Assert.True(header.IsTest);
        Assert.Equal(17, header.OriginatorAddress);
        Assert.Equal(0x1234, header.CommonAddress);
        Assert.Equal(6, header.ObjectsOffset);
        var decode = new Iec10xAsduDecoder().Decode(raw);
        Assert.True(decode.IsNegativeConfirm);
        Assert.True(decode.IsTest);
        Assert.Equal(header.CommonAddress, decode.CommonAddress);
        Assert.False(Iec10xAsduHeader.TryParse(raw.AsSpan(0, 5), 2, 2, out _));
        Assert.False(Iec10xAsduHeader.TryParse(raw, 3, 2, out _));
    }

    [Fact]
    public void CorruptedAndTruncatedWireNeverGetTrustedLayout()
    {
        byte[] body = [0x28, 0x69, 0x00, 31, 1, 3, 0, 105, 0, 0, 0, 0, 2,
            0x2A, 0xAB, 0x04, 0x11, 0x85, 0x0B, 0x1A];
        var raw = new byte[] { 0x68, (byte)body.Length, (byte)body.Length, 0x68 }
            .Concat(body).Concat([(byte)(body.Sum(x => x) & 255), 0x16]).ToArray();
        Assert.True(Ft12WireLayout.TryParse(raw, 2, out _));
        Assert.False(Ft12WireLayout.TryParse(raw.AsSpan(0, raw.Length - 1), 2, out _));
        Assert.True(Ft12WireLayout.TryParse(raw, 1, out _)); // checksum alone cannot infer link width
        var tampered = raw.ToArray();
        tampered[4] ^= 1;
        Assert.False(Ft12WireLayout.TryParse(tampered, 2, out _));
        var badLength = raw.ToArray();
        badLength[2] ^= 1;
        Assert.False(Ft12WireLayout.TryParse(badLength, 2, out _));
    }
}
