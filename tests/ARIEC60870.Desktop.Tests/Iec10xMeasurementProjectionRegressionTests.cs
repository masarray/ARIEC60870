// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Mapping;
using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec10xMeasurementProjectionRegressionTests
{
    private static Iec10xAsduDecode Decode(byte type, int ioa,
        byte low, byte high, byte qds = 0)
        => new Iec10xAsduDecoder(2, 2, 3).Decode(
        [
            type, 1, 20, 0, 105, 0,
            (byte)ioa, (byte)(ioa >> 8), (byte)(ioa >> 16),
            low, high, qds
        ]);

    private static Iec10xPointMappingEntry Poaq() => new()
    {
        Ca = 105,
        Ioa = 790449,
        TypeId = 9,
        Name = "TRAFO SOGI TM POAQ",
        CommandPolicy = "MonitorOnly",
        Unit = "MW",
        ScalingMode = "TwoPoint",
        X0 = 0,
        X100 = 1,
        Y0 = 0,
        Y100 = 1200
    };

    [Fact]
    public void ScreenshotNormalizedMaximumMustDisplayEngineering1200NotRawOneMW()
    {
        // TI9 32767 / 32768 with the original QDS OV bit set. The display
        // conversion must never erase the separately reported quality.
        var asdu = Decode(9, 790449, 0xff, 0x7f, 0x01);
        var obj = Assert.Single(asdu.Objects);
        Assert.True(asdu.IsHeaderValid);
        Assert.Empty(asdu.Issues);
        Assert.Equal(790449, obj.InformationObjectAddress);
        Assert.Equal("1199.96337891 MW",
            Iec10xMeasurementProjection.Render(Poaq(), asdu, obj,
                "0.999969482422 MW"));

        var half = Decode(9, 790449, 0, 0x40);
        Assert.Equal("600 MW", Iec10xMeasurementProjection.Render(
            Poaq(), half, Assert.Single(half.Objects), "0.5 MW"));
    }

    [Fact]
    public void ScreenshotQMustShowUncalibratedCountsNeverInheritedMvar()
    {
        var point = new Iec10xPointMappingEntry
        {
            Ca = 105,
            Ioa = 790439,
            TypeId = 11,
            Name = "KOPEL TM Q 1",
            Unit = "counts",
            CommandPolicy = "MonitorOnly"
        };
        var asdu = Decode(11, 790439, 0x49, 0x35);
        var obj = Assert.Single(asdu.Objects);
        Assert.Equal("13641 counts",
            Iec10xMeasurementProjection.Render(point, asdu, obj, "13641 MVar"));

        var value = new ValueRow(new Iec103ValuePoint
        {
            Key = "101/LINK A/105/790439",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = "Link A",
            EvidenceReference = "101|seq:1|object:1",
            CommonAddress = 105,
            InformationObjectAddress = 790439,
            TypeId = 11,
            SignalName = "Old KOPEL REACTIVE POWER (Q)",
            RawValue = "Scaled=13641",
            DisplayValue = "13641 MVar"
        }, sourceAsdu: asdu, sourceObject: obj);
        var names = new Iec10xPointDisplayIndex(new Iec10xPointMappingProfile
        {
            CommonAddress = 105,
            Points = [point]
        });
        Iec10xValueNameReconciler.Apply([value], names);
        Assert.Equal("KOPEL TM Q 1", value.Signal);
        Assert.Equal("13641 counts", value.Value);
        Assert.Equal("Scaled=13641", value.RawValue);
    }

    [Fact]
    public void MissingDecodedWireSampleCanNeverMasqueradeAsAppliedScaling()
    {
        var message = Iec10xMeasurementProjection.Render(Poaq(), null, null,
            "0.999969482422 MW");
        Assert.Equal("no trusted numerical source [scaling not verified]", message);

        var asdu = Decode(9, 790449, 0xff, 0x7f, 0x01);
        Assert.Equal("Normalized=0.99997",
            Iec10xMeasurementProjection.Render(null, asdu,
                Assert.Single(asdu.Objects), "0.999969482422 MW"));
    }

    [Theory]
    [InlineData(790349, 13)]
    [InlineData(1790446, 13)]
    [InlineData(1790447, 11)]
    public void ScreenshotExtraIoasNeverSilentlyAliasDifferentSourceAddresses(int ioa, int type)
    {
        var index = new Iec10xPointDisplayIndex(new Iec10xPointMappingProfile
        {
            CommonAddress = 105,
            Points =
            [
                new Iec10xPointMappingEntry
                    { Ca = 105, Ioa = 790439, TypeId = 11, Name = "KOPEL Q" },
                new Iec10xPointMappingEntry
                    { Ca = 105, Ioa = 790446, TypeId = 13, Name = "FEEDER P" },
                new Iec10xPointMappingEntry
                    { Ca = 105, Ioa = 790447, TypeId = 11, Name = "FEEDER Q" }
            ]
        });
        Assert.Null(index.Resolve(105, ioa, type).Point);
        Assert.Null(index.Resolve(105, ioa, type, allowNameOnly: true).Point);
    }
}
