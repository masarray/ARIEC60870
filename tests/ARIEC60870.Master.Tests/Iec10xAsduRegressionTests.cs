// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec10xAsduRegressionTests
{
    [Fact]
    public void GeneralInterrogationBuilderRoundTripsConfiguredAddressSizes()
    {
        var settings = Settings(commonAddress: 0x0201);
        var raw = Iec10xAsduBuilder.GeneralInterrogation(settings);

        var decoded = new Iec10xAsduDecoder(cotSize: 2, caSize: 2, ioaSize: 3).Decode(raw);

        Assert.Equal(100, decoded.TypeId);
        Assert.True(decoded.IsControlCommand);
        Assert.Equal(6, decoded.CauseOfTransmission);
        Assert.Equal(0x0201, decoded.CommonAddress);
        Assert.Equal(0, decoded.InformationObjectAddress);
        Assert.Contains("QOI=20", decoded.ValueText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SinglePointSequenceDecodesSeparateObjectsAndIoas()
    {
        var raw = new byte[]
        {
            0x01, 0x82,       // Type 1, SQ=1, two objects
            0x14, 0x00,       // COT=20, OA=0
            0x01, 0x00,       // CA=1
            0x64, 0x00, 0x00, // base IOA=100
            0x01,             // IOA 100 ON
            0x00              // IOA 101 OFF
        };

        var decoded = new Iec10xAsduDecoder().Decode(raw);

        Assert.Equal(2, decoded.ObjectCount);
        Assert.True(decoded.IsSequence);
        Assert.Equal(2, decoded.Objects.Count);
        Assert.Equal(100, decoded.Objects[0].InformationObjectAddress);
        Assert.Equal(101, decoded.Objects[1].InformationObjectAddress);
        Assert.Equal("SP=ON", decoded.Objects[0].ValueText);
        Assert.Equal("SP=OFF", decoded.Objects[1].ValueText);
        Assert.Equal("Good", decoded.Objects[0].QualityText);
    }

    [Fact]
    public void SinglePointQualityFlagsRemainVisibleForInvalidData()
    {
        var raw = new byte[]
        {
            0x01, 0x01,
            0x03, 0x00,
            0x01, 0x00,
            0x0A, 0x00, 0x00,
            0x81 // ON + invalid quality flag
        };

        var decoded = new Iec10xAsduDecoder().Decode(raw);

        Assert.Equal("M_SP_NA_1 single-point", decoded.TypeName);
        Assert.Equal("SP=ON", decoded.ValueText);
        Assert.Equal("Invalid", decoded.QualityText);
        Assert.Contains("Quality=Invalid", decoded.ObjectSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DoubleCommandBuilderEncodesSelectBeforeOperateAndQualifier()
    {
        var settings = Settings();
        var raw = Iec10xAsduBuilder.DoubleCommand(settings, ioa: 77, dcs: 2, select: true, qualifier: 3);

        var decoded = new Iec10xAsduDecoder().Decode(raw);

        Assert.Equal(46, decoded.TypeId);
        Assert.True(decoded.IsControlCommand);
        Assert.Equal(77, decoded.InformationObjectAddress);
        Assert.Contains("ON", decoded.ValueText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("select", decoded.ValueText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("QU=3", decoded.ValueText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cp56TimeEncoderDecodesToExpectedCalendarFields()
    {
        var time = new DateTime(2026, 6, 15, 7, 8, 9, 123, DateTimeKind.Local);
        var encoded = Iec10xAsduBuilder.EncodeCp56Time2a(time);
        var decoded = Iec10xAsduDecoder.DecodeCp56Time2a(encoded);

        Assert.Contains("2026-06-15", decoded, StringComparison.Ordinal);
        Assert.Contains("07:08:09.123", decoded, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void UnsupportedTypeRetainsExactWireBytesWithoutGuessingObjectLayout(
        int ioaWidth, bool sequence)
    {
        // Type 17 is reserved/unsupported here. Neither the SQ flag nor
        // the apparent IOA bytes establishes a standard element length.
        var raw = new byte[] { 17, sequence ? (byte)0x82 : (byte)0x02, 3, 1 }
            .Concat(Enumerable.Repeat((byte)0xFF, ioaWidth))
            .Concat(new byte[] { 0x81, 0x00, 0x68, 0x10 })
            .ToArray();
        var decoded = new Iec10xAsduDecoder(cotSize: 1, caSize: 1, ioaSize: ioaWidth)
            .Decode(raw);

        Assert.True(decoded.IsHeaderValid);
        Assert.Equal(17, decoded.TypeId);
        Assert.Equal(2, decoded.ObjectCount);
        Assert.Equal(sequence, decoded.IsSequence);
        Assert.Equal(ioaWidth, decoded.InformationObjectAddressWidth);
        Assert.Empty(decoded.Objects);
        Assert.Null(decoded.InformationObjectAddress);
        Assert.Empty(decoded.ValueText);
        Assert.Empty(decoded.QualityText);
        Assert.True(decoded.RawBytes.SequenceEqual(raw));
        Assert.Contains(decoded.Issues, issue => issue.Contains("Unsupported Type ID 17", StringComparison.Ordinal));
        Assert.Contains("raw payload bytes", decoded.ObjectSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VendorPrivateTypeRemainsOpaqueWithoutSpuriousCorruption()
    {
        var raw = new byte[] { 200, 0x82, 3, 1, 0xFF, 0xFF, 0x00, 0xE5, 0x68 };
        var decoded = new Iec10xAsduDecoder(1, 1, 3).Decode(raw);

        Assert.True(decoded.IsOpaqueVendorSpecific);
        Assert.Empty(decoded.Objects);
        Assert.Empty(decoded.Issues);
        Assert.Null(decoded.InformationObjectAddress);
        Assert.True(decoded.RawBytes.SequenceEqual(raw));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void KnownSq1SequenceDecodesOnlyWithinConfiguredIoaWidth(int ioaWidth)
    {
        var maxIoa = (1 << (8 * ioaWidth)) - 1;
        byte[] AsduAt(int baseIoa) =>
        [
            1, 0x82, 3, 1,
            .. Enumerable.Range(0, ioaWidth).Select(i => (byte)(baseIoa >> (8 * i))),
            1, 0
        ];
        var validRaw = AsduAt(maxIoa - 1);
        var valid = new Iec10xAsduDecoder(1, 1, ioaWidth).Decode(validRaw);
        Assert.Empty(valid.Issues);
        Assert.Equal(2, valid.Objects.Count);
        Assert.Equal(maxIoa - 1, valid.Objects[0].InformationObjectAddress);
        Assert.Equal(maxIoa, valid.Objects[1].InformationObjectAddress);
        Assert.Equal(-1, valid.Objects[1].InformationObjectOffset);
        Assert.True(valid.RawBytes.SequenceEqual(validRaw));

        var overflowRaw = AsduAt(maxIoa);
        var overflow = new Iec10xAsduDecoder(1, 1, ioaWidth).Decode(overflowRaw);
        Assert.Single(overflow.Objects);
        Assert.Equal(maxIoa, overflow.Objects[0].InformationObjectAddress);
        Assert.Contains(overflow.Issues, issue => issue.Contains("exceeds", StringComparison.OrdinalIgnoreCase));
        Assert.True(overflow.RawBytes.SequenceEqual(overflowRaw));
    }

    [Fact]
    public void NormalizedWithoutQdsHasNoInventedQualityOrTrailingObject()
    {
        // IEC-101/104 TI21 M_ME_ND_1: signed NVA, no QDS on wire.
        var raw = new byte[] { 21, 0x01, 3, 1, 0x34, 0x00, 0x00, 0x00, 0x40 };
        var decoded = new Iec10xAsduDecoder(1, 1, 3).Decode(raw);
        Assert.Empty(decoded.Issues);
        Assert.Single(decoded.Objects);
        Assert.Equal(0x34, decoded.InformationObjectAddress);
        Assert.Contains("Normalized=", decoded.ValueText, StringComparison.Ordinal);
        Assert.Null(decoded.Objects[0].QualityDetails);
        Assert.Empty(decoded.QualityText);
        Assert.True(decoded.RawBytes.SequenceEqual(raw));
    }

    private static Iec103MasterSettings Settings(int commonAddress = 1) => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec104,
        CauseOfTransmissionSize = 2,
        CommonAddressSize = 2,
        InformationObjectAddressSize = 3,
        CommonAddress = commonAddress
    };
}
