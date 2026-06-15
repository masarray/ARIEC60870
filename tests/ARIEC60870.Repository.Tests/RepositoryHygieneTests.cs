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
            "docs/DESKTOP_ARCHITECTURE_CLEANUP.md",
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


    [Fact]
    public void DesktopCodeBehindIsSplitIntoFeatureOwnedPartials()
    {
        var root = FindRepositoryRoot();
        var desktop = root.File("src/ARIEC60870.Desktop");
        var shell = Path.Combine(desktop, "MainWindow.xaml.cs");
        var features = Path.Combine(desktop, "Features");

        Assert.True(File.Exists(shell), "MainWindow shell file is missing.");
        Assert.True(Directory.Exists(features), "Desktop feature partial folder is missing.");

        var expectedFeatureFiles = new[]
        {
            "MainWindow.CommandDock.cs",
            "MainWindow.SetupPreferences.cs",
            "MainWindow.Session.cs",
            "MainWindow.RuntimeProof.cs",
            "MainWindow.LiveEvidencePipeline.cs",
            "MainWindow.FrameInspector.cs",
            "MainWindow.WorkspaceSelection.cs",
            "MainWindow.MappingValueStatus.cs",
            "MainWindow.CaptureFiles.cs",
            "MainWindow.TriggerCapture.cs",
            "MainWindow.Reporting.cs",
            "MainWindow.Export.cs"
        };

        foreach (var fileName in expectedFeatureFiles)
        {
            Assert.True(File.Exists(Path.Combine(features, fileName)), $"Missing desktop feature partial: {fileName}");
        }

        var shellLineCount = File.ReadLines(shell).Count();
        Assert.InRange(shellLineCount, 1, 1200);

        foreach (var featureFile in Directory.EnumerateFiles(features, "MainWindow.*.cs"))
        {
            var lineCount = File.ReadLines(featureFile).Count();
            Assert.InRange(lineCount, 40, 1600);
        }
    }

    [Fact]
    public void DesktopLocalRuntimePathsAreCentralized()
    {
        var root = FindRepositoryRoot();
        var desktop = root.File("src/ARIEC60870.Desktop");
        var pathService = Path.Combine(desktop, "Services", "LocalWorkspacePaths.cs");
        var mainWindowFiles = Directory
            .EnumerateFiles(desktop, "MainWindow*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("LocalWorkspacePaths.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(File.Exists(pathService), "Local workspace path service is missing.");
        Assert.Contains("SetupPreferencesFile", File.ReadAllText(pathService));
        Assert.Contains("TriggerCaptureFolder", File.ReadAllText(pathService));

        foreach (var file in mainWindowFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("SpecialFolder.LocalApplicationData", content);
            Assert.DoesNotContain("setup-preferences.json", content);
        }
    }

    [Fact]
    public void DesktopPublicRowsLiveInViewModelsInsteadOfMainWindowShell()
    {
        var root = FindRepositoryRoot();
        var desktop = root.File("src/ARIEC60870.Desktop");
        var shell = File.ReadAllText(Path.Combine(desktop, "MainWindow.xaml.cs"));

        Assert.True(File.Exists(Path.Combine(desktop, "ViewModels", "StatusHistoryRow.cs")));
        Assert.True(File.Exists(Path.Combine(desktop, "ViewModels", "TriggerCaptureRow.cs")));
        Assert.DoesNotContain("public sealed record StatusHistoryRow", shell);
        Assert.DoesNotContain("public sealed record TriggerCaptureRow", shell);
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
