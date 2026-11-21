// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec101SetupWidthIsolationTests
{
    [Fact]
    public void ModernIec101WidthsAreNeverReplacedByIEC103WhenRestarting()
    {
        var original = new Iec101SetupWidths(2, 2, 2, 3);
        var restored = Iec101SetupWidths.Restore(1, "Iec103", original, out var review);
        Assert.Equal(original, restored);
        Assert.False(review);
    }

    [Theory]
    [InlineData("Iec103")]
    [InlineData("Iec104")]
    [InlineData("Iec101Passive")]
    public void LegacyOtherProtocolProfileDoesNotImpersonateVerifiedIec101(string mode)
    {
        var stale = new Iec101SetupWidths(1, 1, 1, 1);
        var restored = Iec101SetupWidths.Restore(0, mode, stale, out var review);
        Assert.Equal(Iec101SetupWidths.GenericDefaults, restored);
        Assert.True(review);
    }

    [Fact]
    public void LegacyExplicitIec101OneOctetProfileIsNotSilentlyRewritten()
    {
        var original = new Iec101SetupWidths(1, 1, 1, 1);
        Assert.Equal(original, Iec101SetupWidths.Restore(0, "Iec101", original, out var review));
        Assert.False(review);
        Assert.True(original.IsAllOne);
    }

    [Fact]
    public void InvalidSavedWidthsNeverBecomeActiveWithoutReview()
    {
        var restored = Iec101SetupWidths.Restore(1, "Iec101",
            new Iec101SetupWidths(0, 3, 0, 5), out var review);
        Assert.Equal(Iec101SetupWidths.GenericDefaults, restored);
        Assert.True(review);
    }

    [Theory]
    [InlineData("Iec101")]
    [InlineData("Iec103")]
    [InlineData("Iec104")]
    public void UnverifiedModernWidthsRemainUnverifiedAcrossSavedModes(string mode)
    {
        var entered = new Iec101SetupWidths(2, 2, 2, 3);
        var restored = Iec101SetupWidths.Restore(
            2, mode, entered, out var review, persistedReviewRequired: true);
        Assert.Equal(entered, restored);
        Assert.True(review);
    }

    [Fact]
    public void ExplicitlyApprovedModernWidthsRemainStableAcrossModes()
    {
        var entered = new Iec101SetupWidths(1, 2, 2, 3);
        var restored = Iec101SetupWidths.Restore(
            2, "Iec103", entered, out var review, persistedReviewRequired: false);
        Assert.Equal(entered, restored);
        Assert.False(review);
    }

    [Fact]
    public void InvalidOrUnknownSchemaRequiresOperatorReview()
    {
        var invalid = new Iec101SetupWidths(0, 0, 0, 0);
        Assert.Equal(Iec101SetupWidths.GenericDefaults,
            Iec101SetupWidths.Restore(2, "Iec101", invalid, out var invalidReview));
        Assert.True(invalidReview);
        Assert.Equal(Iec101SetupWidths.GenericDefaults,
            Iec101SetupWidths.Restore(99, "Iec101", Iec101SetupWidths.GenericDefaults, out var futureReview));
        Assert.True(futureReview);
    }

    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(1, 2, 2, 3)]
    [InlineData(2, 2, 2, 3)]
    [InlineData(2, 1, 1, 1)]
    public void DifferentLegalIec101WidthsAreAllPreserved(int link, int cot, int ca, int ioa)
    {
        var saved = new Iec101SetupWidths(link, cot, ca, ioa);
        Assert.Equal(saved, Iec101SetupWidths.Restore(1, "Iec104", saved, out var review));
        Assert.False(review);
    }
}
