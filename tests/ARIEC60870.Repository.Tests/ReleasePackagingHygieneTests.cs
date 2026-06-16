// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace ARIEC60870.Repository.Tests;

public sealed class ReleasePackagingHygieneTests
{
    [Fact]
    public void PublicReleaseWorkflowBuildsOneUserFacingWindowsZip()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(root.File(".github/workflows/release-package.yml"));

        Assert.Contains("Build Windows single-file release", workflow);
        Assert.Contains("ARIEC60870-v${{ needs.resolve.outputs.version }}-win-x64.zip", workflow);
        Assert.DoesNotContain("win-x64-portable.zip", workflow);
        Assert.DoesNotContain("win-x64-singlefile.zip", workflow);
        Assert.DoesNotContain("build_singlefile", workflow);
    }

    [Fact]
    public void PublicPackagingScriptDoesNotCreateBatchLaunchersOrCliExecutables()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(root.File("scripts/publish-windows-portable.ps1"));

        Assert.Contains("ARIEC60870.exe", script);
        Assert.Contains("PublishSingleFile=true", script);
        Assert.Contains("exactly one executable", script);
        Assert.DoesNotContain("Start-ARIEC60870", script);
        Assert.DoesNotContain("Open-CLI-Help", script);
        Assert.DoesNotContain("ARIEC60870.Cli.csproj", script);
        Assert.DoesNotContain("ARIEC60870.Cli.exe", script);
    }

    [Fact]
    public void ReleaseVerifierRejectsBatchLaunchersAndMultipleExecutables()
    {
        var root = FindRepositoryRoot();
        var verifier = File.ReadAllText(root.File("scripts/verify-release-package.ps1"));

        Assert.Contains("ARIEC60870.exe", verifier);
        Assert.Contains("README_RELEASE.txt", verifier);
        Assert.Contains("docs/USER_GUIDE.md", verifier);
        Assert.Contains("must not include batch launchers", verifier);
        Assert.Contains("exactly one executable", verifier);
    }

    [Fact]
    public void PublicDocsTellUsersToRunTheExeDirectly()
    {
        var root = FindRepositoryRoot();
        var readme = File.ReadAllText(root.File("README.md"));
        var quickStart = File.ReadAllText(root.File("docs/QUICK_START.md"));
        var releasePackaging = File.ReadAllText(root.File("docs/RELEASE_PACKAGING.md"));

        foreach (var text in new[] { readme, quickStart, releasePackaging })
        {
            Assert.Contains("ARIEC60870.exe", text);
            Assert.DoesNotContain("Start-ARIEC60870", text);
            Assert.DoesNotContain("Open-CLI-Help", text);
            Assert.DoesNotContain("win-x64-portable", text);
            Assert.DoesNotContain("win-x64-singlefile", text);
        }
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ARIEC60870.sln")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return current!;
    }
}

file static class ReleasePackagingDirectoryInfoExtensions
{
    public static string File(this DirectoryInfo directory, string relativePath)
        => Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
