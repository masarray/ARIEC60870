// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

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
            if (HasAnotherInstalledInstance(plan.InstalledExecutable))
                throw new InvalidOperationException(
                    "Another ArSee window is still open. Close other instances before updating.");
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

            // Existing application closure is under ArSee's journal gate.
            // Never let Inno's Restart Manager silently close a second user
            // instance with a live IEC-101/103/104 link.
            if (HasAnotherInstalledInstance(plan.InstalledExecutable))
                throw new InvalidOperationException(
                    "An ArSee window reopened during staging. Installation safely cancelled.");
            var innoLog = Path.Combine(plan.StageDirectory, "update-inno.log");
            using var setup = Process.Start(new ProcessStartInfo(plan.InstallerPath)
            {
                UseShellExecute = false,
                WorkingDirectory = plan.StageDirectory,
                ArgumentList =
                {
                    "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-",
                    "/NORESTARTAPPLICATIONS", "/NOCLOSEAPPLICATIONS",
                    "/NOFORCECLOSEAPPLICATIONS", "/LOG=" + innoLog
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

            // Process.Start alone is not success. Require the WPF window's
            // first ContentRendered event from the same PID and expected version.
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

            WriteStatus(plan, "Success " + plan.Tag + "; new WPF first frame acknowledged");
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
                var rollbackVerified = false;

                if (canRollback)
                {
                    try
                    {
                        RestoreInstallation(backupDir!, Path.GetDirectoryName(plan.InstalledExecutable)!);
                        if (!string.Equals(CalculateFileSha256(plan.InstalledExecutable),
                                priorExecutableDigest, StringComparison.Ordinal))
                            throw new IOException("Restored executable failed its backup checksum.");
                        rollbackVerified = true;
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
                        (rollbackVerified || !backupComplete))
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
                if (Environment.GetEnvironmentVariable("ARSEE_UPDATER_CI_MODE") == "1")
                    return 1; // Explicit unattended Windows acceptance; log already persisted.
                MessageBox.Show(
                    "ArSee could not confirm a successful update. Stored profiles and capture evidence were not deleted.\n\n" +
                    exception.Message + "\n\n" + guidance,
                    "ArSee update not completed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                if (Environment.GetEnvironmentVariable("ARSEE_UPDATER_CI_MODE") == "1")
                    return 1;
                MessageBox.Show("ArSee update plan could not be validated. No application files were changed.\n\n" +
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
            !ReleaseUpdateService.HasManagedInstallation(plan.InstalledExecutable) ||
            !IsRegisteredInstallationTarget(plan.InstalledExecutable))
            throw new InvalidDataException("Update plan failed local path, version or installation validation.");
        return plan;
    }

    /// <summary>
    /// The managed target must agree with Inno's HKCU installed-app record.
    /// This permits operator-chosen installation directories but refuses
    /// update plans aiming at a mock ARIEC60870.exe in an arbitrary folder.
    /// </summary>
    private static bool IsRegisteredInstallationTarget(string executable)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (root is null) return false;
            var folder = Path.GetFullPath(Path.GetDirectoryName(executable)!);
            foreach (var subkey in root.GetSubKeyNames())
            {
                using var app = root.OpenSubKey(subkey);
                if (!string.Equals(app?.GetValue("DisplayName") as string,
                        "ArSee", StringComparison.OrdinalIgnoreCase))
                    continue;
                var location = app?.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(location) &&
                    string.Equals(folder,
                        Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)),
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or System.Security.SecurityException) { }
        return false;
    }

    private static bool HasAnotherInstalledInstance(string executable)
    {
        var expected = Path.GetFullPath(executable);
        foreach (var process in Process.GetProcessesByName(
                     Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId || process.HasExited)
                    continue;
                try
                {
                    if (string.Equals(Path.GetFullPath(process.MainModule!.FileName),
                            expected, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException
                    or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
                {
                    // Cannot establish an ArSee process's path: fail closed.
                    return true;
                }
            }
        }
        return false;
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
