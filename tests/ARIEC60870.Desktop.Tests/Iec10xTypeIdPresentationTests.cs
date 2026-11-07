// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec10xTypeIdPresentationTests
{
    [Theory]
    [InlineData(13, "M_ME_NC_1")]
    [InlineData(30, "M_SP_TB_1")]
    [InlineData(31, "M_DP_TB_1")]
    [InlineData(32, "M_ST_TB_1")]
    [InlineData(36, "M_ME_TF_1")]
    [InlineData(45, "C_SC_NA_1")]
    [InlineData(100, "C_IC_NA_1")]
    public void KnownTypeIdRetainsWireNumberAndStandardIdentifier(int ti, string symbol)
    {
        var tooltip = Iec10xTypeIdPresentation.Tooltip(ti);
        Assert.Contains($"TI{ti}", tooltip);
        Assert.Contains(symbol, tooltip);
    }

    [Fact]
    public void UnknownTypeIdIsNeverGuessed()
        => Assert.Contains("Unknown", Iec10xTypeIdPresentation.Tooltip(200));

    [Fact]
    public void TypeBadgeExplanationDoesNotAssertAnyValueOrValidity()
        => Assert.Equal("STP — Step Position",
            IecSignalTypeBadge.FromDecoded("101", 32)?.Explanation);
}
