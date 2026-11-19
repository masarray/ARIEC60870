// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Xunit;

namespace ARIEC60870.Repository.Tests;

public sealed class PublicLandingP2Tests
{
    [Theory]
    [InlineData("index.html")]
    [InlineData("id/index.html")]
    [InlineData("demo.html")]
    [InlineData("id/demo.html")]
    public void ScreenshotGalleryIsOrderedKeyboardAccessibleAndDoesNotAutoplay(string page)
    {
        var site = Read("site/" + page);
        var mirror = Read("docs/" + page);
        Assert.Equal(site, mirror);

        var images = Regex.Matches(site, @"class=""screen-frame zoom-trigger""");
        Assert.Equal(4, images.Count);
        for (var step = 1; step <= 4; step++)
            Assert.Contains($"class=\"gallery-step\" aria-hidden=\"true\">{step:00} / 04", site);

        Assert.Contains("class=\"gallery-guide\"", site);
        Assert.Contains("class=\"lightbox-toolbar\"", site);
        Assert.Contains("class=\"lightbox-prev\"", site);
        Assert.Contains("class=\"lightbox-next\"", site);
        Assert.Contains("class=\"lightbox-close\"", site);

        var video = Regex.Match(site, @"<video\b[^>]*>").Value;
        Assert.NotEmpty(video);
        Assert.Contains("controls", video);
        Assert.Contains("preload=\"none\"", video);
        Assert.DoesNotContain("autoplay", video);
        Assert.DoesNotContain("loop", video);
    }

    [Theory]
    [InlineData("wiki.html")]
    [InlineData("id/wiki.html")]
    public void WikiOffersShortcutsToRealSections(string page)
    {
        var site = Read("site/" + page);
        Assert.Equal(site, Read("docs/" + page));
        var shortcuts = Regex.Match(site, @"<nav class=""reading-paths""[\s\S]*?</nav>").Value;
        Assert.NotEmpty(shortcuts);

        foreach (var id in new[] { "engineering-handbook", "beginner-primer", "troubleshooting-matrix", "glossary" })
        {
            Assert.Contains($"href=\"#{id}\"", shortcuts);
            Assert.Contains($"id=\"{id}\"", site);
        }
    }

    [Theory]
    [InlineData("faq.html", "faq-en-q")]
    [InlineData("id/faq.html", "faq-id-q")]
    public void FaqPopularLinksOpenExistingQuestionsAndKeepNoJsAnswers(string page, string prefix)
    {
        var site = Read("site/" + page);
        Assert.Equal(site, Read("docs/" + page));
        var shortcuts = Regex.Match(site, @"<nav class=""faq-quicklinks""[\s\S]*?</nav>").Value;
        Assert.NotEmpty(shortcuts);

        foreach (var number in new[] { 1, 5, 10, 14 })
        {
            var id = prefix + number;
            Assert.Contains($"href=\"#{id}\"", shortcuts);
            Assert.Contains($"id=\"{id}\"", site);
        }

        Assert.Equal(16, Regex.Matches(site, @"class=""faq-accordion-trigger""").Count);
        Assert.Equal(16, Regex.Matches(site, @"aria-expanded=""true""").Count);
        Assert.Equal(16, Regex.Matches(site, @"aria-hidden=""false""").Count);
        Assert.DoesNotContain("wiki.html#iec101", site);
    }

    [Fact]
    public void SharedScriptRestoresGalleryFocusAndOpensDeepLinkedFaqs()
    {
        var script = Read("site/script.js");
        var css = Read("site/styles.css");
        Assert.Equal(script, Read("docs/script.js"));
        Assert.Equal(css, Read("docs/styles.css"));

        foreach (var required in new[]
        {
            "lightboxPrev?.addEventListener(\"click\"",
            "lightboxNext?.addEventListener(\"click\"",
            "event.key === \"ArrowLeft\"",
            "event.key === \"ArrowRight\"",
            "lightbox.showModal()",
            "screenshotOpener?.focus()",
            "window.addEventListener(\"hashchange\", revealFromHash)",
            "trigger.scrollIntoView({ block: \"start\" })",
            "panel.inert = !open",
            "document.documentElement.classList.add(\"faq-enhanced\")",
        })
            Assert.Contains(required, script);

        Assert.Contains("html:not(.faq-enhanced) .faq-accordion-panel", css);
        Assert.Contains(".gallery-step", css);
        Assert.Contains(".reading-paths", css);
        Assert.Contains(".faq-quicklinks", css);
        Assert.Contains(".lightbox-toolbar button:focus-visible", css);
        Assert.Contains("html[lang=\"id\"] .screen-frame.zoom-trigger::after", css);
    }

    private static string Read(string path)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "ARIEC60870.sln")))
            root = root.Parent;
        if (root == null) throw new DirectoryNotFoundException("Repository root not found.");
        return File.ReadAllText(Path.Combine(root.FullName, path.Replace('/', Path.DirectorySeparatorChar)));
    }
}
