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
        Assert.InRange(title.Length, 30, 100); // Readable concise headlines are intentional.
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

    [Theory]
    [InlineData("site/index.html", "docs/index.html")]
    [InlineData("site/id/index.html", "docs/id/index.html")]
    public void LandingPresentationHasPredictableOrderAndKeyboardScrollableGallery(string source, string mirror)
    {
        var root = RepositoryRoot();
        var html = File.ReadAllText(Path.Combine(root.FullName, source.Replace('/', Path.DirectorySeparatorChar)));
        var mirrored = File.ReadAllText(Path.Combine(root.FullName, mirror.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Equal(html, mirrored);

        var ids = Regex.Matches(html, @"<section[^>]+id=""(?<id>[^""]+)""")
            .Select(x => x.Groups["id"].Value)
            .Where(x => x is "product" or "screens" or "capabilities" or "open-engineering" or "wiki" or "download")
            .ToArray();
        Assert.Equal(new[] { "product", "screens", "capabilities", "open-engineering", "wiki", "download" }, ids);
        Assert.Contains("class=\"screen-grid\" tabindex=\"0\" role=\"region\" aria-label=", html);
        Assert.Contains("class=\"gallery-swipe-hint\"", html);
        Assert.Equal(11, Regex.Matches(html, "class=\"screen-frame zoom-trigger\"").Count);
        Assert.Contains("v3.6.20", html);
        Assert.Contains("IEC-60870.webm", html);
    }

    [Fact]
    public void DownloadDemoAndHomeShareOneLightweightLayoutContract()
    {
        var root = RepositoryRoot();
        var css = File.ReadAllText(Path.Combine(root.FullName, "site/styles.css"));
        Assert.Equal(css, File.ReadAllText(Path.Combine(root.FullName, "docs/styles.css")));
        foreach (var required in new[] { "--public-radius:", "--public-space:",
            ".landing-home #product .proof-card", ".landing-home #screens .screen-grid",
            "scroll-snap-type: x mandatory", ".landing-home .capability-atlas",
            ".demo-page .demo-story-card", ".download-page .doc-card" })
            Assert.Contains(required, css);

        foreach (var path in new[] { "download.html", "id/download.html" })
        {
            var site = File.ReadAllText(Path.Combine(root.FullName, "site", path.Replace('/', Path.DirectorySeparatorChar)));
            var mirror = File.ReadAllText(Path.Combine(root.FullName, "docs", path.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(site, mirror);
            Assert.Contains("<main class=\"doc-main download-page\">", site);
            Assert.Contains("data-release-filename=\"installer\"", site);
            Assert.Contains("data-release-filename=\"zip\"", site);
        }
    }

    [Theory]
    [InlineData("index.html")]
    [InlineData("id/index.html")]
    [InlineData("demo.html")]
    [InlineData("id/demo.html")]
    [InlineData("download.html")]
    [InlineData("id/download.html")]
    public void MainLandingPagesPreloadTheLocallyLicensedInterVariableFont(string page)
    {
        var root = RepositoryRoot();
        var path = page.Replace('/', Path.DirectorySeparatorChar);
        var html = File.ReadAllText(Path.Combine(root.FullName, "site", path));
        Assert.Equal(html, File.ReadAllText(Path.Combine(root.FullName, "docs", path)));
        var fontPath = page.StartsWith("id/", StringComparison.Ordinal)
            ? "../assets/fonts/InterVariable.woff2"
            : "assets/fonts/InterVariable.woff2";
        Assert.Contains($"<link rel=\"preload\" href=\"{fontPath}\" as=\"font\" type=\"font/woff2\" crossorigin />", html);
    }

    [Fact]
    public void InterIsBundledLocallyWithItsLicenseAndNoRemoteFontDependency()
    {
        var root = RepositoryRoot();
        var css = File.ReadAllText(Path.Combine(root.FullName, "site/styles.css"));
        Assert.Equal(css, File.ReadAllText(Path.Combine(root.FullName, "docs/styles.css")));
        Assert.Contains("@font-face", css);
        Assert.Contains("font-family: \"Inter\";", css);
        Assert.Contains("assets/fonts/InterVariable.woff2", css);
        Assert.Contains("font-display: swap;", css);
        Assert.Contains("font-family: \"Inter\", ui-sans-serif", css);
        Assert.DoesNotContain("fonts.googleapis.com", css);
        Assert.DoesNotContain("fonts.gstatic.com", css);
        Assert.DoesNotContain("font-family: Aptos, Inter", css);

        var siteFont = File.ReadAllBytes(Path.Combine(root.FullName, "site/assets/fonts/InterVariable.woff2"));
        var mirroredFont = File.ReadAllBytes(Path.Combine(root.FullName, "docs/assets/fonts/InterVariable.woff2"));
        Assert.InRange(siteFont.Length, 100_000, 600_000);
        Assert.Equal(new byte[] { (byte)'w', (byte)'O', (byte)'F', (byte)'2' }, siteFont[..4]);
        Assert.Equal(siteFont, mirroredFont);
        var license = File.ReadAllText(Path.Combine(root.FullName, "site/assets/fonts/OFL.txt"));
        Assert.Contains("SIL OPEN FONT LICENSE", license);
        Assert.Equal(license, File.ReadAllText(Path.Combine(root.FullName, "docs/assets/fonts/OFL.txt")));
    }

    [Theory]
    [InlineData("site/index.html")]
    [InlineData("site/id/index.html")]
    public void HeroPrioritizesSoftwareAndReleasesWithoutDuplicatedBadgeTiles(string path)
    {
        var root = RepositoryRoot();
        var html = File.ReadAllText(Path.Combine(root.FullName, path.Replace('/', Path.DirectorySeparatorChar)));
        var hero = html[html.IndexOf("<section class=\"hero\">", StringComparison.Ordinal)..];
        hero = hero[..hero.IndexOf("</section>", StringComparison.Ordinal)];
        Assert.Contains("data-auto-preview", hero);
        Assert.Contains("class=\"preview-meta\"", hero);
        Assert.Contains("data-release", html); // Release resolver and its metadata survive.
        Assert.DoesNotContain("class=\"metric-row\"", hero);
        Assert.DoesNotContain("class=\"trust-strip\"", hero);
        Assert.Equal(2, Regex.Matches(hero, "class=\"button (primary|secondary)\"").Count);
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
        Assert.Contains(".landing-home #product .proof-card span:not(.card-top-icon)", css);
        Assert.Contains(".landing-home .hero-copy .lead", css);
        Assert.Contains("max-width: 56ch;", css);
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
