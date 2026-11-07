// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Mapping;
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
