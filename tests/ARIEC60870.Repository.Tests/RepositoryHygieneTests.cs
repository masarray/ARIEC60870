// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ARIEC60870.Repository.Tests;

public sealed class RepositoryHygieneTests
{
    [Fact]
    public void DirectoryBuildVersionIsReflectedInPublicSiteAndManifest()
    {
        var root = FindRepositoryRoot();
        var props = XDocument.Load(root.File("Directory.Build.props"));
        var version = props.Descendants("Version").FirstOrDefault()?.Value;

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Contains(version!, File.ReadAllText(root.File("site/index.html")));
        Assert.Contains("IEC 60870", File.ReadAllText(root.File("site/site.webmanifest")));
    }

    [Fact]
    public void RequiredPublicRepositoryFilesExist()
    {
        var root = FindRepositoryRoot();
        var required = new[]
        {
            "README.md",
            "CHANGELOG.md",
            "LICENSE",
            "NOTICE",
            "THIRD_PARTY_NOTICES.md",
            "CONTRIBUTING.md",
            "SECURITY.md",
            "SUPPORT.md",
            "CODE_OF_CONDUCT.md",
            "docs/QUICK_START.md",
            "docs/TROUBLESHOOTING.md",
            "docs/VALIDATION_MATRIX.md",
            "site/index.html",
            "site/sitemap.xml",
            "site/robots.txt",
            "site/site.webmanifest",
            ".github/PULL_REQUEST_TEMPLATE.md",
            ".github/dependabot.yml",
            ".github/workflows/release-package.yml",
            ".github/workflows/scorecard.yml"
        };

        foreach (var path in required)
        {
            Assert.True(File.Exists(root.File(path)), $"Missing required repository file: {path}");
        }
    }

    [Fact]
    public void ReadmeDocumentationLinksPointToExistingFiles()
    {
        var root = FindRepositoryRoot();
        var readme = File.ReadAllText(root.File("README.md"));
        var links = Regex.Matches(readme, @"\]\((?<path>(docs/|samples/|CHANGELOG\.md|CONTRIBUTING\.md|SECURITY\.md|LICENSE|NOTICE|THIRD_PARTY_NOTICES\.md)[^)#]+)(#[^)]+)?\)")
            .Select(match => match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(links);
        foreach (var link in links)
        {
            Assert.True(File.Exists(root.File(link)), $"README link points to a missing file: {link}");
        }
    }

    [Fact]
    public void ReleaseWorkflowUsesLeastPrivilegeTopLevelPermissions()
    {
        var root = FindRepositoryRoot();
        var releaseWorkflow = File.ReadAllText(root.File(".github/workflows/release-package.yml"));

        Assert.Contains("permissions:\n  contents: read", releaseWorkflow);
        Assert.Contains("contents: write", releaseWorkflow);
        Assert.Contains("attestations: write", releaseWorkflow);
        Assert.Contains("Generate SPDX dependency SBOM", releaseWorkflow);
    }

    [Fact]
    public void PublicSiteDoesNotContainRawScreenshotFolder()
    {
        var root = FindRepositoryRoot();

        Assert.False(Directory.Exists(root.File("site/screenshot")), "site/screenshot should not be committed.");
        Assert.False(Directory.Exists(root.File("landing/screenshot")), "landing/screenshot should not be committed.");
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

file static class DirectoryInfoExtensions
{
    public static string File(this DirectoryInfo directory, string relativePath)
        => Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
