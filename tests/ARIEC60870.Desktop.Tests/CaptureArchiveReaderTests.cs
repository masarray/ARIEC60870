// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class CaptureArchiveReaderTests
{
    [Fact]
    public void StreamsFullArchiveAndKeepsOnlyLastRowsAfterVerifiedHash()
    {
        var lines = Enumerable.Range(1, 1800)
            .Select(n => JsonSerializer.Serialize(new CaptureFrameSnapshot
            {
                Sequence = n, RawHex = "10 09 01 0A 16",
                ProtocolMode = "101", ProtocolTraceTitle = "frame"
            }) + "\n");
        var frames = string.Concat(lines);
        var path = CreateCapture(frames, Sha(frames), 1800);
        try
        {
            var result = CaptureArchiveReader.Read(path, previewLimit: 25);
            Assert.Equal(1800, result.TotalRows);
            Assert.Equal(25, result.Rows.Count);
            Assert.Equal(1776, result.Rows[0].Sequence);
            Assert.Equal(1800, result.Rows[^1].Sequence);
            Assert.True(result.IntegrityVerified);
            Assert.Equal("SHA-256 VERIFIED", result.TrustDescription);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ProfileProvenanceSurvivesVerifiedCaptureReopen()
    {
        var frames = JsonSerializer.Serialize(new CaptureFrameSnapshot { Sequence = 1, RawHex = "E5" }) + "\n";
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ariec");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Add(archive, "frames.jsonl", frames);
                Add(archive, "manifest.json", JsonSerializer.Serialize(new
                {
                    FramesSha256 = Sha(frames), RowCount = 1, ProtocolMode = "Iec101",
                    InteroperabilityProfileId = "project-bay-a",
                    InteroperabilityProfileEdition = "revision-2",
                    InteroperabilityProfileFingerprint = "project-bay-a@revision-2"
                }));
            }
            var read = CaptureArchiveReader.Read(path);
            Assert.True(read.IntegrityVerified);
            Assert.Equal("Iec101", read.CapturedProtocolMode);
            Assert.Equal("project-bay-a", read.CapturedProfileId);
            Assert.Equal("revision-2", read.CapturedProfileEdition);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ChangedArchiveBytesCannotBeRenderedAsVerifiedRows()
    {
        var frames = JsonSerializer.Serialize(new CaptureFrameSnapshot
        {
            Sequence = 1, RawHex = "10 00 01 01 16"
        }) + "\n";
        var path = CreateCapture(frames.Replace("10 00", "10 FF", StringComparison.Ordinal),
            Sha(frames), 1);
        try
        {
            var err = Assert.Throws<InvalidDataException>(() => CaptureArchiveReader.Read(path));
            Assert.Contains("SHA-256 mismatch", err.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LegacyArchiveWithoutHashIsVisibleButExplicitlyUnverified()
    {
        var frames = JsonSerializer.Serialize(new CaptureFrameSnapshot
        {
            Sequence = 7, RawHex = "10 09 01 0A 16"
        }) + "\n";
        var path = CreateCapture(frames, hash: null, rowCount: null);
        try
        {
            var result = CaptureArchiveReader.Read(path);
            Assert.Single(result.Rows);
            Assert.False(result.IntegrityVerified);
            Assert.Contains("LEGACY", result.TrustDescription);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OversizedJsonlFrameIsRejectedBeforeAllocatingLargeRows()
    {
        var frames = Convert.ToBase64String(RandomNumberGenerator.GetBytes(450 * 1024)) + "\n";
        var path = CreateCapture(frames, hash: null, rowCount: null);
        try
        {
            var error = Assert.Throws<InvalidDataException>(() => CaptureArchiveReader.Read(path));
            Assert.Contains("oversized JSONL", error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DuplicateZipEntriesAreRejectedBeforeDecoding()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ariec");
        try
        {
            using (var file = File.Create(path))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                Add(archive, "frames.jsonl", "{}\n");
                Add(archive, "frames.jsonl", "{}\n");
            }
            Assert.Throws<InvalidDataException>(() => CaptureArchiveReader.Read(path));
        }
        finally { File.Delete(path); }
    }

    private static string CreateCapture(string frames, string? hash, int? rowCount)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ariec");
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        Add(archive, "frames.jsonl", frames);
        if (hash is not null || rowCount.HasValue)
            Add(archive, "manifest.json", JsonSerializer.Serialize(new
            {
                FramesSha256 = hash ?? string.Empty,
                RowCount = rowCount
            }));
        return path;
    }

    private static void Add(ZipArchive archive, string name, string text)
    {
        using var stream = archive.CreateEntry(name).Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string Sha(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
