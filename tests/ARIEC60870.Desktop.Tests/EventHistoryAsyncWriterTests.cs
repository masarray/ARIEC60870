// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.IO;
using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;
using Xunit.Abstractions;

namespace ARIEC60870.Desktop.Tests;

public sealed class EventHistoryAsyncWriterTests(ITestOutputHelper output)
{
    private static RelayEventRow Row(long number)
    {
        const string raw = "10 08 69 71 16";
        var source = new Iec103RelayEventLogEntry
        {
            EvidenceSequenceNumber = number + 1000,
            PhysicalSource = "SINGLE",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            SignalName = "IOA " + (number % 50),
            NewValue = number % 2 == 0 ? "ON" : "OFF",
            RawHex = raw,
            TypeId = 30,
            CommonAddress = 105,
            InformationObjectAddress = (int)(number % 50),
            ArrivalTimeUtc = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc)
        };
        var original = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = source.EvidenceSequenceNumber,
            TimestampUtc = source.ArrivalTimeUtc,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            PhysicalSource = source.PhysicalSource,
            RawHex = raw,
            LinkAddress = 105,
            PhysicalStreamId = 1,
            RawStreamOffset = number * 9,
            TypeId = 30,
            CommonAddressNumber = 105,
            InformationObjectAddress = (int)(number % 50)
        };
        return new RelayEventRow(source, original, number);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(10000)]
    [InlineData(50000)]
    public async Task IndexedBurstRemainsOrderedAndBoundedWithOffDispatcherWriter(int count)
    {
        using var journal = new EventHistoryJournal();
        using var writer = new EventHistoryAsyncWriter(journal, capacity: 4096, batchSize: 64);
        var timer = Stopwatch.StartNew();
        for (var n = 1; n <= count; n++)
        {
            Assert.True(writer.TryEnqueue(Row(n)), writer.FailureReason);
            // Sustain a high rate without relying on scheduler races or
            // forcing an overflow unrelated to persistence correctness.
            if (n % 1024 == 0) await writer.DrainAsync();
        }
        await writer.DrainAsync();
        timer.Stop();

        Assert.Null(writer.FailureReason);
        Assert.Equal(count, writer.AcceptedCount);
        Assert.Equal(count, writer.IndexedCount);
        Assert.Equal(0, writer.PendingCount);
        Assert.Equal(count, journal.Count);
        Assert.True(timer.Elapsed < TimeSpan.FromMinutes(2),
            $"50k-event regression exceeded two minutes: {timer.Elapsed}");
        var first = journal.ReadAfter(0, 100);
        var middleStart = (count / 2 / 100) * 100;
        var middle = journal.ReadAfter(middleStart, 100);
        var last = journal.ReadAfter(count - 100, 100);
        Assert.Equal(1, first[0].EventNumber);
        Assert.Equal(middleStart + 1, middle[0].EventNumber);
        Assert.Equal(count, last[^1].EventNumber);
        Assert.True(last[^1].Original!.RawHexSharedWithSource);
        Assert.Equal(string.Empty, last[^1].Original!.RawHex);
        Assert.Equal("10 08 69 71 16", last[^1].ToRow().OriginalFrameEvidence!.RawHex);
        Assert.Equal((long)count * 9, last[^1].ToRow().OriginalFrameEvidence!.RawStreamOffset);
        output.WriteLine($"Event History: {count:N0} indexed; {timer.Elapsed.TotalSeconds:F2}s; " +
            $"{count / Math.Max(timer.Elapsed.TotalSeconds, .001):F0} events/s; " +
            $"{journal.BytesWritten:N0} bytes; 0 pending/unindexed.");
    }

    [Fact]
    public async Task DiskQuotaFailureIsVisibleAndPreservesVerifiedPrefix()
    {
        using var journal = new EventHistoryJournal(maxBytes: 4000);
        using var writer = new EventHistoryAsyncWriter(journal, capacity: 64, batchSize: 8);
        for (var n = 1; n <= 20; n++)
            Assert.True(writer.TryEnqueue(Row(n)));
        await Assert.ThrowsAsync<IOException>(() => writer.DrainAsync());
        Assert.NotNull(writer.FailureReason);
        Assert.True(writer.IndexedCount < writer.AcceptedCount);
        // Wait for the reader to mark its terminal state rather than infer
        // that a failed batch was wholly written.
        for (var i = 0; i < 200 && !writer.IsCompleted; i++)
            await Task.Delay(5);
        Assert.True(writer.IsCompleted);
        Assert.Equal(0, writer.PendingCount);
        Assert.Equal(writer.IndexedCount, journal.ReadAfter(0,
            (int)Math.Min(200, writer.IndexedCount)).Count);
        Assert.False(writer.TryEnqueue(Row(21)));
    }

    [Fact]
    public async Task IndexedPagesAreReadableWhileBackgroundWriterIsAppending()
    {
        using var journal = new EventHistoryJournal();
        using var writer = new EventHistoryAsyncWriter(journal, capacity: 1024, batchSize: 32);
        for (var i = 1; i <= 700; i++)
        {
            Assert.True(writer.TryEnqueue(Row(i)));
            if (i % 100 == 0 && journal.Count > 0)
            {
                var page = journal.ReadAfter(0, (int)Math.Min(100, journal.Count));
                Assert.Equal(1, page[0].EventNumber);
                Assert.Equal(page.Count, page[^1].EventNumber);
            }
        }
        await writer.DrainAsync();
        Assert.Equal(700, journal.Count);
        Assert.Equal(700, journal.ReadAfter(600, 100)[^1].EventNumber);
    }
}
