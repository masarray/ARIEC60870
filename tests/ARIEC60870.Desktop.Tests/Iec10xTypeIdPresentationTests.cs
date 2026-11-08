// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec10xTypeIdPresentationTests
{
    [Theory]
    [InlineData(13, "Floating-point measurement")]
    [InlineData(30, "Single point with timestamp")]
    [InlineData(31, "Double point with timestamp")]
    [InlineData(32, "Step position with timestamp")]
    [InlineData(36, "Floating-point measurement with timestamp")]
    [InlineData(45, "Single command")]
    [InlineData(100, "General interrogation")]
    [InlineData(21, "Normalized measurement without quality descriptor")]
    [InlineData(33, "32-bit status with timestamp")]
    [InlineData(70, "End of initialization")]
    [InlineData(106, "Delay acquisition")]
    [InlineData(48, "setpoint normalized")]
    [InlineData(64, "bitstring command with")]
    public void KnownTypeIdRetainsWireNumberAndStandardIdentifier(int ti, string symbol)
    {
        var tooltip = Iec10xTypeIdPresentation.Tooltip(ti);
        Assert.Contains($"TI-{ti}", tooltip);
        Assert.DoesNotContain("CP56", tooltip);
        Assert.Contains(symbol, tooltip);
    }

    [Fact]
    public void MismatchedTypesShowTwoShortHumanReadableDescriptions()
    {
        var text = Iec10xTypeIdPresentation.Compare(11, 13);
        Assert.Contains("Signal List: TI-11 — Scaled measurement", text);
        Assert.Contains("Received: TI-13 — Floating-point measurement", text);
        Assert.DoesNotContain("conversion", text);
    }

    [Fact]
    public void Iec103TypeIdNeverBorrowsIec101PointSemantics()
    {
        Assert.Equal("TI-31 — IEC-103 ASDU",
            Iec10xTypeIdPresentation.Tooltip(31, "103"));
        Assert.Contains("Double point with timestamp",
            Iec10xTypeIdPresentation.Tooltip(31, "101"));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(256)]
    [InlineData(-1)]
    public void UnknownAndOutOfRangeTypeIdsStayUnclassified(int typeId)
    {
        Assert.Contains("Unknown", Iec10xTypeIdPresentation.Tooltip(typeId));
    }

    [Fact]
    public void NewlySupportedTypeUsesCoreCanonicalDescription()
    {
        Assert.Contains("M_BO_TB_1",
            ARIEC60870.Master.Protocol.Iec10x.Iec10xAsduDecoder.TypeName(33));
        Assert.Contains("End of initialization", Iec10xTypeIdPresentation.Name(70));
        Assert.Equal("TI-33 — IEC-103 ASDU", Iec10xTypeIdPresentation.Tooltip(33, "103"));
    }

    [Fact]
    public void UnknownTypeIdIsNeverGuessed()
        => Assert.Contains("Unknown", Iec10xTypeIdPresentation.Tooltip(200));

    [Fact]
    public void TypeBadgeExplanationDoesNotAssertAnyValueOrValidity()
        => Assert.Equal("STP — Step Position",
            IecSignalTypeBadge.FromDecoded("101", 32)?.Explanation);
}
