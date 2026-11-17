// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using ARIEC60870.Core.Mapping;
using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec10xMappedNameParityTests
{
    private static Iec10xPointDisplayIndex Names() => new(new Iec10xPointMappingProfile
    {
        CommonAddress = 105,
        Points =
        [
            new() { Ca = 105, Ioa = 16712689, TypeId = 31,
                Name = "FEEDER TSD CB", Group = "Telesignal Double",
                CommandPolicy = "MonitorOnly" },
            new() { Ca = 105, Ioa = 68542, TypeId = 46,
                Name = "FEEDER RCD CB", CommandPolicy = "DoubleCommandRemoteOnly" }
        ]
    });

    private static RelayEventRow FeederEvent(int ca = 105) => new(new Iec103RelayEventLogEntry
    {
        ProtocolMode = Iec60870ProtocolMode.Iec101,
        PhysicalSource = "Link A",
        EvidenceSequenceNumber = 99,
        CommonAddress = ca,
        InformationObjectAddress = 16712689,
        TypeId = 31,
        SignalName = "IOA 16712689",
        NewValue = "OFF",
        CauseOfTransmission = "3",
        RawHex = "10 08 69 71 16",
        RelayTimeText = "2026-11-16 09:20:27.389"
    }, eventNumber: 1);

    [Fact]
    public void ValuesAndEventsRenderSameFeederNameWithoutChangingIoa()
    {
        var names = Names();
        var value = new ValueRow(new Iec103ValuePoint
        {
            Key = "101/LINK A/105/16712689",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = "Link A",
            CommonAddress = 105,
            InformationObjectAddress = 16712689,
            TypeId = 31,
            SignalName = "IOA 16712689"
        });
        var hit = names.Resolve(105, 16712689, 31);
        value.ApplyDisplayName(hit.Point!.Name, hit.Point.Group, mapped: true);
        var history = FeederEvent();
        history.ApplyDisplayNames(names);
        Assert.Equal("FEEDER TSD CB", value.Signal);
        Assert.Equal(value.Signal, history.Signal);
        Assert.Equal("16712689", value.IoAddress);
        Assert.Equal("16712689", history.IoAddress);
        Assert.Equal("Yes", history.Mapped);
        Assert.Equal("IOA 16712689", history.SourceEntry.SignalName);
    }

    [Fact]
    public async Task IndexedHistorySearchPageAndCsvShareActiveMappedName()
    {
        var names = Names();
        using var journal = new EventHistoryJournal();
        journal.Append(FeederEvent());
        var filtered = EventHistoryQueryEngine.Build(journal, journal.Count,
            "FEEDER TSD CB", ["Digital"], [],
            _ => "Digital", names: names);
        var row = Assert.Single(filtered.ReadDisplayPage(journal, 0, names: names));
        Assert.Equal("FEEDER TSD CB", row.Signal);
        Assert.Equal(1, row.EventNumber);
        var filename = Path.Combine(Path.GetTempPath(),
            "ariec-mapped-name-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            Assert.Equal(1, await EventHistoryCsvExporter.ExportAsync(
                journal, filtered, filename, names: names));
            var text = await File.ReadAllTextAsync(filename);
            Assert.Contains("FEEDER TSD CB", text);
            Assert.Contains("16712689", text);
        }
        finally
        {
            if (File.Exists(filename)) File.Delete(filename);
        }
    }

    [Fact]
    public void NameOnlyCaMismatchIsVisibleButRecordedCaIsUnchanged()
    {
        var row = FeederEvent(ca: 1);
        row.ApplyDisplayNames(Names());
        Assert.Equal("FEEDER TSD CB", row.Signal);
        Assert.Equal("Name only", row.Mapped);
        Assert.Equal("1", row.CommonAddress);
        Assert.Equal(1, row.SourceEntry.CommonAddress);
    }

    private static ValueRow ObservedValue(string source, int ca, int ioa,
        int type, bool verified = true)
        => new(new Iec103ValuePoint
        {
            Key = Iec10xObservationIdentity.ValueKey(Iec60870ProtocolMode.Iec101,
                source, ca, ioa),
            PhysicalSource = source,
            EvidenceReference = verified ? "101|seq:100|object:0" : "",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            CommonAddress = ca,
            InformationObjectAddress = ioa,
            TypeId = type,
            SignalName = $"IOA {ioa}"
        });

    [Fact]
    public void ProfileSwitchRecoversFeederNameForAcceptedSingleStationCaAlias()
    {
        var value = ObservedValue("Link A", ca: 1, ioa: 16712689, type: 3);
        Iec10xValueNameReconciler.Apply([value], Names());
        Assert.Equal("FEEDER TSD CB", value.Signal);
        Assert.Equal("1", value.CommonAddress);
        Assert.Equal("16712689", value.IoAddress);
        Assert.Equal("Yes", value.Mapped);
    }

    [Fact]
    public void ProfileSwitchDoesNotGuessFeederNameAcrossMultipleCaOnSameSource()
    {
        var feeder = ObservedValue("Link A", ca: 1, ioa: 16712689, type: 31);
        var otherStation = ObservedValue("Link A", ca: 2, ioa: 16712690, type: 31);
        Iec10xValueNameReconciler.Apply([feeder, otherStation], Names());
        Assert.Equal("IOA 16712689", feeder.Signal);
        Assert.Equal("No", feeder.Mapped);
        Assert.Equal("1", feeder.CommonAddress);
        Assert.Equal("IOA 16712690", otherStation.Signal);
    }

    [Fact]
    public void IndependentSourcesCanRetainSafeUniqueNameAliases()
    {
        var a = ObservedValue("Link A", ca: 1, ioa: 16712689, type: 31);
        var b = ObservedValue("Link B", ca: 2, ioa: 16712689, type: 3);
        Iec10xValueNameReconciler.Apply([a, b], Names());
        Assert.Equal("FEEDER TSD CB", a.Signal);
        Assert.Equal("FEEDER TSD CB", b.Signal);
        Assert.NotEqual(a.CommonAddress, b.CommonAddress);
    }

    [Fact]
    public void UnverifiedPlaceholderCannotBorrowFeederNameFromAnotherCa()
    {
        var placeholder = ObservedValue("SINGLE", ca: 1, ioa: 16712689, type: 31,
            verified: false);
        Iec10xValueNameReconciler.Apply([placeholder], Names());
        Assert.Equal("IOA 16712689", placeholder.Signal);
        Assert.Equal("No", placeholder.Mapped);
        var exact = ObservedValue("SINGLE", ca: 105, ioa: 16712689, type: 31,
            verified: false);
        Iec10xValueNameReconciler.Apply([exact], Names());
        Assert.Equal("FEEDER TSD CB", exact.Signal);
    }

    [Fact]
    public void ObservedControlEventCanUseNameWithoutChangingItsTargetOrStage()
    {
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(
            [46, 1, 7, 0, 105, 0, 190, 11, 1, 0x81]);
        var evidence = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            TypeId = 46,
            CommonAddressNumber = 105,
            InformationObjectAddress = 68542,
            AsduDetails = asdu
        };
        var label = Names().Resolve(105, 68542, 46).Point?.Name;
        var projected = Iec10xCommandEventProjection.Project(evidence, mappedSignalName: label);
        Assert.NotNull(projected);
        Assert.Contains("FEEDER RCD CB", projected!.SignalName);
        Assert.Contains("IOA 68542", projected.SignalName);
        Assert.Equal(68542, projected.InformationObjectAddress);
    }
}
