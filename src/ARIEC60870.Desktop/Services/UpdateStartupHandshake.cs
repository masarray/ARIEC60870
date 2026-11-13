// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace ARIEC60870.Desktop.Services;

/// <summary>
/// A successful Process.Start is not proof that WPF initialized. The newly
/// installed application acknowledges its own first visible window to the
/// external update worker. Tokens are per-update and scoped to updates/.
/// No COM device is opened and no protocol session is auto-started.
/// </summary>
public static class UpdateStartupHandshake
{
    public const string StartupArgument = "--update-startup-ack";
    public const string AckFileName = "startup-ack.json";
    private const int MaxAckBytes = 1024;

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    public static string GetAckPath(UpdateInstallPlan plan)
        => Path.Combine(plan.StageDirectory, AckFileName);

    public static bool IsValidToken(string? token)
        => token is { Length: 48 } && token.All(Uri.IsHexDigit);

    public static bool IsValidAckPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var full = Path.GetFullPath(path);
            if (!string.Equals(Path.GetFileName(full), AckFileName, StringComparison.Ordinal))
                return false;
            var stage = Path.GetDirectoryName(full);
            if (stage is null || !ReleaseUpdateService.TryParseStableTag(Path.GetFileName(stage), out _))
                return false;
            var expected = Path.Combine(ReleaseUpdateService.UpdatesRoot, Path.GetFileName(stage), AckFileName);
            return string.Equals(full, Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Invoked only from MainWindow.ContentRendered on the normal WPF dispatcher.</summary>
    public static void WriteReadyAcknowledgement(string[] args)
    {
        if (args.Length != 3 || !string.Equals(args[0], StartupArgument, StringComparison.Ordinal) ||
            !IsValidAckPath(args[1]) || !IsValidToken(args[2]))
            return;
        var ackFile = Path.GetFullPath(args[1]);
        var tempFile = ackFile + ".tmp";
        try
        {
            var ack = new UpdateStartupAck(
                Environment.ProcessId, args[2],
                FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileVersion ?? "");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(ack);
            if (bytes.Length > MaxAckBytes) return;
            File.WriteAllBytes(tempFile, bytes);
            File.Move(tempFile, ackFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A missing acknowledgement keeps the backup; WPF is still usable.
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static bool IsExpectedAck(string json, int processId, string token, Version expectedVersion)
    {
        if (!IsValidToken(token) || json.Length > MaxAckBytes) return false;
        try
        {
            var ack = JsonSerializer.Deserialize<UpdateStartupAck>(json);
            return ack is not null && ack.Pid == processId &&
                string.Equals(ack.Token, token, StringComparison.Ordinal) &&
                Version.TryParse(ack.FileVersion, out var version) &&
                version >= expectedVersion;
        }
        catch (JsonException) { return false; }
    }

    public static async Task<bool> WaitForReadyAsync(
        string ackPath, Process process, string token, Version expected,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!IsValidAckPath(ackPath)) return false;
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited) return false;
            try
            {
                if (File.Exists(ackPath) &&
                    new FileInfo(ackPath).Length <= MaxAckBytes &&
                    IsExpectedAck(await File.ReadAllTextAsync(ackPath, cancellationToken)
                        .ConfigureAwait(false), process.Id, token, expected))
                    return true;
            }
            catch (IOException) { }
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        return false;
    }
}

public sealed record UpdateStartupAck(int Pid, string Token, string FileVersion);
