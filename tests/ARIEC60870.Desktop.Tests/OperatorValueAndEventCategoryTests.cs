// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class OperatorValueAndEventCategoryTests
{
    [Theory]
    [InlineData("101", "13", "Float=178", "178")]
    [InlineData("104", "36", "Float=-217.75", "-217.75")]
    [InlineData("101", "13", "Float=1.25E-3", "1.25E-3")]
    [InlineData("101", "13", "Float=150.3, QDS=0x00", "150.3")]
    [InlineData("101", "13", "Float=NaN", "Float=NaN")]
    [InlineData("101", "13", "Float=corrupt", "Float=corrupt")]
    [InlineData("103", "13", "Float=178", "Float=178")]
    [InlineData("101", "1", "SP=ON", "SP=ON")]
    [InlineData("101", "11", "Scaled=178", "Scaled=178")]
    public void GridLabelDoesNotRepeatTypedFloatBadge(
        string protocol, string typeId, string value, string expected) =>
        Assert.Equal(expected, IecOperatorValueFormatter.ForGrid(protocol, typeId, value));

    [Theory]
    [InlineData("101", "5", "ST=7", "7")]
    [InlineData("104", "32", "ST=-64", "-64")]
    [InlineData("101", "5", "ST=64", "ST=64")]
    [InlineData("101", "32", "ST=7, TRANSIENT=1", "ST=7, TRANSIENT=1")]
    [InlineData("101", "5", "ST=invalid", "ST=invalid")]
    [InlineData("103", "5", "ST=7", "ST=7")]
    public void StepPositionPresentationIsLosslessOutsideBareValidNumber(
        string protocol, string typeId, string raw, string expected)
        => Assert.Equal(expected, IecOperatorValueFormatter.ForGrid(protocol, typeId, raw));

    [Fact]
    public void IncludeAndExcludeAreIndependentAndExclusionWins()
    {
        var include = new HashSet<string> { "Digital", "Measurement", "GI" };
        var exclude = new HashSet<string> { "Measurement" };
        Assert.True(EventCategoryFilter.Allows("Digital", include, exclude));
        Assert.False(EventCategoryFilter.Allows("Measurement", include, exclude));
        Assert.False(EventCategoryFilter.Allows("Commands", include, exclude));
        Assert.True(EventCategoryFilter.Allows("GI", include, exclude));
        include.Clear();
        Assert.False(EventCategoryFilter.Allows("GI", include, exclude));
    }
}
