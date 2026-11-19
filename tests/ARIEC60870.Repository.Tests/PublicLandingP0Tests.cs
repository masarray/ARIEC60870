// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Xunit;

namespace ARIEC60870.Repository.Tests;

public sealed class PublicLandingP0Tests
{
    [Theory]
    [InlineData("site/index.html")]
    [InlineData("site/id/index.html")]
    [InlineData("docs/index.html")]
    [InlineData("docs/id/index.html")]
    public void LandingGalleryHasWorkingZoomMarkupAndFourDifferentScreenshots(string page)
    {
        var path = Path.Combine(RepositoryRoot().FullName, page.Replace('/', Path.DirectorySeparatorChar));
        var html = File.ReadAllText(path);

        Assert.Contains("<dialog class=\"lightbox\"", html);
        Assert.Contains("class=\"lightbox-close\"", html);
        Assert.Contains("<figcaption></figcaption>", html);

        var screenshots = Regex.Matches(
            html,
            @"class=""screen-frame zoom-trigger""[^>]*data-full=""(?<src>[^""]+)""");
        Assert.Equal(4, screenshots.Count);

        var uniqueImageHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match screenshot in screenshots)
        {
            var relativePath = screenshot.Groups["src"].Value.Replace('/', Path.DirectorySeparatorChar);
            var imagePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, relativePath));
            Assert.True(File.Exists(imagePath), "Missing screenshot: " + imagePath);

            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath)));
            Assert.True(uniqueImageHashes.Add(hash),
                "Different gallery features must not reuse identical screenshot bytes: " + relativePath);
        }
    }

    [Fact]
    public void IndonesianZoomHasAKeyboardDismissibleDialog()
    {
        var root = RepositoryRoot();
        var html = File.ReadAllText(Path.Combine(root.FullName, "site", "id", "index.html"));
        var script = File.ReadAllText(Path.Combine(root.FullName, "site", "script.js"));

        Assert.Contains("aria-label=\"Pratinjau screenshot\"", html);
        Assert.Contains("aria-label=\"Tutup pratinjau\"", html);
        Assert.Contains(">Tutup</button>", html);
        Assert.Contains("lightbox.showModal()", script);
        Assert.Contains("lightboxClose?.addEventListener(\"click\"", script);
    }

    [Fact]
    public void PrimaryDownloadButtonsKeepReadableWhiteTextEvenOnHover()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot().FullName, "site", "styles.css"));
        var match = Regex.Match(css, @"--brand-action:\s*(?<hex>#[0-9a-fA-F]{6})\s*;");

        Assert.True(match.Success, "Action background token must be declared.");
        Assert.Contains(".button.primary { background: var(--brand-action); color: #fff;", css);
        Assert.Contains(".button.primary::after { background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.08), transparent); }", css);

        var foregroundContrast = ContrastWithWhite(match.Groups["hex"].Value, 0);
        var hoverContrast = ContrastWithWhite(match.Groups["hex"].Value, 0.08);
        Assert.True(foregroundContrast >= 4.5, "Default primary CTA contrast falls below WCAG AA.");
        Assert.True(hoverContrast >= 4.5, "Hovered primary CTA contrast falls below WCAG AA.");
    }

    private static double ContrastWithWhite(string hex, double whiteOverlay)
    {
        var values = new double[3];
        for (var i = 0; i < 3; i++)
        {
            var sRgb = Convert.ToInt32(hex.Substring(1 + i * 2, 2), 16) / 255.0;
            sRgb = sRgb * (1 - whiteOverlay) + whiteOverlay;
            values[i] = sRgb <= 0.04045 ? sRgb / 12.92 : Math.Pow((sRgb + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * values[0] + 0.7152 * values[1] + 0.0722 * values[2];
        return 1.05 / (luminance + 0.05);
    }

    private static DirectoryInfo RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ARIEC60870.sln")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new DirectoryNotFoundException("ARIEC60870 repository root not found.");
    }
}
