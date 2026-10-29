// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class EventReadableValueTests
{
    [Theory]
    [InlineData(1, "SP=ON", "Single Point ON")]
    [InlineData(30, "SP=OFF", "Single Point OFF")]
    [InlineData(3, "DP=ON", "Double Point ON")]
    [InlineData(31, "DP=OFF", "Double Point OFF")]
    [InlineData(3, "DP=INTERMEDIATE", "Double Point INTERMEDIATE")]
    [InlineData(3, "DP=UNKNOWN", "Double Point INDETERMINATE")]
    [InlineData(13, "12.438", "12.438")]
    [InlineData(3, "DP=invalid decoder", "DP=invalid decoder")]
    public void ExplicitSourceTypesBecomeFriendlyEventValuesWithoutGuessing(int typeId, string raw, string expected)
        => Assert.Equal(expected, RelayEventRow.FormatDisplayValue(typeId, null, raw));

    [Fact]
    public void ExportEvidenceRetainsOriginalBeforeAfterFields()
    {
        var item = new Iec103RelayEventLogEntry
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            TypeId = 3,
            SignalType = "Double Point",
            PreviousValue = "DP=OFF",
            NewValue = "DP=ON"
        };
        var row = new RelayEventRow(item);
        Assert.Equal("DP=OFF", row.Previous);
        Assert.Equal("DP=ON", row.NewValue);
        Assert.Equal("Double Point ON", row.DisplayValue);
    }
}
