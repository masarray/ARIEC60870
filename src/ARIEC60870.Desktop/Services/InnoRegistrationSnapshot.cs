// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using Microsoft.Win32;

namespace ARIEC60870.Desktop.Services;

/// <summary>
/// Captures the exact HKCU Inno uninstall record before an upgrade. A file-only
/// rollback would otherwise leave Windows Apps & Features claiming the failed
/// new version is still installed. No system-wide registration is touched.
/// </summary>
public sealed class InnoRegistrationSnapshot
{
    private const string UninstallRoot =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private readonly string _subkeyName;
    private readonly string _installedDirectory;
    private readonly Dictionary<string, (RegistryValueKind Kind, object Value)> _values;

    private InnoRegistrationSnapshot(string subkeyName, string installedDirectory,
        Dictionary<string, (RegistryValueKind Kind, object Value)> values)
    {
        _subkeyName = subkeyName;
        _installedDirectory = installedDirectory;
        _values = values;
    }

    public string PreviousDisplayVersion
        => (string)_values["DisplayVersion"].Value;

    public static InnoRegistrationSnapshot Capture(string installedExecutable)
    {
        var expectedDirectory = Path.GetFullPath(
            Path.GetDirectoryName(installedExecutable)!);
        using var root = Registry.CurrentUser.OpenSubKey(UninstallRoot)
            ?? throw new IOException("Missing Windows per-user uninstall registration.");
        InnoRegistrationSnapshot? matched = null;
        foreach (var name in root.GetSubKeyNames())
        {
            using var candidate = root.OpenSubKey(name);
            if (!IsSameTarget(candidate, expectedDirectory))
                continue;
            if (matched is not null)
                throw new IOException("Multiple ArSee uninstall records match this installation.");
            var values = new Dictionary<string,
                (RegistryValueKind Kind, object Value)>(StringComparer.OrdinalIgnoreCase);
            foreach (var valueName in candidate!.GetValueNames())
            {
                var kind = candidate.GetValueKind(valueName);
                if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString
                    or RegistryValueKind.DWord or RegistryValueKind.QWord
                    or RegistryValueKind.MultiString or RegistryValueKind.Binary))
                    throw new IOException("Unsupported uninstall registry value type.");
                var value = candidate.GetValue(valueName, null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames)
                    ?? throw new IOException("Uninstall registry value changed during backup.");
                values.Add(valueName, value switch
                {
                    byte[] bytes => (kind, (object)bytes.ToArray()),
                    string[] strings => (kind, (object)strings.ToArray()),
                    _ => (kind, value)
                });
            }
            if (!values.TryGetValue("DisplayVersion", out var version) ||
                version.Value is not string { Length: > 0 })
                throw new IOException("Previous installed version is not registered.");
            matched = new InnoRegistrationSnapshot(name, expectedDirectory, values);
        }
        return matched ?? throw new IOException("No matching ArSee per-user Inno registration.");
    }

    public bool IsRegisteredVersion(string installedExecutable, Version version)
    {
        if (!TargetMatches(installedExecutable)) return false;
        using var key = Registry.CurrentUser.OpenSubKey(UninstallRoot + "\\" + _subkeyName);
        if (!IsSameTarget(key, _installedDirectory)) return false;
        return Version.TryParse(key?.GetValue("DisplayVersion") as string, out var found) &&
            found >= version;
    }

    public void Restore(string installedExecutable)
    {
        if (!TargetMatches(installedExecutable))
            throw new IOException("Refusing to restore uninstall registration to a different installation.");
        using var key = Registry.CurrentUser.CreateSubKey(
            UninstallRoot + "\\" + _subkeyName, writable: true)
            ?? throw new IOException("Windows would not reopen the prior uninstall registration.");

        // Restore the precise original record, including uninstall command,
        // DisplayVersion, install location, and Inno-specific metadata.
        foreach (var name in key.GetValueNames())
            if (!_values.ContainsKey(name))
                key.DeleteValue(name, throwOnMissingValue: false);
        foreach (var pair in _values)
            key.SetValue(pair.Key, pair.Value.Value, pair.Value.Kind);
        key.Flush();

        if (!IsSameTarget(key, _installedDirectory) ||
            !string.Equals(key.GetValue("DisplayVersion") as string,
                PreviousDisplayVersion, StringComparison.Ordinal))
            throw new IOException("Restored Inno uninstall registration verification failed.");
    }

    private bool TargetMatches(string installedExecutable) =>
        string.Equals(Path.GetFullPath(Path.GetDirectoryName(installedExecutable)!),
            _installedDirectory, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameTarget(RegistryKey? key, string folder)
    {
        if (!string.Equals(key?.GetValue("DisplayName") as string,
                "ArSee", StringComparison.OrdinalIgnoreCase))
            return false;
        var location = key?.GetValue("InstallLocation") as string;
        if (string.IsNullOrWhiteSpace(location)) return false;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)),
                folder, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
            or PathTooLongException) { return false; }
    }
}
