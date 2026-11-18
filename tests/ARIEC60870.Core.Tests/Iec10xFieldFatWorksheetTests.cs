// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using ARIEC60870.Core.Mapping;
using Xunit;

namespace ARIEC60870.Core.Tests;

public sealed class Iec10xFieldFatWorksheetTests
{
    private static Iec10xPointMappingProfile Bundled()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "profiles", "utility_fat_iec10x_default_profile.json");
            if (File.Exists(path)) return Iec10xPointMappingProfile.LoadFromFile(path);
        }
        throw new FileNotFoundException("Bundled FAT Signal List is missing.");
    }

    [Fact]
    public void ExportsExactly31UnexecutedRowsAndPreservesOriginalSource()
    {
        var profile = Bundled();
        Assert.Equal(31, profile.Points.Count);
        profile.DefaultSettings!.TcpHost = "secret-private-host.invalid";
        profile.DefaultSettings.SerialAddressHint = "do-not-export";
        var before = JsonSerializer.Serialize(profile);

        var sheet = Iec10xFieldFatWorksheet.ExportCsv(profile);

        Assert.Equal(before, JsonSerializer.Serialize(profile));
        Assert.Equal(33, sheet.Split("\r\n", StringSplitOptions.None).Length);
        Assert.Equal(31, sheet.Split("\"NOT TESTED\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("secret-private-host", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-export", sheet, StringComparison.Ordinal);
        Assert.Contains("\"790447\",\"11\",\"FEEDER TM Q\"", sheet, StringComparison.Ordinal);
        Assert.Contains("\"790449\",\"9\",\"TRAFO SOGI TM POAQ\"", sheet, StringComparison.Ordinal);
        Assert.Contains("\"Provisional Q SCADA reference - confirm Q source item to exact IOA\"", sheet, StringComparison.Ordinal);
        Assert.Contains("\"Observe command/feedback only - NO TX\"", sheet, StringComparison.Ordinal);
        Assert.Contains("\"Independent RTU/SCADA reference\"", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("PASS", sheet, StringComparison.Ordinal); // No made-up field success.
    }

    [Fact]
    public void TwoPointAndFactorShowOnlyTheActiveFormulaNotDormantParameters()
    {
        var profile = Bundled();
        var poaq = Assert.Single(profile.Points, p => p.Ioa == 790449);
        Assert.Equal("TwoPoint", poaq.ScalingMode);
        poaq.Scale = 9999.75; // Inactive when TwoPoint is selected.
        var feeder = Assert.Single(profile.Points, p => p.Ioa == 790447);
        feeder.ScalingMode = "Factor";
        feeder.Unit = "counts";
        feeder.Scale = 2.25;
        feeder.X0 = -123456.125; // Stored but dormant in Factor.
        feeder.X100 = 123456.125;
        feeder.Y0 = -112233.55;
        feeder.Y100 = 112233.55;
        var csv = Iec10xFieldFatWorksheet.ExportCsv(profile);

        Assert.Contains("\"TwoPoint\",\"MW\",\"0\",\"1\",\"0\",\"1200\",\"\",\"\",\"\",\"\",\"\",", csv, StringComparison.Ordinal);
        Assert.Contains("\"Factor\",\"counts\",\"\",\"\",\"\",\"\",\"2.25\",", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("9999.75", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("112233.55", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("-123456.125", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void HandlesSpreadsheetFormulaPayloadsAndQuotedMultilineLabelsSafely()
    {
        var profile = new Iec10xPointMappingProfile
        {
            ProfileName = " =2+3",
            CommonAddress = 109,
            Points =
            [
                new Iec10xPointMappingEntry
                {
                    Ca = 109,
                    Ioa = 123,
                    TypeId = 13,
                    Name = "=HYPERLINK(\"https://not-real.invalid\",\"test\")",
                    BayType = "\t@hidden",
                    TestCategory = "-unsafe",
                    Mnemonic = "Q\r\nsecond line",
                    CommandPolicy = "MonitorOnly"
                }
            ]
        };
        var sheet = Iec10xFieldFatWorksheet.ExportCsv(profile);

        Assert.Contains("\"' =2+3\"", sheet, StringComparison.Ordinal);
        Assert.Contains("\"'=HYPERLINK(\"\"https://not-real.invalid\"\",\"\"test\"\")\"", sheet, StringComparison.Ordinal);
        Assert.Contains("\"'\t@hidden\"", sheet, StringComparison.Ordinal);
        Assert.Contains("\"'-unsafe\"", sheet, StringComparison.Ordinal);
        Assert.Contains("\"Q\r\nsecond line\"", sheet, StringComparison.Ordinal);
        Assert.Contains("\"109\",\"123\",\"13\"", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesInvalidOrEmptyProfilesInsteadOfExportingFalseEvidence()
    {
        var empty = new Iec10xPointMappingProfile();
        Assert.Throws<InvalidOperationException>(() => Iec10xFieldFatWorksheet.ExportCsv(empty));

        var invalid = Bundled();
        var q = Assert.Single(invalid.Points, p => p.Ioa == 790447);
        q.ScalingMode = "TwoPoint";
        q.X0 = 1;
        q.X100 = 1;
        q.Y0 = 0;
        q.Y100 = 100;
        Assert.Throws<InvalidOperationException>(() => Iec10xFieldFatWorksheet.ExportCsv(invalid));
    }
}
