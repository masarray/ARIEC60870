// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class EventHistoryJournalTests
{
    private static RelayEventRow Row(long number)
    {
        const string raw = "10 08 69 71 16";
        var bytes = Convert.FromHexString(raw.Replace(" ", ""));
        var wire = Ft12Parser.ForIec101(1).Decode(bytes);
        var original = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            SequenceNumber = number + 200,
            PhysicalSource = "Link A",
            PhysicalStreamId = 1,
            RawStreamOffset = number * 9,
            Frame = wire,
            LinkAddress = 105,
            RawHex = raw,
            InformationObjectAddress = (int)(number % 19) + 1,
            TypeId = 30
        };
        return new RelayEventRow(new Iec103RelayEventLogEntry
        {
            EvidenceSequenceNumber = original.SequenceNumber,
            PhysicalSource = original.PhysicalSource,
            EvidenceReference = original.EvidenceReference,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            RawHex = raw,
            SignalName = "IOA " + original.InformationObjectAddress,
            NewValue = number % 2 == 0 ? "ON" : "OFF",
            TypeId = 30,
            CommonAddress = 105,
            InformationObjectAddress = original.InformationObjectAddress,
            CauseOfTransmission = "Spontaneous",
            ArrivalTimeUtc = DateTime.UtcNow
        }, original)
        { EventNumber = number };
    }

    [Theory]
    [InlineData(60)]
    [InlineData(500)]
    [InlineData(2150)]
    public void IndexedPagesAndOriginalPhysicalEvidenceSurviveLive420Rollover(int count)
    {
        using var journal = new EventHistoryJournal();
        for (var i = 1; i <= count; i++) journal.Append(Row(i));
        Assert.Equal(count, journal.Count);
        Assert.Equal((count + 99) / 100, EventHistoryPagination.PageCount(count));
        var first = journal.ReadAfter(0, 100);
        Assert.Equal(Math.Min(100, count), first.Count);
        Assert.Equal(1, first[0].EventNumber);
        Assert.Equal("IOA 2", first[0].Source.SignalName);
        if (count > 420)
        {
            var middle = journal.ReadAfter(200, 100);
            Assert.Equal(201, middle[0].EventNumber);
            Assert.Equal(300, middle[^1].EventNumber);
        }
        var last = journal.ReadAfter(Math.Max(0, count - 100), 100);
        Assert.Equal(count, last[^1].EventNumber);
        var original = last[^1].ToRow().OriginalFrameEvidence;
        Assert.NotNull(original);
        Assert.Equal("10 08 69 71 16", original.RawHex);
        Assert.Equal(105, original.Frame?.LinkAddress);
        Assert.Equal((long)count * 9, original.RawStreamOffset);
        Assert.Equal(last[^1].Source.EvidenceReference, original.EvidenceReference);
        Assert.Equal(count, last[^1].ToRow().EventNumber);
    }

    [Fact]
    public void ReadersAreBoundedAndIndexUsesOriginalImmutableNumbers()
    {
        using var journal = new EventHistoryJournal();
        for (var i = 1; i <= 201; i++) journal.Append(Row(i));
        Assert.Equal(200, journal.ReadAfter(0, 200).Count);
        Assert.Single(journal.ReadAfter(200, 100));
        Assert.Empty(journal.ReadAfter(201, 100));
        Assert.Equal(200, EventHistoryPagination.StartForEvent(250));
        Assert.Equal(100, EventHistoryPagination.StartForEvent(101));
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.ReadAfter(0, 201));
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.ReadAfter(-1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventHistoryPagination.StartForEvent(0));
        Assert.Throws<InvalidDataException>(() => journal.Append(Row(203)));
        Assert.Equal(201, journal.Count);
    }

    [Fact]
    public void BoundedIndexFailsExplicitlyInsteadOfSilentlyReplacingOlderRows()
    {
        using var journal = new EventHistoryJournal(maxBytes: 10000);
        var attempted = 0;
        Assert.Throws<IOException>(() =>
        {
            for (var i = 1; i <= 1000; i++)
            {
                attempted++;
                journal.Append(Row(i));
            }
        });
        Assert.True(attempted > 1);
        Assert.Equal(attempted - 1, journal.Count);
        Assert.Equal(1, journal.ReadAfter(0, 1).Single().EventNumber);
    }
}
