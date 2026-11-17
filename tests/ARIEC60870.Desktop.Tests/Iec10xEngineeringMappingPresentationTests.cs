// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Mapping;
using ARIEC60870.Core.Model;
using ARIEC60870.Desktop;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec10xEngineeringMappingPresentationTests
{
    private static Iec10xAsduDecode ScaledAsdu()
        => new Iec10xAsduDecoder(1, 1, 3).Decode(
            [11, 1, 3, 105, 0xAF, 0x0F, 0, 0x49, 0x35, 0]);

    private static Iec10xPointDisplayIndex Index(int ca, double scale)
        => new(new Iec10xPointMappingProfile
        {
            CommonAddress = ca,
            Points =
            [
                new Iec10xPointMappingEntry
                {
                    Ca = ca,
                    Ioa = 4015,
                    TypeId = 11,
                    Name = "FEEDER REACTIVE POWER (Q)",
                    Unit = "MVar",
                    Scale = scale
                }
            ]
        });

    private static RelayEventRow Row(int ca = 105, string observedValue = "Scaled=13641")
    {
        var asdu = ScaledAsdu();
        var source = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            CommonAddressNumber = ca,
            InformationObjectAddress = 4015,
            TypeId = 11,
            AsduDetails = asdu,
            InformationObjectDetails = Assert.Single(asdu.Objects)
        };
        return new RelayEventRow(new Iec103RelayEventLogEntry
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            CommonAddress = ca,
            InformationObjectAddress = 4015,
            TypeId = 11,
            SignalType = "M_ME_NB_1",
            SignalName = "IOA 4015",
            NewValue = observedValue
        }, source);
    }

    [Fact]
    public void HistoryPageAppliesActiveSignalListEngineeringFactorWithoutEditingWireValue()
    {
        var row = Row();
        row.ApplyDisplayNames(Index(105, 0.1));
        Assert.Equal("FEEDER REACTIVE POWER (Q)", row.Signal);
        Assert.Equal("1364.1 MVar", row.DisplayValue);
        Assert.Equal("1364.1 MVar", row.CompactDisplayValue);
        Assert.Equal("Scaled=13641", row.SourceEntry.NewValue);
        Assert.Equal("13641", row.OriginalFrameEvidence!.InformationObjectDetails!.ShortValue
            .Replace("Scaled=", "", StringComparison.Ordinal));
    }

    [Fact]
    public void NameOnlyCommonAddressAliasCannotBorrowAnotherStationsEngineeringFactor()
    {
        var row = Row(ca: 1);
        row.ApplyDisplayNames(Index(105, 0.1));
        Assert.Equal("FEEDER REACTIVE POWER (Q)", row.Signal);
        Assert.Equal("Name only", row.Mapped);
        Assert.Equal("Scaled=13641", row.DisplayValue);
        Assert.Equal(1, row.SourceEntry.CommonAddress);
    }

    [Fact]
    public void HistoricalSearchUsesSameCalibrationAsPageAndDoesNotReDecodeEveryFrame()
    {
        using var journal = new EventHistoryJournal();
        var original = Row(observedValue: "1364.1 MVar");
        original.EventNumber = 1;
        journal.Append(original);

        var entry = Assert.Single(journal.ReadAfter(0, 1));
        Assert.NotNull(entry.EngineeringSample);
        Assert.Equal(13641, entry.EngineeringSample.SourceNumber);
        Assert.Equal("Scaled=13641", entry.EngineeringSample.OriginalValue);

        var newProfile = Index(105, 0.2);
        var matching = EventHistoryQueryEngine.Build(journal, journal.Count, "2728.2",
            ["Digital", "Measurement", "Commands", "GI", "Other"], [],
            _ => "Measurement", names: newProfile);
        Assert.Equal(1, matching.Count);
        var shown = Assert.Single(matching.ReadDisplayPage(journal, 0, names: newProfile));
        Assert.Equal("2728.2 MVar", shown.DisplayValue);
        Assert.Equal("1364.1 MVar", shown.SourceEntry.NewValue);
        // Source-qualified search row has a small numeric sample, not a
        // decoded physical frame: avoids full FT1.2 parse in 50k scans.
        var queryRow = entry.ToQueryRow();
        Assert.Null(queryRow.OriginalFrameEvidence?.AsduDetails);
        queryRow.ApplyDisplayNames(newProfile);
        Assert.Equal("2728.2 MVar", queryRow.DisplayValue);

        var noCalibration = new Iec10xPointDisplayIndex(
            new Iec10xPointMappingProfile());
        var reset = Assert.Single(matching.ReadDisplayPage(journal, 0,
            names: noCalibration));
        Assert.Equal("Scaled=13641", reset.DisplayValue);
        Assert.Equal("Scaled=13641", reset.CompactDisplayValue);
        Assert.Equal(1, reset.EventNumber);
        var staleSearch = EventHistoryQueryEngine.Build(journal, journal.Count,
            "2728.2", ["Digital", "Measurement", "Commands", "GI", "Other"], [],
            _ => "Measurement", names: noCalibration);
        Assert.Equal(0, staleSearch.Count);
    }

    [Fact]
    public void NameOnlyAliasResetsPreviouslyCalibratedEventToOriginalMeasurement()
    {
        var historical = Row(ca: 1, observedValue: "1364.1 MVar");
        historical.ApplyDisplayNames(Index(105, 0.2));
        Assert.Equal("Name only", historical.Mapped);
        Assert.Equal("Scaled=13641", historical.DisplayValue);
    }

    [Fact]
    public void SaveAndApplyImmediatelyReprojectsCachedValuesFromTrustedWireEvidence()
    {
        var asdu = ScaledAsdu();
        var row = new ValueRow(new Iec103ValuePoint
        {
            Key = "101/LINK A/105/4015",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = "Link A",
            EvidenceReference = "101|seq:4|object:1",
            CommonAddress = 105,
            InformationObjectAddress = 4015,
            TypeId = 11,
            SignalName = "IOA 4015",
            RawValue = "Scaled=13641",
            DisplayValue = "13641 MVar"
        }, sourceAsdu: asdu, sourceObject: Assert.Single(asdu.Objects));

        Iec10xValueNameReconciler.Apply([row], Index(105, 0.1));
        Assert.Equal("1364.1 MVar", row.Value);
        Assert.Equal("Scaled=13641", row.RawValue);

        // Changing the profile again must use the original 13641,
        // not double-scale the previous engineering display of 1364.1.
        Iec10xValueNameReconciler.Apply([row], Index(105, 0.2));
        Assert.Equal("2728.2 MVar", row.Value);
        Iec10xValueNameReconciler.Apply([row], Index(106, 0.4));
        Assert.Equal("Scaled=13641", row.Value);
    }

    [Fact]
    public void DuplicateWithUnfinishedInvalidEngineeringEntryPreservesDraftAndDoesNotThrow()
    {
        var row = new SignalListEditorRow(new Iec10xPointMappingEntry
        {
            Ca = 105,
            Ioa = 4015,
            TypeId = 11,
            Name = "Q"
        });
        row.Scale = "unfinished";
        row.RatioSecondary = "";
        row.Offset = "abc";
        var copied = row.Clone();
        Assert.NotSame(row, copied);
        Assert.Equal("unfinished", copied.Scale);
        Assert.Equal("", copied.RatioSecondary);
        Assert.Equal("abc", copied.Offset);
        Assert.Throws<InvalidOperationException>(() => copied.ToPoint());
        row.Scale = "0.1";
        Assert.Equal("unfinished", copied.Scale);
    }

    [Fact]
    public void EditorRoundTripDoesNotLoseRatioOffsetOrNormalizedFullScale()
    {
        var point = new Iec10xPointMappingEntry
        {
            Ca = 105, Ioa = 4015, TypeId = 11, Name = "Q",
            Scale = 0.1,
            Offset = -2,
            RatioPrimary = 100,
            RatioSecondary = 5,
            NormalizedFullScale = 200
        };
        var row = new SignalListEditorRow(point);
        var restored = row.ToPoint();
        Assert.Equal(point.Scale, restored.Scale);
        Assert.Equal(point.Offset, restored.Offset);
        Assert.Equal(point.RatioPrimary, restored.RatioPrimary);
        Assert.Equal(point.RatioSecondary, restored.RatioSecondary);
        Assert.Equal(point.NormalizedFullScale, restored.NormalizedFullScale);
        row.RatioSecondary = "not a number";
        Assert.Throws<InvalidOperationException>(() => row.ToPoint());
    }
}
