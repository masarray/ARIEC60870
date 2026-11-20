// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace ARIEC60870.Repository.Tests;

/// <summary>Regression guard for the two Start Menu entries and legacy EXE reported on Windows.</summary>
public sealed class ArSeeWindowsIdentityTests
{
    [Fact]
    public void InstallerUsesOneArSeeExecutableAndMigratesOnlyKnownLegacyShortcuts()
    {
        var script = Read("installer/ARIEC60870.iss");

        Assert.Contains("AppName=ArSee", script);
        Assert.Contains("AppId={{{#AppGuid}}", script);
        Assert.Contains("DefaultDirName={localappdata}\\Programs\\ArSee", script);
        Assert.Contains("UsePreviousAppDir=no", script);
        Assert.Contains("function PrepareToInstall(var NeedsRestart: Boolean): String;", script);
        Assert.Contains("if DirExists(NewDir) then", script);
        Assert.Contains("not RemoveDir(LegacyDir)", script);
        Assert.DoesNotContain("DelTree(LegacyDir", script);
        Assert.Contains("UsePreviousGroup=no", script); // do not reuse legacy group name
        Assert.Contains("UninstallDisplayName=ArSee", script);
        Assert.Contains("UninstallDisplayIcon={app}\\ArSee.exe", script);
        Assert.Contains("CloseApplicationsFilter=ARIEC60870.exe,ArSee.exe", script);
        Assert.Contains("OutputBaseFilename=ArSee-v{#AppVersion}-setup-win-x64", script);
        Assert.Contains("DestName: \"ArSee.exe\"", script);
        Assert.DoesNotContain("DestName: \"ARIEC60870.exe\"", script);
        Assert.Contains("Name: \"{group}\\ArSee\"; Filename: \"{app}\\ArSee.exe\"", script);
        Assert.Contains("Name: \"{autodesktop}\\ArSee\"; Filename: \"{app}\\ArSee.exe\"", script);
        Assert.Contains("Type: files; Name: \"{app}\\ARIEC60870.exe\"", script);
        Assert.Contains("Type: files; Name: \"{userprograms}\\ARIEC60870\\ARIEC60870.lnk\"", script);
        Assert.Contains("Type: files; Name: \"{userprograms}\\ARIEC60870\\ArSee.lnk\"", script);
        Assert.Contains("Type: dirifempty; Name: \"{userprograms}\\ARIEC60870\"", script);
        Assert.DoesNotContain("[UninstallDelete]\\n", script);
    }

    [Fact]
    public void NewBrandPackagesAndUpdaterHaveMatchingFailClosedNames()
    {
        var portable = Read("scripts/publish-windows-portable.ps1");
        var verifier = Read("scripts/verify-release-package.ps1");
        var updater = Read("src/ARIEC60870.Desktop/Services/ReleaseUpdateService.cs");
        var worker = Read("src/ARIEC60870.Desktop/Services/ReleaseInstallWorker.cs");
        var workflow = Read(".github/workflows/release-package.yml");

        Assert.Contains("ArSee-v$Version-$Runtime.zip", portable);
        Assert.Contains("ArSee.exe", verifier);
        Assert.DoesNotContain("ARIEC60870.exe", verifier);
        Assert.Contains("ArSee-{tag}-setup-win-x64.exe", updater);
        Assert.Contains("ArSee-\" + plan.Tag + \"-setup-win-x64.exe", worker);
        Assert.Contains("ArSee-v${{ needs.resolve.outputs.version }}-setup-win-x64.exe", workflow);
        Assert.Contains("ArSee-v${{ needs.resolve.outputs.version }}-win-x64.zip", workflow);
    }

    [Fact]
    public void ExactWindowsMigrationFATIsRequiredBeforeReleaseResume()
    {
        var ci = Read(".github/workflows/ci.yml");
        var fat = Read("scripts/test-branding-migration-fat.ps1");
        Assert.Contains("branding-migration-fat:", ci);
        Assert.Contains("needs.branding-migration-fat.result == 'success'", ci);
        Assert.Contains("ARIEC60870-v3.6.17-setup-win-x64.exe", fat);
        Assert.Contains("ArSee.exe", fat);
        Assert.Contains("ARIEC60870.lnk", fat);
        Assert.Contains("Get-ArSeeRegistrations", fat);
        Assert.Contains("branding-migration-preserve.txt", fat);
        Assert.Contains("PreviousVersion = '3.6.17'", fat);
        Assert.Contains("Legacy Programs/ARIEC60870 folder remains after migration.", fat);
        Assert.Contains("-PreviousVersion $old", ci);
    }

    [Fact]
    public void PublishedStableFallbackUsesVerifiedArSeeInstallerNaming()
    {
        var site = Read("site/release-download.js");
        var docs = Read("docs/release-download.js");
        Assert.Equal(site, docs);
        Assert.Contains("const fallbackTag = \"v3.6.18\"", site);
        Assert.Contains("ArSee-v3.6.18-setup-win-x64.exe", Read("site/download.html"));
        Assert.Contains("ArSee-v3.6.18-win-x64.zip", Read("site/id/download.html"));
        Assert.Contains("\"ArSee-\", \"ARIEC60870-\"", site);
    }

    private static string Read(string path)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "ARIEC60870.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Join(root!.FullName, path.Replace('/', Path.DirectorySeparatorChar)));
    }
}
