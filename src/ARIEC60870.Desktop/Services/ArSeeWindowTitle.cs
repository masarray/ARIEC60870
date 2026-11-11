// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;

namespace ARIEC60870.Desktop.Services;

/// <summary>Formats the main window caption from the exact executable's version metadata.</summary>
public static class ArSeeWindowTitle
{
    public const string ProductName = "ArSee • Open Source IEC 60870-101/104/103 Protocol Tester & Analyzer";

    public static string ForAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var information = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return Format(information, assembly.GetName().Version);
    }

    public static string Format(string? informationalVersion, Version? assemblyVersion)
    {
        // NuGet/SemVer build metadata (+release, +commit) is not part of the
        // user-visible version. Preserve prerelease qualifiers (e.g., -rc.4).
        var version = informationalVersion?.Split('+', 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            version = assemblyVersion is { Build: >= 0 }
                ? assemblyVersion.ToString(3)
                : assemblyVersion?.ToString() ?? "0.0.0";
        }

        return $"{ProductName} • version {version}";
    }
}
