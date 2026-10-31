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
