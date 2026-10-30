// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class CaptureArchiveQueryTests
{
    [Fact]
    public void FullVerifiedArchiveSearchFindsFramesOutsideLatest600AndPagesDeterministically()
    {
        var file = Path.Combine(Path.GetTempPath(), "ariec-query-" + Guid.NewGuid().ToString("N") + ".ariec");
        try
        {
            var frames = Enumerable.Range(1, 800).Select(i => new CaptureFrameSnapshot
            {
                Sequence = i, ProtocolMode = "101", Direction = "RX",
                PhysicalSource = "Link A", CommonAddress = "105",
                TypeId = "46", CotCode = "7", Ioa = ((i - 1) % 200).ToString(),
                RawHex = "680D0D680869002E0147006900BE0B01819B16"
            }).ToArray();
            var jsonl = string.Concat(frames.Select(x => JsonSerializer.Serialize(x) + "\n"));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(jsonl)));
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                static void Entry(ZipArchive z, string name, string text)
                {
                    using var writer = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false));
                    writer.Write(text);
                }
                Entry(zip, "frames.jsonl", jsonl);
                Entry(zip, "manifest.json", JsonSerializer.Serialize(new { RowCount = 800, FramesSha256 = hash }));
            }
            var latest = CaptureArchiveReader.Read(file);
            Assert.Equal(600, latest.Rows.Count);
            Assert.Equal(201, latest.Rows[0].Sequence);
            var query = CaptureArchiveQuery.Parse("ioa:42 ca:105 ti:46 cot:7 source:A direction:RX");
            var first = CaptureArchiveReader.Search(file, query, pageSize: 2);
            Assert.True(first.IntegrityVerified);
            Assert.Equal(800, first.TotalRows);
            Assert.Equal(4, first.MatchingRows);
            Assert.Equal(new long[] { 43, 243 }, first.Rows.Select(x => x.Sequence));
            var second = CaptureArchiveReader.Search(file, query, skipMatching: 2, pageSize: 2);
            Assert.Equal(new long[] { 443, 643 }, second.Rows.Select(x => x.Sequence));
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void QueryRequiresRealTypedProvenanceForNegativeAndTestFlags()
    {
        var fixture = new CaptureFrameSnapshot
        {
            ProtocolMode = "101", Direction = "RX", PhysicalSource = "Link A",
            LinkWidth = 2, CotWidth = 2, CaWidth = 2, IoaWidth = 3,
            TypeId = "46", CotCode = "7", CommonAddress = "105", Ioa = "68542",
            RawHex = "680D0D680869002E0147006900BE0B01819B16"
        };
        Assert.True(CaptureArchiveQuery.Parse("ioa:68542 neg:1 test:0").Matches(fixture));
        Assert.False(CaptureArchiveQuery.Parse("ioa:68542 neg:0").Matches(fixture));
        Assert.False(CaptureArchiveQuery.Parse("ioa:68542 test:1").Matches(fixture));
        fixture.LinkWidth = null; // Old capture cannot prove flags from display text
        Assert.False(CaptureArchiveQuery.Parse("neg:1").Matches(fixture));
    }

    [Theory]
    [InlineData("ioa:999999999")]
    [InlineData("cot:64")]
    [InlineData("unknown:value")]
    [InlineData("neg:maybe")]
    [InlineData("ioa:1 ioa:2")]
    public void InvalidFiltersFailBeforeAnyDiskRead(string text)
        => Assert.Throws<FormatException>(() => CaptureArchiveQuery.Parse(text));
}
