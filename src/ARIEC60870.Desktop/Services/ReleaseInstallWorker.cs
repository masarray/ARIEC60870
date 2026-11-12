// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text.Json;
using System.Windows;

namespace ARIEC60870.Desktop.Services;

/// <summary>
/// Runs from a verified copy of the existing self-contained EXE outside {app}.
/// No forced close: the parent must exit normally so live IEC capture and its
/// journal have finished before Inno replaces any installed file.
/// </summary>
public static class ReleaseInstallWorker
{
    public static async Task<int> RunAsync(string planPath)
    {
        UpdateInstallPlan? plan = null;
        string? backupDir = null;
        try
        {
            plan = await ReadValidatedPlanAsync(planPath).ConfigureAwait(false);
            if (!await WaitForParentExitAsync(plan.ParentPid).ConfigureAwait(false))
                throw new InvalidOperationException("ArSee did not close safely. Update was cancelled; no files were modified.");
            if (!await ReleaseUpdateService.VerifyFileAsync(plan.InstallerPath,
                plan.InstallerSha256, plan.InstallerBytes, CancellationToken.None).ConfigureAwait(false))
                throw new InvalidDataException("Staged installer changed or failed SHA-256 verification.");

            // An offline, restorable copy is made BEFORE starting the installer.
            // Settings/capture journals remain entirely outside the install path.
            var installFolder = Path.GetDirectoryName(plan.InstalledExecutable)!;
            backupDir = Path.Combine(plan.StageDirectory, "previous-install");
            BackupInstallation(installFolder, backupDir);
            WriteStatus(plan, "Installing " + plan.Tag);

            using var setup = Process.Start(new ProcessStartInfo(plan.InstallerPath)
            {
                UseShellExecute = false,
                WorkingDirectory = plan.StageDirectory,
                ArgumentList =
                {
                    "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
                    "/NORESTARTAPPLICATIONS", "/CLOSEAPPLICATIONS", "/SP-"
                }
            }) ?? throw new InvalidOperationException("Windows could not launch the installer.");

            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12)))
            {
                try
                {
                    await setup.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Never kill an installer while it may be writing files.
                    throw new TimeoutException("Installer exceeded twelve minutes. Its process was not forcibly terminated.");
                }
            }

            if (setup.ExitCode != 0 || !File.Exists(plan.InstalledExecutable))
                throw new InvalidOperationException("Installer did not finish successfully (exit " + setup.ExitCode + ").");

            var installed = FileVersionInfo.GetVersionInfo(plan.InstalledExecutable).FileVersion;
            if (!ReleaseUpdateService.TryParseStableTag(plan.Tag, out var expected) ||
                !Version.TryParse(installed, out var actual) || actual < expected)
                throw new InvalidDataException("Installed executable does not report the expected new version.");

            WriteStatus(plan, "Installed " + plan.Tag + "; restarting ArSee");
            var restarted = StartInstalledApp(plan.InstalledExecutable);
            // Catch immediate crash/regression; preserve the old application
            // if the new binary fails before the engineer can use it.
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (restarted.HasExited && restarted.ExitCode != 0)
                throw new InvalidOperationException("New ArSee version exited immediately (code " + restarted.ExitCode + ").");

            WriteStatus(plan, "Success " + plan.Tag);
            TryDeleteDirectory(backupDir);
            TryDeleteFile(plan.InstallerPath);
            TryDeleteFile(planPath);
            // The copied worker remains locked until this process exits. A
            // subsequent normal app startup removes completed old staging.
            File.WriteAllText(Path.Combine(plan.StageDirectory, "completed.marker"), DateTime.UtcNow.ToString("O"));
            return 0;
        }
        catch (Exception exception)
        {
            if (plan is not null)
            {
                WriteStatus(plan, "Update failed: " + exception.Message);
                if (backupDir is not null && Directory.Exists(backupDir))
                {
                    try
                    {
                        RestoreInstallation(backupDir, Path.GetDirectoryName(plan.InstalledExecutable)!);
                        WriteStatus(plan, "Previous installation restored.");
                    }
                    catch (Exception recovery)
                    {
                        WriteStatus(plan, "Manual recovery may be needed: " + recovery.Message);
                    }
                }

                // Do not strand the operator. If old EXE exists and can launch,
                // return them to the prior version without opening GitHub.
                try
                {
                    if (File.Exists(plan.InstalledExecutable))
                        StartInstalledApp(plan.InstalledExecutable);
                }
                catch (Exception recovery)
                {
                    WriteStatus(plan, "Cannot restart old installation: " + recovery.Message);
                }
            }
            MessageBox.Show(
                "ArSee could not complete the update. Your stored profiles and capture evidence have not been deleted.\n\n" +
                exception.Message + "\n\nYou can retry from ArSee. Technical details are in the local updates/last-update.log.",
                "ArSee update not completed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return 1;
        }
    }

    public static void CleanupCompletedUpdates()
    {
        try
        {
            if (!Directory.Exists(ReleaseUpdateService.UpdatesRoot)) return;
            foreach (var path in Directory.EnumerateDirectories(ReleaseUpdateService.UpdatesRoot))
            {
                var marker = Path.Combine(path, "completed.marker");
                if (File.Exists(marker) &&
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) > TimeSpan.FromMinutes(1))
                    TryDeleteDirectory(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task<UpdateInstallPlan> ReadValidatedPlanAsync(string planPath)
    {
        var path = Path.GetFullPath(planPath);
        var root = Path.GetFullPath(ReleaseUpdateService.UpdatesRoot) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(path), "install-plan.json", StringComparison.Ordinal))
            throw new InvalidDataException("Update plan path is not trusted.");
        var plan = JsonSerializer.Deserialize<UpdateInstallPlan>(
            await File.ReadAllTextAsync(path).ConfigureAwait(false))
            ?? throw new InvalidDataException("Update plan is empty.");
        if (!ReleaseUpdateService.TryParseStableTag(plan.Tag, out _) ||
            plan.InstallerSha256.Length != 64 ||
            !plan.InstallerSha256.All(Uri.IsHexDigit) ||
            plan.InstallerBytes < 1_000_000 || plan.InstallerBytes > 300L * 1024 * 1024 ||
            plan.ParentPid < 1 ||
            !string.Equals(Path.GetFullPath(plan.StageDirectory) + Path.DirectorySeparatorChar,
                Path.GetDirectoryName(path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.InstallerPath,
                Path.Combine(plan.StageDirectory, "ARIEC60870-" + plan.Tag + "-setup-win-x64.exe"),
                StringComparison.OrdinalIgnoreCase) ||
            !ReleaseUpdateService.HasManagedInstallation(plan.InstalledExecutable))
            throw new InvalidDataException("Update plan failed local path, version or installation validation.");
        return plan;
    }

    private static async Task<bool> WaitForParentExitAsync(int pid)
    {
        try
        {
            using var parent = Process.GetProcessById(pid);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await parent.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (ArgumentException) { return true; } // Parent already exited.
        catch (OperationCanceledException) { return false; }
    }

    private static Process StartInstalledApp(string path)
        => Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path)!
        }) ?? throw new InvalidOperationException("Could not reopen installed ArSee.");

    private static void BackupInstallation(string folder, string backup)
    {
        if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
        Directory.CreateDirectory(backup);
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked files in the application folder prevent safe backup.");
            var relative = Path.GetRelativePath(folder, file);
            var target = Path.Combine(backup, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void RestoreInstallation(string backup, string folder)
    {
        foreach (var file in Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(folder, Path.GetRelativePath(backup, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void WriteStatus(UpdateInstallPlan plan, string status)
    {
        try
        {
            Directory.CreateDirectory(ReleaseUpdateService.UpdatesRoot);
            File.AppendAllText(Path.Combine(ReleaseUpdateService.UpdatesRoot, "last-update.log"),
                DateTime.UtcNow.ToString("O") + " | " + plan.Tag + " | " + status + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string? path)
    {
        try { if (path is not null && Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
