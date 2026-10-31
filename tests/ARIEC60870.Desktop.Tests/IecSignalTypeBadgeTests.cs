// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class IecSignalTypeBadgeTests
{
    [Theory]
    [InlineData(1, "SPS", "Digital")]
    [InlineData(2, "SPS", "Digital")]
    [InlineData(30, "SPS", "Digital")]
    [InlineData(3, "DPS", "Digital")]
    [InlineData(31, "DPS", "Digital")]
    [InlineData(5, "STP", "Digital")]
    [InlineData(32, "STP", "Digital")]
    [InlineData(7, "BSI", "Digital")]
    [InlineData(33, "BSI", "Digital")]
    [InlineData(20, "PSI", "Digital")]
    [InlineData(9, "NVA", "Measurement")]
    [InlineData(10, "NVA", "Measurement")]
    [InlineData(21, "NVA", "Measurement")]
    [InlineData(34, "NVA", "Measurement")]
    [InlineData(11, "INT", "Measurement")]
    [InlineData(12, "INT", "Measurement")]
    [InlineData(35, "INT", "Measurement")]
    [InlineData(13, "FLT", "Measurement")]
    [InlineData(14, "FLT", "Measurement")]
    [InlineData(36, "FLT", "Measurement")]
    [InlineData(15, "BCR", "Counter")]
    [InlineData(16, "BCR", "Counter")]
    [InlineData(37, "BCR", "Counter")]
    [InlineData(45, "SPC", "Command")]
    [InlineData(58, "SPC", "Command")]
    [InlineData(46, "DPC", "Command")]
    [InlineData(59, "DPC", "Command")]
    [InlineData(47, "RCS", "Command")]
    [InlineData(60, "RCS", "Command")]
    [InlineData(48, "SEN", "Command")]
    [InlineData(61, "SEN", "Command")]
    [InlineData(49, "SEI", "Command")]
    [InlineData(62, "SEI", "Command")]
    [InlineData(50, "SEF", "Command")]
    [InlineData(63, "SEF", "Command")]
    [InlineData(51, "BSC", "Command")]
    [InlineData(64, "BSC", "Command")]
    [InlineData(100, "GI", "Service")]
    [InlineData(101, "CIG", "Service")]
    [InlineData(102, "RD", "Service")]
    [InlineData(103, "CSY", "Service")]
    [InlineData(104, "TST", "Service")]
    [InlineData(105, "RST", "Service")]
    [InlineData(107, "TST", "Service")]
    public void TypedFamiliesAreDeterministicAcrossIec101104(int typeId, string label, string family)
    {
        foreach (var protocol in new[] { "101", "104" })
        {
            var badge = IecSignalTypeBadge.FromDecoded(protocol, typeId);
            Assert.Equal(new IecSignalTypeBadge(label, family), badge);
            Assert.Same(badge, IecSignalTypeBadge.FromDecoded(protocol, typeId));
        }
    }

    [Theory]
    [InlineData("103", "1")]
    [InlineData("103", "45")]
    [InlineData("101", "200")]
    [InlineData("104", "-")]
    [InlineData("104", "FLT")]
    [InlineData("101", "-1")]
    [InlineData("101", "13 invalid")]
    [InlineData("unknown", "13")]
    public void UnrecognizedOrOtherProtocolNeverInventsType(string protocol, string type)
        => Assert.Null(IecSignalTypeBadge.FromDecoded(protocol, type));

    [Fact]
    public void MeasurementAndCommandChipsDoNotInferPhysicalStateOrChangeRawValue()
    {
        var measurement = new ValueRow(new Iec103ValuePoint
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            TypeId = 13,
            DisplayValue = "32755.125",
        });
        Assert.Equal("FLT", measurement.TypeBadge?.Label);
        Assert.Null(measurement.DiscreteBadge);
        Assert.Equal("32755.125", measurement.OperatorValue);
        Assert.Equal("32755.125", measurement.FullOperatorValue);

        var stage = new RelayEventRow(new Iec103RelayEventLogEntry
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            TypeId = 46,
            NewValue = "ACTCON · accepted",
            TransactionKind = "Command",
        });
        Assert.Equal("DPC", stage.TypeBadge?.Label);
        Assert.Null(stage.DiscreteBadge);
        Assert.Contains("Command", stage.OperatorValue);
    }

    [Fact]
    public void DigitalDpiRemainsIndependentOfVisualCategory()
    {
        Assert.Equal("DPS", IecSignalTypeBadge.FromDecoded("101", 3)?.Label);
        Assert.Equal(new DigitalStateBadge("DP", "OFF"),
            DigitalStateBadge.FromDecoded("101", "3", "1"));
        Assert.Equal(new DigitalStateBadge("DP", "ON"),
            DigitalStateBadge.FromDecoded("101", "3", "2"));
    }

    [Fact]
    public void CasebookTypeConverterUsesSameCanonicalPresentation()
    {
        var converter = new IecSignalTypeBadgeConverter();
        Assert.Equal(new IecSignalTypeBadge("DPC", "Command"),
            converter.Convert(46, typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(converter.Convert(999, typeof(object), null!,
            System.Globalization.CultureInfo.InvariantCulture));
    }
}
