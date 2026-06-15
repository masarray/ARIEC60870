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

    private static Iec103MasterSettings Settings(int commonAddress = 1) => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec104,
        CauseOfTransmissionSize = 2,
        CommonAddressSize = 2,
        InformationObjectAddressSize = 3,
        CommonAddress = commonAddress
    };
}
