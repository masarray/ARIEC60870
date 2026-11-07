// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class ProtocolFlagPresentationTests
{
    [Fact]
    public void QdsSignalsOnlyActiveFaultBits()
    {
        var q = new Iec10xQualityDetails(Iec10xQualityKind.Qds, 0x81);
        Assert.Equal(new[] { "IV", "OV" }, ProtocolFlagPresentation.Quality(q, null, true).Select(x => x.Text));
        Assert.Contains("OV=1", q.BitDetails);
    }

    [Fact]
    public void DiqHasNoOverflowEvenForTimedDoublePoint()
    {
        var q = Iec10xQualityDetails.Decode(31, new byte[] { 1 });
        Assert.NotNull(q);
        Assert.Null(q!.Overflow);
        Assert.Equal("Good", Assert.Single(ProtocolFlagPresentation.Quality(q, null, true)).Text);
    }

    [Fact]
    public void CounterCarryIsNotMeasurementOverflow()
    {
        var q = new Iec10xQualityDetails(Iec10xQualityKind.Bcr, 0x20);
        Assert.Equal("CY", Assert.Single(ProtocolFlagPresentation.Quality(q, null, true)).Text);
        Assert.Null(q.Overflow);
    }

    [Fact]
    public void ExcessFlagsCollapseWithoutLosingTypedDetails()
    {
        var q = new Iec10xQualityDetails(Iec10xQualityKind.Qds, 0xF1);
        Assert.Equal(new[] { "IV", "OV", "+3" }, ProtocolFlagPresentation.Quality(q, null, true).Select(x => x.Text));
        Assert.Contains("SB=1", q.BitDetails);
    }

    [Fact]
    public void MissingQualityCannotBePresentedAsGood()
        => Assert.Equal("Unknown", Assert.Single(ProtocolFlagPresentation.Quality(null, "-", true)).Text);

    [Fact]
    public void DecodeRealTi13QdsWithFiveFaultsAndNeverHideIvOrOv()
    {
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(
            [13, 1, 20, 0, 1, 0, 1, 0, 0, 0x00, 0x00, 0x48, 0x41, 0xF1]);
        Assert.Empty(asdu.Issues);
        var obj = Assert.Single(asdu.Objects);
        Assert.Equal(0xF1, obj.QualityDetails?.Raw);
        var visual = ProtocolFlagPresentation.QualityState(obj.QualityDetails, obj.QualityText, true);
        Assert.Equal(["IV", "OV", "+3"], visual.Chips.Select(x => x.Text));
        Assert.True(visual.IsInvalid);
        Assert.Contains("IV=1", visual.Tooltip);
        Assert.Contains("OV=1", visual.Tooltip);
        Assert.Contains("NT=1", visual.Tooltip);
        Assert.Contains("SB=1", visual.Tooltip);
        Assert.Contains("BL=1", visual.Tooltip);
        Assert.Equal("", visual.CleanRawLabel);

        var value = new ValueRow(new Iec103ValuePoint
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101, TypeId = 13,
            QualityDetails = obj.QualityDetails, QualityText = obj.QualityText
        });
        var row = new RelayEventRow(new Iec103RelayEventLogEntry
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101, TypeId = 13,
            QualityDetails = obj.QualityDetails, QualityText = obj.QualityText
        });
        Assert.Equal(value.QualityDetail, row.QualityDetail);
        Assert.True(value.IsQualityInvalid);
        Assert.True(row.IsQualityInvalid);
        Assert.Equal("OV", value.QualityChips[1].Text);
    }

    [Fact]
    public void SiqAndDiqDoNotInventQdsOverflow()
    {
        var siq = new Iec10xAsduDecoder(2, 2, 3).Decode(
            [1, 1, 3, 0, 1, 0, 1, 0, 0, 0x91]);
        var diq = new Iec10xAsduDecoder(2, 2, 3).Decode(
            [3, 1, 3, 0, 1, 0, 2, 0, 0, 0xD2]);
        Assert.Empty(siq.Issues);
        Assert.Empty(diq.Issues);
        foreach (var obj in new[] { Assert.Single(siq.Objects), Assert.Single(diq.Objects) })
        {
            var visual = ProtocolFlagPresentation.QualityState(obj.QualityDetails, obj.QualityText, true);
            Assert.Contains(visual.Chips, chip => chip.Text == "IV");
            Assert.DoesNotContain(visual.Chips, chip => chip.Text == "OV");
            Assert.DoesNotContain("OV=", visual.Tooltip);
            Assert.Contains("IV=1", visual.Tooltip);
        }
    }

    [Fact]
    public void CleanFlagsExposeRawQualityByteButDoNotFabricateProblems()
    {
        var siq = Iec10xQualityDetails.Decode(1, [0x01]);
        var visual = ProtocolFlagPresentation.QualityState(siq, "Good", true);
        Assert.Equal("Good", Assert.Single(visual.Chips).Text);
        Assert.Equal("SIQ 0x01", visual.CleanRawLabel);
        Assert.Contains("IV=0", visual.Tooltip);
        Assert.DoesNotContain("OV=", visual.Tooltip);
        Assert.False(visual.IsInvalid);
        var unknown = ProtocolFlagPresentation.QualityState(null, "Good", true);
        Assert.Equal("Unknown", Assert.Single(unknown.Chips).Text);
    }

    [Fact]
    public void CounterCarryAndAdjustedAreNotQdsOverflow()
    {
        var q = new Iec10xQualityDetails(Iec10xQualityKind.Bcr, 0xE2);
        var visual = ProtocolFlagPresentation.QualityState(q, "", true);
        Assert.Equal(["IV", "CY", "CA"], visual.Chips.Select(x => x.Text));
        Assert.Contains("SQ=2", visual.Tooltip);
        Assert.DoesNotContain("OV=", visual.Tooltip);
    }

    [Fact]
    public void CotNegativeAndTestShareOneCell()
    {
        var cot = new Iec10xCauseDetails(0xC7, 7, true, true, 0);
        Assert.Equal(new[] { "NEG", "TEST" }, ProtocolFlagPresentation.Cot(cot).Select(x => x.Text));
        Assert.Contains("OA=0", cot.Detail);
    }

    [Fact]
    public void EventAndValueUseIdenticalFlagVisuals()
    {
        var q = new Iec10xQualityDetails(Iec10xQualityKind.Qds, 1);
        var cot = new Iec10xCauseDetails(3, 3, false, false, 0);
        var v = new ValueRow(new Iec103ValuePoint { ProtocolMode = Iec60870ProtocolMode.Iec101,
            QualityDetails = q, CauseDetails = cot, QualityText = "Overflow", CauseOfTransmission = "spontaneous" });
        var e = new RelayEventRow(new Iec103RelayEventLogEntry { ProtocolMode = Iec60870ProtocolMode.Iec101,
            QualityDetails = q, CauseDetails = cot, QualityText = "Overflow", CauseOfTransmission = "spontaneous" });
        Assert.Equal("OV", Assert.Single(v.QualityChips).Text);
        Assert.Equal("OV", Assert.Single(e.QualityChips).Text);
        Assert.StartsWith("3 ·", v.CotLabel);
        Assert.StartsWith("3 ·", e.CotLabel);
    }
}
