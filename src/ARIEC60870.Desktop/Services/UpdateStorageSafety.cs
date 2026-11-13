// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;

namespace ARIEC60870.Desktop.Services;

/// <summary>
/// Preflight updater storage while the installed application is still intact.
/// Never skip links/junctions when building a rollback backup: skipping files
/// would falsely make an incomplete copy look recoverable.
/// </summary>
public static class UpdateStorageSafety
{
    public const long FreeSpaceReserve = 128L * 1024 * 1024;

    public static long RequiredFreeBytes(long dataBytes, long reserveBytes = FreeSpaceReserve)
    {
        if (dataBytes < 0 || reserveBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(dataBytes));
        return checked(dataBytes + reserveBytes);
    }

    public static void RejectLinkedDirectory(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked update staging or backup directories are not trusted.");
    }

    public static bool SharesVolume(string firstPath, string secondPath)
        => string.Equals(
            Path.GetPathRoot(Path.GetFullPath(firstPath)),
            Path.GetPathRoot(Path.GetFullPath(secondPath)),
            StringComparison.OrdinalIgnoreCase);

    public static void EnsureAvailableSpace(string destination, long additionalBytes)
    {
        var fullPath = Path.GetFullPath(destination);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
            throw new IOException("Update storage volume cannot be identified.");
        var drive = new DriveInfo(root);
        if (!drive.IsReady || drive.AvailableFreeSpace < RequiredFreeBytes(additionalBytes))
            throw new IOException(
                "Not enough available disk space to safely prepare the ArSee update. " +
                "Free some space and retry; the installed application was not changed.");
    }

    /// <summary>
    /// Enumerates files without crossing reparse-point directory boundaries.
    /// An installer tree containing links is rejected instead of partially
    /// backed up or accidentally traversed outside the application folder.
    /// </summary>
    public static IReadOnlyList<string> EnumerateSafeFiles(string installFolder)
    {
        var root = Path.GetFullPath(installFolder);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked installation directories are not supported for automatic rollback.");
        var pending = new Stack<string>();
        var files = new List<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         directory, "*", SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A linked file or directory prevents a trustworthy update backup.");
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(entry);
                else
                    files.Add(entry);
            }
        }
        return files;
    }

    public static long TotalBytes(IReadOnlyList<string> paths)
    {
        long sum = 0;
        foreach (var path in paths)
            sum = checked(sum + new FileInfo(path).Length);
        return sum;
    }
}
