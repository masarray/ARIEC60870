// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
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
        }, original, number);
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
    public void ObservedMasterPollIsPersistedBesideOldRxEventWithoutReassigningItsSource()
    {
        using var journal = new EventHistoryJournal();
        var row = Row(1);
        var original = row.OriginalFrameEvidence!;
        const string requestHex = "10 5A 69 C3 16";
        var request = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 195,
            TimestampUtc = original.TimestampUtc.AddMilliseconds(-25),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.MasterToSlave,
            PhysicalSource = "Link A",
            LinkAddress = 105,
            RawHex = requestHex,
            Frame = Ft12Parser.ForIec101(1).Decode(
                Convert.FromHexString(requestHex.Replace(" ", "")))
        };
        Assert.True(EventFrameExchange.IsObservedPollReply(request, original));
        var withExchange = new RelayEventRow(row.SourceEntry, original,
            eventNumber: 1, observedRequestFrame: request);
        journal.Append(withExchange);
        var recovered = journal.ReadAfter(0, 1).Single().ToRow();
        Assert.Equal(row.EvidenceReference, recovered.OriginalFrameEvidence?.EvidenceReference);
        Assert.Equal(row.RawHex, recovered.OriginalFrameEvidence?.RawHex);
        Assert.Equal(requestHex, recovered.ObservedRequestFrame?.RawHex);
        Assert.Equal(FrameDirection.MasterToSlave, recovered.ObservedRequestFrame?.Direction);
        Assert.Equal(10, recovered.ObservedRequestFrame?.Frame?.LinkControl?.FunctionCode);
        Assert.Equal("FC10", recovered.RequestFcCode);
        Assert.Equal("Class 1 Data Polling", recovered.RequestFcTooltip);
        Assert.True(EventFrameExchange.IsObservedPollReply(
            recovered.ObservedRequestFrame!, recovered.OriginalFrameEvidence!));
    }

    [Theory]
    [InlineData((byte)0x5A, "FC10", "Class 1 Data Polling")]
    [InlineData((byte)0x5B, "FC11", "Class 2 Data Polling")]
    public void EventLogFcDisplaysOnlyVerifiedIEC101PollAndSurvivesIndexedHistory(
        byte control, string expectedCode, string expectedTooltip)
    {
        var row = Row(24);
        var rx = row.OriginalFrameEvidence!;
        var raw = new byte[] { 0x10, control, 0x69,
            (byte)((control + 0x69) & 0xFF), 0x16 };
        var requestFrame = Ft12Parser.ForIec101(1).Decode(raw);
        Assert.True(requestFrame.IsChecksumValid);
        var poll = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 24,
            TimestampUtc = rx.TimestampUtc.AddMilliseconds(-50),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.MasterToSlave,
            PhysicalSource = rx.PhysicalSource,
            LinkAddress = rx.LinkAddress,
            Frame = requestFrame,
            RawHex = requestFrame.Hex
        };
        Assert.True(EventFrameExchange.IsObservedPollReply(poll, rx));
        var verified = new RelayEventRow(row.SourceEntry, rx,
            eventNumber: 1, observedRequestFrame: poll);
        Assert.Equal(expectedCode, verified.RequestFcCode);
        Assert.Equal(expectedTooltip, verified.RequestFcTooltip);
        Assert.True(verified.RequestFcTooltip.Length <= 30);

        using var journal = new EventHistoryJournal();
        journal.Append(verified);
        var restored = journal.ReadAfter(0, 1).Single().ToRow();
        Assert.Equal(expectedCode, restored.RequestFcCode);
        Assert.Equal(expectedTooltip, restored.RequestFcTooltip);

        // No observed TX and unrelated physical sources must never inherit
        // an attractive but fabricated Class 1/2 delivery label.
        Assert.Equal("—", new RelayEventRow(row.SourceEntry, rx).RequestFcCode);
        Assert.Equal("No matched Class 1/2 poll",
            new RelayEventRow(row.SourceEntry, rx).RequestFcTooltip);
        var differentLink = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = poll.SequenceNumber,
            TimestampUtc = poll.TimestampUtc,
            ProtocolMode = poll.ProtocolMode,
            Direction = poll.Direction,
            PhysicalSource = "Different physical source",
            LinkAddress = poll.LinkAddress,
            Frame = requestFrame,
            RawHex = requestFrame.Hex
        };
        Assert.Equal("—", new RelayEventRow(row.SourceEntry, rx,
            observedRequestFrame: differentLink).RequestFcCode);
    }

    [Fact]
    public async Task EventCsvIncludesVerifiedFc10RequestFunctionBesideCOT()
    {
        var row = Row(42);
        var original = row.OriginalFrameEvidence!;
        var raw = new byte[] { 0x10, 0x5A, 0x69, 0xC3, 0x16 };
        var frame = Ft12Parser.ForIec101(1).Decode(raw);
        var request = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 1,
            TimestampUtc = original.TimestampUtc.AddMilliseconds(-25),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = original.PhysicalSource,
            Direction = FrameDirection.MasterToSlave,
            LinkAddress = 105,
            RawHex = frame.Hex,
            Frame = frame
        };
        using var journal = new EventHistoryJournal();
        journal.Append(new RelayEventRow(row.SourceEntry, original,
            eventNumber: 1, observedRequestFrame: request));
        var export = Path.Combine(Path.GetTempPath(),
            "ArSee-fc-export-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var count = await EventHistoryCsvExporter.ExportAsync(
                journal, new EventHistoryQuerySnapshot(1, null), export);
            Assert.Equal(1, count);
            var lines = await File.ReadAllLinesAsync(export);
            Assert.Equal(2, lines.Length);
            Assert.Contains("TI,COT,RequestFC,Quality", lines[0]);
            Assert.Contains("\"FC10\"", lines[1]);
        }
        finally
        {
            if (File.Exists(export)) File.Delete(export);
        }
    }

    [Fact]
    public void IndexedEventRestoresAllRxAsduObjectsAndQualityWithRecordedWidths()
    {
        // One IEC-101 FT1.2 FC8 response with a real M_SP_NA_1 payload.
        // COT=3, CA=105, IOA=7, SIQ=0x81 (ON, IV=1).
        byte[] payload = [1, 1, 3, 0, 105, 0, 7, 0, 0, 0x81];
        byte[] body = [0x08, 0x69, .. payload];
        var length = (byte)body.Length;
        byte[] raw = [0x68, length, length, 0x68, .. body,
            (byte)(body.Sum(o => o) & 0xFF), 0x16];
        var decoded = Iec10xArchivedFrameDecoder.Decode(
            Iec60870ProtocolMode.Iec101, raw, 1, 2, 2, 3);
        Assert.True(decoded.IsIntegrityValid);
        Assert.True(decoded.Asdu?.FirstObject?.QualityDetails?.Invalid);
        var original = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            SequenceNumber = 88,
            TimestampUtc = new DateTime(2026, 11, 13, 7, 12, 0, DateTimeKind.Utc),
            Direction = FrameDirection.SlaveToMaster,
            PhysicalSource = "SINGLE",
            LinkAddress = 105,
            Frame = decoded.SerialFrame,
            AsduDetails = decoded.Asdu,
            InformationObjectDetails = decoded.Asdu?.FirstObject,
            TypeId = 1,
            CommonAddressNumber = 105,
            InformationObjectAddress = 7,
            RawHex = Convert.ToHexString(raw)
        };
        var entry = new Iec103RelayEventLogEntry
        {
            EvidenceSequenceNumber = original.SequenceNumber,
            ProtocolMode = original.ProtocolMode,
            PhysicalSource = original.PhysicalSource,
            EvidenceReference = original.EvidenceReference,
            RawHex = original.RawHex,
            TypeId = 1,
            CommonAddress = 105,
            InformationObjectAddress = 7,
            SignalName = "Breaker status",
            NewValue = "ON"
        };
        using var journal = new EventHistoryJournal();
        journal.Append(new RelayEventRow(entry, original, 1));
        var restored = journal.ReadAfter(0, 1).Single().ToRow().OriginalFrameEvidence;
        Assert.NotNull(restored);
        Assert.Equal(original.RawHex, restored.RawHex);
        Assert.True(restored.Frame?.IsChecksumValid);
        Assert.Equal(3, restored.AsduDetails?.CauseOfTransmission);
        Assert.Equal(105, restored.AsduDetails?.CommonAddress);
        Assert.Equal(7, restored.AsduDetails?.FirstObject?.InformationObjectAddress);
        Assert.True(restored.AsduDetails?.FirstObject?.QualityDetails?.Invalid);
        Assert.Equal(7, restored.InformationObjectDetails?.InformationObjectAddress);
        Assert.Contains("Quality: Invalid", new EvidenceRow(restored).TimelineSignalDigest);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 801, false)]
    [InlineData(100, 801, false)]
    [InlineData(700, 801, false)]
    [InlineData(800, 801, true)]
    [InlineData(0, 100, true)]
    public void PageBottomResumesOnlyOnActualLastPage(
        long pageStart, long eventCount, bool expected)
    {
        Assert.Equal(expected,
            EventHistoryPagination.CanResumeFromVisiblePage(pageStart, eventCount));
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
