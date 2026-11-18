// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Reflection;
using ARIEC60870.Core.Mapping;
using ARIEC60870.Desktop;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class SignalListEditorScadaReferenceTransactionTests
{
    // Exercise the exact private proposal builder without showing any modal
    // dialog or applying the range to the operator's active profile.
    private static (string Field, string Before, string After)[] Proposed(
        SignalListEditorRow row, Iec10xScadaRangeReference range)
    {
        var method = typeof(SignalListEditorWindow).GetMethod(
            "PlanScadaReferenceChanges", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Missing SCADA range transaction planner.");
        var result = method.Invoke(null, [row, range]) as IEnumerable
            ?? throw new InvalidOperationException("Invalid SCADA range transaction result.");
        return result.Cast<object>().Select(change =>
        {
            var type = change.GetType();
            string Read(string property) => (string?)type.GetProperty(property)?.GetValue(change)
                ?? throw new InvalidOperationException("Missing change property: " + property);
            return (Read("Field"), Read("Before"), Read("After"));
        }).ToArray();
    }

    [Theory]
    [InlineData(790447, Iec10xScadaRangeId.ReactiveDiriSendiri, -2078d, 2078d)]
    [InlineData(790439, Iec10xScadaRangeId.ReactiveLab, -260d, 260d)]
    [InlineData(790449, Iec10xScadaRangeId.PoaqFeedback, 0d, 1200d)]
    public void ReferenceIsSingleSignalAtomicDraftAndCanBeReversed(
        int ioa, Iec10xScadaRangeId selection, double low, double high)
    {
        var ti = ioa == 790449 ? 9 : 11;
        var point = new Iec10xPointMappingEntry
        {
            Ca = 105, Ioa = ioa, TypeId = ti, Name = "FAT selected",
            Unit = "counts", CommandPolicy = "MonitorOnly",
            ExpectedClass = 2, ExpectedCot = 3,
            ScalingMode = "Factor", Scale = 2.5,
            RatioPrimary = 2000, RatioSecondary = 1
        };
        Assert.True(Iec10xScadaRangeReferences.TryForPoint(point, selection, out var range));
        Assert.NotNull(range);
        var row = new SignalListEditorRow(point);
        var neighbor = new SignalListEditorRow(new Iec10xPointMappingEntry
        {
            Ca = 105, Ioa = 70537, TypeId = 48,
            Name = "POOP command", Unit = "MW",
            CommandPolicy = "SetpointNormalizedRemoteOnly", EngineeringMin = 0,
            EngineeringMax = 1000
        });
        var before = row.Clone();
        var neighborBefore = neighbor.ToPoint();
        var changes = Proposed(row, range!);
        var expectedFields = new[]
        {
            "ScalingMode", "X0", "X100", "Y0", "Y100",
            "Unit", "EngineeringMin", "EngineeringMax"
        };
        Assert.Equal(expectedFields.OrderBy(x => x),
            changes.Select(x => x.Field).OrderBy(x => x));
        Assert.All(changes, change =>
            Assert.Equal(change.Before, (string?)typeof(SignalListEditorRow)
                .GetProperty(change.Field)?.GetValue(row)));

        foreach (var change in changes)
            typeof(SignalListEditorRow).GetProperty(change.Field)!.SetValue(row, change.After);
        var applied = row.ToPoint();
        new Iec10xPointMappingProfile { Points = [applied] }.Validate();
        Assert.Equal("TwoPoint", applied.ScalingMode);
        Assert.Equal(low, applied.Y0);
        Assert.Equal(high, applied.Y100);
        Assert.Equal(2.5, applied.Scale);
        Assert.Equal(2000, applied.RatioPrimary);
        Assert.Equal(point.CommandPolicy, applied.CommandPolicy);
        Assert.Equal(point.Ioa, applied.Ioa);
        Assert.Equal(point.TypeId, applied.TypeId);
        Assert.Equal(1000, neighbor.ToPoint().EngineeringMax);
        Assert.Equal(neighborBefore.CommandPolicy, neighbor.ToPoint().CommandPolicy);

        // Reusing the same reference must not create a phantom edit.
        Assert.Empty(Proposed(row, range!));
        for (var i = changes.Length - 1; i >= 0; i--)
            typeof(SignalListEditorRow).GetProperty(changes[i].Field)!
                .SetValue(row, changes[i].Before);
        var restored = row.ToPoint();
        Assert.Equal(before.ToPoint().ScalingMode, restored.ScalingMode);
        Assert.Equal(before.ToPoint().Unit, restored.Unit);
        Assert.Null(restored.X0);
        Assert.Null(restored.Y100);
        Assert.Equal(1000, neighbor.ToPoint().EngineeringMax);
    }
}
