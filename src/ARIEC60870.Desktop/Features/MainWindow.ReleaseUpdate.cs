// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using System.Globalization;
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
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/masarray/ARIEC60870/releases/latest";
    private const string LatestReleaseBrowserUrl = "https://github.com/masarray/ARIEC60870/releases/latest";
    private static readonly TimeSpan ReleaseUpdateInitialDelay = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan ReleaseUpdateNetworkTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ReleaseUpdateMinimumCheckInterval = TimeSpan.FromHours(20);

    private CancellationTokenSource? _releaseUpdateCheckCancellation;
    private Uri? _releaseUpdateUri;

    private void StartLazyReleaseUpdateCheck()
    {
        if (_releaseUpdateCheckCancellation is not null)
        {
            return;
        }

        _releaseUpdateCheckCancellation = new CancellationTokenSource();
        var token = _releaseUpdateCheckCancellation.Token;
        _ = Task.Run(() => RunLazyReleaseUpdateCheckAsync(token), token);
    }

    private void CancelReleaseUpdateCheck()
    {
        try
        {
            _releaseUpdateCheckCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // App shutdown path: no user-visible action required.
        }
    }

    private async Task RunLazyReleaseUpdateCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ReleaseUpdateInitialDelay, cancellationToken).ConfigureAwait(false);
            if (!ShouldCheckReleaseUpdate())
            {
                return;
            }

            var latest = await FetchLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            SaveReleaseUpdateCheckState(latest?.TagName);

            if (latest is null || latest.IsDraft || latest.IsPrerelease)
            {
                return;
            }

            var currentVersion = GetCurrentProductVersion();
            var latestVersion = TryParseVersion(latest.TagName ?? latest.Name);
            if (currentVersion is null || latestVersion is null || latestVersion <= currentVersion)
            {
                return;
            }

            await Dispatcher.InvokeAsync(
                () => ShowReleaseUpdateNotification(latest, latestVersion),
                DispatcherPriority.ApplicationIdle,
                cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Network timeout is treated as offline/noisy network. Stay silent.
        }
        catch (OperationCanceledException)
        {
            // Shutdown or app closing. Silent by design.
        }
        catch (HttpRequestException)
        {
            // Offline, DNS, proxy, firewall, or GitHub unreachable: stay silent.
        }
        catch (IOException)
        {
            // State-file write/read issue must never disturb protocol testing.
        }
        catch (JsonException)
        {
            // Bad/changed response format: stay silent and let future builds improve it.
        }
        catch (Exception)
        {
            // Update check is deliberately best-effort and non-critical.
        }
    }

    private static async Task<ReleaseUpdateInfo?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            Timeout = ReleaseUpdateNetworkTimeout
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.UserAgent.ParseAdd("ARIEC60870-EvidenceAnalyzer/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var tagName = ReadString(root, "tag_name");
        var name = ReadString(root, "name");
        var htmlUrl = ReadString(root, "html_url");
        var draft = ReadBoolean(root, "draft");
        var prerelease = ReadBoolean(root, "prerelease");
        var publishedAt = ReadString(root, "published_at");

        if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(htmlUrl))
        {
            return null;
        }

        return new ReleaseUpdateInfo(tagName, name, htmlUrl, draft, prerelease, publishedAt);
    }

    private void ShowReleaseUpdateNotification(ReleaseUpdateInfo latest, Version latestVersion)
    {
        if (UpdateAvailableButton is null)
        {
            return;
        }

        _releaseUpdateUri = Uri.TryCreate(latest.HtmlUrl, UriKind.Absolute, out var uri)
            ? uri
            : new Uri(LatestReleaseBrowserUrl);

        UpdateAvailableButton.Content = $"Update {latest.TagName}";
        UpdateAvailableButton.Visibility = Visibility.Visible;
        UpdateAvailableButton.ToolTip = $"New ARIEC60870 release {latest.TagName} is available. Current version: {GetCurrentProductVersionText()}. Click to open the release page.";

        // Low-noise trace only when there is a real update. No modal dialog, no issue, no diagnostic.
        AppendSessionLog($"Update available: ARIEC60870 {latest.TagName} is newer than installed version {GetCurrentProductVersionText()}.");
    }

    private void UpdateAvailable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var target = _releaseUpdateUri?.ToString() ?? LatestReleaseBrowserUrl;
            Process.Start(new ProcessStartInfo(target)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ModernMessageBox.Show(
                this,
                "Could not open the release page automatically. Please open the ARIEC60870 GitHub releases page manually.",
                "Open release page",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            AppendSessionLog("Release page could not be opened: " + ex.Message);
        }
    }

    private static bool ShouldCheckReleaseUpdate()
    {
        try
        {
            var path = LocalWorkspacePaths.ReleaseUpdateStateFile;
            if (!File.Exists(path))
            {
                return true;
            }

            var state = JsonSerializer.Deserialize<ReleaseUpdateState>(File.ReadAllText(path));
            if (state is null || state.LastCheckedUtc == default)
            {
                return true;
            }

            return DateTime.UtcNow - state.LastCheckedUtc >= ReleaseUpdateMinimumCheckInterval;
        }
        catch
        {
            return true;
        }
    }

    private static void SaveReleaseUpdateCheckState(string? latestTag)
    {
        try
        {
            var path = LocalWorkspacePaths.ReleaseUpdateStateFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var state = new ReleaseUpdateState
            {
                LastCheckedUtc = DateTime.UtcNow,
                LastSeenTag = latestTag ?? string.Empty
            };
            File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Local state is optional. Never let it affect the analyzer.
        }
    }

    private static Version? GetCurrentProductVersion()
        => TryParseVersion(GetCurrentProductVersionText()) ?? Assembly.GetExecutingAssembly().GetName().Version;

    private static string GetCurrentProductVersionText()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            return info;
        }

        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
    }

    private static Version? TryParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = value.Trim();
        if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[1..];
        }

        var firstDigitIndex = clean.IndexOfAny(new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' });
        if (firstDigitIndex > 0)
        {
            clean = clean[firstDigitIndex..];
        }

        var suffixIndex = clean.IndexOfAny(new[] { '-', '+', ' ' });
        if (suffixIndex >= 0)
        {
            clean = clean[..suffixIndex];
        }

        var parts = clean.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var normalized = parts.Take(4).ToList();
        while (normalized.Count < 3)
        {
            normalized.Add("0");
        }

        var versionText = string.Join(".", normalized.Select(part => new string(part.TakeWhile(char.IsDigit).ToArray())));
        if (versionText.Split('.').Any(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        return Version.TryParse(versionText, out var parsed) ? parsed : null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    private sealed class ReleaseUpdateInfo
    {
        public ReleaseUpdateInfo(string tagName, string? name, string htmlUrl, bool isDraft, bool isPrerelease, string? publishedAt)
        {
            TagName = tagName;
            Name = name ?? tagName;
            HtmlUrl = htmlUrl;
            IsDraft = isDraft;
            IsPrerelease = isPrerelease;
            PublishedAt = publishedAt ?? string.Empty;
        }

        public string TagName { get; }
        public string Name { get; }
        public string HtmlUrl { get; }
        public bool IsDraft { get; }
        public bool IsPrerelease { get; }
        public string PublishedAt { get; }
    }

    private sealed class ReleaseUpdateState
    {
        public DateTime LastCheckedUtc { get; set; }
        public string LastSeenTag { get; set; } = string.Empty;
    }
}
