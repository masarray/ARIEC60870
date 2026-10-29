// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class ProtocolStatusBadgesTests
{
    [Fact]
    public void QdsOverflowAndInvalidRenderInsideOneQualityCell()
    {
        var q = new Iec10xQualityDetails(Iec10xQualityKind.Qds, 0x81);
        var chips = ProtocolStatusBadges.ForQuality(q, "ignored old string");
        Assert.Equal(new[] { "INVALID", "OVF" }, chips.Select(c => c.Text));
        Assert.Contains("OV=1", ProtocolStatusBadges.QualityDetail(q, ""));
        Assert.Contains("IV=1", ProtocolStatusBadges.QualityDetail(q, ""));
    }

    [Fact]
    public void DiqType31HasNoOverflowAndCounterCarryCannotBecomeOvf()
    {
        var diq = new Iec10xQualityDetails(Iec10xQualityKind.Diq, 0x01);
        Assert.Equal(new[] { "Good" }, ProtocolStatusBadges.ForQuality(diq, "").Select(b => b.Text));
        Assert.Contains("OV=N/A", ProtocolStatusBadges.QualityDetail(diq, ""));
        var counter = new Iec10xQualityDetails(Iec10xQualityKind.Bcr, 0x20);
        Assert.Equal(new[] { "CARRY" }, ProtocolStatusBadges.ForQuality(counter, "").Select(b => b.Text));
        Assert.DoesNotContain("OVF", ProtocolStatusBadges.ForQuality(counter, "").Select(b => b.Text));
    }

    [Fact]
    public void MultipleFlagsAreBoundedButAllRawBitsRemainInTooltip()
    {
        var q = new Iec10xQualityDetails(Iec10xQualityKind.Qds, 0xF1);
        var chips = ProtocolStatusBadges.ForQuality(q, "");
        Assert.Equal(new[] { "INVALID", "OVF", "NT", "+2" }, chips.Select(b => b.Text));
        Assert.Contains("BL=1", ProtocolStatusBadges.QualityDetail(q, ""));
        Assert.Contains("SB=1", ProtocolStatusBadges.QualityDetail(q, ""));
    }

    [Fact]
    public void MissingQualityNeverClaimsGoodAndLegacyQualityIsVisible()
    {
        Assert.Equal("—", ProtocolStatusBadges.ForQuality(null, "").Single().Text);
        Assert.Equal("—", ProtocolStatusBadges.ForQuality(null, "-").Single().Text);
        Assert.Equal("Legacy status", ProtocolStatusBadges.ForQuality(null, "Legacy status").Single().Text);
    }

    [Fact]
    public void NegativeTestCotProducesOnlyNecessaryChips()
    {
        var cot = new Iec10xCauseDetails(0xC7, 7, true, true, 0);
        Assert.Equal("7 · Activation confirmation", ProtocolStatusBadges.CotText(cot, ""));
        Assert.Equal(new[] { "NEG", "TEST" }, ProtocolStatusBadges.ForCot(cot).Select(b => b.Text));
        Assert.Contains("P/N=1", ProtocolStatusBadges.CotDetail(cot, ""));
        Assert.Contains("OA=0", ProtocolStatusBadges.CotDetail(cot, ""));
        Assert.Empty(ProtocolStatusBadges.ForCot(new Iec10xCauseDetails(0x03, 3, false, false, 0)));
    }

    [Fact]
    public void ValueAndEventRowsReceiveSameTypedFlagsNotStringGuesses()
    {
        var q = new Iec10xQualityDetails(Iec10xQualityKind.Qds, 0x81);
        var cot = new Iec10xCauseDetails(0x43, 3, true, false, 0);
        var value = new ValueRow(new Iec103ValuePoint
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            SignalName = "MW 1", DisplayValue = "32767",
            QualityText = "Good", QualityDetails = q, CauseDetails = cot
        });
        var ev = new RelayEventRow(new Iec103RelayEventLogEntry
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            SignalName = "MW 1", NewValue = "32767",
            QualityText = "Good", QualityDetails = q, CauseDetails = cot
        });
        Assert.Equal(new[] { "INVALID", "OVF" }, value.QualityBadges.Select(b => b.Text));
        Assert.Equal(value.QualityBadges.Select(b => b.Text), ev.QualityBadges.Select(b => b.Text));
        Assert.Equal("3 · spontaneous", value.Cot, ignoreCase: true);
        Assert.Equal(new[] { "NEG" }, ev.CotBadges.Select(b => b.Text));
    }
}
