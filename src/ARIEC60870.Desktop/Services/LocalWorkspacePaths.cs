// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;

namespace ARIEC60870.Desktop.Services;

/// <summary>
/// Centralized local file-system layout for ARIEC60870 Desktop runtime data.
/// Keeping these paths outside MainWindow prevents setup persistence, capture rules,
/// and future workspace services from hard-coding app-data folders in UI handlers.
/// </summary>
public static class LocalWorkspacePaths
{
    public static string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ARIEC60870");

    public static string SetupPreferencesFile => Path.Combine(AppDataRoot, "setup-preferences.json");

    public static string TriggerCaptureFolder => Path.Combine(AppDataRoot, "trigger-captures");
}
