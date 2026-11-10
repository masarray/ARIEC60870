// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class FrameTraceEngineerReadabilityTests
{
    [Fact]
    public void DistinctIoaTimestampsStayAdjacentToTheirOwnSignalAndQuality()
    {
        var t1 = new Iec10xCp56Time2aDetails {
            Year = 2026, Month = 11, Day = 10, Hour = 20, Minute = 34,
            MillisecondsInMinute = 42365
        };
        var t2 = t1 with { MillisecondsInMinute = 43987,
            InvalidFlag = true, SubstitutedFlag = true, SummerTimeFlag = true };
        var asdu = new Iec10xAsduDecode {
            TypeId = 30, ObjectCount = 2, InformationObjectAddressWidth = 3,
            CauseOfTransmission = 3,
            Objects = [
                new Iec10xInformationObject { Index = 1,
                    InformationObjectAddress = 100, ValueText = "SP=ON",
                    QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Siq, 0),
                    Cp56TimeDetails = t1 },
                new Iec10xInformationObject { Index = 2,
                    InformationObjectAddress = 101, ValueText = "SP=OFF",
                    QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Siq, 0x80),
                    Cp56TimeDetails = t2 }
            ]
        };
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            AsduDetails = asdu, Direction = FrameDirection.SlaveToMaster,
            RawHex = "E5"
        });
        var parts = row.TimelineSignalDigest.Split(Environment.NewLine);
        Assert.Equal(4, parts.Length);
        Assert.Contains("IOA 100 [ IOA3:0 · IOA2:0 · IOA1:100 ] · TI-30 · Value: ON · 10-11-2026 20:34:42.365", parts[0]);
        Assert.Contains("TI-30 Single Point Signal · Quality: Good · Timestamp: Valid · Genuine (GEN=0) · DST OFF", parts[1]);
        Assert.Contains("IOA 101 [ IOA3:0 · IOA2:0 · IOA1:101 ] · TI-30 · Value: OFF · 10-11-2026 20:34:43.987", parts[2]);
        Assert.Contains("Quality: Invalid", parts[3]);
        Assert.Contains("Timestamp: Invalid · Substituted (GEN=1) · DST ON", parts[3]);
        Assert.DoesNotContain("42.365", parts[2]);
    }

    [Fact]
    public void UnavailableOrPartialTimeNeverClaimsValidGenuineOrDst()
    {
        var asdu = new Iec10xAsduDecode { TypeId = 2, InformationObjectAddressWidth = 3 };
        var without = new Iec10xInformationObject { InformationObjectAddress = 5, ValueText = "SP=ON" };
        var partial = new Iec10xInformationObject {
            InformationObjectAddress = 6, ValueText = "SP=OFF", TimestampText = "20:34"
        };
        Assert.Contains("Timestamp: No Timestamp", Iec10xSignalTraceFormatter.TraceObject(asdu, without));
        var line = Iec10xSignalTraceFormatter.TraceObject(asdu, partial);
        Assert.Contains("RTU partial time: 20:34", line);
        Assert.Contains("Timestamp: Partial (CP56 validity / GEN / DST unknown)", line);
        Assert.DoesNotContain("Genuine (GEN=0)", line);
    }


    [Theory]
    [InlineData(0x00, "Invalid [00]", "Good")]
    [InlineData(0x01, "OFF [01]", "Good")]
    [InlineData(0x02, "ON [10]", "Good")]
    [InlineData(0x03, "Invalid [11]", "Good")]
    [InlineData(0x82, "ON [10]", "Invalid")]
    public void DoublePointTraceStatesComeFromTypedDiqBitsNotNarrativeText(
        byte rawDiq, string expectedState, string expectedQuality)
    {
        var obj = new Iec10xInformationObject
        {
            Index = 1,
            InformationObjectAddress = 101,
            ValueText = "DP=STALE NARRATIVE",
            QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Diq, rawDiq)
        };
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 31, InformationObjectAddressWidth = 3, Objects = [obj],
            ObjectCount = 1
        };
        var trace = Iec10xSignalTraceFormatter.TraceObject(asdu, obj);
        Assert.Contains("TI-31 · Value: " + expectedState, trace);
        Assert.Contains("Quality: " + expectedQuality, trace);
        Assert.Contains("Timestamp: No Timestamp", trace);
        Assert.DoesNotContain("STALE NARRATIVE", trace);

        // Legacy scan remains decoder-facing; no global command/DP rewrite.
        Assert.Contains("Value: STALE NARRATIVE", Iec10xSignalTraceFormatter.ScanValue(asdu, obj));
    }

    [Fact]
    public void MissingDiqCannotInventDpBitPatternAndSinglePointNeverGetsDpBits()
    {
        var dp = new Iec10xAsduDecode { TypeId = 3, InformationObjectAddressWidth = 3 };
        var unknown = new Iec10xInformationObject
        {
            InformationObjectAddress = 42, ValueText = "DP=INDETERMINATE"
        };
        var withoutWire = Iec10xSignalTraceFormatter.TraceObject(dp, unknown);
        Assert.Contains("Value: INDETERMINATE", withoutWire);
        Assert.DoesNotContain("[00]", withoutWire);
        var single = new Iec10xAsduDecode { TypeId = 30, InformationObjectAddressWidth = 3 };
        var sp = new Iec10xInformationObject
        {
            InformationObjectAddress = 42, ValueText = "SP=ON",
            QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Siq, 0x01)
        };
        Assert.Contains("TI-30 · Value: ON", Iec10xSignalTraceFormatter.TraceObject(single, sp));
        Assert.DoesNotContain("[01]", Iec10xSignalTraceFormatter.TraceObject(single, sp));
    }

    [Fact]
    public void TypedAcdIsOwnAmberCaptionNotAnErrorOrDuplicateDiagnostic()
    {
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            Frame = new Ft12FrameDecode {
                IsChecksumValid = true, IsLengthValid = true,
                LinkControl = new LinkControlInfo { Prm = false, FunctionCode = 9, Acd = true }
            }
        });
        Assert.Equal("Pending", row.TraceVisualClass);
        Assert.Equal("Data Class 1 Pending (ACD): TRUE", row.TimelineAcdPending);
        Assert.DoesNotContain("Class 1 pending (ACD)", row.TimelineDiagnosticAlerts);
        Assert.Contains("Class 1 pending (ACD)", row.TimelineReadableAlerts);
    }

    [Fact]
    public void ClipboardMatchesDisplayedRowFieldsWithoutHiddenFourthSignal()
    {
        var asdu = new Iec10xAsduDecode {
            TypeId = 1, InformationObjectAddressWidth = 3, ObjectCount = 4,
            CauseOfTransmission = 3,
            Objects = Enumerable.Range(1, 4).Select(i => new Iec10xInformationObject
                { Index = i, InformationObjectAddress = 70 + i, ValueText = "SP=ON" }).ToArray()
        };
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            SequenceNumber = 31, TimestampUtc = new DateTime(2026,11,10,20,34,45,DateTimeKind.Utc),
            RawHex = "E5", AsduDetails = asdu
        });
        var copy = FrameTraceRowClipboardFormatter.FormatOne(row);
        Assert.Contains(row.TimelineTitle, copy);
        Assert.Contains(row.TimelineHex, copy);
        Assert.Contains(row.TimelineSignalDigest, copy);
        Assert.Contains(row.TimelineSignalOverflow, copy);
        Assert.DoesNotContain("IOA 74", copy);
        Assert.Equal(copy + Environment.NewLine + Environment.NewLine + copy,
            FrameTraceRowClipboardFormatter.Format([row, row]));
    }
}
