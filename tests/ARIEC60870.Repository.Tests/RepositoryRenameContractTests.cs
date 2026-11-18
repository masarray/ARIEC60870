// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace ARIEC60870.Repository.Tests;

public sealed class RepositoryRenameContractTests
{
    private const string NewRepo = "https://github.com/masarray/arsee";
    private const string NewPages = "https://masarray.github.io/arsee/";

    private static DirectoryInfo FindRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null;
             dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ARIEC60870.sln")))
                return dir;
        throw new FileNotFoundException("ArSee source checkout not found.");
    }

    private static string Read(DirectoryInfo root, string relative) =>
        File.ReadAllText(Path.Combine(root.FullName,
            relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void UpdateDiscoveryAndAssetVerificationUseRenamedOfficialOrigin()
    {
        var root = FindRoot();
        var updater = Read(root, "src/ARIEC60870.Desktop/Services/ReleaseUpdateService.cs");
        Assert.Contains("https://api.github.com/repos/masarray/arsee/releases/latest", updater);
        Assert.Contains(NewRepo + "/releases/download/", updater);
        Assert.DoesNotContain("github.com/masarray/ARIEC60870", updater,
            StringComparison.OrdinalIgnoreCase);

        var ci = Read(root, ".github/workflows/ci.yml");
        var updaterFat = Read(root, "scripts/test-updater-worker-fat.ps1");
        Assert.DoesNotContain("github.com/masarray/ARIEC60870", ci,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("github.com/masarray/ARIEC60870", updaterFat,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicReleaseResolverUsesCanonicalApiAndMatchingAssetOrigin()
    {
        var root = FindRoot();
        var site = Read(root, "site/release-download.js");
        Assert.Contains(NewRepo, site);
        Assert.Contains("https://api.github.com/repos/masarray/arsee/releases?per_page=30", site);
        Assert.Contains("a.browser_download_url === repo +", site);
        Assert.Equal(site, Read(root, "docs/release-download.js"));
    }

    [Fact]
    public void PublicSiteMetadataAndCanonicalUrlsFollowRenamedPagesPath()
    {
        var root = FindRoot();
        Assert.Contains(NewPages, Read(root, ".github/repository-metadata.yml"));
        Assert.Contains(NewPages, Read(root, "site/index.html"));
        Assert.Contains(NewPages, Read(root, "site/seo-manifest.json"));
        Assert.Contains(NewPages, Read(root, "site/sitemap.xml"));
        Assert.Contains(NewPages, Read(root, "site/robots.txt"));
        Assert.Contains(NewRepo, Read(root, "site/index.html"));
        Assert.Contains(NewRepo, Read(root, "README.md"));
        Assert.Contains(NewPages, Read(root, "README.md"));
        Assert.Equal(Read(root, "site/index.html"), Read(root, "docs/index.html"));
        Assert.Equal(Read(root, "site/sitemap.xml"), Read(root, "docs/sitemap.xml"));
        Assert.Equal(Read(root, "site/site.webmanifest"), Read(root, "docs/site.webmanifest"));
    }

    [Fact]
    public void LivePagesNeverAdvertiseOldRepositoryOrOldProjectSite()
    {
        var root = FindRoot();
        var siteRoot = Path.Combine(root.FullName, "site");
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".html", ".js", ".json", ".txt", ".xml", ".webmanifest"
        };
        foreach (var path in Directory.EnumerateFiles(siteRoot, "*",
                     SearchOption.AllDirectories)
                 .Where(path => extensions.Contains(Path.GetExtension(path))))
        {
            var content = File.ReadAllText(path);
            Assert.DoesNotContain("github.com/masarray/ARIEC60870", content,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("masarray.github.io/ARIEC60870/", content,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
