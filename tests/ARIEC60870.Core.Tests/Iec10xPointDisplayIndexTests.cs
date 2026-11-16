// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Mapping;
using Xunit;

namespace ARIEC60870.Core.Tests;

public sealed class Iec10xPointDisplayIndexTests
{
    private static Iec10xPointMappingProfile Profile() => new()
    {
        CommonAddress = 105,
        Points =
        [
            new() { Ca = 105, Ioa = 16712689, TypeId = 31,
                Name = "FEEDER CB STATUS", CommandPolicy = "MonitorOnly" },
            new() { Ca = 105, Ioa = 68542, TypeId = 46,
                Name = "FEEDER CB OPEN/CLOSE COMMAND",
                CommandPolicy = "DoubleCommandRemoteOnly", FeedbackIoa = 16712689 }
        ]
    };

    [Fact]
    public void KnownFeederStatusUsesItsRealNameInTimeTaggedAndUntimedEvents()
    {
        var index = new Iec10xPointDisplayIndex(Profile());
        Assert.Equal("FEEDER CB STATUS", index.Resolve(105, 16712689, 31).Point?.Name);
        Assert.Equal("FEEDER CB STATUS", index.Resolve(105, 16712689, 3).Point?.Name);
        Assert.False(index.Resolve(105, 16712689, 31).NameOnly);
    }

    [Fact]
    public void ReadOnlyUniqueProcessAliasNeverChangesIdentityOrCommands()
    {
        var index = new Iec10xPointDisplayIndex(Profile());
        var name = index.Resolve(1, 16712689, 31, allowNameOnly: true);
        Assert.Equal("FEEDER CB STATUS", name.Point?.Name);
        Assert.True(name.NameOnly);
        Assert.True(name.CommonAddressMismatch);
        Assert.Null(index.Resolve(1, 16712689, 31).Point);
        Assert.Null(index.Resolve(1, 68542, 46, allowNameOnly: true).Point);
        Assert.Equal("FEEDER CB OPEN/CLOSE COMMAND",
            index.Resolve(105, 68542, 46).Point?.Name);
    }

    [Fact]
    public void WrongKindAndUnknownIoaNeverBorrowAName()
    {
        var index = new Iec10xPointDisplayIndex(Profile());
        Assert.Null(index.Resolve(1, 16712689, 13, allowNameOnly: true).Point);
        Assert.Null(index.Resolve(105, 388725, 30, allowNameOnly: true).Point);
    }

    [Fact]
    public void MultiStationAndDuplicateIoaAreNeverAutoNamed()
    {
        var profile = Profile();
        profile.Points.Add(new Iec10xPointMappingEntry
        {
            Ca = 106, Ioa = 16712689, TypeId = 31,
            Name = "OTHER STATION CB STATUS"
        });
        var index = new Iec10xPointDisplayIndex(profile);
        Assert.Equal("FEEDER CB STATUS", index.Resolve(105, 16712689, 31).Point?.Name);
        Assert.Equal("OTHER STATION CB STATUS", index.Resolve(106, 16712689, 31).Point?.Name);
        Assert.Null(index.Resolve(1, 16712689, 31, allowNameOnly: true).Point);
    }
}
