// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using ARIEC60870.Core.Mapping;
using Xunit;

namespace ARIEC60870.Core.Tests;

public sealed class Iec10xScadaIoListProfileSyncTests
{
    private static Iec10xPointMappingProfile Reference()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var path = Path.Combine(d.FullName, "profiles",
                "utility_fat_iec10x_default_profile.json");
            if (File.Exists(path)) return Iec10xPointMappingProfile.LoadFromFile(path);
        }
        throw new FileNotFoundException("Bundled 31-point profile not found.");
    }

    private static Iec10xPointMappingProfile Copy(Iec10xPointMappingProfile p) =>
        JsonSerializer.Deserialize<Iec10xPointMappingProfile>(JsonSerializer.Serialize(p))!;

    [Fact]
    public void ExplicitSyncReconcilesAll31NamesAndLegacyRawQWithoutMutatingSource()
    {
        var reference = Reference();
        var legacy = Copy(reference);
        legacy.ProfileName = "Operator FAT working profile";
        legacy.DefaultSettings!.LinkAddress = 7;
        foreach (var point in legacy.Points)
        {
            point.Name = "OLD " + point.Name;
            point.BayType = "OLD BAY";
            point.Mnemonic = "OLD";
            point.TestCategory = "OLD";
            if (point.TypeId == 11) point.Unit = "MVar";
        }
        var originalJson = JsonSerializer.Serialize(legacy);
        var result = Iec10xScadaIoListProfileSync.PrepareCopy(legacy, reference);
        Assert.Equal(31, result.MatchedPoints);
        Assert.Equal(31, result.RenamedPoints);
        Assert.Equal(31, result.UpdatedBusinessMetadata);
        Assert.Equal(3, result.ResetUnverifiedReactiveUnits);
        Assert.Equal(originalJson, JsonSerializer.Serialize(legacy));
        Assert.Equal("Operator FAT working profile", result.UpdatedProfile.ProfileName);
        Assert.Equal(7, result.UpdatedProfile.DefaultSettings!.LinkAddress);
        Assert.Equal(reference.Points.Select(p => p.Name),
            result.UpdatedProfile.Points.Select(p => p.Name));

        var second = Iec10xScadaIoListProfileSync.PrepareCopy(result.UpdatedProfile, reference);
        Assert.False(second.HasChanges);
    }

    [Fact]
    public void PreservesFieldCalibrationAndCommandsAndLeavesOtherStationsUntouched()
    {
        var reference = Reference();
        var working = Copy(reference);
        var calibrated = Assert.Single(working.Points, p => p.Ioa == 790447);
        calibrated.Name = "Legacy raw Q name";
        calibrated.Unit = "MVar";
        calibrated.ScalingMode = "TwoPoint";
        calibrated.X0 = -20000;
        calibrated.X100 = 20000;
        calibrated.Y0 = -1000;
        calibrated.Y100 = 1000;

        var factor = Assert.Single(working.Points, p => p.Ioa == 790439);
        factor.Unit = "MVar";
        factor.Scale = 0.125;

        // Make the remaining local identity TI11 Q a *legacy* MVar label.
        // The bundled modern source itself already uses counts.
        Assert.Single(working.Points, p => p.Ioa == 790443).Unit = "MVar";

        var other = Copy(reference).Points.Single(p => p.Ioa == 790443);
        other.Ca = 106;
        other.Name = "OTHER STATION REACTIVE POWER";
        other.Unit = "MVar";
        working.Points.Add(other);

        var command = Assert.Single(working.Points, p => p.Ioa == 68542);
        command.FeedbackIoa = 16712704; // Field-specific command config: do not overwrite.
        var result = Iec10xScadaIoListProfileSync.PrepareCopy(working, reference);
        Assert.Equal(31, result.MatchedPoints);
        Assert.Equal(1, result.ResetUnverifiedReactiveUnits);
        Assert.Equal("OTHER STATION REACTIVE POWER",
            Assert.Single(result.UpdatedProfile.Points, p => p.Ioa == 790443 && p.Ca == 106).Name);
        var q = Assert.Single(result.UpdatedProfile.Points, p => p.Ioa == 790447);
        Assert.Equal("FEEDER TM Q", q.Name);
        Assert.Equal("TwoPoint", q.ScalingMode);
        Assert.Equal(-20000, q.X0);
        Assert.Equal(20000, q.X100);
        Assert.Equal(-1000, q.Y0);
        Assert.Equal(1000, q.Y100);
        Assert.Equal("MVar", q.Unit);
        Assert.Equal(0.125, Assert.Single(result.UpdatedProfile.Points, p => p.Ioa == 790439).Scale);
        Assert.Equal("MVar", Assert.Single(result.UpdatedProfile.Points, p => p.Ioa == 790439).Unit);
        Assert.Equal(16712704, Assert.Single(result.UpdatedProfile.Points, p => p.Ioa == 68542).FeedbackIoa);
        Assert.Equal("DoubleCommandRemoteOnly",
            Assert.Single(result.UpdatedProfile.Points, p => p.Ioa == 68542).CommandPolicy);
    }

    [Fact]
    public void ExplicitlyUpgradesOnlySourceDocumentedLegacyNormalizedIdentity()
    {
        var reference = Reference();
        var old = Copy(reference);
        var poaq = Assert.Single(old.Points, p => p.Ioa == 790449);
        poaq.ScalingMode = "Factor";
        poaq.X0 = null;
        poaq.X100 = null;
        poaq.Y0 = null;
        poaq.Y100 = null;
        poaq.NormalizedFullScale = 1;
        Assert.Equal("MW", poaq.Unit);
        var result = Iec10xScadaIoListProfileSync.PrepareCopy(old, reference);
        Assert.Equal(1, result.AppliedDocumentedNormalizedRanges);
        var updated = Assert.Single(result.UpdatedProfile.Points, p => p.Ioa == 790449);
        Assert.Equal("TwoPoint", updated.ScalingMode);
        Assert.Equal(0, updated.X0);
        Assert.Equal(1, updated.X100);
        Assert.Equal(0, updated.Y0);
        Assert.Equal(1000, updated.Y100);
        Assert.True(Iec10xEngineeringValue.TryFormat(updated,
            new Iec10xEngineeringSample(9, 105, 790449, 0.5, "Normalized=0.5"),
            out var reading));
        Assert.Equal("500 MW", reading);
        Assert.Equal("Factor", poaq.ScalingMode); // Source not mutated.

        // Custom/full-scale configurations must not be accidentally replaced.
        poaq.NormalizedFullScale = 750;
        var calibrated = Iec10xScadaIoListProfileSync.PrepareCopy(old, reference);
        Assert.Equal(0, calibrated.AppliedDocumentedNormalizedRanges);
        var kept = Assert.Single(calibrated.UpdatedProfile.Points, p => p.Ioa == 790449);
        Assert.Equal("Factor", kept.ScalingMode);
        Assert.Equal(750, kept.NormalizedFullScale);
    }

    [Fact]
    public void DifferentCaOrTypeNeverInheritsOtherStationEngineeringOrNames()
    {
        var reference = Reference();
        var mismatched = Copy(reference);
        mismatched.CommonAddress = 106;
        foreach (var p in mismatched.Points)
        {
            p.Ca = 106;
            p.Name = "CUSTOM " + p.Name;
        }
        var r = Iec10xScadaIoListProfileSync.PrepareCopy(mismatched, reference);
        Assert.Equal(0, r.MatchedPoints);
        Assert.False(r.HasChanges);
        Assert.All(r.UpdatedProfile.Points, p => Assert.StartsWith("CUSTOM ", p.Name));

        var wrongType = Copy(reference);
        var q = Assert.Single(wrongType.Points, p => p.Ioa == 790447);
        q.TypeId = 13;
        q.Name = "KEEP WRONG-TYPE TEST";
        var w = Iec10xScadaIoListProfileSync.PrepareCopy(wrongType, reference);
        Assert.Equal(30, w.MatchedPoints);
        Assert.Equal("KEEP WRONG-TYPE TEST",
            Assert.Single(w.UpdatedProfile.Points, p => p.Ioa == 790447).Name);
    }

    [Fact]
    public void InvalidOrUnrelatedReferenceIsRefused()
    {
        var reference = Reference();
        var wrong = Copy(reference);
        wrong.Points.RemoveAt(0);
        Assert.Throws<InvalidOperationException>(() =>
            Iec10xScadaIoListProfileSync.PrepareCopy(reference, wrong));
    }
}
