// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class DigitalStateBadgeTests
{
    [Theory]
    [InlineData("101", "1", "SP=ON", "SP", "ON")]
    [InlineData("104", "30", "Single Point OFF", "SP", "OFF")]
    [InlineData("101", "3", "DP=ON", "DP", "ON")]
    [InlineData("104", "31", "Double Point OFF", "DP", "OFF")]
    [InlineData("101", "3", "1", "DP", "OFF")]
    [InlineData("101", "3", "2", "DP", "ON")]
    [InlineData("101", "3", "0", "DP", "INTERMEDIATE")]
    [InlineData("101", "3", "3", "DP", "INDETERMINATE")]
    [InlineData("101", "1", "0", "SP", "OFF")]
    [InlineData("101", "1", "1", "SP", "ON")]
    public void BadgeMatchesTypedDigitalEvidence(string protocol, string ti, string raw, string kind, string state)
    {
        Assert.Equal(new DigitalStateBadge(kind, state),
            DigitalStateBadge.FromDecoded(protocol, ti, raw));
    }

    [Theory]
    [InlineData("103", "1", "SP=ON")]
    [InlineData("101", "13", "32767")]
    [InlineData("104", "45", "ON")]
    [InlineData("101", "1", "May be ON")]
    [InlineData("101", "1", "DP=ON")]
    [InlineData("101", "3", "SP=ON")]
    [InlineData("101", "3", "Command accepted")]
    [InlineData("101", "1", "SP=ON INVALID")]
    [InlineData("104", "1", "")]
    public void UnknownOrNonProcessPayloadNeverInventsState(string protocol, string ti, string raw) =>
        Assert.Null(DigitalStateBadge.FromDecoded(protocol, ti, raw));
}
