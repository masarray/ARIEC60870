// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class CaptureArchiveIndexTests
{
    [Fact]
    public void FiftyThousandVerifiedFramesArePagedWithoutReopeningZip()
    {
        var path = CreateArchive(50_000);
        try
        {
            using var index = CaptureArchiveReader.OpenIndexed(path);
            Assert.True(index.IntegrityVerified);
            Assert.Equal(50_000, index.TotalRows);
            var all = new CaptureArchiveQuery();
            var first = index.Search(all, pageSize: 150);
            Assert.Equal(50_000, first.MatchingRows);
            Assert.Equal(150, first.Rows.Count);
            Assert.Equal(1, first.Rows[0].Sequence);

            var end = index.Search(all, skipMatching: 49_850, pageSize: 150);
            Assert.Equal(49_851, end.Rows[0].Sequence);
            Assert.Equal(50_000, end.Rows[^1].Sequence);
            Assert.Empty(index.Search(all, skipMatching: 50_000).Rows);

            // Deliberately remove original container: already verified local
            // session pages still exist but must not be mislabeled as the
            // CURRENT source. Reopening would require original ZIP again.
            File.Delete(path);
            Assert.False(index.IsCurrentSource(path));
            var middle = index.Search(all, skipMatching: 12_000, pageSize: 2);
            Assert.Equal(new long[] { 12_001, 12_002 }, middle.Rows.Select(x => x.Sequence));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void MultiSourceArchivePreservesOriginalAppendOrderEvenWhenSequencesRepeatOrReset()
    {
        // JSONL is a physical source-ordered journal. Sequence can reset on
        // a second link or imported session; sorting destroys source chronology.
        CaptureFrameSnapshot[] originals =
        [
            new() { Sequence = 41, PhysicalSource = "Link A", PhysicalStreamId = 0, RawStreamOffset = 0, Direction = "RX", ProtocolMode = "101", RawHex = "E5" },
            new() { Sequence = 1, PhysicalSource = "Link B", PhysicalStreamId = 1, RawStreamOffset = 0, Direction = "RX", ProtocolMode = "101", RawHex = "E5" },
            new() { Sequence = 42, PhysicalSource = "Link A", PhysicalStreamId = 0, RawStreamOffset = 1, Direction = "RX", ProtocolMode = "101", RawHex = "E5" },
            new() { Sequence = 1, PhysicalSource = "Link B", PhysicalStreamId = 1, RawStreamOffset = 1, Direction = "RX", ProtocolMode = "101", RawHex = "E5" },
            new() { Sequence = 2, PhysicalSource = "Link A", PhysicalStreamId = 0, RawStreamOffset = 2, Direction = "RX", ProtocolMode = "101", RawHex = "E5" }
        ];
        var path = Path.Join(Path.GetTempPath(),
            "ariec-wire-order-" + Guid.NewGuid().ToString("N") + ".ariec");
        try
        {
            var frames = string.Concat(originals.Select(x => JsonSerializer.Serialize(x) + "\n"));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(frames)));
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("frames.jsonl").Open(),
                    new UTF8Encoding(false)))
                    writer.Write(frames);
                using var manifest = new StreamWriter(zip.CreateEntry("manifest.json").Open(),
                    new UTF8Encoding(false));
                manifest.Write(JsonSerializer.Serialize(new { FramesSha256 = hash, RowCount = originals.Length }));
            }

            var preview = CaptureArchiveReader.Read(path, previewLimit: 3);
            Assert.True(preview.IntegrityVerified);
            Assert.Equal(new long[] { 42, 1, 2 }, preview.Rows.Select(x => x.Sequence));
            Assert.Equal(new int?[] { 0, 1, 0 }, preview.Rows.Select(x => x.PhysicalStreamId));
            var plainSearch = CaptureArchiveReader.Search(path,
                CaptureArchiveQuery.Parse("source:A"), pageSize: 5);
            Assert.Equal(new long[] { 41, 42, 2 }, plainSearch.Rows.Select(x => x.Sequence));
            Assert.Equal(new long?[] { 0, 1, 2 }, plainSearch.Rows.Select(x => x.RawStreamOffset));

            using var index = CaptureArchiveReader.OpenIndexed(path);
            var first = index.Search(new CaptureArchiveQuery(), pageSize: 2);
            Assert.Equal(new long[] { 41, 1 }, first.Rows.Select(x => x.Sequence));
            Assert.Equal(new int?[] { 0, 1 }, first.Rows.Select(x => x.PhysicalStreamId));
            var middle = index.Search(new CaptureArchiveQuery(), skipMatching: 2, pageSize: 3);
            Assert.Equal(new long[] { 42, 1, 2 }, middle.Rows.Select(x => x.Sequence));
            var onlyB = index.Search(CaptureArchiveQuery.Parse("source:B"), pageSize: 5);
            Assert.Equal(new long[] { 1, 1 }, onlyB.Rows.Select(x => x.Sequence));
            Assert.Equal(new long?[] { 0, 1 }, onlyB.Rows.Select(x => x.RawStreamOffset));
            var onlyA = index.Search(CaptureArchiveQuery.Parse("source:A"), pageSize: 5);
            Assert.Equal(new long[] { 41, 42, 2 }, onlyA.Rows.Select(x => x.Sequence));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void QueryChangesCommitAtomicallyAndOffsetsRespectPhysicalSource()
    {
        var path = CreateArchive(1_000);
        try
        {
            using var index = CaptureArchiveReader.OpenIndexed(path);
            var a = index.Search(CaptureArchiveQuery.Parse("source:A direction:RX"), pageSize: 3);
            Assert.Equal(500, a.MatchingRows);
            Assert.Equal(new long[] { 2, 4, 6 }, a.Rows.Select(x => x.Sequence));
            var b = index.Search(CaptureArchiveQuery.Parse("source:B"), pageSize: 3);
            Assert.Equal(500, b.MatchingRows);
            Assert.Equal(new long[] { 1, 3, 5 }, b.Rows.Select(x => x.Sequence));
            var nextA = index.Search(CaptureArchiveQuery.Parse("source:A direction:RX"),
                skipMatching: 450, pageSize: 2);
            Assert.Equal(new long[] { 902, 904 }, nextA.Rows.Select(x => x.Sequence));
            Assert.True(index.IsCurrentSource(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CancellationNeverPublishesIncompleteOrUnverifiedIndex()
    {
        var path = CreateArchive(1_000);
        try
        {
            using var index = CaptureArchiveReader.OpenIndexed(path);
            var original = new CaptureArchiveQuery();
            Assert.Equal(1_000, index.Search(original).MatchingRows);
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                index.Search(CaptureArchiveQuery.Parse("source:A"), cancellationToken: cancel.Token));
            // Previously committed match offsets survive a canceled query.
            Assert.Equal(1_000, index.Search(original, skipMatching: 998).MatchingRows);
            Assert.Equal(new long[] { 999, 1000 },
                index.Search(original, skipMatching: 998).Rows.Select(x => x.Sequence));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BrokenHashNeverProducesAnIndexOrSearchPage()
    {
        var path = CreateArchive(15, corruptManifestHash: true);
        try
        {
            Assert.Throws<InvalidDataException>(() => CaptureArchiveReader.OpenIndexed(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LegacyCaptureKeepsExplicitUnverifiedStatusAcrossPages()
    {
        var path = CreateArchive(500, includeHash: false);
        try
        {
            using var index = CaptureArchiveReader.OpenIndexed(path);
            var page = index.Search(new CaptureArchiveQuery(), skipMatching: 400, pageSize: 10);
            Assert.False(page.IntegrityVerified);
            Assert.Contains("LEGACY", page.TrustDescription);
            Assert.Equal(401, page.Rows[0].Sequence);
        }
        finally { File.Delete(path); }
    }

    private static string CreateArchive(int count, bool corruptManifestHash = false,
        bool includeHash = true)
    {
        var path = Path.Join(Path.GetTempPath(),
            "ariec-index-test-" + Guid.NewGuid().ToString("N") + ".ariec");
        var json = new StringBuilder(count * 170);
        for (var i = 1; i <= count; i++)
        {
            json.Append(JsonSerializer.Serialize(new CaptureFrameSnapshot
            {
                Sequence = i, ProtocolMode = "101",
                PhysicalSource = i % 2 == 0 ? "Link A" : "Link B",
                Direction = "RX", RawHex = "E5",
                Time = "2026-10-31T00:00:00Z"
            })).Append('\n');
        }
        var frames = json.ToString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(frames)));
        if (corruptManifestHash) hash = new string('0', 64);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("frames.jsonl").Open(),
                new UTF8Encoding(false)))
                writer.Write(frames);
            if (includeHash)
            {
                using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open(),
                    new UTF8Encoding(false));
                writer.Write(JsonSerializer.Serialize(new { FramesSha256 = hash, RowCount = count }));
            }
        }
        return path;
    }
}
