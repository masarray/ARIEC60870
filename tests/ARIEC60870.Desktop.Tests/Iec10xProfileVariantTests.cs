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
