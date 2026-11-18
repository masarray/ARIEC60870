// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class SignalMapActivationPolicyTests
{
    [Fact]
    public void LoadingAndSavingOnlyStagesNewProfileUntilExplicitApply()
    {
        var policy = new SignalMapActivationPolicy();
        Assert.False(policy.HasUnappliedProfile);
        Assert.False(policy.MustConfirmDiscard(hasUnsavedEdits: false, applied: false));

        policy.PreviewLoaded();
        Assert.True(policy.HasUnappliedProfile);
        Assert.True(policy.MustConfirmDiscard(hasUnsavedEdits: false, applied: false));

        // A normal Save has no authority to reproject live Values.
        policy.SavedWithoutApply();
        Assert.True(policy.HasUnappliedProfile);
        Assert.True(policy.MustConfirmDiscard(hasUnsavedEdits: false, applied: false));
        Assert.False(policy.MustConfirmDiscard(hasUnsavedEdits: false, applied: true));

        policy.Applied();
        Assert.False(policy.HasUnappliedProfile);
        Assert.False(policy.MustConfirmDiscard(hasUnsavedEdits: false, applied: true));
        Assert.True(policy.MustConfirmDiscard(hasUnsavedEdits: true, applied: false));
    }

    [Fact]
    public void ExplicitApplyCanActivateCleanReadOnlyReferenceWithoutRewritingIt()
    {
        var file = Path.Combine(Path.GetTempPath(),
            "arsee-activation-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, "original bytes");
            Assert.True(SignalMapActivationPolicy.CanApplyExistingFileWithoutWrite(
                explicitApply: true, dirty: false, forceSaveAs: false,
                protectedSourcePath: null, targetPath: file));
            Assert.False(SignalMapActivationPolicy.CanApplyExistingFileWithoutWrite(
                explicitApply: false, dirty: false, forceSaveAs: false,
                protectedSourcePath: null, targetPath: file));
            Assert.False(SignalMapActivationPolicy.CanApplyExistingFileWithoutWrite(
                explicitApply: true, dirty: true, forceSaveAs: false,
                protectedSourcePath: null, targetPath: file));
            Assert.False(SignalMapActivationPolicy.CanApplyExistingFileWithoutWrite(
                explicitApply: true, dirty: false, forceSaveAs: true,
                protectedSourcePath: null, targetPath: file));
            Assert.False(SignalMapActivationPolicy.CanApplyExistingFileWithoutWrite(
                explicitApply: true, dirty: false, forceSaveAs: false,
                protectedSourcePath: file, targetPath: file));
            Assert.Equal("original bytes", File.ReadAllText(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(@"C:\reference\utility_fat_iec10x_default_profile.json", true)]
    [InlineData(@"C:\site\other_user_profile.json", false)]
    [InlineData("", false)]
    public void PackagedReferenceNameCannotBeOverwrittenByEditedDraft(
        string path, bool expected)
    {
        Assert.Equal(expected, SignalMapActivationPolicy.IsBundledReferencePath(path));
    }
}
