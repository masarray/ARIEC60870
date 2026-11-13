// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using Xunit;

namespace ARIEC60870.Repository.Tests;

public sealed class ObservationAcceptanceIsolationTests
{
    [Fact]
    public void ObservationAcceptanceBeginsWithFreshWindowNotMutatedByOtherGuiProbes()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "ARIEC60870.Desktop", "App.xaml.cs"));
        const string branch = "if (e.Args.Contains(\"--observation-ui-acceptance\"";
        const string nextBranch = "if (e.Args.Contains(\"--installer-master-close-smoke\"";
        var start = source.IndexOf(branch, StringComparison.Ordinal);
        var end = source.IndexOf(nextBranch, start + branch.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Missing OBS-C startup command");
        var acceptance = source[start..end];

        Assert.Contains("var window = new MainWindow();", acceptance);
        Assert.Contains("await window.RunObservationUiAcceptanceProbeAsync(output);", acceptance);
        Assert.DoesNotContain("await window.RunPassiveGuiSmokeProbeAsync();", acceptance);
        Assert.DoesNotContain("await window.RunPassiveHistoryGuiSmokeProbeAsync();", acceptance);

        // The independent smoke command is still available; do not bypass
        // actual TX/RX event-to-frame validation to obtain a green result.
        var smokeStart = source.IndexOf(
            "if (e.Args.Contains(\"--passive-gui-smoke\"", StringComparison.Ordinal);
        Assert.True(smokeStart > end, "Separate passive GUI smoke launch was removed");
        var smoke = source[smokeStart..];
        Assert.Contains("await window.RunPassiveGuiSmokeProbeAsync();", smoke);
        Assert.Contains("await window.RunPassiveHistoryGuiSmokeProbeAsync();", smoke);
        Assert.Contains("await window.RunEventHistoryGuiSmokeProbeAsync();", smoke);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ARIEC60870.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Cannot locate ARIEC60870.sln");
    }
}
