// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Mapping;
using ARIEC60870.Desktop;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class SignalListEditorMetadataRoundTripTests
{
    [Fact]
    public void SaveApplyProjectionPreservesIoListBusinessTypeAndEngineeringLimits()
    {
        var point = new Iec10xPointMappingEntry
        {
            Ca = 105,
            Ioa = 790447,
            TypeId = 11,
            Name = "FEEDER TM Q",
            BayType = "FEEDER",
            Mnemonic = "Q",
            TestCategory = "TM",
            Unit = "MVar",
            ScalingMode = "TwoPoint",
            X0 = -32767,
            X100 = 32767,
            Y0 = -1732.05,
            Y100 = 1732.05,
            EngineeringMin = -1732.05,
            EngineeringMax = 1732.05,
            ExpectedClass = 2,
            CommandPolicy = "MonitorOnly"
        };
        var row = new SignalListEditorRow(point);
        Assert.Equal("TM", row.TestCategory);
        Assert.Equal("-1732.05", row.EngineeringMin);
        Assert.Equal("1732.05", row.EngineeringMax);

        var duplicate = row.Clone();
        Assert.Equal("TM", duplicate.TestCategory);
        Assert.Equal("-1732.05", duplicate.EngineeringMin);
        Assert.Equal("1732.05", duplicate.EngineeringMax);

        var restored = duplicate.ToPoint();
        Assert.Equal("TM", restored.TestCategory);
        Assert.Equal(point.EngineeringMin, restored.EngineeringMin);
        Assert.Equal(point.EngineeringMax, restored.EngineeringMax);
        Assert.Equal(point.X0, restored.X0);
        Assert.Equal(point.Y100, restored.Y100);
        Assert.Equal(point.CommandPolicy, restored.CommandPolicy);
        new Iec10xPointMappingProfile { Points = [restored] }.Validate();
    }

    [Fact]
    public void OptionalEngineeringLimitsStayNullInsteadOfFabricatingCalibration()
    {
        var row = new SignalListEditorRow(new Iec10xPointMappingEntry
        {
            Ca = 105, Ioa = 790447, TypeId = 11, Name = "FEEDER TM Q",
            TestCategory = "TM", Unit = "counts"
        });
        Assert.Empty(row.EngineeringMin);
        Assert.Empty(row.EngineeringMax);
        var point = row.ToPoint();
        Assert.Null(point.EngineeringMin);
        Assert.Null(point.EngineeringMax);
        Assert.Equal("counts", point.Unit);

        row.EngineeringMax = "not numeric";
        Assert.Throws<InvalidOperationException>(() => row.ToPoint());
    }
}
