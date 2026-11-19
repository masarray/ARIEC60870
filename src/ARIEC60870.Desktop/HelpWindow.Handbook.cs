// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ARIEC60870.Desktop;

/// <summary>
/// The desktop and public Wiki share one canonical educational text corpus.
/// The Windows app ships the same JSON file locally so learning works offline.
/// Existing contextual field-help topics remain separate and keep their F1 keys.
/// </summary>
public partial class HelpWindow
{
    internal const string HandbookRelativePath = "Learning/iec60870-handbook.json";

    private static IReadOnlyList<HelpTopic> LoadSharedHandbook()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Learning", "iec60870-handbook.json");
        if (!File.Exists(path))
        {
            return new[]
            {
                new HelpTopic("learn-unavailable", "", "Engineering handbook unavailable",
                    "Buku belajar engineer tidak tersedia",
                    "The offline handbook file is missing from this installation.",
                    "File buku belajar offline tidak ada di instalasi ini.",
                    "handbook missing package offline", new[]
                    {
                        new HelpSection("Check the package", "Periksa paket",
                            new[] { "Repair or reinstall ArSee with the complete Learning folder. Online Field Wiki remains available from the header." },
                            new[] { "Perbaiki atau pasang ulang ArSee beserta folder Learning. Field Wiki online tetap dapat dibuka dari header." },
                            Array.Empty<string>(), Array.Empty<string>())
                    })
            };
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported shared handbook schema version.");

        var uniqueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chapters = new List<HelpTopic>();
        foreach (var chapter in root.GetProperty("chapters").EnumerateArray())
        {
            var key = Read(chapter, "key");
            if (!key.StartsWith("learn-", StringComparison.Ordinal) || !uniqueKeys.Add(key))
                throw new InvalidDataException("Duplicate or invalid handbook key: " + key);

            var sections = new List<HelpSection>();
            foreach (var section in chapter.GetProperty("sections").EnumerateArray())
            {
                HelpLab? lab = null;
                if (section.TryGetProperty("lab", out var labData))
                {
                    var kind = Read(labData, "wireKind");
                    var hex = labData.GetProperty("wireHex").ValueKind == JsonValueKind.Null
                        ? null : Read(labData, "wireHex");
                    if (kind != "conceptual" && string.IsNullOrWhiteSpace(hex))
                        throw new InvalidDataException("Missing lab frame bytes: " + key);
                    lab = new HelpLab(
                        kind, hex,
                        Read(labData, "scenarioEn"), Read(labData, "scenarioId"),
                        ReadArray(labData, "stepsEn"), ReadArray(labData, "stepsId"),
                        Read(labData, "questionEn"), Read(labData, "questionId"),
                        Read(labData, "answerEn"), Read(labData, "answerId"));
                }
                sections.Add(new HelpSection(
                    Read(section, "headingEn"), Read(section, "headingId"),
                    ReadArray(section, "paragraphsEn"), ReadArray(section, "paragraphsId"),
                    ReadArray(section, "bulletsEn"), ReadArray(section, "bulletsId"),
                    ReadArray(section, "framesEn"), ReadArray(section, "framesId"), lab));
            }

            var references = ReadArray(chapter, "sources");
            if (references.Length != 0)
            {
                sections.Add(new HelpSection(
                    "Sources and standards", "Rujukan standar dan sumber",
                    new[] { "Use the applicable licensed standard and approved project interoperability profile for formal conformance. These are public catalogues and example conventions, not substitutes for normative requirements." },
                    new[] { "Gunakan standar berlisensi yang berlaku dan interoperability profile proyek untuk penilaian kesesuaian formal. Ini adalah katalog publik dan contoh konvensi, bukan pengganti ketentuan normatif." },
                    references, references));
            }

            chapters.Add(new HelpTopic(key, string.Empty,
                Read(chapter, "titleEn"), Read(chapter, "titleId"),
                Read(chapter, "summaryEn"), Read(chapter, "summaryId"),
                Read(chapter, "searchText"), sections));
        }

        if (chapters.Count == 0)
            throw new InvalidDataException("The shared IEC 60870 handbook has no chapters.");
        return chapters;
    }

    private Border BuildTraceLab(HelpLab lab)
    {
        // A single quiet teaching panel shared by all protocol lessons.
        // The answer is not revealed until an explicit keyboard/mouse action.
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = Pick("GUIDED TRACE LAB", "LATIHAN ANALISIS TRACE"),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Ink500Brush"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        body.Children.Add(new TextBlock
        {
            Text = Pick(lab.ScenarioEn, lab.ScenarioId),
            FontSize = 12.2,
            Foreground = (Brush)FindResource("Ink700Brush"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19
        });

        if (!string.IsNullOrWhiteSpace(lab.WireHex))
        {
            // Native read-only TextBox lets field engineers select/copy real
            // bytes without treating a static image or synthetic glyph as data.
            body.Children.Add(new TextBox
            {
                Text = lab.WireHex,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = (FontFamily)FindResource("TraceMonoFont"),
                FontSize = 11.5,
                Foreground = (Brush)FindResource("Ink900Brush"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 9, 0, 4)
            });
        }

        var steps = PickList(lab.StepsEn, lab.StepsId);
        for (var i = 0; i < steps.Count; i++)
        {
            body.Children.Add(new TextBlock
            {
                Text = $"{i + 1}. {steps[i]}",
                FontSize = 11.6,
                Foreground = (Brush)FindResource("Ink600Brush"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Margin = new Thickness(0, 5, 0, 0)
            });
        }

        body.Children.Add(new TextBlock
        {
            Text = Pick(lab.QuestionEn, lab.QuestionId),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Ink900Brush"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19,
            Margin = new Thickness(0, 13, 0, 7)
        });

        var answer = new TextBlock
        {
            Text = Pick(lab.AnswerEn, lab.AnswerId),
            FontSize = 11.8,
            Foreground = (Brush)FindResource("Ink700Brush"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 9, 0, 0),
            Tag = "HelpLabAnswer"
        };
        var reveal = new Button
        {
            Content = Pick("Show explanation", "Tampilkan pembahasan"),
            Style = (Style)FindResource("ArSeeToolbarActionButtonStyle"),
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            Tag = "HelpLabReveal",
        };
        reveal.Click += (_, _) =>
        {
            var show = answer.Visibility != Visibility.Visible;
            answer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            reveal.Content = show
                ? Pick("Hide explanation", "Sembunyikan pembahasan")
                : Pick("Show explanation", "Tampilkan pembahasan");
        };
        body.Children.Add(reveal);
        body.Children.Add(answer);

        return new Border
        {
            Background = (Brush)FindResource("Panel75Brush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 12, 12, 12),
            Margin = new Thickness(0, 14, 0, 3),
            Tag = "GuidedTraceLab",
            Child = body
        };
    }

    private sealed record HelpLab(
        string WireKind, string? WireHex,
        string ScenarioEn, string ScenarioId,
        IReadOnlyList<string> StepsEn, IReadOnlyList<string> StepsId,
        string QuestionEn, string QuestionId,
        string AnswerEn, string AnswerId);

    private static string Read(JsonElement element, string property) =>
        element.GetProperty(property).GetString() ??
        throw new InvalidDataException("Empty handbook field: " + property);

    private static string[] ReadArray(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray()
            .Select(value => value.GetString() ??
                throw new InvalidDataException("Null handbook array entry: " + property))
            .ToArray();
}
