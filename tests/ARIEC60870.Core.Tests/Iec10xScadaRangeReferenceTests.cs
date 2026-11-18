// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Core.Mapping;
using Xunit;

namespace ARIEC60870.Core.Tests;

public sealed class Iec10xScadaRangeReferenceTests
{
    private static Iec10xPointMappingEntry Point(int ioa, int ti, string policy = "MonitorOnly") => new()
    {
        Ca = 105,
        Ioa = ioa,
        TypeId = ti,
        Name = "Selected test point",
        CommandPolicy = policy,
        Unit = "counts"
    };

    private static Iec10xScadaRangeReference Choice(
        Iec10xPointMappingEntry point, Iec10xScadaRangeId id)
    {
        Assert.True(Iec10xScadaRangeReferences.TryForPoint(point, id, out var range));
        return Assert.IsType<Iec10xScadaRangeReference>(range);
    }

    private static double Display(
        Iec10xPointMappingEntry point, Iec10xScadaRangeReference range,
        double decodedSample)
    {
        point.ScalingMode = "TwoPoint";
        point.X0 = range.X0;
        point.X100 = range.X100;
        point.Y0 = range.Y0;
        point.Y100 = range.Y100;
        point.Unit = range.Unit;

        // Prior Factor-mode CT/PT is dormant in TwoPoint. Never compound it.
        point.Scale = 99;
        point.Offset = -33;
        point.RatioPrimary = 2000;
        point.RatioSecondary = 1;
        new Iec10xPointMappingProfile { Points = [point] }.Validate();
        Assert.True(Iec10xEngineeringValue.TryFormat(point,
            new Iec10xEngineeringSample(point.TypeId!.Value, 105,
                point.Ioa, decodedSample, decodedSample.ToString(CultureInfo.InvariantCulture)),
            out var text));
        return double.Parse(text[..text.LastIndexOf(' ')],
            CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData(790447)]
    [InlineData(790439)]
    [InlineData(790443)]
    public void BothQReferencesAreAvailableForOneQButNeverAutoSelected(int ioa)
    {
        var point = Point(ioa, 11);
        var own = Choice(point, Iec10xScadaRangeId.ReactiveDiriSendiri);
        var lab = Choice(point, Iec10xScadaRangeId.ReactiveLab);
        Assert.Equal((-32768d, 32767d, -2078d, 2078d),
            (own.X0, own.X100, own.Y0, own.Y100));
        Assert.Equal((-32768d, 32767d, -260d, 260d),
            (lab.X0, lab.X100, lab.Y0, lab.Y100));
        Assert.Equal("counts", point.Unit);
        Assert.Equal("Factor", point.ScalingMode);
        Assert.Null(point.X0);

        Assert.Equal(-2078, Display(Point(ioa, 11), own, -32768), 8);
        Assert.Equal(2078, Display(Point(ioa, 11), own, 32767), 8);
        Assert.Equal(-260, Display(Point(ioa, 11), lab, -32768), 8);
        Assert.Equal(260, Display(Point(ioa, 11), lab, 32767), 8);

        // Literal asymmetric IEC signed endpoints imply a small positive
        // value at zero. Never silently use symmetric ±32767 instead.
        Assert.InRange(Display(Point(ioa, 11), own, 0), 0.0317, 0.03172);
        Assert.InRange(Display(Point(ioa, 11), lab, 0), 0.00396, 0.00398);
    }

    [Fact]
    public void CorrectedPoaQFeedbackUses1200AndDoesNotAffectPooPCommand()
    {
        var feedback = Point(790449, 9);
        var range = Choice(feedback, Iec10xScadaRangeId.PoaqFeedback);
        Assert.Equal((0d, 1d, 0d, 1200d),
            (range.X0, range.X100, range.Y0, range.Y100));
        Assert.Equal(600, Display(Point(790449, 9), range, 0.5), 8);
        Assert.InRange(Display(Point(790449, 9), range, 32767d / 32768d),
            1199.96, 1200);

        var poop = Point(70537, 48, "SetpointNormalizedRemoteOnly");
        poop.EngineeringMin = 0;
        poop.EngineeringMax = 1000;
        Assert.False(Iec10xScadaRangeReferences.TryForPoint(
            poop, Iec10xScadaRangeId.PoaqFeedback, out _));
        Assert.Equal(1000, poop.EngineeringMax);
        Assert.Equal("Factor", poop.ScalingMode);
    }

    [Fact]
    public void WrongStationTypeIoaAndCommandNeverReceiveReference()
    {
        var wrongCa = Point(790447, 11);
        wrongCa.Ca = 106;
        var wrongTi = Point(790447, 13);
        var wrongIoa = Point(790440, 11);
        var command = Point(790447, 11, "DoubleCommand");
        var tap = Point(790448, 32);
        var q = Iec10xScadaRangeId.ReactiveDiriSendiri;
        foreach (var point in new[] { wrongCa, wrongTi, wrongIoa, command, tap })
            Assert.False(Iec10xScadaRangeReferences.TryForPoint(point, q, out _));

        Assert.False(Iec10xScadaRangeReferences.TryForPoint(
            Point(790447, 11), Iec10xScadaRangeId.PoaqFeedback, out _));
        Assert.False(Iec10xScadaRangeReferences.TryForPoint(
            Point(790449, 9), Iec10xScadaRangeId.ReactiveLab, out _));
    }
}
