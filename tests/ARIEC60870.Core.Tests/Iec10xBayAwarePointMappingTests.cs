// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Mapping;
using Xunit;

namespace ARIEC60870.Core.Tests;

/// <summary>
/// The 31 unique CA=105 observations and controls in the user-supplied
/// IEC-101/104 SCADA IO list. Display names are project mapping metadata,
/// never wire evidence or proof of control success.
/// </summary>
public sealed class Iec10xBayAwarePointMappingTests
{
    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "profiles",
                "utility_fat_iec10x_default_profile.json");
            if (File.Exists(candidate)) return directory.FullName;
        }
        throw new FileNotFoundException("Source checkout for the bundled IEC-10x profile was not found.");
    }

    private static Iec10xPointMappingProfile Load()
    {
        var path = Path.Combine(RepositoryRoot(), "profiles",
            "utility_fat_iec10x_default_profile.json");
        return Iec10xPointMappingProfile.LoadFromFile(path);
    }

    [Theory]
    [InlineData(790448, 32, "TRAFO SOGI TC TPI")]
    [InlineData(790446, 13, "FEEDER TM P")]
    [InlineData(790447, 11, "FEEDER TM Q")]
    [InlineData(790438, 13, "KOPEL TM P 1")]
    [InlineData(790439, 11, "KOPEL TM Q 1")]
    [InlineData(790442, 13, "TRAFO TM P 2")]
    [InlineData(790443, 11, "TRAFO TM Q 2")]
    [InlineData(68542, 46, "FEEDER RCD CB")]
    [InlineData(68539, 46, "KOPEL RCD CB 1")]
    [InlineData(68550, 46, "TRAFO RCD CB 2")]
    [InlineData(74537, 47, "TRAFO SOGI RCD TC")]
    [InlineData(16712689, 31, "FEEDER TSD CB")]
    [InlineData(16712686, 31, "KOPEL TSD CB 1")]
    [InlineData(16712704, 31, "TRAFO TSD CB 2")]
    [InlineData(16712694, 31, "FEEDER TSD LR")]
    [InlineData(16712701, 31, "KOPEL TSD LR")]
    [InlineData(16712708, 31, "TRAFO TSD LR")]
    [InlineData(16712709, 31, "TRAFO SOGI TSD LRT")]
    [InlineData(16712710, 31, "TRAFO SOGI TSD TCC")]
    [InlineData(8388754, 30, "BUSBAR TSS VS")]
    [InlineData(790449, 9, "TRAFO SOGI TM POAQ")]
    [InlineData(70537, 48, "TRAFO SOGI RCA POOP")]
    [InlineData(8388710, 30, "RTU TSS TSFT")]
    [InlineData(8388711, 30, "RTU TSS RCFT")]
    [InlineData(8388712, 30, "RTU TSS TMFT")]
    [InlineData(8388713, 30, "RTU TSS ANFT")]
    [InlineData(8388714, 30, "RTU/GATEWAY TSS L1FT")]
    [InlineData(8388715, 30, "RTU/GATEWAY TSS L2FT")]
    [InlineData(8388716, 30, "GATEWAY TSS MPU 1")]
    [InlineData(8388717, 30, "GATEWAY TSS MPU 2")]
    [InlineData(8388725, 30, "GATEWAY TSS IEDF")]
    public void SourceTableBayAndTypeArePreservedForAll31Points(int ioa, int typeId, string expectedName)
    {
        var profile = Load();
        Assert.Equal(expectedName, profile.ResolveObservedSignal(105, ioa, typeId)?.Name);
        Assert.Equal(expectedName, profile.Resolve(105, ioa, typeId)?.Name);
    }

    [Fact]
    public void All31PointsHaveDistinctCaIoaAndTiAndSyncedDocumentation()
    {
        var profile = Load();
        profile.Validate();
        Assert.Equal(31, profile.Points.Count);
        Assert.Equal(31, profile.Points.Select(p => (p.Ca, p.Ioa, p.TypeId)).Distinct().Count());
        Assert.All(profile.Points, p =>
        {
            Assert.Equal(105, p.Ca);
            Assert.False(string.IsNullOrWhiteSpace(p.BayType));
            Assert.StartsWith(p.BayType + " ", p.Name, StringComparison.Ordinal);
        });
        var root = RepositoryRoot();
        Assert.Equal(
            File.ReadAllText(Path.Combine(root, "profiles", "utility_fat_iec10x_default_profile.json")),
            File.ReadAllText(Path.Combine(root, "docs", "profiles", "utility_fat_iec10x_default_profile.json")));
        var tss = Assert.Single(profile.TestScenarios, s => s.Id == "TSS");
        foreach (var ioa in new[] { 8388710, 8388711, 8388712, 8388713 })
            Assert.Contains(ioa, tss.InformationObjectAddresses);
    }

    [Theory]
    [InlineData(16712689, 68542, "FEEDER")]
    [InlineData(16712686, 68539, "KOPEL")]
    [InlineData(16712704, 68550, "TRAFO")]
    public void CommandsKeepDistinctBaySpecificFeedback(int feedbackIoa, int commandIoa, string bay)
    {
        var profile = Load();
        var feedback = Assert.Single(profile.Points, p => p.Ioa == feedbackIoa);
        var command = Assert.Single(profile.Points, p => p.Ioa == commandIoa);
        var lab = bay == "FEEDER" ? "" : bay == "KOPEL" ? " 1" : " 2";
        Assert.Equal(bay + " TSD CB" + lab, feedback.Name);
        Assert.Equal(bay + " RCD CB" + lab, command.Name);
        Assert.Equal(feedbackIoa, command.FeedbackIoa);
        Assert.Equal(31, feedback.TypeId);
        Assert.Equal(46, command.TypeId);
    }

    [Fact]
    public void EveryDocumentPointUsesBayTypeTypeMnemonicAndOptionalLabNumber()
    {
        var profile = Load();
        var labNumbers = new Dictionary<int, string>
        {
            [790438] = "1",
            [790439] = "1",
            [790442] = "2",
            [790443] = "2",
            [68539] = "1",
            [68550] = "2",
            [16712686] = "1",
            [16712704] = "2",
            [8388716] = "1",
            [8388717] = "2",
        };
        Assert.Equal(31, profile.Points.Count);
        foreach (var point in profile.Points)
        {
            Assert.False(string.IsNullOrWhiteSpace(point.TestCategory));
            Assert.False(string.IsNullOrWhiteSpace(point.Mnemonic));
            var name = point.BayType + " " + point.TestCategory + " " + point.Mnemonic +
                (labNumbers.TryGetValue(point.Ioa, out var lab) ? " " + lab : "");
            Assert.Equal(name, point.Name);
        }
        var pdfOrder = new[]
        {
            790448,
            790446,
            790447,
            790438,
            790439,
            790442,
            790443,
            68542,
            68539,
            68550,
            74537,
            16712689,
            16712686,
            16712704,
            16712694,
            16712701,
            16712708,
            16712709,
            16712710,
            8388754,
            790449,
            70537,
            8388710,
            8388711,
            8388712,
            8388713,
            8388714,
            8388715,
            8388716,
            8388717,
            8388725,
        };
        Assert.Equal(pdfOrder, profile.Points.Select(point => point.Ioa).ToArray());
    }

    [Fact]
    public void DocumentedNormalizedPoaQUsesPositiveOnlyZeroTo1000Range()
    {
        var profile = Load();
        var point = Assert.Single(profile.Points, p => p.Ioa == 790449);
        Assert.Equal(9, point.TypeId);
        Assert.Equal("TwoPoint", point.ScalingMode);
        Assert.Equal(0, point.X0);
        Assert.Equal(1, point.X100);
        Assert.Equal(0, point.Y0);
        Assert.Equal(1000, point.Y100);
        var sample = new Iec10xEngineeringSample(9, 105, 790449, 0.5, "Normalized=0.5");
        Assert.True(Iec10xEngineeringValue.TryFormat(point, sample, out var display));
        Assert.Equal("500 MW", display);
        var command = Assert.Single(profile.Points, p => p.Ioa == 70537);
        Assert.Equal(48, command.TypeId);
        Assert.NotEqual("TwoPoint", command.ScalingMode);
        Assert.Equal(1000, command.EngineeringMax);
    }

    [Fact]
    public void UnverifiedTi11HasNoGuessedMvarOrEngineeringRangeInDefaultProfile()
    {
        var profile = Load();
        foreach (var ioa in new[] { 790447, 790439, 790443 })
        {
            var point = Assert.Single(profile.Points, p => p.Ioa == ioa);
            Assert.Equal(11, point.TypeId);
            Assert.Equal("counts", point.Unit);
            Assert.Equal("Factor", point.ScalingMode);
            Assert.Null(point.EngineeringMin);
            Assert.Null(point.EngineeringMax);
            var raw = new Iec10xEngineeringSample(11, 105, ioa, 13641, "Scaled=13641");
            Assert.True(Iec10xEngineeringValue.TryFormat(point, raw, out var display));
            Assert.Equal("13641 counts", display);
            Assert.Equal("Scaled=13641", raw.OriginalValue);
        }
    }

    [Fact]
    public void UnverifiedCtptNominalCandidateIsOptInAndDoesNotScaleFloatAgain()
    {
        var root = RepositoryRoot();
        // Installer/portable scripts copy profiles/*; the unverified
        // nominal-range hypothesis must remain downloadable docs-only.
        Assert.False(File.Exists(Path.Combine(root, "profiles",
            "PROVISIONAL_io_list_scada_101_ctpt_nominal.json")));
        var path = Path.Combine(root, "docs", "profiles",
            "PROVISIONAL_io_list_scada_101_ctpt_nominal.json");
        var profile = Iec10xPointMappingProfile.LoadFromFile(path);
        Assert.Equal(31, profile.Points.Count);
        Assert.Contains("PROVISIONAL", profile.ProfileName);
        var nominal = Math.Sqrt(3) * 500 * 2000 / 1000;
        foreach (var ioa in new[] { 790447, 790439, 790443 })
        {
            var point = Assert.Single(profile.Points, p => p.Ioa == ioa);
            Assert.Equal("TwoPoint", point.ScalingMode);
            Assert.Equal(-32767, point.X0);
            Assert.Equal(32767, point.X100);
            Assert.Equal(-nominal, point.Y0);
            Assert.Equal(nominal, point.Y100);
            var sample = new Iec10xEngineeringSample(11, 105, ioa, 0, "Scaled=0");
            Assert.True(Iec10xEngineeringValue.TryFormat(point, sample, out var formatted));
            Assert.Equal("0 MVar", formatted);
        }
        foreach (var ioa in new[] { 790446, 790438, 790442 })
        {
            var point = Assert.Single(profile.Points, p => p.Ioa == ioa);
            Assert.Equal(13, point.TypeId);
            Assert.Equal("Factor", point.ScalingMode);
            var sample = new Iec10xEngineeringSample(13, 105, ioa, -250.25, "Float=-250.25");
            Assert.True(Iec10xEngineeringValue.TryFormat(point, sample, out var formatted));
            Assert.Equal("-250.25 MW", formatted);
        }
    }

    [Fact]
    public void LegacyAndLiveLookupFailClosedOnWrongCaOrWrongTypeFamily()
    {
        var profile = Load();
        Assert.Null(profile.Resolve(106, 16712686, 31)); // Not the configured station.
        Assert.Null(profile.Resolve(105, 16712686, 30)); // Single-point != double-point.
        Assert.Null(profile.Resolve(105, 68539, 31)); // Command != process indication.
        Assert.Null(profile.Resolve(105, 16712701, 46)); // LR status != control command.
        Assert.Null(profile.Resolve(105, 16712700, 31)); // Unlisted IOA.
        Assert.Null(profile.Resolve(null, 16712686, 31)); // No trusted station identity.
        Assert.Null(profile.Resolve(105, 16712686, null)); // No trusted type identity.

        // The permitted GI untimed double-point variant may inherit the same
        // specific bay name without changing the received TI in evidence.
        Assert.Equal("KOPEL TSD CB 1", profile.Resolve(105, 16712686, 3)?.Name);
    }

    [Fact]
    public void DoublePointStateLabelsAreNotConflatedWithControlAcknowledgements()
    {
        var profile = Load();
        var cb = Assert.Single(profile.Points, p => p.Ioa == 16712686);
        var lr = Assert.Single(profile.Points, p => p.Ioa == 16712701);
        Assert.Equal("Open", cb.ResolveDisplayValue("1"));
        Assert.Equal("Closed", cb.ResolveDisplayValue("2"));
        Assert.Equal("Local", lr.ResolveDisplayValue("1"));
        Assert.Equal("Remote", lr.ResolveDisplayValue("2"));
        Assert.Equal("BUSBAR TSS VS",
            profile.ResolveObservedSignal(105, 8388754, 30)?.Name);
    }
}
