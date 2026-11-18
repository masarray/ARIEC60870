// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ARIEC60870.Repository.Tests;

public sealed class SharedHandbookTests
{
    [Fact]
    public void EngineeringHandbookHasRealBilingualChaptersAndSections()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Source("site/learning/iec60870-handbook.json")));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Contains("IEC", root.GetProperty("noticeEn").GetString());
        var chapters = root.GetProperty("chapters").EnumerateArray().ToArray();
        Assert.True(chapters.Length >= 8, "Cover the family, 101, 103, 104, NUC, redundancy, semantics and field FAT.");
        Assert.Equal(chapters.Length, chapters.Select(c => c.GetProperty("key").GetString()).Distinct().Count());
        var expected = new[]
        {
            "learn-iec-family", "learn-iec101", "learn-iec103", "learn-iec104",
            "learn-nuc", "learn-redundancy", "learn-semantics", "learn-field-guide"
        };
        foreach (var key in expected)
        {
            var chapter = Assert.Single(chapters, c => c.GetProperty("key").GetString() == key);
            Assert.False(string.IsNullOrWhiteSpace(chapter.GetProperty("titleEn").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(chapter.GetProperty("titleId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(chapter.GetProperty("summaryEn").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(chapter.GetProperty("summaryId").GetString()));
            var sections = chapter.GetProperty("sections").EnumerateArray().ToArray();
            Assert.True(sections.Length >= 3, key + " needs a real learning sequence");
            foreach (var section in sections)
            {
                Assert.False(string.IsNullOrWhiteSpace(section.GetProperty("headingEn").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(section.GetProperty("headingId").GetString()));
                Assert.NotEmpty(section.GetProperty("paragraphsEn").EnumerateArray());
                Assert.NotEmpty(section.GetProperty("paragraphsId").EnumerateArray());
                Assert.Equal(section.GetProperty("paragraphsEn").GetArrayLength(),
                    section.GetProperty("paragraphsId").GetArrayLength());
            }
            Assert.NotEmpty(chapter.GetProperty("sources").EnumerateArray());
        }
    }

    [Fact]
    public void DesktopAndBothWebLanguagesUseExactlyOneCanonicalJson()
    {
        var project = File.ReadAllText(Source("src/ARIEC60870.Desktop/ARIEC60870.Desktop.csproj"));
        var desktop = File.ReadAllText(Source("src/ARIEC60870.Desktop/HelpWindow.Handbook.cs"));
        var helpWindow = File.ReadAllText(Source("src/ARIEC60870.Desktop/HelpWindow.xaml.cs"));
        Assert.Contains(@"site\learning\iec60870-handbook.json", project);
        Assert.Contains(@"Link=""Learning\iec60870-handbook.json""", project);
        Assert.Contains("LoadSharedHandbook().Concat(CreateTopics())", helpWindow);
        Assert.Contains("iec60870-handbook.json", desktop);
        Assert.Contains("GetProperty(\"schemaVersion\")", desktop);

        var websiteSource = File.ReadAllText(Source("site/learning/iec60870-handbook.json"));
        Assert.Equal(websiteSource, File.ReadAllText(Source("docs/learning/iec60870-handbook.json")));
        foreach (var language in new[] { "wiki.html", "id/wiki.html" })
        {
            var html = File.ReadAllText(Source("site/" + language));
            Assert.Equal(html, File.ReadAllText(Source("docs/" + language)));
            Assert.Contains("iec60870-handbook.json", html);
            Assert.Contains("handbook-reader.js", html);
            Assert.Contains("id=\"arsee-handbook\"", html);
        }
        foreach (var asset in new[] { "handbook-reader.js", "handbook-reader.css" })
            Assert.Equal(File.ReadAllText(Source("site/learning/" + asset)),
                File.ReadAllText(Source("docs/learning/" + asset)));
    }

    [Fact]
    public void ReaderUsesSafeDomAndExplicitProtocolProfileCaveats()
    {
        var js = File.ReadAllText(Source("site/learning/handbook-reader.js"));
        Assert.Contains("textContent", js);
        Assert.DoesNotContain("innerHTML", js);
        Assert.Contains("response.json()", js);
        Assert.Contains("aria-current", js);

        var corpus = File.ReadAllText(Source("site/learning/iec60870-handbook.json"));
        Assert.Contains("not a universal IEC requirement", corpus);
        Assert.Contains("Class 1", corpus);
        Assert.Contains("STARTDT", corpus);
        Assert.Contains("FUN/INF", corpus);
        Assert.Contains("post-switch GI", corpus);
    }

    private static string Source(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Cannot locate repository file: " + relative);
    }
}
