// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace ARIEC60870.Repository.Tests;

public sealed class PublicLandingP3Tests
{
    [Fact]
    public void StaticContentRemainsVisibleWithoutJavaScriptOrObservers()
    {
        var css = Read("site/styles.css");
        var script = Read("site/script.js");
        Assert.Equal(css, Read("docs/styles.css"));
        Assert.Equal(script, Read("docs/script.js"));

        // A previous global .reveal rule hid most of the site when JS was unavailable.
        Assert.DoesNotContain(".reveal { opacity: 0", css);
        Assert.DoesNotContain(".reveal.is-visible", css);
        // One optional video-only observer is allowed; static page content never
        // depends on observation or JavaScript to become visible.
        Assert.Contains("document.querySelector(\"video[data-auto-preview]\")", script);
        Assert.Contains("const observer = new IntersectionObserver", script);
        Assert.Contains("prefers-reduced-motion: reduce", script);
        Assert.Contains("navigator.connection?.saveData", script);
        Assert.DoesNotContain("revealItems", script);

        // FAQ content still has a readable baseline when script enhancement is absent.
        Assert.Contains("html:not(.faq-enhanced) .faq-accordion-panel", css);
        Assert.Contains("document.documentElement.classList.add(\"faq-enhanced\")", script);
    }

    [Fact]
    public void PointerMotionDoesNotAttachHandlersToEveryCardOrCreateRippleNodes()
    {
        var css = Read("site/styles.css");
        var script = Read("site/script.js");
        Assert.DoesNotContain("pointermove", script);
        Assert.DoesNotContain("kinetic-ready", script);
        Assert.DoesNotContain("click-ripple", script);
        Assert.DoesNotContain(".kinetic-ready", css);
        Assert.DoesNotContain(".click-ripple", css);
        Assert.DoesNotContain("will-change: opacity, transform", css);
    }

    [Fact]
    public void FocusAndResponsiveControlsUseSharedAccessibleTokens()
    {
        var css = Read("site/styles.css");
        Assert.Contains("--content-width: 1180px;", css);
        Assert.Contains("--page-gutter: 24px;", css);
        Assert.Contains("--focus-outline: 2px solid var(--brand-action);", css);
        Assert.Contains("width: min(var(--content-width), calc(100vw - var(--page-gutter) - var(--page-gutter)));", css);
        Assert.Contains(":where(a, button, summary):focus-visible", css);
        Assert.Contains(".screen-frame.zoom-trigger:focus-visible", css);
        Assert.Contains(".reading-paths a, .faq-quicklinks a { min-height: 44px; }", css);
        Assert.Contains("@media (hover: none)", css);
        Assert.Contains(".screen-frame.zoom-trigger::after { opacity: 1; transform: none; }", css);

        var readme = Read("site/README.md");
        Assert.Equal(readme, Read("docs/README.md"));
        Assert.Contains("P3 keeps a single self-hosted CSS bundle", readme);
    }

    [Theory]
    [InlineData("index.html")]
    [InlineData("id/index.html")]
    [InlineData("demo.html")]
    [InlineData("id/demo.html")]
    [InlineData("faq.html")]
    [InlineData("id/faq.html")]
    [InlineData("wiki.html")]
    [InlineData("id/wiki.html")]
    public void P3KeepsBothLocalesAndOriginalPageMarkupUntouched(string page)
    {
        // These pages are intentionally unchanged: preserve P0-P2 layout, SEO and download truth.
        Assert.Equal(Read("site/" + page), Read("docs/" + page));
    }

    private static string Read(string path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Join(directory.FullName, "ARIEC60870.sln")))
            directory = directory.Parent;
        if (directory == null) throw new DirectoryNotFoundException("Repository root not found.");
        return File.ReadAllText(Path.Join(directory.FullName, path.Replace('/', Path.DirectorySeparatorChar)));
    }
}
