// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
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
    public void EmptySelectionIsExplicitAndCannotInventFrames()
    {
        var result = SelectedFrameShareFormatter.Format(Array.Empty<EvidenceRow>());
        Assert.Contains("Selected frames: 0", result);
        Assert.DoesNotContain("## Frame", result);
    }
}
