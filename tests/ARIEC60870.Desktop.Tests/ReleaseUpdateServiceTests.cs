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
    public void UpdateStartupTokenIsUniqueAndCannotBeSpoofedByAnotherProcessOrVersion()
    {
        var token = UpdateStartupHandshake.NewToken();
        var other = UpdateStartupHandshake.NewToken();
        Assert.NotEqual(token, other);
        Assert.True(UpdateStartupHandshake.IsValidToken(token));
        Assert.False(UpdateStartupHandshake.IsValidToken(token[..47]));
        Assert.False(UpdateStartupHandshake.IsValidToken(new string('z', 48)));

        var record = new UpdateStartupAck(1234, token, "3.6.9.0");
        var json = System.Text.Json.JsonSerializer.Serialize(record);
        Assert.True(UpdateStartupHandshake.IsExpectedAck(json, 1234, token, new Version(3, 6, 9)));
        Assert.False(UpdateStartupHandshake.IsExpectedAck(json, 5678, token, new Version(3, 6, 9)));
        Assert.False(UpdateStartupHandshake.IsExpectedAck(json, 1234, other, new Version(3, 6, 9)));
        Assert.False(UpdateStartupHandshake.IsExpectedAck(json, 1234, token, new Version(3, 7, 0)));
        Assert.False(UpdateStartupHandshake.IsExpectedAck("{broken", 1234, token, new Version(3, 6, 9)));
        Assert.False(UpdateStartupHandshake.IsExpectedAck(new string(' ', 2048), 1234, token, new Version(3, 6, 9)));
    }

    [Fact]
    public void StartupAcknowledgementCannotWriteArbitraryFileOutsideUpdateStaging()
    {
        var root = ReleaseUpdateService.UpdatesRoot;
        Assert.True(UpdateStartupHandshake.IsValidAckPath(
            Path.Combine(root, "v3.6.9", UpdateStartupHandshake.AckFileName)));
        Assert.False(UpdateStartupHandshake.IsValidAckPath(
            Path.Combine(root, "v3.6.9", "any-other-name.json")));
        Assert.False(UpdateStartupHandshake.IsValidAckPath(
            Path.Combine(Path.GetTempPath(), "v3.6.9", UpdateStartupHandshake.AckFileName)));
        Assert.False(UpdateStartupHandshake.IsValidAckPath(
            Path.Combine(root, "v3.6.9-rc.1", UpdateStartupHandshake.AckFileName)));
    }

    [Theory]
    [InlineData(0L, 128L, 128L)]
    [InlineData(500L, 128L, 628L)]
    public void UpdateSpaceRequirementIncludesExplicitReserve(long data, long reserve, long expected)
    {
        Assert.Equal(expected, UpdateStorageSafety.RequiredFreeBytes(data, reserve));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UpdateStorageSafety.RequiredFreeBytes(-1, reserve));
        Assert.Throws<OverflowException>(() =>
            UpdateStorageSafety.RequiredFreeBytes(long.MaxValue, reserve));
    }

    [Fact]
    public void BackupInventoryIncludesNestedFilesAndCountsBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "arsee-safe-backup-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "plugins"));
        try
        {
            File.WriteAllBytes(Path.Combine(root, "main.exe"), new byte[11]);
            File.WriteAllBytes(Path.Combine(root, "plugins", "plugin.dll"), new byte[7]);
            var files = UpdateStorageSafety.EnumerateSafeFiles(root);
            Assert.Equal(2, files.Count);
            Assert.Equal(18, UpdateStorageSafety.TotalBytes(files));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void BackupInventoryRefusesSymlinkDirectoryWithoutTraversingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "arsee-symlink-backup-" +
            Guid.NewGuid().ToString("N"));
        var external = Path.Combine(Path.GetTempPath(), "arsee-symlink-outside-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(external);
        try
        {
            File.WriteAllText(Path.Combine(external, "outside.txt"), "never-copy");
            var link = Path.Combine(root, "linked-external");
            try
            {
                Directory.CreateSymbolicLink(link, external);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Windows machines without developer mode may deny symlink creation.
                return;
            }
            Assert.Throws<IOException>(() => UpdateStorageSafety.EnumerateSafeFiles(root));
            Assert.Throws<IOException>(() => UpdateStorageSafety.RejectLinkedDirectory(link));
        }
        finally
        {
            // Never recursively delete through the link to external content.
            var link = Path.Combine(root, "linked-external");
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, true);
            Directory.Delete(external, true);
        }
    }

    [Fact]
    public void FailedUpgradeRestoresExactPreviousInnoRegistryAndRemovesNewValues()
    {
        if (!OperatingSystem.IsWindows()) return;
        var keyPath = @"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\ArSee-Test-" +
            Guid.NewGuid().ToString("N");
        var folder = Path.Combine(Path.GetTempPath(), "arsee-reg-rollback-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "ARIEC60870.exe");
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath)!)
            {
                key.SetValue("DisplayName", "ArSee");
                key.SetValue("InstallLocation", folder);
                key.SetValue("DisplayVersion", "3.6.7");
                key.SetValue("UninstallString", Path.Combine(folder, "unins000.exe"));
                key.SetValue("EstimatedSize", 154,
                    Microsoft.Win32.RegistryValueKind.DWord);
            }
            var original = InnoRegistrationSnapshot.Capture(exe);
            Assert.Equal("3.6.7", original.PreviousDisplayVersion);
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, true)!)
            {
                key.SetValue("DisplayVersion", "3.6.8");
                key.SetValue("UninstallString", "wrong-uninstaller.exe");
                key.SetValue("EstimatedSize", 999,
                    Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("FailedUpdateResidue", "remove-me");
            }
            Assert.True(original.IsRegisteredVersion(exe, new Version(3, 6, 8)));
            original.Restore(exe);
            using var verified = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath)!;
            Assert.Equal("3.6.7", verified.GetValue("DisplayVersion"));
            Assert.Equal(Path.Combine(folder, "unins000.exe"),
                verified.GetValue("UninstallString"));
            Assert.Equal(154, verified.GetValue("EstimatedSize"));
            Assert.Equal(Microsoft.Win32.RegistryValueKind.DWord,
                verified.GetValueKind("EstimatedSize"));
            Assert.Null(verified.GetValue("FailedUpdateResidue"));
            Assert.False(original.IsRegisteredVersion(exe, new Version(3, 6, 8)));
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(keyPath,
                throwOnMissingSubKey: false);
            Directory.Delete(folder, true);
        }
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
