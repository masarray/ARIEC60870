// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Mapping;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec10xProfileVariantTests
{
    [Theory]
    [InlineData(30, 1)]
    [InlineData(31, 3)]
    [InlineData(32, 5)]
    [InlineData(35, 11)]
    [InlineData(36, 13)]
    public void ExactIoaAndCaMayUseUntimedGiVariant(int expected, int actual)
    {
        var profile = new Iec10xPointMappingProfile
        {
            CommonAddress = 105,
            Points = [new Iec10xPointMappingEntry
                { Ca = 105, Ioa = 16712686, TypeId = expected, Name = "Known Signal" }]
        };
        Assert.Equal("Known Signal",
            profile.ResolveObservedSignal(105, 16712686, actual)?.Name);
        Assert.Null(profile.ResolveObservedSignal(106, 16712686, actual));
        Assert.Null(profile.ResolveObservedSignal(105, 16712687, actual));
        Assert.Null(profile.ResolveObservedSignal(105, 16712686, 45));
    }

    [Fact]
    public void AutoCaMapsSimulator22SignalsWithoutChangingProjectCommonAddress()
    {
        int[] sp = [8388714,8388715,8388716,8388717,8388725,8388754,
            16712694,16712701,16712708,16712709,16712710];
        int[] dp = [16712686,16712689,16712704];
        int[] analog = [790446,790447,790438,790439,790442,790443,790449];
        var profile = new Iec10xPointMappingProfile { CommonAddress = 105 };
        foreach (var ioa in sp) profile.Points.Add(new() { Ca=105, Ioa=ioa, TypeId=30, Name=$"SP {ioa}" });
        foreach (var ioa in dp) profile.Points.Add(new() { Ca=105, Ioa=ioa, TypeId=31, Name=$"DP {ioa}" });
        foreach (var ioa in analog) profile.Points.Add(new() { Ca=105, Ioa=ioa, TypeId=13, Name=$"AI {ioa}" });
        profile.Points.Add(new() { Ca=105, Ioa=790448, TypeId=32, Name="Tap" });
        var adapter = new Iec10xRuntimeCaReconciler(profile);
        Assert.True(adapter.IsSingleStation);
        int matched = 0, first = 0;
        foreach (var point in profile.Points)
        {
            var outcome = adapter.Observe("SINGLE", 1, point.Ioa, point.TypeId!.Value);
            if (outcome.Point is not null) matched++;
            if (outcome.FirstAlias) first++;
            Assert.True(outcome.ReadOnlyAlias);
            Assert.Equal(point.Name, outcome.Point?.Name);
        }
        Assert.Equal(22, matched);
        Assert.Equal(1, first);
        Assert.Equal(105, profile.CommonAddress);
        Assert.All(profile.Points, p => Assert.Equal(105, p.Ca));
        Assert.Null(profile.ResolveObservedSignal(1, 16712686, 31));
    }

    [Fact]
    public void SmartCaIsFailClosedForWrongFamilyMultiStationOrMixedSource()
    {
        var profile = new Iec10xPointMappingProfile
        {
            CommonAddress = 105,
            Points =
            [
                new() { Ca=105, Ioa=16712686, TypeId=31, Name="CB" },
                new() { Ca=105, Ioa=8388714, TypeId=30, Name="Alarm" }
            ]
        };
        var reconciler = new Iec10xRuntimeCaReconciler(profile);
        Assert.Null(reconciler.Observe("LINK A", 1, 16712686, 13).Point);
        Assert.Equal("CB", reconciler.Observe("LINK A", 1, 16712686, 31).Point?.Name);
        Assert.True(reconciler.Observe("LINK A", 2, 8388714, 30).SourceCaConflict);
        Assert.Null(reconciler.Observe("LINK A", 1, 8388714, 30).Point);
        Assert.Equal("Alarm", reconciler.Observe("LINK B", 1, 8388714, 30).Point?.Name);
        var multi = new Iec10xPointMappingProfile
        {
            CommonAddress = 105,
            Points = [new() { Ca=105, Ioa=5, TypeId=30, Name="A" },
                      new() { Ca=106, Ioa=5, TypeId=30, Name="B" }]
        };
        var blocked = new Iec10xRuntimeCaReconciler(multi);
        Assert.False(blocked.IsSingleStation);
        Assert.Null(blocked.Observe("SINGLE", 1, 5, 30).Point);
    }

    [Theory]
    [InlineData(31, 1, true)]   // DP project, SP observed: name only
    [InlineData(11, 13, true)]  // scaled project, float observed
    [InlineData(30, 13, false)] // digital name must not label analog value
    [InlineData(46, 3, false)]  // command must never label process feedback
    public void UniqueIoaMaySupplyNameWithExplicitTiMismatch(int expected, int observed, bool accepted)
    {
        var profile = new Iec10xPointMappingProfile
        {
            CommonAddress = 105,
            Points = [new() { Ca = 105, Ioa = 16712694, TypeId = expected, Name = "Approved IOA name" }]
        };
        var result = new Iec10xRuntimeCaReconciler(profile).Observe("SINGLE", 1, 16712694, observed);
        Assert.Equal(accepted, result.Point is not null);
        Assert.Equal(accepted, result.TypeMismatch);
        Assert.Equal(accepted, result.ReadOnlyAlias);
        Assert.Null(profile.ResolveObservedSignal(1, 16712694, observed));
    }

    [Fact]
    public void MultipleProjectDefinitionsAtSameIoaNeverAutoNameWrongTi()
    {
        var profile = new Iec10xPointMappingProfile
        {
            CommonAddress = 105,
            Points =
            [
                new() { Ca = 105, Ioa = 100, TypeId = 30, Name = "SP" },
                new() { Ca = 105, Ioa = 100, TypeId = 31, Name = "DP" }
            ]
        };
        var result = new Iec10xRuntimeCaReconciler(profile).Observe("SINGLE", 1, 100, 3);
        Assert.Null(result.Point);
    }

    [Fact]
    public void MismatchedCaIsDiagnosedButNeverAliasedAutomatically()
    {
        var profile = new Iec10xPointMappingProfile
        {
            CommonAddress = 105,
            Points =
            [
                new Iec10xPointMappingEntry { Ca = 105, Ioa = 16712686, TypeId = 31, Name = "CB" },
                new Iec10xPointMappingEntry { Ca = 105, Ioa = 8388714, TypeId = 30, Name = "Link fault" }
            ]
        };
        Assert.Null(profile.ResolveObservedSignal(1, 16712686, 31));
        Assert.Equal(105, profile.FindUniqueMismatchedCommonAddress(1, 16712686, 31));
        Assert.Equal(105, profile.FindUniqueMismatchedCommonAddress(1, 8388714, 1));
        Assert.Null(profile.FindUniqueMismatchedCommonAddress(1, 16712686, 13));
        Assert.Null(profile.FindUniqueMismatchedCommonAddress(105, 16712686, 31));
        Assert.Null(profile.FindUniqueMismatchedCommonAddress(1, 99999, 31));
    }

    [Fact]
    public void ExplicitSingleStationRebindCreatesNewValidatedProfileWithoutChangingOriginal()
    {
        var profile = new Iec10xPointMappingProfile
        {
            CommonAddress = 105,
            DefaultSettings = new Iec10xInteroperabilityDefaults { CommonAddress = 105, LinkAddress = 105 },
            Points =
            [
                new Iec10xPointMappingEntry { Ca = 105, Ioa = 16712686, TypeId = 31, Name = "CB" },
                new Iec10xPointMappingEntry { Ca = 105, Ioa = 8388714, TypeId = 30, Name = "Link fault" }
            ]
        };
        var aligned = profile.RebindSingleStationCommonAddress(1);
        Assert.Equal(105, profile.CommonAddress);
        Assert.Equal(105, profile.Points[0].Ca);
        Assert.Equal(1, aligned.CommonAddress);
        Assert.All(aligned.Points, p => Assert.Equal(1, p.Ca));
        Assert.Equal(1, aligned.DefaultSettings?.CommonAddress);
        Assert.Equal(105, aligned.DefaultSettings?.LinkAddress);
        Assert.Equal("CB", aligned.ResolveObservedSignal(1, 16712686, 3)?.Name);
        Assert.Null(profile.ResolveObservedSignal(1, 16712686, 3));
    }

    [Fact]
    public void MixedStationProfileCannotBeBulkRebound()
    {
        var profile = new Iec10xPointMappingProfile
        {
            CommonAddress = 105,
            Points =
            [
                new Iec10xPointMappingEntry { Ca = 105, Ioa = 1, TypeId = 30, Name = "A" },
                new Iec10xPointMappingEntry { Ca = 106, Ioa = 2, TypeId = 30, Name = "B" }
            ]
        };
        Assert.Throws<InvalidOperationException>(() => profile.RebindSingleStationCommonAddress(1));
    }

    [Fact]
    public void ValuesCarriesObservedDl105AndCa1WithProfileMismatch()
    {
        var row = new ValueRow(new Iec103ValuePoint
        {
            Key = "frame", ProtocolMode = Iec60870ProtocolMode.Iec101,
            CommonAddress = 1, InformationObjectAddress = 16712686,
            SignalName = "CIRCUIT BREAKER", DisplayValue = "OFF"
        }, wireDl: 105, configuredDl: 105, configuredCa: 105,
           mappedCa: 105, readOnlyAlias: true);
        Assert.Equal("105", row.LinkAddress);
        Assert.Equal("1", row.CommonAddress);
        Assert.True(row.IsCaMismatch);
        Assert.False(row.IsDlMismatch);
        Assert.Contains("Name match only", row.CaExplanation);
        Assert.Contains("16712686", row.Address);
    }

    [Fact]
    public void ValuesDifferentiatesDlMismatchFromCaMismatch()
    {
        var row = new ValueRow(new Iec103ValuePoint
        {
            Key = "frame", ProtocolMode = Iec60870ProtocolMode.Iec101,
            CommonAddress = 1, InformationObjectAddress = 10
        }, wireDl: 106, configuredDl: 105, configuredCa: 1, mappedCa: 1);
        Assert.True(row.IsDlMismatch);
        Assert.False(row.IsCaMismatch);
    }

    [Fact]
    public void ValuesNeverInventsObservedDlForProfilePlaceholder()
    {
        var row = new ValueRow(new Iec103ValuePoint
        {
            Key = "pending", ProtocolMode = Iec60870ProtocolMode.Iec101,
            CommonAddress = 105, InformationObjectAddress = 10,
            DisplayValue = "awaiting live value"
        });
        Assert.Equal("-", row.LinkAddress);
        Assert.False(row.IsDlMismatch);
        Assert.False(row.IsCaMismatch);
    }

    [Fact]
    public void StructuredIoaTooltipShowsAllThreeOctetsInCorrectWireOrder()
    {
        var tooltip = Iec10xIoaPresentation.StructuredTooltip(16712694);
        Assert.Equal("IOA 16712694\nIOA1 (LOW)  = 246\nIOA2 (MID)  = 3\nIOA3 (HIGH) = 255", tooltip);
        Assert.DoesNotContain("0x", tooltip);
        Assert.DoesNotContain("wire order", tooltip);
    }

    [Fact]
    public void ValueShowsReceivedTiAndHighlightsOnlyDifferentTypeFamily()
    {
        var row = new ValueRow(new Iec103ValuePoint
        {
            Key = "wire", ProtocolMode = Iec60870ProtocolMode.Iec101,
            CommonAddress = 1, TypeId = 1,
            InformationObjectAddress = 16712694, SignalName = "LOCAL REMOTE CB"
        }, wireDl: 105, configuredDl: 105, mappedTi: 31);
        Assert.Equal("TI-1", row.TiLabel);
        Assert.True(row.IsTiMismatch);
        Assert.Contains("TI-31", row.TiExplanation);
        Assert.Contains("IOA3 (HIGH)", row.IoaTooltip);
        var compatible = new ValueRow(new Iec103ValuePoint
        {
            Key = "wire2", ProtocolMode = Iec60870ProtocolMode.Iec101,
            TypeId = 1, InformationObjectAddress = 1
        }, mappedTi: 30);
        Assert.False(compatible.IsTiMismatch);
    }

    [Fact]
    public void WrongFamilyOrAmbiguousProfileDoesNotFabricateSignal()
    {
        var profile = new Iec10xPointMappingProfile
        {
            CommonAddress = 105,
            Points =
            [
                new Iec10xPointMappingEntry { Ca=105, Ioa=1, TypeId=30, Name="SP" },
                new Iec10xPointMappingEntry { Ca=105, Ioa=1, TypeId=31, Name="DP" }
            ]
        };
        Assert.Equal("DP", profile.ResolveObservedSignal(105, 1, 3)?.Name);
        Assert.Equal("SP", profile.ResolveObservedSignal(105, 1, 1)?.Name);
        Assert.Null(profile.ResolveObservedSignal(105, 1, 5));
        Assert.False(Iec10xPointTypeFamily.SameObservationFamily(30, 31));
        Assert.False(Iec10xPointTypeFamily.SameObservationFamily(46, 3));
    }
}
