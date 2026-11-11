// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.IO;
using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class EventHistoryQueryTests
{
    private static readonly string[] All =
        ["Digital", "Measurement", "Commands", "GI", "Other"];

    private static RelayEventRow Make(long number)
    {
        var digital = number % 2 == 1;
        var source = new Iec103RelayEventLogEntry
        {
            EvidenceSequenceNumber = number + 100,
            ArrivalTimeUtc = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = "SINGLE",
            SignalName = number == 1 ? "=1+1" : "IOA " + number.ToString(CultureInfo.InvariantCulture),
            NewValue = digital ? "ON" : "23.5",
            TypeId = digital ? 30 : 13,
            InformationObjectAddress = (int)number,
            CommonAddress = 105,
            CauseOfTransmission = "3",
            RawHex = "10 08 69 71 16",
            RelayTimeText = "2026-11-11 10:00:00.000",
            QualityText = "Good",
            EvidenceReference = "SINGLE/" + number.ToString(CultureInfo.InvariantCulture)
        };
        return new RelayEventRow(source, new Iec103MasterEvidenceEvent
        {
            SequenceNumber = number + 100,
            PhysicalSource = source.PhysicalSource,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            RawHex = source.RawHex,
            LinkAddress = 105,
            // EvidenceReference derives from physical source and sequence.
        }, number);
    }

    private static string Category(RelayEventRow row) =>
        row.TypeId == "30" ? "Digital" : "Measurement";

    [Fact]
    public void FullHistoryFiltersNumbersAndCsvExtendBeyondPreview420()
    {
        using var journal = new EventHistoryJournal();
        for (var i = 1; i <= 2150; i++) journal.Append(Make(i));

        var full = EventHistoryQueryEngine.Build(journal, journal.Count,
            "", All, [], Category);
        Assert.False(full.IsFiltered);
        Assert.Equal(2150, full.Count);
        Assert.Equal(2101, full.ReadPage(journal, 2100)[0].EventNumber);
        Assert.Equal(2150, full.ReadPage(journal, 2100)[^1].EventNumber);

        var digital = EventHistoryQueryEngine.Build(journal, journal.Count,
            "", ["Digital"], [], Category);
        Assert.Equal(1075, digital.Count);
        Assert.Equal(1, digital.ReadPage(journal, 0)[0].EventNumber);
        Assert.Equal(2150 - 1, digital.ReadPage(journal, 1000)[^1].EventNumber);
        Assert.Equal(-1, digital.FindPosition(2));
    }

    [Fact]
    public async Task FilteredCsvUsesMatchingSourceIdsAndNeutralizesFormulaCells()
    {
        using var journal = new EventHistoryJournal();
        for (var i = 1; i <= 650; i++) journal.Append(Make(i));
        var one = EventHistoryQueryEngine.Build(journal, journal.Count,
            "#1", All, [], Category);
        Assert.Single(one.ReadPage(journal, 0));
        Assert.Equal(1, one.OriginalNumberAt(0));
        Assert.Equal(-1, one.FindPosition(2));

        var destination = Path.Combine(Path.GetTempPath(),
            "ArSee-filter-csv-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            Assert.Equal(1, await EventHistoryCsvExporter.ExportAsync(
                journal, one, destination));
            var lines = File.ReadAllLines(destination);
            Assert.Equal(2, lines.Length);
            Assert.Contains("\"'=1+1\"", lines[1]);
            var originalReference = one.ReadPage(journal, 0)[0]
                .ToRow().OriginalFrameEvidence!.EvidenceReference;
            Assert.Contains("\"" + originalReference + "\"", lines[1]);
            Assert.Contains("\"10 08 69 71 16\"", lines[1]);
        }
        finally
        {
            if (File.Exists(destination)) File.Delete(destination);
        }
    }

    [Fact]
    public void IncrementalFilteredQueryDoesNotReindexAllOlderRows()
    {
        using var journal = new EventHistoryJournal();
        for (var i = 1; i <= 450; i++) journal.Append(Make(i));
        var first = EventHistoryQueryEngine.Build(journal, 450,
            "", ["Measurement"], [], Category);
        Assert.Equal(225, first.Count);
        for (var i = 451; i <= 600; i++) journal.Append(Make(i));
        var second = EventHistoryQueryEngine.Extend(journal, first, 600,
            "", ["Measurement"], [], Category);
        Assert.Equal(300, second.Count);
        Assert.Equal(600, second.OriginalNumberAt(299));
        Assert.Equal(450, first.Ceiling); // old page horizon remains immutable
        Assert.Equal(450, first.OriginalNumberAt(224));
    }

    [Fact]
    public void SparseIndexedPageKeepsOriginalOrderAndRejectsOutOfRange()
    {
        using var journal = new EventHistoryJournal();
        for (var i = 1; i <= 300; i++) journal.Append(Make(i));
        var page = journal.ReadSelected([1, 123, 300]);
        Assert.Equal([1L, 123L, 300L], page.Select(x => x.EventNumber));
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.ReadSelected([0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.ReadSelected([301]));
    }
}
