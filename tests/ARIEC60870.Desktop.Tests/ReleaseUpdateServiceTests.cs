// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using ARIEC60870.Desktop.Services;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class ReleaseUpdateServiceTests
{
    [Theory]
    [InlineData("v3.6.9", true)]
    [InlineData("v13.2.1", true)]
    [InlineData("v3.6.8-rc.7", false)]
    [InlineData("v3.6.8+debug", false)]
    [InlineData("3.6.8", false)]
    [InlineData("v3.6.8/other", false)]
    [InlineData("v3.6.8.0", false)]
    [InlineData("v4.0", false)]
    public void OnlyExactStableReleaseTagsCanBeInstalled(string tag, bool allowed)
    {
        Assert.Equal(allowed, ReleaseUpdateService.TryParseStableTag(tag, out _));
    }

    [Theory]
    [InlineData("https://github.com/masarray/ARIEC60870/releases/download/v3.6.9/ARIEC60870-v3.6.9-setup-win-x64.exe", true)]
    [InlineData("http://github.com/masarray/ARIEC60870/releases/download/v3.6.9/ARIEC60870-v3.6.9-setup-win-x64.exe", false)]
    [InlineData("https://evil.example/releases/download/v3.6.9/ARIEC60870-v3.6.9-setup-win-x64.exe", false)]
    [InlineData("https://github.com/masarray/other/releases/download/v3.6.9/ARIEC60870-v3.6.9-setup-win-x64.exe", false)]
    [InlineData("https://github.com/masarray/ARIEC60870/releases/download/v3.6.9/other.exe", false)]
    [InlineData("https://github.com/masarray/ARIEC60870/releases/download/v3.6.9/ARIEC60870-v3.6.9-setup-win-x64.exe?mirror=1", false)]
    public void InstallerIsOnlyFetchedFromExactOfficialGithubRelease(string url, bool allowed)
    {
        Assert.Equal(allowed, ReleaseUpdateService.IsOfficialAssetUrl(url,
            "v3.6.9", "ARIEC60870-v3.6.9-setup-win-x64.exe"));
    }

    [Fact]
    public void ChecksumMustMatchTheExactInstallerFileName()
    {
        var correct = new string('a', 64);
        var other = new string('b', 64);
        var sums = other + "  other.exe\n" +
                   correct + "  ARIEC60870-v3.6.9-setup-win-x64.exe\n";
        Assert.Equal(correct, ReleaseUpdateService.FindDigest(sums, "ARIEC60870-v3.6.9-setup-win-x64.exe"));
        Assert.Null(ReleaseUpdateService.FindDigest(sums, "ARIEC60870-v3.6.8-setup-win-x64.exe"));
        Assert.Null(ReleaseUpdateService.FindDigest("untrusted", "ARIEC60870-v3.6.9-setup-win-x64.exe"));
    }

    [Fact]
    public async Task InstallerHashAndLengthBothMustMatch()
    {
        var path = Path.Combine(Path.GetTempPath(), "arsee-update-hash-" + Guid.NewGuid().ToString("N"));
        try
        {
            var content = new byte[] { 1, 2, 3, 4, 5 };
            await File.WriteAllBytesAsync(path, content);
            var digest = Convert.ToHexString(SHA256.HashData(content));
            Assert.True(await ReleaseUpdateService.VerifyFileAsync(path, digest, 5, CancellationToken.None));
            Assert.False(await ReleaseUpdateService.VerifyFileAsync(path, digest, 4, CancellationToken.None));
            Assert.False(await ReleaseUpdateService.VerifyFileAsync(path, new string('0', 64), 5, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PortableCopiesAreNeverSilentlyReplaced()
    {
        var folder = Path.Combine(Path.GetTempPath(), "arsee-update-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var executable = Path.Combine(folder, "ARIEC60870.exe");
            File.WriteAllText(executable, "fixture");
            Assert.False(ReleaseUpdateService.HasManagedInstallation(executable));
            File.WriteAllText(Path.Combine(folder, "unins000.exe"), "fixture");
            File.WriteAllText(Path.Combine(folder, "unins000.dat"), "fixture");
            Assert.True(ReleaseUpdateService.HasManagedInstallation(executable));
            Assert.False(ReleaseUpdateService.HasManagedInstallation(Path.Combine(folder, "other.exe")));
        }
        finally { Directory.Delete(folder, true); }
    }
}
