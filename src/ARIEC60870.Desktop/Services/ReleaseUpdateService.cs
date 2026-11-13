// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ARIEC60870.Desktop.Services;

/// <summary>
/// A release is only offered after a stable semver, exact official asset name,
/// official GitHub download URL, and GitHub SHA-256 evidence agree. The app
/// never executes a download in its own process or without operator consent.
/// </summary>
public static class ReleaseUpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/masarray/ARIEC60870/releases/latest";
    private const string ReleaseOrigin = "https://github.com/masarray/ARIEC60870/releases/download/";
    private const long MaximumInstallerBytes = 300L * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();

    public static string UpdatesRoot => Path.Combine(LocalWorkspacePaths.AppDataRoot, "updates");

    public static bool HasManagedInstallation(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath);
        return directory is not null &&
            string.Equals(Path.GetFileName(executablePath), "ARIEC60870.exe", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(directory, "unins000.exe")) &&
            File.Exists(Path.Combine(directory, "unins000.dat"));
    }

    public static bool TryParseStableTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (tag is null || !Regex.IsMatch(tag, @"^v[0-9]+\.[0-9]+\.[0-9]+$", RegexOptions.CultureInvariant))
            return false;
        return Version.TryParse(tag[1..], out version!);
    }

    public static bool IsOfficialAssetUrl(string? url, string tag, string name)
    {
        if (!TryParseStableTag(tag, out _)) return false;
        var expected = ReleaseOrigin + tag + "/" + name;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(uri.AbsoluteUri, expected, StringComparison.Ordinal);
    }

    public static async Task<ReleaseUpdateCandidate?> FindLatestAsync(Version installedVersion, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        var release = doc.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            return null;

        var tag = release.GetProperty("tag_name").GetString();
        if (!TryParseStableTag(tag, out var version) || version <= installedVersion)
            return null;

        var name = $"ARIEC60870-{tag}-setup-win-x64.exe";
        var assets = release.GetProperty("assets");
        if (assets.ValueKind != JsonValueKind.Array) return null;
        JsonElement installer = default;
        JsonElement checksum = default;
        foreach (var asset in assets.EnumerateArray())
        {
            var assetName = asset.GetProperty("name").GetString();
            if (assetName == name) installer = asset;
            if (assetName == "SHA256SUMS.txt") checksum = asset;
        }
        if (installer.ValueKind != JsonValueKind.Object || checksum.ValueKind != JsonValueKind.Object)
            return null;

        var url = installer.GetProperty("browser_download_url").GetString();
        var sumsUrl = checksum.GetProperty("browser_download_url").GetString();
        if (!IsOfficialAssetUrl(url, tag!, name) ||
            !IsOfficialAssetUrl(sumsUrl, tag!, "SHA256SUMS.txt"))
            throw new InvalidDataException("Release asset download URL is not official.");
        var size = installer.GetProperty("size").GetInt64();
        if (size < 1_000_000 || size > MaximumInstallerBytes)
            throw new InvalidDataException("Installer size is outside the expected safe range.");

        var sumsText = await Client.GetStringAsync(sumsUrl, token).ConfigureAwait(false);
        var sumsDigest = FindDigest(sumsText, name);
        if (sumsDigest is null)
            throw new InvalidDataException("No matching installer entry in official SHA256SUMS.txt.");
        var apiDigest = installer.TryGetProperty("digest", out var digestElement) &&
                        digestElement.ValueKind == JsonValueKind.String
            ? digestElement.GetString() : null;
        if (apiDigest is not null &&
            !string.Equals(apiDigest, "sha256:" + sumsDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub release digest disagrees with published SHA256SUMS.txt.");

        return new ReleaseUpdateCandidate(tag!, version, name, new Uri(url!), sumsDigest, size);
    }

    public static string? FindDigest(string sums, string fileName)
    {
        foreach (Match match in Regex.Matches(sums, @"(?m)^([0-9a-fA-F]{64})\s+\*?(\S+)\s*$",
                     RegexOptions.CultureInvariant))
        {
            if (string.Equals(match.Groups[2].Value, fileName, StringComparison.Ordinal))
                return match.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }

    public static async Task<UpdateInstallPlan> DownloadAndStageAsync(
        ReleaseUpdateCandidate release, string installedExecutable,
        IProgress<int>? progress, CancellationToken token)
    {
        if (!HasManagedInstallation(installedExecutable))
            throw new InvalidOperationException("Automatic replacement requires an existing ArSee Inno Setup installation.");
        if (!IsOfficialAssetUrl(release.DownloadUrl.AbsoluteUri, release.Tag, release.FileName))
            throw new InvalidDataException("Untrusted installer URL.");

        var stage = Path.Combine(UpdatesRoot, release.Tag);
        Directory.CreateDirectory(stage);
        var stagedInstaller = Path.Combine(stage, release.FileName);
        var part = stagedInstaller + ".part";
        // The worker must exist outside the installation before its parent
        // closes. Fail early while the operator can still use the old app.
        // Existing verified downloads do not need a second full download.
        var requiredDownload = await VerifyFileAsync(stagedInstaller,
            release.Sha256, release.ByteCount, token).ConfigureAwait(false)
            ? 0L : release.ByteCount;
        var workerBytes = new FileInfo(installedExecutable).Length;
        UpdateStorageSafety.EnsureAvailableSpace(stage,
            checked(requiredDownload + workerBytes));
        if (!await VerifyFileAsync(stagedInstaller, release.Sha256, release.ByteCount, token).ConfigureAwait(false))
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(part)) File.Delete(part);
                    using var response = await Client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is long advertised && advertised != release.ByteCount)
                        throw new InvalidDataException("Downloaded installer length differs from GitHub metadata.");
                    await using (var output = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                    await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                    {
                        var buffer = new byte[131072];
                        long bytes = 0;
                        int read;
                        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                        {
                            bytes += read;
                            if (bytes > release.ByteCount)
                                throw new InvalidDataException("Installer exceeds expected byte count.");
                            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                            progress?.Report((int)(100 * bytes / release.ByteCount));
                        }
                    }
                    if (!await VerifyFileAsync(part, release.Sha256, release.ByteCount, token).ConfigureAwait(false))
                        throw new InvalidDataException("Installer SHA-256 verification failed.");
                    File.Move(part, stagedInstaller, true);
                    break;
                }
                catch (Exception ex) when (attempt < 3 &&
                    (ex is HttpRequestException or IOException or TaskCanceledException) &&
                    !token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), token).ConfigureAwait(false);
                }
                finally
                {
                    if (File.Exists(part)) File.Delete(part);
                }
            }
        }

        // Copy the *published self-contained single-file application* out of
        // the installation directory. It becomes a short-lived independent
        // worker, so replacing the installed executable is never self-mutation.
        var worker = Path.Combine(stage, "ArSee.UpdateWorker.exe");
        File.Copy(installedExecutable, worker, true);
        var plan = new UpdateInstallPlan
        {
            Tag = release.Tag,
            InstallerPath = stagedInstaller,
            InstallerSha256 = release.Sha256,
            InstallerBytes = release.ByteCount,
            InstalledExecutable = installedExecutable,
            ParentPid = Environment.ProcessId,
            StageDirectory = stage
        };
        var planFile = Path.Combine(stage, "install-plan.json");
        await File.WriteAllTextAsync(planFile, JsonSerializer.Serialize(plan), token).ConfigureAwait(false);
        return plan;
    }

    public static void LaunchWorker(UpdateInstallPlan plan)
    {
        var exe = Path.Combine(plan.StageDirectory, "ArSee.UpdateWorker.exe");
        var manifest = Path.Combine(plan.StageDirectory, "install-plan.json");
        if (Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = plan.StageDirectory,
            ArgumentList = { "--apply-release-update", manifest }
        }) is null)
            throw new InvalidOperationException("Update worker failed to launch.");
    }

    public static async Task<bool> VerifyFileAsync(string path, string digest, long length, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != length) return false;
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        using var sha = SHA256.Create();
        var actual = await sha.ComputeHashAsync(input, token).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(actual), digest, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ArSee-Desktop-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

public sealed record ReleaseUpdateCandidate(
    string Tag, Version Version, string FileName, Uri DownloadUrl,
    string Sha256, long ByteCount);

public sealed class UpdateInstallPlan
{
    public string Tag { get; set; } = "";
    public string InstallerPath { get; set; } = "";
    public string InstallerSha256 { get; set; } = "";
    public long InstallerBytes { get; set; }
    public string InstalledExecutable { get; set; } = "";
    public int ParentPid { get; set; }
    public string StageDirectory { get; set; } = "";
}
