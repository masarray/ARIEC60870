// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;

namespace ARIEC60870.Desktop;

/// <summary>
/// Controls the editor-to-live Values boundary. Reading or writing a JSON
/// document is not authorization to replace the running station Signal Map.
/// Only an explicit Save &amp; Apply can grant that authority.
/// This class contains no WPF event coupling or persisted application state.
/// </summary>
public sealed class SignalMapActivationPolicy
{
    public bool HasUnappliedProfile { get; private set; }

    public void PreviewLoaded() => HasUnappliedProfile = true;

    public void SavedWithoutApply() => HasUnappliedProfile = true;

    public void Applied() => HasUnappliedProfile = false;

    public bool MustConfirmDiscard(bool hasUnsavedEdits, bool applied)
        => !applied && (hasUnsavedEdits || HasUnappliedProfile);

    /// <summary>
    /// A clean, existing file may become active without being rewritten
    /// (important for read-only bundled references). Pending changes still
    /// require a new file, and explicit Save As is never skipped.
    /// </summary>
    public static bool CanReuseExistingFileWithoutWrite(
        bool dirty, bool forceSaveAs,
        string? protectedSourcePath, string? targetPath)
        => !dirty && !forceSaveAs &&
            string.IsNullOrWhiteSpace(protectedSourcePath) &&
            !string.IsNullOrWhiteSpace(targetPath) &&
            File.Exists(targetPath);

    public static bool CanApplyExistingFileWithoutWrite(
        bool explicitApply, bool dirty, bool forceSaveAs,
        string? protectedSourcePath, string? targetPath)
        => explicitApply && CanReuseExistingFileWithoutWrite(
            dirty, forceSaveAs, protectedSourcePath, targetPath);

    public static bool IsBundledReferencePath(string? targetPath)
        => !string.IsNullOrWhiteSpace(targetPath) &&
            string.Equals(Path.GetFileName(targetPath),
                "utility_fat_iec10x_default_profile.json",
                StringComparison.OrdinalIgnoreCase);
}
