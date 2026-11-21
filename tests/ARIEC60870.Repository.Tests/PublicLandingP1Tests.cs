// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using System.Text.Json;
using Xunit;

namespace ARIEC60870.Repository.Tests;

public sealed class PublicLandingP1Tests
{
    [Theory]
    [InlineData("index.html", "id/index.html")]
    [InlineData("demo.html", "id/demo.html")]
    [InlineData("download.html", "id/download.html")]
    public void BilingualVisitorCanReachTheCorrespondingPage(string english, string indonesian)
    {
        var root = RepositoryRoot();
        var en = File.ReadAllText(Path.Combine(root.FullName, "site", english));
        var id = File.ReadAllText(Path.Combine(root.FullName, "site", indonesian));

        Assert.Contains("<html lang=\"en\">", en);
        Assert.Contains("<html lang=\"id\">", id);
        Assert.Contains($"href=\"id/{(english == "index.html" ? "" : english)}\"", en);
        Assert.Contains($"href=\"../{(english == "index.html" ? "" : english)}\"", id);

        foreach (var relative in new[] { english, indonesian })
        {
            var site = File.ReadAllText(Path.Combine(root.FullName, "site", relative.Replace('/', Path.DirectorySeparatorChar)));
            var mirror = File.ReadAllText(Path.Combine(root.FullName, "docs", relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(site, mirror);
            Assert.Contains("<link rel=\"canonical\"", site);
            Assert.Contains("hreflang=\"en\"", site);
            Assert.Contains("hreflang=\"id\"", site);
        }
    }

    [Theory]
    [InlineData("site/index.html")]
    [InlineData("site/id/index.html")]
    public void LandingUsesShortBenefitHeadlineAndReadableHeroCopy(string path)
    {
        var root = RepositoryRoot();
        var html = File.ReadAllText(Path.Combine(root.FullName, path.Replace('/', Path.DirectorySeparatorChar)));
        var title = Regex.Match(html, @"<h1>(?<text>[^<]+)</h1>").Groups["text"].Value;
        var lead = Regex.Match(html, @"<p class=""lead"">(?<text>[^<]+)</p>").Groups["text"].Value;

        Assert.NotEmpty(title);
        Assert.NotEmpty(lead);
        Assert.DoesNotContain("Protocol Tester & Analyzer", title);
        Assert.InRange(title.Length, 45, 100);
        Assert.InRange(lead.Length, 90, 310);
        // Public stable source of truth changes only after an actual release publishes.
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "site/seo-manifest.json")));
        var stable = manifest.RootElement.GetProperty("latestStableVersion").GetString();
        Assert.False(string.IsNullOrWhiteSpace(stable));
        var assetPrefix = manifest.RootElement.GetProperty("assetPrefix").GetString();
        Assert.Contains(assetPrefix, new[] { "ArSee", "ARIEC60870" });
        Assert.Contains("v" + stable, html);
        // Previous stable must agree with the release-channel table, not a
        // hard-coded historical version that becomes stale after publishing.
        var previous = Regex.Match(html,
            @"(?:Previous stable:|Stabil sebelumnya:)\s*<a href=""https://github.com/masarray/arsee/releases/tag/(?<tag>v\d+\.\d+\.\d+)"">(?<text>v\d+\.\d+\.\d+)</a>");
        Assert.True(previous.Success, "Hero must identify the previous public stable release.");
        Assert.Equal(previous.Groups["tag"].Value, previous.Groups["text"].Value);
        Assert.NotEqual("v" + stable, previous.Groups["tag"].Value);
        var readme = File.ReadAllText(Path.Combine(root.FullName, "README.md"));
        Assert.Contains($"https://github.com/masarray/arsee/releases/tag/v{stable}", readme);
        Assert.Contains($"{assetPrefix}-v{stable}-setup-win-x64.exe", html);
        Assert.Contains($"{assetPrefix}-v{stable}-win-x64.zip", html);
    }

    [Theory]
    [InlineData("site/index.html")]
    [InlineData("site/id/index.html")]
    [InlineData("site/demo.html")]
    [InlineData("site/id/demo.html")]
    [InlineData("site/download.html")]
    [InlineData("site/id/download.html")]
    public void PrimaryNavigationKeepsLocalPagesAndLanguageSwitch(string path)
    {
        var root = RepositoryRoot();
        var html = File.ReadAllText(Path.Combine(root.FullName, path.Replace('/', Path.DirectorySeparatorChar)));
        var nav = Regex.Match(html, @"<nav class=""nav-links""[\s\S]*?</nav>").Value;

        Assert.NotEmpty(nav);
        foreach (var href in new[] { "demo.html", "wiki.html", "faq.html", "download.html" })
            Assert.Contains($"href=\"{href}\"", nav);
        Assert.Contains("class=\"lang-switch\"", nav);
    }

    [Theory]
    [InlineData("site/index.html", "docs/index.html")]
    [InlineData("site/id/index.html", "docs/id/index.html")]
    public void ProductLandingsExposeFullFeatureAtlasAndRealScreenshots(string sitePath, string mirrorPath)
    {
        var root = RepositoryRoot();
        var site = File.ReadAllText(Path.Combine(root.FullName, sitePath.Replace('/', Path.DirectorySeparatorChar)));
        var mirror = File.ReadAllText(Path.Combine(root.FullName, mirrorPath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Equal(site, mirror);
        Assert.Contains("id=\"capabilities\"", site);
        Assert.Equal(8, Regex.Matches(site, "class=\"capability-item\"").Count);
        Assert.Equal(11, Regex.Matches(site, "class=\"screen-frame zoom-trigger\"").Count);
        Assert.Contains("data-auto-preview", site);
        Assert.Contains("IEC-60870.webm", site);
        Assert.DoesNotContain("<video autoplay", site);

        var paths = Regex.Matches(site, @"data-full=""(?<path>[^""]+\.webp)""")
            .Select(match => match.Groups["path"].Value)
            .ToArray();
        Assert.Equal(11, paths.Length);
        Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var image in paths)
        {
            var relative = image.StartsWith("../", StringComparison.Ordinal) ? "site/" + image[3..] : "site/" + image;
            Assert.True(File.Exists(Path.Combine(root.FullName, relative.Replace('/', Path.DirectorySeparatorChar))),
                $"Missing screenshot: {image}");
        }
    }

    [Fact]
    public void LightweightMotionHonorsVisitorPreferences()
    {
        var root = RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root.FullName, "site/script.js"));
        var css = File.ReadAllText(Path.Combine(root.FullName, "site/styles.css"));
        Assert.Contains("IntersectionObserver", script);
        Assert.Contains("prefers-reduced-motion: reduce", script);
        Assert.Contains("navigator.connection?.saveData", script);
        Assert.Contains("autoPreview.play().catch", script);
        Assert.Contains("capability-atlas", css);
        Assert.Equal(script, File.ReadAllText(Path.Combine(root.FullName, "docs/script.js")));
        Assert.Equal(css, File.ReadAllText(Path.Combine(root.FullName, "docs/styles.css")));
    }

    [Fact]
    public void SharedMobileNavigationIsKeyboardFriendlyAndHasNoJsFallback()
    {
        var root = RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root.FullName, "site/script.js"));
        var css = File.ReadAllText(Path.Combine(root.FullName, "site/styles.css"));

        Assert.Contains("document.documentElement.classList.add(\"nav-enhanced\")", script);
        Assert.Contains("aria-expanded", script);
        Assert.Contains("aria-controls", script);
        Assert.Contains("event.key === \"Escape\"", script);
        Assert.Contains("toggle.focus()", script);
        Assert.Contains(".site-header .nav-toggle { display: none; }", css);
        Assert.Contains("html.nav-enhanced .site-header .nav-links.is-open", css);
        Assert.Contains("min-height: 44px", css);
        Assert.Contains("html.nav-enhanced .site-header .brand small { display: none; }", css);
        Assert.Contains(".landing-home .proof-card span { font-size: 15px;", css);
        Assert.Contains(".landing-home .hero-copy .lead { max-width: 62ch; font-size: 16px;", css);
        Assert.Equal(script, File.ReadAllText(Path.Combine(root.FullName, "docs/script.js")));
        Assert.Equal(css, File.ReadAllText(Path.Combine(root.FullName, "docs/styles.css")));
    }

    [Fact]
    public void LocalizedPagesAppearInSitemapAndManifest()
    {
        var root = RepositoryRoot();
        var sitemap = File.ReadAllText(Path.Combine(root.FullName, "site/sitemap.xml"));
        var manifest = File.ReadAllText(Path.Combine(root.FullName, "site/seo-manifest.json"));

        foreach (var path in new[] { "id/demo.html", "id/download.html" })
        {
            Assert.Contains("https://masarray.github.io/arsee/" + path, sitemap);
            Assert.Contains("\"path\": \"" + path + "\"", manifest);
        }

        var download = File.ReadAllText(Path.Combine(root.FullName, "site/id/download.html"));
        Assert.Contains("SHA256SUMS.txt", download);
        Assert.Contains("Rilis stabil terbaru", download);
        Assert.Contains("lingkungan engineering yang diizinkan", download);
        Assert.Contains("data-release-filename=\"installer\"", download);
        Assert.Contains("data-release-filename=\"zip\"", download);
        Assert.Contains("release-download.js", download);

        var demo = File.ReadAllText(Path.Combine(root.FullName, "site/id/demo.html"));
        Assert.Contains("../assets/screenshots/IEC-60870.webm", demo);
        Assert.Contains("<dialog class=\"lightbox\"", demo);
        Assert.Equal(4, Regex.Matches(demo, "zoom-trigger").Count);
    }

    private static DirectoryInfo RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ARIEC60870.sln")))
            directory = directory.Parent;
        return directory ?? throw new DirectoryNotFoundException("ARIEC60870 repository root not found.");
    }
}
