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
    public void RequiredPublicRepositoryFilesExist()
    {
        var root = FindRepositoryRoot();
        var required = new[]
        {
            "README.md", "CHANGELOG.md", "LICENSE", "NOTICE", "THIRD_PARTY_NOTICES.md",
            "CONTRIBUTING.md", "SECURITY.md", "SUPPORT.md", "CODE_OF_CONDUCT.md",
            "Directory.Build.props", "ARIEC60870.sln",
            "docs/QUICK_START.md", "docs/TROUBLESHOOTING.md", "docs/DESKTOP_ARCHITECTURE_CLEANUP.md",
            "docs/VALIDATION_MATRIX.md", "docs/TESTING_STRATEGY.md", "docs/GITHUB_SECURITY_AUTOMATION.md",
            "tests/README.md", "site/index.html", "site/sitemap.xml", "site/robots.txt",
            "site/site.webmanifest", "site/seo-manifest.json", "site/llms.txt", "site/humans.txt",
            "docs/index.html", "docs/sitemap.xml", "docs/robots.txt", "docs/site.webmanifest",
            "docs/.nojekyll", "docs/.pages-compatibility-mirror",
            ".github/PULL_REQUEST_TEMPLATE.md", ".github/dependabot.yml",
            ".github/workflows/ci.yml", ".github/workflows/pages.yml",
            ".github/workflows/release-package.yml", ".github/workflows/scorecard.yml"
        };

        foreach (var path in required)
        {
            Assert.True(File.Exists(root.File(path)), $"Missing required repository file: {path}");
        }
    }

    [Fact]
    public void VersionIsAlignedWithPublicSiteAndSeoManifest()
    {
        var root = FindRepositoryRoot();
        var props = XDocument.Load(root.File("Directory.Build.props"));
        var version = props.Descendants("Version").FirstOrDefault()?.Value;

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Contains(version!, File.ReadAllText(root.File("site/index.html")));
        Assert.Contains(version!, File.ReadAllText(root.File("site/seo-manifest.json")));
        Assert.Contains("IEC 60870", File.ReadAllText(root.File("site/site.webmanifest")));
    }

    [Fact]
    public void ReadmeLinksPointToExistingLocalFiles()
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
    public void AutomationConfigurationIsProfessionalAndStable()
    {
        var root = FindRepositoryRoot();
        var ci = NormalizeNewlines(File.ReadAllText(root.File(".github/workflows/ci.yml")));
        var pages = NormalizeNewlines(File.ReadAllText(root.File(".github/workflows/pages.yml")));
        var release = NormalizeNewlines(File.ReadAllText(root.File(".github/workflows/release-package.yml")));
        var dependabot = NormalizeNewlines(File.ReadAllText(root.File(".github/dependabot.yml")));
        var scorecard = NormalizeNewlines(File.ReadAllText(root.File(".github/workflows/scorecard.yml")));
        var dependencyReview = NormalizeNewlines(File.ReadAllText(root.File(".github/workflows/dependency-review.yml")));

        Assert.Contains("name: CI", ci);
        Assert.Contains("protocol-smoke-tests.log", ci);
        Assert.Contains("XPlat Code Coverage", ci);
        Assert.Contains("name: Pages", pages);
        Assert.Contains("path: site", pages);
        Assert.Contains("docs/index.html is out of sync", pages);
        Assert.Contains("name: Package", release);
        Assert.Contains("dotnet test ARIEC60870.sln", release);
        Assert.Contains("Generate SPDX dependency SBOM", release);
        Assert.Contains("attestations: write", release);
        Assert.Contains("contents: write", release);
        Assert.Contains("ARIEC60870-v${{ needs.resolve.outputs.version }}-win-x64.zip", release);
        Assert.DoesNotContain("win-x64-portable.zip", release);
        Assert.DoesNotContain("win-x64-singlefile.zip", release);
        Assert.Contains("package-ecosystem: \"github-actions\"", dependabot);
        Assert.Contains("package-ecosystem: \"nuget\"", dependabot);
        Assert.Contains("directories:", dependabot);
        Assert.Contains("github-actions-minor-patch", dependabot);
        Assert.Contains("version-update:semver-major", dependabot);
        Assert.DoesNotContain("github-actions-major", dependabot);
        Assert.DoesNotContain("dotnet-major-updates", dependabot);
        Assert.Contains("publish_results: true", scorecard);
        Assert.Contains("id-token: write", scorecard);
        Assert.Contains("if: ${{ vars.ENABLE_DEPENDENCY_REVIEW == 'true' }}", dependencyReview);
        Assert.Contains("Dependency Graph", dependencyReview);
    }

    [Fact]
    public void PublicSiteUsesCanonicalSiteSourceWithDocsFallback()
    {
        var root = FindRepositoryRoot();
        var forbidden = new[]
        {
            "landing/index.html", "landing/styles.css", "landing/script.js", "landing/sitemap.xml", "landing/robots.txt",
            "site/screenshot", "landing/screenshot"
        };

        foreach (var path in forbidden)
        {
            Assert.False(File.Exists(root.File(path)) || Directory.Exists(root.File(path)), $"Legacy or raw public site path should not exist: {path}");
        }

        var siteRuntimeFiles = new[]
        {
            "index.html", "404.html", "styles.css", "script.js", "robots.txt", "sitemap.xml",
            "site.webmanifest", "seo-manifest.json", "llms.txt", "humans.txt", "download.html",
            "faq.html", "protocol-coverage.html", "quick-start.html", "troubleshooting.html"
        };

        Assert.True(File.Exists(root.File("docs/.pages-compatibility-mirror")), "docs/ compatibility marker is missing.");
        foreach (var file in siteRuntimeFiles)
        {
            Assert.True(File.Exists(root.File($"site/{file}")), $"Missing canonical site file: {file}");
            Assert.True(File.Exists(root.File($"docs/{file}")), $"Missing docs compatibility file: {file}");
        }

        var siteIndex = File.ReadAllText(root.File("site/index.html"));
        var docsIndex = File.ReadAllText(root.File("docs/index.html"));
        Assert.Contains("ARIEC60870 Evidence Analyzer", siteIndex);
        Assert.Contains("ARIEC60870 Evidence Analyzer", docsIndex);
        Assert.Contains("https://masarray.github.io/ARIEC60870/", docsIndex);
        Assert.True(Directory.Exists(root.File("docs/assets")), "docs/assets compatibility mirror is missing.");
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
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.False(string.IsNullOrWhiteSpace(canonical));
            var html = File.ReadAllText(root.File($"site/{path}"));
            Assert.Contains($"<link rel=\"canonical\" href=\"{canonical}\"", html);
            Assert.Contains("property=\"og:title\"", html);
            Assert.Contains("property=\"og:description\"", html);
            Assert.Contains(canonical!, sitemap);
        }
    }

    [Fact]
    public void DesktopArchitectureAndNativePdfGuardsRemainInPlace()
    {
        var root = FindRepositoryRoot();
        var desktop = root.File("src/ARIEC60870.Desktop");
        var shell = Path.Combine(desktop, "MainWindow.xaml.cs");
        var features = Path.Combine(desktop, "Features");
        var pathService = Path.Combine(desktop, "Services", "LocalWorkspacePaths.cs");
        var pdfService = root.File("src/ARIEC60870.Desktop/Reporting/EvidencePdfReportService.cs");
        var notices = File.ReadAllText(root.File("THIRD_PARTY_NOTICES.md"));

        Assert.True(File.Exists(shell));
        Assert.True(Directory.Exists(features));
        Assert.True(File.Exists(pathService));
        Assert.InRange(File.ReadLines(shell).Count(), 1, 1200);
        Assert.Contains("SetupPreferencesFile", File.ReadAllText(pathService));
        Assert.Contains("TriggerCaptureFolder", File.ReadAllText(pathService));
        Assert.True(File.Exists(Path.Combine(desktop, "ViewModels", "StatusHistoryRow.cs")));
        Assert.True(File.Exists(Path.Combine(desktop, "ViewModels", "TriggerCaptureRow.cs")));
        Assert.Contains("NativePdfDocument", File.ReadAllText(pdfService));
        Assert.Contains("xref", File.ReadAllText(pdfService));
        Assert.Contains("built-in native PDF engine", notices, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicBrandingRoadmapAndReportLanguageAreCurrent()
    {
        var root = FindRepositoryRoot();
        var readme = File.ReadAllText(root.File("README.md"));
        var roadmap = File.ReadAllText(root.File("docs/ROADMAP.md"));
        var site = File.ReadAllText(root.File("site/index.html"));
        var docsMirror = File.ReadAllText(root.File("docs/index.html"));
        var activeDocs = Directory.EnumerateFiles(root.FullName, "*.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}docs{Path.DirectorySeparatorChar}archive{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Contains("ARIEC60870 Evidence Analyzer", readme);
        Assert.Contains("Native PDF evidence report | Implemented baseline", roadmap);
        Assert.DoesNotContain("PDF evidence report | Low", roadmap, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("planned next", roadmap, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("application/ld+json", site);
        Assert.Contains("\"@type\": \"SoftwareApplication\"", site);
        Assert.Contains("\"@type\": \"FAQPage\"", site);
        Assert.Contains("\"@type\": \"BreadcrumbList\"", site);
        Assert.Contains("Native PDF evidence report", site);
        Assert.Contains("ARIEC60870 Evidence Analyzer", docsMirror);
        Assert.DoesNotContain("api.scorecard.dev", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("img.shields.io/github/v/release", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("img.shields.io/github/v/release", site, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Protocol" + " Lab", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Protocol" + " Lab", site, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Protocol" + " Lab", docsMirror, StringComparison.OrdinalIgnoreCase);

        foreach (var file in activeDocs)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Export HTML" + " / PDF", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("standalone" + " HTML", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("print" + " to PDF", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("browser" + " print", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SecurityAutomationDocumentationMatchesDependabotPolicy()
    {
        var root = FindRepositoryRoot();
        var docs = File.ReadAllText(root.File("docs/GITHUB_SECURITY_AUTOMATION.md"));
        var dependabot = File.ReadAllText(root.File(".github/dependabot.yml"));

        Assert.Contains("github-actions-minor-patch", docs);
        Assert.Contains("dotnet-test-tooling", docs);
        Assert.Contains("dotnet-runtime-packages", docs);
        Assert.Contains("Major updates are intentionally ignored", docs);
        Assert.Contains("version-update:semver-major", dependabot);
        Assert.DoesNotContain("github-actions-major", docs);
        Assert.DoesNotContain("dotnet-major-updates", docs);
    }

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n");

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
