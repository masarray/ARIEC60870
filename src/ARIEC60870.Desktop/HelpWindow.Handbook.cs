// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Text.Json;

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
                sections.Add(new HelpSection(
                    Read(section, "headingEn"), Read(section, "headingId"),
                    ReadArray(section, "paragraphsEn"), ReadArray(section, "paragraphsId"),
                    ReadArray(section, "bulletsEn"), ReadArray(section, "bulletsId"),
                    ReadArray(section, "framesEn"), ReadArray(section, "framesId")));
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

    private static string Read(JsonElement element, string property) =>
        element.GetProperty(property).GetString() ??
        throw new InvalidDataException("Empty handbook field: " + property);

    private static string[] ReadArray(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray()
            .Select(value => value.GetString() ??
                throw new InvalidDataException("Null handbook array entry: " + property))
            .ToArray();
}
