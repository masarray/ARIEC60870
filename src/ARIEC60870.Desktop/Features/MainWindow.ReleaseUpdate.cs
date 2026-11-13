// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ARIEC60870.Desktop.Services;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private const string LatestReleaseBrowserUrl = "https://github.com/masarray/ARIEC60870/releases/latest";
    private static readonly TimeSpan ReleaseUpdateInitialDelay = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan ReleaseUpdateMinimumCheckInterval = TimeSpan.FromHours(20);

    private CancellationTokenSource? _releaseUpdateCheckCancellation;
    private ReleaseUpdateCandidate? _releaseUpdateCandidate;
    private UpdateInstallPlan? _pendingUpdatePlan;
    private CancellationTokenSource? _releaseUpdateDownloadCancellation;
    private bool _releaseUpdateIsDownloading;

    private void StartLazyReleaseUpdateCheck()
    {
        if (_releaseUpdateCheckCancellation is not null) return;
        _releaseUpdateCheckCancellation = new CancellationTokenSource();
        _ = Task.Run(() => RunLazyReleaseUpdateCheckAsync(_releaseUpdateCheckCancellation.Token));
    }

    private void CancelReleaseUpdateCheck()
    {
        try { _releaseUpdateCheckCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        try { _releaseUpdateDownloadCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async Task RunLazyReleaseUpdateCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ReleaseUpdateInitialDelay, cancellationToken).ConfigureAwait(false);
            ReleaseInstallWorker.CleanupCompletedUpdates();
            if (!ShouldCheckReleaseUpdate()) return;
            using var networkTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            networkTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            var latest = await ReleaseUpdateService.FindLatestAsync(
                GetCurrentProductVersion(), networkTimeout.Token).ConfigureAwait(false);
            SaveReleaseUpdateCheckState(latest?.Tag);
            if (latest is null) return;
            await Dispatcher.InvokeAsync(
                () => ShowReleaseUpdateNotification(latest),
                DispatcherPriority.ApplicationIdle, cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { }
        catch (IOException) { }
        catch (JsonException) { }
        catch (Exception)
        {
            // Release discovery must never interrupt IEC communication or WPF.
            // An explicit retry remains available through the public releases page.
        }
    }

    private void ShowReleaseUpdateNotification(ReleaseUpdateCandidate latest)
    {
        if (UpdateAvailableButton is null) return;
        _releaseUpdateCandidate = latest;
        UpdateAvailableButton.Content = $"Update available · {latest.Tag}";
        UpdateAvailableButton.Visibility = Visibility.Visible;
        UpdateAvailableButton.ToolTip =
            $"ArSee {latest.Tag} is available. Installed: {GetCurrentProductVersionText()}. Click to update.";

        AppendSessionLog($"Update available: {latest.Tag}; current version {GetCurrentProductVersionText()}.");

        // One non-repeating popup per version, only on an idle station.
        // An active Master / passive monitor must not be obscured by a modal
        // update prompt during an operator's capture or command operation.
        var state = ReadReleaseUpdateState();
        if (state?.LastPromptedTag == latest.Tag ||
            !ReleaseUpdateService.HasManagedInstallation(Environment.ProcessPath ?? string.Empty) ||
            IsReleaseUpdateSessionActive())
            return;
        SaveReleaseUpdateCheckState(latest.Tag, promptedTag: latest.Tag);
        if (ModernMessageBox.Show(this,
            $"ArSee {latest.Tag} is available.\n\nDownload and install the update now? ArSee will safely close, install, and reopen automatically.\n\nYour settings, signal lists and saved captures are preserved. The published Windows package may be unsigned.",
            "Update available", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _ = BeginUpdateAsync(alreadyConfirmed: true);
    }

    private async void UpdateAvailable_Click(object sender, RoutedEventArgs e)
        => await BeginUpdateAsync();

    private bool IsReleaseUpdateSessionActive() =>
        _sessionCancellation is not null || _passiveMonitorSession is not null ||
        _passiveReplayCancellation is not null;

    private async Task BeginUpdateAsync(bool alreadyConfirmed = false)
    {
        if (_releaseUpdateIsDownloading || _releaseUpdateCandidate is null) return;
        var currentExe = Environment.ProcessPath ?? string.Empty;
        if (!ReleaseUpdateService.HasManagedInstallation(currentExe))
        {
            ModernMessageBox.Show(this,
                "This copy of ArSee is running as a portable application. Automatic replacement is supported for the installed version. Download the official installer from GitHub Releases to switch to managed updates.",
                "Portable installation", MessageBoxButton.OK, MessageBoxImage.Information);
            OpenOfficialReleasePage();
            return;
        }

        var candidate = _releaseUpdateCandidate;
        if (candidate.Version <= GetCurrentProductVersion()) return;
        if (_pendingUpdatePlan is not null)
        {
            if (ModernMessageBox.Show(this,
                "The update is already downloaded and verified. Close ArSee and install it now? Any active IEC session will first be finalized by the normal safe shutdown procedure.",
                "Install prepared update", MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
                Close();
            return;
        }

        if (!alreadyConfirmed)
        {
            var prompt = IsReleaseUpdateSessionActive()
                ? "An IEC session is active. Download now, then install only after you close ArSee normally?\n\nMonitoring and evidence recording will continue during download."
                : "Download and install the verified update? ArSee will close normally, run Windows Setup and reopen.\n\nSaved profiles and captures will be preserved.";
            if (ModernMessageBox.Show(this, prompt, "ArSee update",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
        }

        _releaseUpdateIsDownloading = true;
        UpdateAvailableButton.IsEnabled = false;
        using var downloadCancellation = new CancellationTokenSource();
        _releaseUpdateDownloadCancellation = downloadCancellation;
        try
        {
            UpdateAvailableButton.Content = "Downloading 0%";
            var progress = new Progress<int>(percent =>
            {
                if (UpdateAvailableButton.IsLoaded)
                    UpdateAvailableButton.Content = $"Downloading {percent}%";
            });
            // No application shutdown before the package is fully downloaded,
            // exact-size verified and SHA-256 matched.
            var plan = await ReleaseUpdateService.DownloadAndStageAsync(
                candidate, currentExe, progress, downloadCancellation.Token);
            _pendingUpdatePlan = plan;
            Closed += LaunchVerifiedUpdateWorkerOnClosed;
            if (IsReleaseUpdateSessionActive())
            {
                // A verified download never interrupts an ongoing IEC session.
                UpdateAvailableButton.Content = "Ready · install on exit";
                UpdateAvailableButton.ToolTip =
                    "Installer verified. Close ArSee normally after the IEC session ends to apply the update.";
            }
            else
            {
                UpdateAvailableButton.Content = "Installing after safe close…";
                Close();
            }
        }
        catch (OperationCanceledException) when (downloadCancellation.IsCancellationRequested)
        {
            // A window close during download never launches the worker.
            if (IsLoaded)
                UpdateAvailableButton.Content = $"Update available · {candidate.Tag}";
        }
        catch (Exception ex)
        {
            AppendSessionLog("Update download/staging failed: " + ex.Message);
            ModernMessageBox.Show(this,
                "The update could not be prepared. ArSee remains installed and usable.\n\n" +
                ex.Message + "\n\nCheck your internet connection and free disk space, then retry.",
                "Update not started", MessageBoxButton.OK, MessageBoxImage.Warning);
            UpdateAvailableButton.Content = $"Update available · {candidate.Tag}";
        }
        finally
        {
            _releaseUpdateDownloadCancellation = null;
            _releaseUpdateIsDownloading = false;
            if (IsLoaded)
                UpdateAvailableButton.IsEnabled = true;
        }
    }

    private void LaunchVerifiedUpdateWorkerOnClosed(object? sender, EventArgs e)
    {
        Closed -= LaunchVerifiedUpdateWorkerOnClosed;
        if (_pendingUpdatePlan is null) return;
        try
        {
            ReleaseUpdateService.LaunchWorker(_pendingUpdatePlan);
        }
        catch (Exception ex)
        {
            // A failed process launch must be visible rather than silently
            // stranding the operator with a closed application.
            MessageBox.Show("ArSee update worker did not start. Open ArSee again normally.\n\n" +
                ex.Message, "Update could not start", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OpenOfficialReleasePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(LatestReleaseBrowserUrl)
            { UseShellExecute = true });
        }
        catch (Exception) { }
    }

    private static bool ShouldCheckReleaseUpdate()
    {
        var state = ReadReleaseUpdateState();
        // If the engineer clicked Later and reopened ArSee, rediscover the
        // pending version instead of hiding the update button for 20 hours.
        if (ReleaseUpdateService.TryParseStableTag(state?.LastSeenTag, out var seen) &&
            seen > GetCurrentProductVersion()) return true;
        return state is null || state.LastCheckedUtc == default ||
            DateTime.UtcNow - state.LastCheckedUtc >= ReleaseUpdateMinimumCheckInterval;
    }

    private static ReleaseUpdateState? ReadReleaseUpdateState()
    {
        try
        {
            var path = LocalWorkspacePaths.ReleaseUpdateStateFile;
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ReleaseUpdateState>(File.ReadAllText(path))
                : null;
        }
        catch (Exception) { return null; }
    }

    private static void SaveReleaseUpdateCheckState(string? latestTag, string? promptedTag = null)
    {
        try
        {
            var path = LocalWorkspacePaths.ReleaseUpdateStateFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var state = ReadReleaseUpdateState() ?? new ReleaseUpdateState();
            state.LastCheckedUtc = DateTime.UtcNow;
            if (latestTag is not null) state.LastSeenTag = latestTag;
            if (promptedTag is not null) state.LastPromptedTag = promptedTag;
            File.WriteAllText(path, JsonSerializer.Serialize(state));
        }
        catch (Exception) { }
    }

    private static Version GetCurrentProductVersion()
    {
        var version = GetCurrentProductVersionText().Split('+', '-')[0].TrimStart('v', 'V');
        return Version.TryParse(version, out var parsed) ? parsed
            : Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    }

    private static string GetCurrentProductVersionText()
        => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.0.0";

    private sealed class ReleaseUpdateState
    {
        public DateTime LastCheckedUtc { get; set; }
        public string LastSeenTag { get; set; } = "";
        public string LastPromptedTag { get; set; } = "";
    }
}
