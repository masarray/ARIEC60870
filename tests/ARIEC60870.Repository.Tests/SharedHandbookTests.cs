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
        Assert.True(chapters.Length >= 16, "R3 handbook requires sixteen theory and trace-lab chapters.");
        Assert.True(chapters.Sum(ch => ch.GetProperty("sections").GetArrayLength()) >= 70,
            "R3 handbook needs at least 70 bilingual sections.");
        Assert.Equal("Engineering Handbook R3 — Verified Trace Labs", root.GetProperty("bookEdition").GetString());
        Assert.Equal(chapters.Length, chapters.Select(c => c.GetProperty("key").GetString()).Distinct().Count());
        var expected = new[]
        {
            "learn-iec-family", "learn-iec101", "learn-iec103", "learn-iec104",
            "learn-nuc", "learn-redundancy", "learn-semantics", "learn-field-guide",
            "learn-framing", "learn-lifecycle", "learn-gateways", "learn-interop",
            "learn-lab-101", "learn-lab-103", "learn-lab-104", "learn-lab-redundancy"
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
            var chapterNumber = Array.IndexOf(chapters, chapter) + 1;
            Assert.Equal(chapterNumber.ToString("00"), chapter.GetProperty("number").GetString());
            for (var i = 0; i < sections.Length; i++)
            {
                Assert.Equal($"{chapterNumber}.{i + 1}", sections[i].GetProperty("number").GetString());
                Assert.Equal($"{key}-s{i + 1}", sections[i].GetProperty("id").GetString());
            }
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
    public void R3TraceLabsAreCompleteAndShareTheirAnswersAcrossDesktopAndWeb()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Source("site/learning/iec60870-handbook.json")));
        var chapters = document.RootElement.GetProperty("chapters").EnumerateArray().ToArray();
        var exercises = chapters
            .SelectMany(ch => ch.GetProperty("sections").EnumerateArray())
            .Where(section => section.TryGetProperty("lab", out _))
            .Select(section => section.GetProperty("lab")).ToArray();
        Assert.True(exercises.Length >= 20);
        Assert.True(exercises.Count(lab =>
            lab.GetProperty("wireHex").ValueKind == JsonValueKind.String) >= 14);
        Assert.True(exercises.Count(lab =>
            !lab.GetProperty("expectedValid").GetBoolean()) >= 2);

        foreach (var lab in exercises)
        {
            foreach (var field in new[] { "scenario", "question", "answer" })
            {
                Assert.False(string.IsNullOrWhiteSpace(lab.GetProperty(field + "En").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(lab.GetProperty(field + "Id").GetString()));
            }
            Assert.True(lab.GetProperty("stepsEn").GetArrayLength() > 0);
            Assert.Equal(lab.GetProperty("stepsEn").GetArrayLength(),
                lab.GetProperty("stepsId").GetArrayLength());
            Assert.Equal(JsonValueKind.Object, lab.GetProperty("expectedFields").ValueKind);
        }

        var loader = File.ReadAllText(Source("src/ARIEC60870.Desktop/HelpWindow.Handbook.cs"));
        Assert.Contains("Read(labData, \"wireHex\")", loader);
        Assert.Contains("BuildTraceLab(HelpLab lab)", loader);
        Assert.Contains("HelpLabReveal", loader);
        Assert.Contains("HelpLabAnswer", loader);
        Assert.Contains("Visibility.Collapsed", loader);

        var renderer = File.ReadAllText(Source("src/ARIEC60870.Desktop/HelpWindow.xaml.cs"));
        Assert.Contains("BuildTraceLab(section.Lab)", renderer);
        Assert.Contains("learn-lab-101", renderer);
        Assert.Contains("learn-lab-103", renderer);
        Assert.Contains("learn-lab-104", renderer);
    }

    [Fact]
    public void ReaderUsesSafeDomAndExplicitProtocolProfileCaveats()
    {
        var js = File.ReadAllText(Source("site/learning/handbook-reader.js"));
        Assert.Contains("textContent", js);
        Assert.DoesNotContain("innerHTML", js);
        Assert.Contains("response.json()", js);
        Assert.Contains("aria-current", js);
        Assert.Contains("aria-expanded", js);
        Assert.Contains("learning-section-button", js);
        Assert.Contains("learning-protocol-badge", js);
        Assert.Contains("scrollIntoView", js);
        Assert.Contains("learning-trace-lab", js);
        Assert.Contains("learning-lab-reveal", js);
        Assert.Contains("aria-controls", js);
        Assert.Contains("answer.hidden = !reveal", js);

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
