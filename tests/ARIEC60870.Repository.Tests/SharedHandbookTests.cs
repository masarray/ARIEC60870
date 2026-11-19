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
        Assert.True(chapters.Length >= 28, "R4.1 handbook requires twelve user-guide chapters plus sixteen engineering chapters.");
        Assert.True(chapters.Sum(ch => ch.GetProperty("sections").GetArrayLength()) >= 128,
            "R4.1 handbook requires 128 bilingual theory, trace and operator sections.");
        Assert.Equal("Engineering Handbook R4.1 — Detailed Signal Map Guide", root.GetProperty("bookEdition").GetString());
        Assert.Equal(chapters.Length, chapters.Select(c => c.GetProperty("key").GetString()).Distinct().Count());
        var expected = new[]
        {
            "learn-iec-family", "learn-iec101", "learn-iec103", "learn-iec104",
            "learn-nuc", "learn-redundancy", "learn-semantics", "learn-field-guide",
            "learn-framing", "learn-lifecycle", "learn-gateways", "learn-interop",
            "learn-lab-101", "learn-lab-103", "learn-lab-104", "learn-lab-redundancy",
            "learn-use-start", "learn-use-setup", "learn-use-signal-map", "learn-use-101", "learn-use-103",
            "learn-use-104", "learn-use-dual", "learn-use-passive", "learn-use-views",
            "learn-use-commands", "learn-use-evidence", "learn-use-diagnose"
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
    public void R4ArSeeUserGuideIsGroundedAndBilingualOnBothSurfaces()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Source("site/learning/iec60870-handbook.json")));
        var chapters = document.RootElement.GetProperty("chapters").EnumerateArray()
            .Where(chapter => chapter.GetProperty("key").GetString()!.StartsWith("learn-use-", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(12, chapters.Length);
        var lessons = chapters.SelectMany(ch => ch.GetProperty("sections").EnumerateArray()).ToArray();
        Assert.Equal(58, lessons.Length);
        Assert.All(lessons, section =>
        {
            Assert.True(section.TryGetProperty("workflow", out var workflow));
            Assert.True(workflow.GetProperty("stepsEn").GetArrayLength() >= 3);
            Assert.Equal(workflow.GetProperty("stepsEn").GetArrayLength(),
                workflow.GetProperty("stepsId").GetArrayLength());
            foreach (var name in new[] { "expected", "troubleshooting" })
            {
                Assert.False(string.IsNullOrWhiteSpace(workflow.GetProperty(name + "En").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(workflow.GetProperty(name + "Id").GetString()));
            }
        });

        var wpf = File.ReadAllText(Source("src/ARIEC60870.Desktop/HelpWindow.xaml.cs"));
        var loader = File.ReadAllText(Source("src/ARIEC60870.Desktop/HelpWindow.Handbook.cs"));
        Assert.Contains("BuildUserWorkflow(section.Workflow)", wpf);
        Assert.Contains("GetHelpNavigationGroup", wpf);
        Assert.Contains("learn-use-start", wpf);
        Assert.Contains("ReadArray(workflowData, \"stepsEn\")", loader);
        Assert.Contains("Read(workflowData, \"troubleshootingId\")", loader);

        var wiki = File.ReadAllText(Source("site/learning/handbook-reader.js"));
        Assert.Contains("workflow['steps' + suffix]", wiki);
        Assert.Contains("lookup(workflow, 'expected')", wiki);
        Assert.Contains("lookup(workflow, 'troubleshooting')", wiki);
        Assert.DoesNotContain("innerHTML", wiki);
    }

    [Fact]
    public void SignalMapLessonsMatchEditorFieldNamesAndOpenFromF1()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Source("site/learning/iec60870-handbook.json")));
        var guide = document.RootElement.GetProperty("chapters").EnumerateArray()
            .Single(chapter => chapter.GetProperty("key").GetString() == "learn-use-signal-map");
        var lessons = guide.GetProperty("sections").EnumerateArray().ToArray();
        Assert.Equal(13, lessons.Length);
        Assert.All(lessons, section =>
        {
            Assert.True(section.TryGetProperty("workflow", out var workflow));
            Assert.True(workflow.GetProperty("stepsEn").GetArrayLength() >= 3);
            Assert.Equal(workflow.GetProperty("stepsEn").GetArrayLength(),
                workflow.GetProperty("stepsId").GetArrayLength());
        });

        var editor = File.ReadAllText(Source("src/ARIEC60870.Desktop/SignalListEditorWindow.cs"));
        foreach (var label in new[]
        {
            "Signal Map", "Save & Apply", "Validate entire list", "Validate signal",
            "Generate name", "Common address (CA)", "Information object (IOA)",
            "IEC Type ID (TI)", "Scaling mode", "State map", "Feedback IOA",
            "Sync 31 I/O reference...", "Export blank Field FAT worksheet..."
        })
            Assert.Contains(label, editor);
        Assert.Contains("if (e.Key == Key.F1)", editor);
        Assert.Contains("Signal Map guide (F1)", editor);
        Assert.Contains("new HelpWindow(\"learn-use-signal-map\")", editor);

        var text = string.Join(" ",
            lessons.SelectMany(section => section.GetProperty("workflow")
                .GetProperty("stepsEn").EnumerateArray().Select(step => step.GetString())));
        Assert.Contains("TI11", text);
        Assert.Contains("Two-point", text);
        Assert.Contains("IEC103", text);
        Assert.Contains("Save & Apply", text);
    }

    [Fact]
    public void UserGuideNamesControlsThatActuallyExistInTheArSeeMainWindow()
    {
        var window = File.ReadAllText(Source("src/ARIEC60870.Desktop/MainWindow.xaml"));
        // Source-backed UI contracts: if a primary control is removed/renamed,
        // revise the user guide rather than let stale instructions ship.
        foreach (var control in new[]
        {
            "SetupButton", "StartButton", "HelpButton", "OpenCaptureButton",
            "SaveCaptureRailButton", "ExportTraceRailButton", "SignalListButton",
            "ProtocolModeComboBox", "TransportModeComboBox", "TcpHostBox",
            "PortComboBox", "InteroperabilityConventionComboBox", "CommandDockPanel",
            "CommandDockGiButton", "CommandDockReadButton", "CommandDockClockSyncButton",
            "CommandTypeComboBox", "CommandOperationModeComboBox", "PassiveSourceComboBox",
            "PassiveFullRecordingCheckBox", "NavValueButton", "NavEventButton",
            "NavReportButton"
        })
            Assert.Contains("x:Name=\"" + control + "\"", window);

        foreach (var label in new[]
        {
            "IEC 60870-5-101 - Telecontrol serial",
            "IEC 60870-5-103 - Protection relay serial",
            "IEC 60870-5-104 - Telecontrol TCP/IP",
            "IEC 60870-5-101 - Dual link redundancy",
            "IEC 60870-5-101 - Passive Monitor",
            "Physical RX-only", "Virtual Inline", "Built-in demo simulation",
            "GI • TI-100", "Read IOA • TI-102", "Timesync • TI-103"
        })
            Assert.Contains(label, window);
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
        Assert.Contains("learning-user-workflow", js);
        Assert.Contains("learning-nav-divider", js);
        Assert.Contains("aria-controls", js);
        Assert.Contains("answer.hidden = !reveal", js);

        var corpus = File.ReadAllText(Source("site/learning/iec60870-handbook.json"));
        Assert.Contains("not a universal IEC requirement", corpus);
        Assert.Contains("Class 1", corpus);
        Assert.Contains("STARTDT", corpus);
        Assert.Contains("FUN/INF", corpus);
        Assert.Contains("post-switch GI", corpus);
        // The native WPF reader uses plain TextBlocks, not Markdown parsing.
        Assert.DoesNotContain("**", corpus);
    }

    private static string Source(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Join(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Cannot locate repository file: " + relative);
    }
}
