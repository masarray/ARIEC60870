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
    [InlineData(790448, 32, "TRAFO SOGI TAP POSITION (1/19)")]
    [InlineData(790446, 13, "FEEDER ACTIVE POWER (P)")]
    [InlineData(790447, 11, "FEEDER REACTIVE POWER (Q)")]
    [InlineData(790438, 13, "KOPEL ACTIVE POWER (P)")]
    [InlineData(790439, 11, "KOPEL REACTIVE POWER (Q)")]
    [InlineData(790442, 13, "TRAFO ACTIVE POWER (P)")]
    [InlineData(790443, 11, "TRAFO REACTIVE POWER (Q)")]
    [InlineData(68542, 46, "FEEDER CB OPEN/CLOSE COMMAND")]
    [InlineData(68539, 46, "KOPEL CB OPEN/CLOSE COMMAND")]
    [InlineData(68550, 46, "TRAFO CB OPEN/CLOSE COMMAND")]
    [InlineData(74537, 47, "TRAFO SOGI TAP CHANGER RAISE/LOWER COMMAND")]
    [InlineData(16712689, 31, "FEEDER CB STATUS")]
    [InlineData(16712686, 31, "KOPEL CB STATUS")]
    [InlineData(16712704, 31, "TRAFO CB STATUS")]
    [InlineData(16712694, 31, "FEEDER CB LOCAL/REMOTE")]
    [InlineData(16712701, 31, "KOPEL CB LOCAL/REMOTE")]
    [InlineData(16712708, 31, "TRAFO CB LOCAL/REMOTE")]
    [InlineData(16712709, 31, "TRAFO SOGI TAP CHANGER LOCAL/REMOTE")]
    [InlineData(16712710, 31, "TRAFO SOGI TAP CHANGER AUTO/MANUAL")]
    [InlineData(8388754, 30, "BUSBAR VOLTAGE STATUS")]
    [InlineData(790449, 9, "TRAFO SOGI REAL POWER SETTING (0/1000)")]
    [InlineData(70537, 48, "TRAFO SOGI REAL POWER SETPOINT (0/1000)")]
    [InlineData(8388710, 30, "RTU DIGITAL INPUT ALARM")]
    [InlineData(8388711, 30, "RTU DIGITAL OUTPUT ALARM")]
    [InlineData(8388712, 30, "RTU ANALOG INPUT ALARM")]
    [InlineData(8388713, 30, "RTU ANALOG OUTPUT ALARM")]
    [InlineData(8388714, 30, "RTU/GATEWAY MAIN LINK FAULT")]
    [InlineData(8388715, 30, "RTU/GATEWAY BACKUP LINK FAULT")]
    [InlineData(8388716, 30, "GATEWAY MAIN PROTECTION UNIT 1 TRIP")]
    [InlineData(8388717, 30, "GATEWAY MAIN PROTECTION UNIT 2 TRIP")]
    [InlineData(8388725, 30, "GATEWAY IED FAULTY")]
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
        Assert.Equal(bay + " CB STATUS", feedback.Name);
        Assert.Equal(bay + " CB OPEN/CLOSE COMMAND", command.Name);
        Assert.Equal(feedbackIoa, command.FeedbackIoa);
        Assert.Equal(31, feedback.TypeId);
        Assert.Equal(46, command.TypeId);
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
        Assert.Equal("KOPEL CB STATUS", profile.Resolve(105, 16712686, 3)?.Name);
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
        Assert.Equal("BUSBAR VOLTAGE STATUS",
            profile.ResolveObservedSignal(105, 8388754, 30)?.Name);
    }
}
