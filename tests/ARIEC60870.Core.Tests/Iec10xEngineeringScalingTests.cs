// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using ARIEC60870.Core.Mapping;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Core.Tests;

public sealed class Iec10xEngineeringScalingTests
{
    private static Iec10xAsduDecode Decode(byte type, params byte[] element)
    {
        var raw = new byte[] { type, 1, 3, 105, 0xAF, 0x0F, 0 }
            .Concat(element).ToArray();
        return new Iec10xAsduDecoder(cotSize: 1, caSize: 1, ioaSize: 3).Decode(raw);
    }

    private static Iec10xPointMappingEntry Point(int type, double scale = 1) => new()
    {
        Ca = 105, Ioa = 4015, TypeId = type, Name = "FEEDER REACTIVE POWER (Q)",
        Unit = "MVar", Scale = scale, CommandPolicy = "MonitorOnly"
    };

    [Fact]
    public void ScaledTi11AppliesOperatorResolutionRatioOffsetOnlyToDisplay()
    {
        // TI11 SVA wire integer 13641: the unit label alone cannot imply MVar.
        var asdu = Decode(11, 0x49, 0x35, 0);
        var point = Point(11, 0.1);
        point.RatioPrimary = 5;
        point.RatioSecondary = 4;
        point.Offset = 10;
        Assert.Empty(asdu.Issues);
        Assert.True(Iec10xEngineeringValue.TryFormat(point, asdu,
            Assert.Single(asdu.Objects), out var display));
        Assert.Equal("1715.125 MVar", display);
        Assert.Equal("Scaled=13641", asdu.ValueText);
        Assert.Equal(new byte[] { 0x49, 0x35, 0 }, asdu.Objects[0].RawBytes);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(21)]
    public void NormalizedNvaUsesFullScaleNotRawInteger(int type)
    {
        var bytes = type == 21 ? new byte[] { 0x00, 0x40 } :
            new byte[] { 0x00, 0x40, 0x00 };
        var asdu = Decode((byte)type, bytes);
        var point = Point(type);
        point.NormalizedFullScale = 400;
        Assert.True(Iec10xEngineeringValue.TryFormat(point, asdu,
            Assert.Single(asdu.Objects), out var display));
        Assert.Equal("200 MVar", display);
        if (type == 21) Assert.Null(asdu.Objects[0].QualityDetails);
    }

    [Fact]
    public void FloatTi13UsesOriginalIeeeBytesInsteadOfRoundedDisplay()
    {
        var asdu = Decode(13, 0x00, 0x00, 0xE0, 0x3F, 0x00);
        var point = Point(13, 1000);
        Assert.True(Iec10xEngineeringValue.TryFormat(point, asdu,
            Assert.Single(asdu.Objects), out var display));
        Assert.Equal("1750 MVar", display);
    }

    [Fact]
    public void NegativeSvaUsesSignedLittleEndianAndNeverChangesQuality()
    {
        var asdu = Decode(11, 0x9C, 0xFF, 0x80);
        var point = Point(11, 0.01);
        Assert.True(Iec10xEngineeringValue.TryFormat(point, asdu,
            Assert.Single(asdu.Objects), out var display));
        Assert.Equal("-1 MVar", display);
        Assert.Contains("Invalid", asdu.QualityText);
    }

    [Fact]
    public void WrongFamilyCorruptionCommandsAndMissingBytesCannotProduceEngineeringValue()
    {
        var raw = Decode(11, 0x49, 0x35, 0x00);
        Assert.False(Iec10xEngineeringValue.TryFormat(Point(9), raw,
            Assert.Single(raw.Objects), out _));
        var invalid = Decode(11, 0x49);
        Assert.NotEmpty(invalid.Issues);
        Assert.False(Iec10xEngineeringValue.TryFormat(Point(11), invalid,
            Assert.Single(invalid.Objects), out _));
        var command = Decode(46, 2);
        Assert.False(Iec10xEngineeringValue.TryFormat(Point(46), command,
            Assert.Single(command.Objects), out _));
        var nonfinite = Decode(13, 0, 0, 0xC0, 0x7F, 0);
        Assert.False(Iec10xEngineeringValue.TryFormat(Point(13), nonfinite,
            nonfinite.FirstObject, out _));
    }

    [Fact]
    public void V1ProfilesRemainCompatibleAndRejectDangerousScaleSettings()
    {
        var old = JsonSerializer.Deserialize<Iec10xPointMappingEntry>(
            "{\"ca\":105,\"ioa\":4015,\"typeId\":11,\"name\":\"Q\",\"unit\":\"MVar\",\"scale\":0.1}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(1, old.RatioPrimary);
        Assert.Equal(1, old.RatioSecondary);
        Assert.Equal(1, old.NormalizedFullScale);
        Assert.Equal(0, old.Offset);
        var profile = new Iec10xPointMappingProfile { Points = [old] };
        profile.Validate();
        old.RatioSecondary = 0;
        Assert.Throws<InvalidOperationException>(() => profile.Validate());
        old.RatioSecondary = 1;
        old.Scale = double.NaN;
        Assert.Throws<InvalidOperationException>(() => profile.Validate());
    }
}
