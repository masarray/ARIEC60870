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
                    Ca = ca, Ioa = 4015, TypeId = 11,
                    Name = "FEEDER REACTIVE POWER (Q)",
                    Unit = "MVar", Scale = scale
                }
            ]
        });

    private static RelayEventRow Row(int ca = 105)
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
            CommonAddress = ca, InformationObjectAddress = 4015,
            TypeId = 11, SignalType = "M_ME_NB_1",
            SignalName = "IOA 4015", NewValue = "Scaled=13641"
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
    public void SaveAndApplyImmediatelyReprojectsCachedValuesFromTrustedWireEvidence()
    {
        var asdu = ScaledAsdu();
        var row = new ValueRow(new Iec103ValuePoint
        {
            Key = "101/LINK A/105/4015",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = "Link A", EvidenceReference = "101|seq:4|object:1",
            CommonAddress = 105, InformationObjectAddress = 4015,
            TypeId = 11, SignalName = "IOA 4015",
            RawValue = "Scaled=13641", DisplayValue = "13641 MVar"
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
    public void EditorRoundTripDoesNotLoseRatioOffsetOrNormalizedFullScale()
    {
        var point = new Iec10xPointMappingEntry
        {
            Ca = 105, Ioa = 4015, TypeId = 11, Name = "Q",
            Scale = 0.1, Offset = -2, RatioPrimary = 100,
            RatioSecondary = 5, NormalizedFullScale = 200
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
