// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
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
            "docs/TESTING_STRATEGY.md",
            "docs/GITHUB_SECURITY_AUTOMATION.md",
            "tests/README.md",
            "tests/ARIEC60870.Core.Tests/ARIEC60870.Core.Tests.csproj",
            "tests/ARIEC60870.Master.Tests/ARIEC60870.Master.Tests.csproj",
            "tests/ARIEC60870.Reporting.Tests/ARIEC60870.Reporting.Tests.csproj",
            "tests/ARIEC60870.Desktop.Tests/ARIEC60870.Desktop.Tests.csproj",
            "site/index.html",
            "site/sitemap.xml",
            "site/robots.txt",
            "site/site.webmanifest",
            "site/seo-manifest.json",
            "site/llms.txt",
            "site/humans.txt",
            "docs/index.html",
            "docs/sitemap.xml",
            "docs/robots.txt",
            "docs/site.webmanifest",
            "docs/.nojekyll",
            "docs/.pages-compatibility-mirror",
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
    public void DependabotConfigurationScansActualNugetManifestDirectories()
    {
        var root = FindRepositoryRoot();
        var dependabot = NormalizeNewlines(File.ReadAllText(root.File(".github/dependabot.yml")));

        Assert.Contains("package-ecosystem: \"github-actions\"", dependabot);
        Assert.Contains("package-ecosystem: \"nuget\"", dependabot);
        Assert.Contains("directories:", dependabot);
        Assert.Contains("/src/ARIEC60870.Master", dependabot);
        Assert.Contains("/tests/ARIEC60870.Core.Tests", dependabot);
        Assert.Contains("/tests/ARIEC60870.Repository.Tests", dependabot);
        Assert.Contains("dotnet-test-tooling", dependabot);
        Assert.Contains("github-actions-minor-patch", dependabot);
        Assert.Contains("version-update:semver-major", dependabot);
        Assert.DoesNotContain("github-actions-major", dependabot);
        Assert.DoesNotContain("dotnet-major-updates", dependabot);
        Assert.DoesNotContain("commit-message:\n      prefix: \"deps(actions)\"", dependabot);
    }

    [Fact]
    public void DependencyReviewWorkflowDoesNotFailWhenDependencyGraphIsNotEnabledYet()
    {
        var root = FindRepositoryRoot();
        var dependencyReview = NormalizeNewlines(File.ReadAllText(root.File(".github/workflows/dependency-review.yml")));

        Assert.Matches(@"uses: actions/dependency-review-action@v[0-9]+", dependencyReview);
        Assert.Contains("if: ${{ vars.ENABLE_DEPENDENCY_REVIEW == 'true' }}", dependencyReview);
        Assert.Contains("Dependency Review is waiting for repository enablement", dependencyReview);
        Assert.Contains("ENABLE_DEPENDENCY_REVIEW=true", dependencyReview);
        Assert.Contains("Dependency Graph", dependencyReview);
    }

    [Fact]
    public void OpenSsfScorecardWorkflowFollowsPublishingRestrictions()
    {
        var root = FindRepositoryRoot();
        var scorecard = NormalizeNewlines(File.ReadAllText(root.File(".github/workflows/scorecard.yml")));

        Assert.Contains("permissions:\n  contents: read", scorecard);
        Assert.Contains("security-events: write", scorecard);
        Assert.Contains("id-token: write", scorecard);
        Assert.Contains("publish_results: true", scorecard);
        Assert.Matches(@"ossf/scorecard-action@v[0-9]+(\.[0-9]+){0,2}", scorecard);
        Assert.DoesNotContain("branch_protection_rule", scorecard);
        Assert.DoesNotContain("workflow_dispatch", scorecard);
        Assert.DoesNotContain("permissions:\n  contents: read\n  security-events: write\n  id-token: write", scorecard);
    }

    [Fact]
    public void ReleaseWorkflowUsesLeastPrivilegeTopLevelPermissions()
    {
        var root = FindRepositoryRoot();
        var releaseWorkflow = NormalizeNewlines(File.ReadAllText(root.File(".github/workflows/release-package.yml")));

        Assert.Contains("permissions:\n  contents: read", releaseWorkflow);
        Assert.Contains("contents: write", releaseWorkflow);
        Assert.Contains("attestations: write", releaseWorkflow);
        Assert.Contains("Generate SPDX dependency SBOM", releaseWorkflow);
    }

    [Fact]
    public void CiPublishesTrxCoverageAndSmokeArtifacts()
    {
        var root = FindRepositoryRoot();
        var ciWorkflow = NormalizeNewlines(File.ReadAllText(root.File(".github/workflows/ci.yml")));

        Assert.Contains("protocol-smoke-tests.log", ciWorkflow);
        Assert.Contains("--logger", ciWorkflow);
        Assert.Contains("XPlat Code Coverage", ciWorkflow);
        Assert.Contains("ARIEC60870.Core.Tests", ciWorkflow);
        Assert.Contains("ARIEC60870.Master.Tests", ciWorkflow);
        Assert.Contains("ARIEC60870.Reporting.Tests", ciWorkflow);
        Assert.Contains("ARIEC60870.Desktop.Tests", ciWorkflow);
    }

    [Fact]
    public void SolutionIncludesAllFirstClassTestProjects()
    {
        var root = FindRepositoryRoot();
        var solution = File.ReadAllText(root.File("ARIEC60870.sln"));
        var expected = new[]
        {
            @"tests\ARIEC60870.Protocol.Tests\ARIEC60870.Protocol.Tests.csproj",
            @"tests\ARIEC60870.Repository.Tests\ARIEC60870.Repository.Tests.csproj",
            @"tests\ARIEC60870.Core.Tests\ARIEC60870.Core.Tests.csproj",
            @"tests\ARIEC60870.Master.Tests\ARIEC60870.Master.Tests.csproj",
            @"tests\ARIEC60870.Reporting.Tests\ARIEC60870.Reporting.Tests.csproj",
            @"tests\ARIEC60870.Desktop.Tests\ARIEC60870.Desktop.Tests.csproj"
        };

        foreach (var path in expected)
        {
            Assert.Contains(path, solution);
        }
    }

    [Fact]
    public void PublicSiteDoesNotContainRawScreenshotFolder()
    {
        var root = FindRepositoryRoot();

        Assert.False(Directory.Exists(root.File("site/screenshot")), "site/screenshot should not be committed.");
        Assert.False(Directory.Exists(root.File("landing/screenshot")), "landing/screenshot should not be committed.");
    }

    [Fact]
    public void PublicSiteKeepsCanonicalSourceAndDocsCompatibilityMirror()
    {
        var root = FindRepositoryRoot();
        var forbiddenLegacySource = new[]
        {
            "landing/index.html",
            "landing/styles.css",
            "landing/script.js",
            "landing/sitemap.xml",
            "landing/robots.txt"
        };

        foreach (var mirror in forbiddenLegacySource)
        {
            Assert.False(File.Exists(root.File(mirror)), $"Legacy landing source should not be committed: {mirror}");
        }

        var mirroredRuntimeFiles = new[]
        {
            "index.html",
            "404.html",
            "styles.css",
            "script.js",
            "robots.txt",
            "sitemap.xml",
            "site.webmanifest",
            "seo-manifest.json",
            "llms.txt",
            "humans.txt",
            "download.html",
            "faq.html",
            "protocol-coverage.html",
            "quick-start.html",
            "troubleshooting.html"
        };

        Assert.True(File.Exists(root.File("docs/.pages-compatibility-mirror")), "docs/ compatibility mirror marker is missing.");
        foreach (var file in mirroredRuntimeFiles)
        {
            var siteFile = root.File($"site/{file}");
            var docsFile = root.File($"docs/{file}");
            Assert.True(File.Exists(siteFile), $"Canonical site file is missing: {siteFile}");
            Assert.True(File.Exists(docsFile), $"/docs GitHub Pages compatibility file is missing: {docsFile}");
            Assert.Equal(File.ReadAllText(siteFile), File.ReadAllText(docsFile));
        }

        Assert.True(Directory.Exists(root.File("docs/assets")), "/docs GitHub Pages compatibility assets are missing.");

        var pagesWorkflow = NormalizeNewlines(File.ReadAllText(root.File(".github/workflows/pages.yml")));
        Assert.Contains("name: Pages", pagesWorkflow);
        Assert.Contains("path: site", pagesWorkflow);
        Assert.DoesNotContain("path: landing", pagesWorkflow);
    }

    [Fact]
    public void SeoManifestSitemapAndHtmlCanonicalUrlsStayAligned()
    {
        var root = FindRepositoryRoot();
        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(root.File("site/seo-manifest.json")));
        var manifest = manifestDocument.RootElement;
        var sitemap = File.ReadAllText(root.File("site/sitemap.xml"));

        Assert.Equal("site/", manifest.GetProperty("sourceOfTruth").GetString());
        Assert.Equal("https://masarray.github.io/ARIEC60870/", manifest.GetProperty("canonicalBaseUrl").GetString());

        foreach (var page in manifest.GetProperty("pages").EnumerateArray())
        {
            var path = page.GetProperty("path").GetString();
            var canonical = page.GetProperty("canonical").GetString();
            var title = page.GetProperty("title").GetString();
            var description = page.GetProperty("description").GetString();

            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.False(string.IsNullOrWhiteSpace(canonical));
            Assert.False(string.IsNullOrWhiteSpace(title));
            Assert.False(string.IsNullOrWhiteSpace(description));

            var htmlPath = root.File($"site/{path}");
            Assert.True(File.Exists(htmlPath), $"SEO manifest page is missing: {path}");

            var html = File.ReadAllText(htmlPath);
            Assert.Contains($"<link rel=\"canonical\" href=\"{canonical}\"", html);
            Assert.Contains("property=\"og:title\"", html);
            Assert.Contains("property=\"og:description\"", html);
            Assert.Contains(canonical!, sitemap);
        }
    }

    [Fact]
    public void SiteProvidesExplicitIconsAndMachineReadableDiscoveryFiles()
    {
        var root = FindRepositoryRoot();
        var manifest = File.ReadAllText(root.File("site/site.webmanifest"));
        var robots = File.ReadAllText(root.File("site/robots.txt"));
        var llms = File.ReadAllText(root.File("site/llms.txt"));

        Assert.True(File.Exists(root.File("site/assets/brand/favicon.ico")));
        Assert.True(File.Exists(root.File("site/assets/brand/ariec60870-icon-180.png")));
        Assert.True(File.Exists(root.File("site/assets/brand/ariec60870-icon-192.png")));
        Assert.True(File.Exists(root.File("site/assets/brand/ariec60870-icon-512.png")));
        Assert.Contains("ariec60870-icon-192.png", manifest);
        Assert.Contains("Sitemap: https://masarray.github.io/ARIEC60870/sitemap.xml", robots);
        Assert.Contains("IEC 60870-5-101", llms);
        Assert.Contains("IEC 60870-5-104", llms);
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

    [Fact]
    public void ReportWorkspaceExportsPdfDirectlyWithoutHtmlPrintWorkflowLanguage()
    {
        var root = FindRepositoryRoot();
        var activeFiles = Directory
            .EnumerateFiles(root.FullName, "*.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}docs{Path.DirectorySeparatorChar}archive{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var file in activeFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Export HTML" + " / PDF", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("standalone" + " HTML", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("print" + " to PDF", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("browser" + " print", text, StringComparison.OrdinalIgnoreCase);
        }

        var xaml = File.ReadAllText(root.File("src/ARIEC60870.Desktop/MainWindow.xaml"));
        Assert.Contains("Export PDF", xaml, StringComparison.Ordinal);
        Assert.Contains("ExportPdf_Click", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ExportMarkdown_Click", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfEngineIsNativeAndDependencyFree()
    {
        var root = FindRepositoryRoot();
        var desktopProject = File.ReadAllText(root.File("src/ARIEC60870.Desktop/ARIEC60870.Desktop.csproj"));
        var notices = File.ReadAllText(root.File("THIRD_PARTY_NOTICES.md"));
        var service = File.ReadAllText(root.File("src/ARIEC60870.Desktop/Reporting/EvidencePdfReportService.cs"));

        var forbiddenPdfPackage = new string(new[] { 'Q', 'u', 'e', 's', 't', 'P', 'D', 'F' });
        Assert.DoesNotContain("PackageReference Include=\"" + forbiddenPdfPackage + "\"", desktopProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(forbiddenPdfPackage, notices, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(forbiddenPdfPackage, service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LicenseType.Community", service, StringComparison.Ordinal);
        Assert.Contains("NativePdfDocument", service, StringComparison.Ordinal);
        Assert.Contains("xref", service, StringComparison.Ordinal);
        Assert.Contains("built-in native PDF engine", notices, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicBrandingAndRoadmapAreCurrent()
    {
        var root = FindRepositoryRoot();
        var activeFiles = new[]
        {
            "README.md",
            "Directory.Build.props",
            "docs/ARCHITECTURE.md",
            "docs/ROADMAP.md",
            "docs/GITHUB_SECURITY_AUTOMATION.md",
            "site/index.html",
            "site/seo-manifest.json",
            "scripts/generate-sbom-lite.ps1",
            "src/ARIEC60870.Desktop/MainWindow.xaml",
            "src/ARIEC60870.Desktop/Reporting/EvidencePdfReportService.cs"
        };

        var legacyName = "Protocol" + " Lab";
        foreach (var file in activeFiles)
        {
            var text = File.ReadAllText(root.File(file));
            Assert.DoesNotContain(legacyName, text, StringComparison.OrdinalIgnoreCase);
        }

        var readme = File.ReadAllText(root.File("README.md"));
        Assert.Contains("ARIEC60870 Evidence Analyzer", readme, StringComparison.Ordinal);
        Assert.Contains("[![CI]", readme, StringComparison.Ordinal);
        Assert.Contains("[![Pages]", readme, StringComparison.Ordinal);
        Assert.Contains("[![Package]", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("api.scorecard.dev", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("img.shields.io/github/v/release", readme, StringComparison.OrdinalIgnoreCase);

        var roadmap = File.ReadAllText(root.File("docs/ROADMAP.md"));
        Assert.Contains("Native PDF evidence report | Implemented baseline", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain("PDF evidence report | Low", roadmap, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("planned next", roadmap, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Basic PDF evidence export is available", roadmap, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicSiteKeepsRichStructuredDataForSearchEngines()
    {
        var root = FindRepositoryRoot();
        var site = File.ReadAllText(root.File("site/index.html"));
        var docsMirror = File.ReadAllText(root.File("docs/index.html"));

        Assert.Contains("application/ld+json", site, StringComparison.Ordinal);
        Assert.Contains("\"@type\": \"SoftwareApplication\"", site, StringComparison.Ordinal);
        Assert.Contains("\"@type\": \"FAQPage\"", site, StringComparison.Ordinal);
        Assert.Contains("\"@type\": \"BreadcrumbList\"", site, StringComparison.Ordinal);
        Assert.Contains("Native PDF evidence report", site, StringComparison.Ordinal);
        Assert.DoesNotContain("img.shields.io/github/v/release", site, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(site, docsMirror);
    }

    [Fact]
    public void SecurityAutomationDocumentationMatchesDependabotPolicy()
    {
        var root = FindRepositoryRoot();
        var docs = File.ReadAllText(root.File("docs/GITHUB_SECURITY_AUTOMATION.md"));
        var dependabot = File.ReadAllText(root.File(".github/dependabot.yml"));

        Assert.Contains("github-actions-minor-patch", docs, StringComparison.Ordinal);
        Assert.Contains("dotnet-test-tooling", docs, StringComparison.Ordinal);
        Assert.Contains("dotnet-runtime-packages", docs, StringComparison.Ordinal);
        Assert.Contains("Major updates are intentionally ignored", docs, StringComparison.Ordinal);
        Assert.Contains("version-update:semver-major", dependabot, StringComparison.Ordinal);
        Assert.DoesNotContain("github-actions-major", docs, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-major-updates", docs, StringComparison.Ordinal);
    }

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

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
