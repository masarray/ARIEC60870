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
        Assert.Equal(new[] { "INVALID", "OVF" }, ProtocolFlagPresentation.Quality(q, null, true).Select(x => x.Text));
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
        Assert.Equal("CARRY", Assert.Single(ProtocolFlagPresentation.Quality(q, null, true)).Text);
        Assert.Null(q.Overflow);
    }

    [Fact]
    public void ExcessFlagsCollapseWithoutLosingTypedDetails()
    {
        var q = new Iec10xQualityDetails(Iec10xQualityKind.Qds, 0xF1);
        Assert.Equal(new[] { "INVALID", "OVF", "+3" }, ProtocolFlagPresentation.Quality(q, null, true).Select(x => x.Text));
        Assert.Contains("SB=1", q.BitDetails);
    }

    [Fact]
    public void MissingQualityCannotBePresentedAsGood()
        => Assert.Equal("Unknown", Assert.Single(ProtocolFlagPresentation.Quality(null, "-", true)).Text);

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
        Assert.Equal("OVF", Assert.Single(v.QualityChips).Text);
        Assert.Equal("OVF", Assert.Single(e.QualityChips).Text);
        Assert.StartsWith("3 ·", v.CotLabel);
        Assert.StartsWith("3 ·", e.CotLabel);
    }
}
