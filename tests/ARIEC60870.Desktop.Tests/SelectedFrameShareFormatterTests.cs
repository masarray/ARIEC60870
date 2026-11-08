// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class SelectedFrameShareFormatterTests
{
    [Fact]
    public void SelectionCreatesReadableExcerptWithOriginalBytesAndSource()
    {
        var cause = new Iec10xCauseDetails(0x43, 3, true, false, 0);
        var eventRow = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 27,
            TimestampUtc = DateTime.UtcNow,
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            RawHex = "10 29 69 92 16",
            PhysicalSource = "Link B",
            PhysicalStreamId = 3,
            RawStreamOffset = 123456,
            CauseOfTransmission = 3,
            CauseDetails = cause,
            QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Qds, 0x81),
            TypeId = 9,
            CommonAddressNumber = 10,
            InformationObjectAddress = 88,
            Summary = "Synthetic status response",
            SignalDisplayValue = "32767",
            RelayTimestampText = "11:12:13"
        });
        var result = SelectedFrameShareFormatter.Format(new[] { eventRow });
        Assert.Contains("Selected Protocol Frames", result);
        Assert.Contains("EXCERPT", result);
        Assert.Contains("Link B", result);
        Assert.Contains("123456", result);
        Assert.Contains("COT bits", result);
        Assert.Contains("P/N=1", result);
        Assert.Contains("OV=1", result);
        Assert.Contains("IV=1", result);
        Assert.Contains("10 29 69 92 16", result);
        Assert.Contains("not calibrated", result);
    }

    [Fact]
    public void MultipleSelectedFramesCopyAsOneOrderedPasteReadyText()
    {
        EvidenceRow Row(long number, string hex) => new(new Iec103MasterEvidenceEvent
        {
            SequenceNumber = number,
            TimestampUtc = DateTime.UtcNow,
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            RawHex = hex,
            Summary = "Observed IEC-101 frame"
        });
        var result = SelectedFrameShareFormatter.Format(new[]
        {
            Row(30, "10 29 69 92 16"),
            Row(12, "E5")
        });
        Assert.Contains("Selected frames: 2", result);
        Assert.Contains("## Frame 12", result);
        Assert.Contains("## Frame 30", result);
        Assert.Contains("E5", result);
        Assert.Contains("10 29 69 92 16", result);
        Assert.True(result.IndexOf("## Frame 12", StringComparison.Ordinal) <
            result.IndexOf("## Frame 30", StringComparison.Ordinal));
    }


    [Fact]
    public void ClipboardMatchesTraceWithoutForensicReportNoise()
    {
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 11,
            TimestampUtc = new DateTime(2026, 10, 30, 0, 35, 57, DateTimeKind.Utc),
            Direction = FrameDirection.MasterToSlave,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            RawHex = "10 5A 69 C3 16",
            Summary = "Class 1 request",
            DataClass = "Class 1"
        });
        var copy = SelectedFrameShareFormatter.FormatForClipboard(new[] { row });
        Assert.Contains("TX", copy);
        Assert.Contains("2026-10-30", copy);
        Assert.Contains("Hex Frame: 10 5A 69 C3 16", copy);
        Assert.DoesNotContain("COT bits:", copy);
        Assert.DoesNotContain("Selected Protocol Frames", copy);
        Assert.DoesNotContain("not calibrated", copy);
    }

    [Fact]
    public void CompactClipboardKeepsMultipleFramesChronological()
    {
        EvidenceRow Row(long seq, string hex) => new(new Iec103MasterEvidenceEvent
        {
            SequenceNumber = seq,
            TimestampUtc = new DateTime(2026, 10, 30, 0, 35, 57, DateTimeKind.Utc),
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            RawHex = hex, Summary = "Response"
        });
        var copy = SelectedFrameShareFormatter.FormatForClipboard(new[] { Row(9,"E5"), Row(2,"10 29 69 92 16") });
        Assert.True(copy.IndexOf("10 29 69 92 16", StringComparison.Ordinal) <
            copy.IndexOf("E5", StringComparison.Ordinal));
    }
    [Fact]
    public void EmptySelectionIsExplicitAndCannotInventFrames()
    {
        var result = SelectedFrameShareFormatter.Format(Array.Empty<EvidenceRow>());
        Assert.Contains("Selected frames: 0", result);
        Assert.DoesNotContain("## Frame", result);
    }
    [Fact]
    public void ClipboardMatchesVisibleHexAndActiveFlagsWithPhysicalProvenance()
    {
        var raw = Convert.FromHexString(
            "681111682869000D0114000100AE0F0C9A994941003A16");
        raw[^3] = 0xF1; // OV, BL, SB, NT, IV physically present in QDS.
        raw[^2] = (byte)(raw.Skip(4).Take(raw.Length - 6).Sum(b => b) & 255);
        var decode = new Iec10xAsduDecoder(2, 2, 3).Decode(
            raw.Skip(7).Take(raw.Length - 9).ToArray());
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 1042,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            TimestampUtc = new DateTime(2026, 11, 8, 1, 2, 3, DateTimeKind.Utc),
            PhysicalSource = "Link B",
            PhysicalStreamId = 3,
            RawStreamOffset = 128766,
            RawHex = Convert.ToHexString(raw),
            Frame = new Ft12Parser(2).Decode(raw),
            AsduDetails = decode,
            LinkAddress = 105
        });
        var copy = SelectedFrameShareFormatter.FormatForClipboard(new[] { row });
        Assert.Contains("[1042]  Host ", copy);
        Assert.Contains("Link B | stream 3 | byte offset 128766", copy);
        Assert.Contains(row.ProtocolTraceRaw, copy);
        Assert.Contains(row.TraceForensic.LinkLayer, copy);
        Assert.Contains(row.TraceForensic.AsduHeader, copy);
        Assert.Contains(row.TraceForensic.InformationObjects, copy);
        Assert.Contains(row.TraceForensic.Validation, copy);
        Assert.Contains("ACTIVE FLAGS: " + row.TraceForensic.ActiveFlags, copy);
        Assert.Contains("#1 OV=1", copy);
        Assert.Contains("#1 BL=1", copy);
        Assert.Contains("#1 SB=1", copy);
        Assert.Contains("#1 NT=1", copy);
        Assert.Contains("#1 IV=1", copy);
        Assert.DoesNotContain("Good |", copy);
    }

}
