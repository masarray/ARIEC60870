// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.IO;
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
        string? priorExecutableDigest = null;
        var parentExited = false;
        var backupComplete = false;
        var installerFinished = true;
        Process? restarted = null;
        try
        {
            plan = await ReadValidatedPlanAsync(planPath).ConfigureAwait(false);
            if (!await WaitForParentExitAsync(plan.ParentPid).ConfigureAwait(false))
                throw new InvalidOperationException(
                    "ArSee did not close safely. Installation was not started.");

            parentExited = true;
            if (!await ReleaseUpdateService.VerifyFileAsync(plan.InstallerPath,
                plan.InstallerSha256, plan.InstallerBytes, CancellationToken.None).ConfigureAwait(false))
                throw new InvalidDataException("Staged installer changed or failed SHA-256 verification.");

            var installFolder = Path.GetDirectoryName(plan.InstalledExecutable)!;
            backupDir = Path.Combine(plan.StageDirectory, "previous-install");
            BackupInstallation(installFolder, backupDir);

            // The backup is verified before allowing the installer to run.
            // An incomplete backup is never considered usable for rollback.
            var priorExe = Path.Combine(backupDir, Path.GetFileName(plan.InstalledExecutable));
            if (!File.Exists(priorExe))
                throw new IOException("Previous executable was not preserved in backup.");
            priorExecutableDigest = CalculateFileSha256(priorExe);
            if (!string.Equals(priorExecutableDigest,
                    CalculateFileSha256(plan.InstalledExecutable), StringComparison.Ordinal))
                throw new IOException("Previous executable backup could not be verified.");
            backupComplete = true;
            WriteStatus(plan, "Installer verified, previous installation backed up: " + plan.Tag);

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

            installerFinished = false;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12)))
            {
                try
                {
                    await setup.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    installerFinished = true;
                }
                catch (OperationCanceledException)
                {
                    // Never overwrite a partially installed application while
                    // Inno might still be writing to its directory.
                    throw new TimeoutException(
                        "Installer exceeded twelve minutes. No forced termination or concurrent rollback was attempted.");
                }
            }

            if (setup.ExitCode != 0 || !File.Exists(plan.InstalledExecutable))
                throw new InvalidOperationException(
                    "Installer did not finish successfully (exit " + setup.ExitCode + ").");

            if (!ReleaseUpdateService.TryParseStableTag(plan.Tag, out var expected) ||
                !Version.TryParse(
                    FileVersionInfo.GetVersionInfo(plan.InstalledExecutable).FileVersion, out var actual) ||
                actual < expected)
                throw new InvalidDataException("Installed executable does not report the expected new version.");

            // Process.Start alone is not success. Require the real WPF window
            // to report Loaded from the same launched PID and expected version.
            var token = UpdateStartupHandshake.NewToken();
            var ackPath = UpdateStartupHandshake.GetAckPath(plan);
            TryDeleteFile(ackPath);
            WriteStatus(plan, "Installer completed: " + plan.Tag + "; verifying WPF startup");
            restarted = StartInstalledApp(plan.InstalledExecutable, ackPath, token);
            var ready = await UpdateStartupHandshake.WaitForReadyAsync(
                ackPath, restarted, token, expected, TimeSpan.FromSeconds(45),
                CancellationToken.None).ConfigureAwait(false);
            if (!ready)
                throw new InvalidOperationException(
                    restarted.HasExited
                        ? "Updated ArSee exited before its WPF window was ready."
                        : "Updated ArSee started but did not confirm its WPF window; leaving its running files untouched.");

            WriteStatus(plan, "Success " + plan.Tag + "; new WPF window acknowledged");
            TryDeleteDirectory(backupDir);
            TryDeleteFile(plan.InstallerPath);
            TryDeleteFile(planPath);
            // A successful restart leaves the worker EXE locked briefly.
            // A subsequent normal app startup prunes completed staging.
            try
            {
                File.WriteAllText(Path.Combine(plan.StageDirectory, "completed.marker"),
                    DateTime.UtcNow.ToString("O"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return 0;
        }
        catch (Exception exception)
        {
            if (plan is not null)
            {
                WriteStatus(plan, "Update did not complete: " + exception.Message);
                var newAppStillRunning = restarted is not null && !restarted.HasExited;
                var canRollback = parentExited && installerFinished &&
                    backupComplete && !newAppStillRunning &&
                    backupDir is not null && priorExecutableDigest is not null &&
                    Directory.Exists(backupDir);

                if (canRollback)
                {
                    try
                    {
                        RestoreInstallation(backupDir!, Path.GetDirectoryName(plan.InstalledExecutable)!);
                        if (!string.Equals(CalculateFileSha256(plan.InstalledExecutable),
                                priorExecutableDigest, StringComparison.Ordinal))
                            throw new IOException("Restored executable failed its backup checksum.");
                        WriteStatus(plan, "Verified previous installed executable restored.");
                    }
                    catch (Exception recovery)
                    {
                        WriteStatus(plan, "Automatic file recovery incomplete: " + recovery.Message);
                    }
                }

                // No process kill, COM interference, or automatic double
                // installation. Restart the old version only if it is safe.
                try
                {
                    if (parentExited && installerFinished && !newAppStillRunning &&
                        File.Exists(plan.InstalledExecutable) &&
                        (canRollback || !backupComplete))
                        StartInstalledApp(plan.InstalledExecutable);
                }
                catch (Exception recovery)
                {
                    WriteStatus(plan, "Failed to reopen existing ArSee: " + recovery.Message);
                }

                var guidance = !installerFinished
                    ? "The installer may still be running. Do not start another update until it exits."
                    : newAppStillRunning
                        ? "The updated application is still running. Its files were not overwritten; a recovery backup was retained."
                        : "You can reopen ArSee or retry the update. Technical details remain in updates/last-update.log.";
                MessageBox.Show(
                    "ArSee could not confirm a successful update. Stored profiles and capture evidence were not deleted.\\n\\n" +
                    exception.Message + "\\n\\n" + guidance,
                    "ArSee update not completed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show("ArSee update plan could not be validated. No application files were changed.\\n\\n" +
                    exception.Message, "ArSee update rejected", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return 1;
        }
        finally
        {
            restarted?.Dispose();
        }
    }

    private static string CalculateFileSha256(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file));
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
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) > TimeSpan.FromSeconds(15))
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
        if (!ReleaseUpdateService.TryParseStableTag(plan.Tag, out var newVersion) ||
            !Version.TryParse(FileVersionInfo.GetVersionInfo(plan.InstalledExecutable).FileVersion,
                out var oldVersion) || newVersion <= oldVersion ||
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

    private static Process StartInstalledApp(string path, string? ackPath = null, string? token = null)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path)!
        };
        if (ackPath is not null && token is not null)
        {
            info.ArgumentList.Add(UpdateStartupHandshake.StartupArgument);
            info.ArgumentList.Add(ackPath);
            info.ArgumentList.Add(token);
        }
        return Process.Start(info) ??
            throw new InvalidOperationException("Could not reopen installed ArSee.");
    }

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
