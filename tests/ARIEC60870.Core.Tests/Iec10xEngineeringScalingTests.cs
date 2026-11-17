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
        Ca = 105,
        Ioa = 4015,
        TypeId = type,
        Name = "FEEDER REACTIVE POWER (Q)",
        Unit = "MVar",
        Scale = scale,
        CommandPolicy = "MonitorOnly"
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
        var wrongCa = Point(11); wrongCa.Ca = 106;
        Assert.False(Iec10xEngineeringValue.TryFormat(wrongCa, raw,
            Assert.Single(raw.Objects), out _));
        var wrongIoa = Point(11); wrongIoa.Ioa = 4016;
        Assert.False(Iec10xEngineeringValue.TryFormat(wrongIoa, raw,
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

    [Theory]
    [InlineData(0, 20000, 0, 1000, "682.05 MVar")]
    [InlineData(20000, 0, 0, 1000, "317.95 MVar")]
    [InlineData(0, 10000, 0, 2000, "2728.2 MVar")]
    public void TwoPointSignedSvaMapsConfiguredSourceEndpointsWithoutLegacyMultiplier(
        double x0, double x100, double y0, double y100, string expected)
    {
        var asdu = Decode(11, 0x49, 0x35, 0);
        var point = Point(11, 99);
        point.ScalingMode = "TwoPoint";
        point.X0 = x0;
        point.X100 = x100;
        point.Y0 = y0;
        point.Y100 = y100;
        point.Offset = -100;
        point.RatioPrimary = 100;
        point.RatioSecondary = 2;
        point.NormalizedFullScale = 500;
        new Iec10xPointMappingProfile { Points = [point] }.Validate();

        Assert.True(Iec10xEngineeringValue.TryFormat(point, asdu,
            Assert.Single(asdu.Objects), out var display));
        Assert.Equal(expected, display);
        Assert.Equal("Scaled=13641", asdu.ValueText);
        Assert.Equal(new byte[] { 0x49, 0x35, 0 }, asdu.Objects[0].RawBytes);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(21)]
    public void TwoPointNormalizedEndpointsUseDecodedPerUnitNotRawSignedInteger(int type)
    {
        var element = type == 21 ? new byte[] { 0, 0x40 } :
            new byte[] { 0, 0x40, 0 };
        var asdu = Decode((byte)type, element);
        var point = Point(type);
        point.ScalingMode = "TwoPoint";
        point.X0 = -1;
        point.X100 = 1;
        point.Y0 = -200;
        point.Y100 = 200;
        point.NormalizedFullScale = 900; // Ignored in this mutually exclusive mode.
        Assert.True(Iec10xEngineeringValue.TryFormat(point, asdu,
            Assert.Single(asdu.Objects), out var text));
        Assert.Equal("100 MVar", text);
        Assert.Equal(type == 21, asdu.FirstObject?.QualityDetails is null);
    }

    [Fact]
    public void TwoPointFloatAndOutOfRangeSamplesAreNotClamped()
    {
        var asdu = Decode(13, 0, 0, 0xE0, 0x3F, 0);
        var point = Point(13);
        point.ScalingMode = "TwoPoint";
        point.X0 = 0;
        point.X100 = 2;
        point.Y0 = 0;
        point.Y100 = 1000;
        Assert.True(Iec10xEngineeringValue.TryFormat(point, asdu,
            Assert.Single(asdu.Objects), out var text));
        Assert.Equal("875 MVar", text);

        // Out-of-range measurements are extrapolated, not falsely clipped or Good.
        var negative = Decode(11, 0x9C, 0xFF, 0x80); // signed -100, QDS Invalid
        point.TypeId = 11;
        point.X0 = 0;
        point.X100 = 100;
        Assert.True(Iec10xEngineeringValue.TryFormat(point, negative,
            Assert.Single(negative.Objects), out text));
        Assert.Equal("-1000 MVar", text);
        Assert.Contains("Invalid", negative.QualityText);
    }

    [Fact]
    public void TwoPointRejectsMissingInvalidAndAmbiguousCalibration()
    {
        var source = Decode(11, 0x49, 0x35, 0);
        var obj = Assert.Single(source.Objects);
        var point = Point(11);
        point.ScalingMode = "TwoPoint";
        point.X0 = 0;
        point.X100 = 100;
        point.Y0 = 0;
        point.Y100 = 1000;
        var profile = new Iec10xPointMappingProfile { Points = [point] };
        profile.Validate();

        point.X100 = 0;
        Assert.Throws<InvalidOperationException>(() => profile.Validate());
        Assert.False(Iec10xEngineeringValue.TryFormat(point, source, obj, out _));
        point.X100 = null;
        Assert.Throws<InvalidOperationException>(() => profile.Validate());
        Assert.False(Iec10xEngineeringValue.TryFormat(point, source, obj, out _));
        point.X100 = 100;
        point.Y100 = double.PositiveInfinity;
        Assert.Throws<InvalidOperationException>(() => profile.Validate());
        Assert.False(Iec10xEngineeringValue.TryFormat(point, source, obj, out _));
        point.Y100 = 1000;
        point.TypeId = null;
        Assert.Throws<InvalidOperationException>(() => profile.Validate());
        Assert.False(Iec10xEngineeringValue.TryFormat(point, source, obj, out _));
        point.TypeId = 46;
        Assert.Throws<InvalidOperationException>(() => profile.Validate());
        Assert.False(Iec10xEngineeringValue.TryFormat(point, source, obj, out _));
        point.TypeId = 11;
        point.CommandPolicy = "DoubleCommand";
        Assert.Throws<InvalidOperationException>(() => profile.Validate());
        Assert.False(Iec10xEngineeringValue.TryFormat(point, source, obj, out _));
        point.CommandPolicy = "MonitorOnly";
        point.ScalingMode = "Unknown";
        Assert.Throws<InvalidOperationException>(() => profile.Validate());
        Assert.False(Iec10xEngineeringValue.TryFormat(point, source, obj, out _));
        point.ScalingMode = "TwoPoint";
        profile.Validate();
        Assert.True(Iec10xEngineeringValue.TryFormat(point, source, obj, out _));
        point.Ca = 106;
        Assert.False(Iec10xEngineeringValue.TryFormat(point, source, obj, out _));
        point.Ca = 105;
        point.Ioa++;
        Assert.False(Iec10xEngineeringValue.TryFormat(point, source, obj, out _));
    }

    [Fact]
    public void LegacyFactorProfileDoesNotEnableTwoPointUnlessExplicitlySelected()
    {
        var json = """{"points":[{"ca":105,"ioa":4015,"typeId":11,"name":"Q","unit":"MVar","scale":0.1}]}""";
        var profile = JsonSerializer.Deserialize<Iec10xPointMappingProfile>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        profile.Validate();
        var point = Assert.Single(profile.Points);
        Assert.Equal("Factor", point.ScalingMode);
        Assert.Null(point.X0);
        var source = Decode(11, 0x49, 0x35, 0);
        Assert.True(Iec10xEngineeringValue.TryFormat(point, source,
            Assert.Single(source.Objects), out var text));
        Assert.Equal("1364.1 MVar", text);
    }

    [Fact]
    public void TinyFloatRetainsNonzeroEngineeringPrecision()
    {
        var data = BitConverter.GetBytes(0.0000001f).Append((byte)0).ToArray();
        var asdu = Decode(13, data);
        Assert.True(Iec10xEngineeringValue.TryFormat(Point(13), asdu,
            Assert.Single(asdu.Objects), out var result));
        Assert.NotEqual("0 MVar", result);
        Assert.Contains("E-", result, StringComparison.OrdinalIgnoreCase);
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
