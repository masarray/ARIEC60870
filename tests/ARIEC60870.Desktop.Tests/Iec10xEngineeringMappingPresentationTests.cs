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
                    Name = "FEEDER TM Q",
                    Unit = "MVar",
                    Scale = scale
                }
            ]
        });

    private static RelayEventRow Row(int ca = 105, string observedValue = "Scaled=13641",
        long eventNumber = 0)
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
        }, source, eventNumber: eventNumber);
    }

    private static Iec10xPointDisplayIndex LinearIndex(int ca = 105) =>
        new(new Iec10xPointMappingProfile
        {
            CommonAddress = ca,
            Points =
            [
                new Iec10xPointMappingEntry
                {
                    Ca = ca,
                    Ioa = 4015,
                    TypeId = 11,
                    Name = "FEEDER TM Q",
                    Unit = "MVar",
                    ScalingMode = "TwoPoint",
                    X0 = 0,
                    X100 = 10000,
                    Y0 = 0,
                    Y100 = 2000,
                    Scale = 99,             // Inactive when TwoPoint is chosen.
                    RatioPrimary = 50,
                    RatioSecondary = 2,
                    Offset = 25
                }
            ]
        });

    [Fact]
    public void TwoPointEditorRoundTripPreservesFourEndpointsAndMode()
    {
        var point = new Iec10xPointMappingEntry
        {
            Ca = 105,
            Ioa = 4015,
            TypeId = 11,
            Name = "Q",
            ScalingMode = "TwoPoint",
            X0 = -32768,
            X100 = 32767,
            Y0 = -2000,
            Y100 = 2000
        };
        var draft = new SignalListEditorRow(point);
        Assert.Equal("TwoPoint", draft.ScalingMode);
        // Legacy/imported casing and hand-edited grid text must not select
        // the Factor editor while applying a TwoPoint calibration.
        draft.ScalingMode = "twopoint";
        Assert.Equal("TwoPoint", draft.ScalingMode);
        draft.ScalingMode = "factor";
        Assert.Equal("Factor", draft.ScalingMode);
        draft.ScalingMode = "TwoPoint";
        Assert.Equal("-32768", draft.X0);
        Assert.Equal("32767", draft.X100);
        var copy = draft.Clone();
        Assert.Equal(draft.X0, copy.X0);
        Assert.Equal(draft.Y100, copy.Y100);

        var restored = copy.ToPoint();
        Assert.Equal(point.ScalingMode, restored.ScalingMode);
        Assert.Equal(point.X0, restored.X0);
        Assert.Equal(point.X100, restored.X100);
        Assert.Equal(point.Y0, restored.Y0);
        Assert.Equal(point.Y100, restored.Y100);
        new Iec10xPointMappingProfile { Points = [restored] }.Validate();

        copy.X100 = copy.X0;
        Assert.Throws<InvalidOperationException>(() =>
            new Iec10xPointMappingProfile { Points = [copy.ToPoint()] }.Validate());
        copy.X100 = "not a number";
        Assert.Throws<InvalidOperationException>(() => copy.ToPoint());
        Assert.Equal("32767", draft.X100);
    }

    [Fact]
    public void TwoPointCalibratesEventHistoryAndValuesButNotRawEvidence()
    {
        var profile = LinearIndex();
        // Journal source ordinals start at one and must remain contiguous.
        var row = Row(eventNumber: 1);
        row.ApplyDisplayNames(profile);
        Assert.Equal("2728.2 MVar", row.DisplayValue);
        Assert.Equal("Scaled=13641", row.SourceEntry.NewValue);

        using var journal = new EventHistoryJournal();
        journal.Append(row);
        var entry = Assert.Single(journal.ReadAfter(0, 1));
        Assert.Equal(13641, entry.EngineeringSample?.SourceNumber);
        var search = EventHistoryQueryEngine.Build(journal, journal.Count, "2728.2",
            ["Digital", "Measurement", "Commands", "GI", "Other"], [],
            _ => "Measurement", names: profile);
        Assert.Equal(1, search.Count);
        var result = Assert.Single(search.ReadDisplayPage(journal, 0, names: profile));
        Assert.Equal("2728.2 MVar", result.DisplayValue);
        Assert.Equal("Scaled=13641", entry.EngineeringSample?.OriginalValue);

        var asdu = ScaledAsdu();
        var value = new ValueRow(new Iec103ValuePoint
        {
            Key = "101/LINK A/105/4015",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = "Link A",
            EvidenceReference = "101|seq:1|object:1",
            CommonAddress = 105,
            InformationObjectAddress = 4015,
            TypeId = 11,
            SignalName = "IOA 4015",
            RawValue = "Scaled=13641",
            DisplayValue = "13641 MVar"
        }, sourceAsdu: asdu, sourceObject: Assert.Single(asdu.Objects));
        Iec10xValueNameReconciler.Apply([value], profile);
        Assert.Equal("2728.2 MVar", value.Value);
        Assert.Equal("Scaled=13641", value.RawValue);
        Iec10xValueNameReconciler.Apply([value], Index(105, 0.1));
        Assert.Equal("1364.1 MVar", value.Value);
        Iec10xValueNameReconciler.Apply([value], profile);
        Assert.Equal("2728.2 MVar", value.Value);

        var differentStation = Row(ca: 1);
        differentStation.ApplyDisplayNames(profile);
        Assert.Equal("Name only", differentStation.Mapped);
        Assert.Equal("Scaled=13641", differentStation.DisplayValue);
    }

    [Fact]
    public void HistoryPageAppliesActiveSignalListEngineeringFactorWithoutEditingWireValue()
    {
        var row = Row();
        row.ApplyDisplayNames(Index(105, 0.1));
        Assert.Equal("FEEDER TM Q", row.Signal);
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
        Assert.Equal("FEEDER TM Q", row.Signal);
        Assert.Equal("Name only", row.Mapped);
        Assert.Equal("Scaled=13641", row.DisplayValue);
        Assert.Equal(1, row.SourceEntry.CommonAddress);
    }

    [Fact]
    public void HistoricalSearchUsesSameCalibrationAsPageAndDoesNotReDecodeEveryFrame()
    {
        using var journal = new EventHistoryJournal();
        var original = Row(observedValue: "1364.1 MVar", eventNumber: 1);
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
    public void ScreenshotPoaQAndQMeasurementsReprojectFromWireOnReferenceApply()
    {
        // Regression for the RC2 screenshot: opening the reference in the editor
        // was not enough to activate it in live Values. Once Save & Apply has
        // activated that profile, the same cached, trusted original wire bytes
        // must be re-projected without new RX or a second scaling pass.
        string? referencePath = null;
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory);
             dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "profiles",
                "utility_fat_iec10x_default_profile.json");
            if (!File.Exists(candidate)) continue;
            referencePath = candidate;
            break;
        }
        Assert.NotNull(referencePath);
        var current = Iec10xPointMappingProfile.LoadFromFile(referencePath);
        var active = new Iec10xPointDisplayIndex(current);

        static Iec10xAsduDecode DecodeMeasured(byte type, int ioa, params byte[] value)
            => new Iec10xAsduDecoder(1, 1, 3).Decode(
                new byte[] { type, 1, 20, 105,
                    (byte)ioa, (byte)(ioa >> 8), (byte)(ioa >> 16) }
                    .Concat(value).ToArray());

        static ValueRow Cached(Iec10xAsduDecode asdu, string oldValue)
        {
            var obj = Assert.Single(asdu.Objects);
            return new ValueRow(new Iec103ValuePoint
            {
                Key = $"101/LINK A/{asdu.CommonAddress}/{obj.InformationObjectAddress}",
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                PhysicalSource = "Link A",
                EvidenceReference = "101|seq:22|object:1",
                CommonAddress = asdu.CommonAddress,
                InformationObjectAddress = obj.InformationObjectAddress,
                TypeId = asdu.TypeId,
                SignalName = $"IOA {obj.InformationObjectAddress}",
                RawValue = obj.ShortValue,
                DisplayValue = oldValue,
                QualityText = obj.QualityText,
                QualityDetails = obj.QualityDetails
            }, sourceAsdu: asdu, sourceObject: obj);
        }

        var poaqSource = DecodeMeasured(9, 790449, 0xFF, 0x7F, 0x01);
        Assert.Empty(poaqSource.Issues);
        var poaq = Cached(poaqSource, "0.999969482 MW");
        var legacy = new Iec10xPointDisplayIndex(new Iec10xPointMappingProfile
        {
            CommonAddress = 105,
            Points = [new Iec10xPointMappingEntry
            {
                Ca = 105, Ioa = 790449, TypeId = 9,
                Name = "Legacy POAQ", Unit = "MW", CommandPolicy = "MonitorOnly"
            }]
        });
        Iec10xValueNameReconciler.Apply([poaq], legacy);
        Assert.Equal("0.999969482 MW", poaq.Value);
        Iec10xValueNameReconciler.Apply([poaq], active);
        Assert.Equal("1199.96338 MW", poaq.Value);
        Assert.Equal("TRAFO SOGI TM POAQ", poaq.Signal);
        Assert.Equal("Normalized=0.99997", poaq.RawValue);
        Assert.Equal("Overflow", poaq.Quality);
        Assert.True(poaq.QualityFlags?.Overflow);
        Assert.Equal(new byte[] { 0xFF, 0x7F, 0x01 },
            poaqSource.Objects[0].RawBytes);

        var qSource = DecodeMeasured(11, 790439, 0x49, 0x35, 0x00);
        Assert.Empty(qSource.Issues);
        var q = Cached(qSource, "13641 counts");
        Iec10xValueNameReconciler.Apply([q], active);
        Assert.Equal("865.096117 MVar", q.Value);
        Assert.Equal("KOPEL TM Q 1", q.Signal);
        Assert.Equal("Scaled=13641", q.RawValue);
        Assert.Equal(new byte[] { 0x49, 0x35, 0x00 },
            qSource.Objects[0].RawBytes);

        // An unseen but numerically similar physical IOA is NOT an excuse to
        // remap the protocol address, interpret a different station, or borrow
        // another object's calibration. The received IOA remains literal.
        var unrelatedSource = DecodeMeasured(13, 1790446,
            0x00, 0x00, 0x00, 0x00, 0x00);
        var unknown = Cached(unrelatedSource, "Float=0");
        Iec10xValueNameReconciler.Apply([unknown], active);
        Assert.Equal("IOA 1790446", unknown.Signal);
        Assert.Equal("1790446", unknown.IoAddress);
        Assert.NotEqual("FEEDER TM P", unknown.Signal);
    }

    [Fact]
    public void EditorRoundTripDoesNotLoseRatioOffsetOrNormalizedFullScale()
    {
        var point = new Iec10xPointMappingEntry
        {
            Ca = 105,
            Ioa = 4015,
            TypeId = 11,
            Name = "Q",
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
