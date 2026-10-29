// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class TracePrecisionAndPropertiesTests
{
    [Theory]
    [InlineData("⇧ [18:26:02] Class 1 request", "Class 1 request")]
    [InlineData("⇩ [18:26:02] [Link B] Single Point response", "[Link B] Single Point response")]
    [InlineData("• [18:26:02] Invalid FT1.2 frame", "Invalid FT1.2 frame")]
    public void Iec101HeaderSeparatesTimestampAndThinArrow(string fullTitle, string expectedHeader)
    {
        var row = new EvidenceRow(new CaptureFrameSnapshot
        {
            Sequence = 12, ProtocolMode = "101", ProtocolName = "IEC-101",
            Time = "18:26:02.387", Direction = "RX",
            ProtocolTraceTitle = fullTitle,
            RawHex = "10 29 69 92 16"
        });

        Assert.Equal("18:26:02.387", row.Time);
        Assert.Equal(expectedHeader, row.ProtocolTraceHeader);
        Assert.Equal(fullTitle, row.ProtocolTraceTitle);
    }

    [Fact]
    public void OtherProtocolsDoNotLoseOriginalTitle()
    {
        var row = new EvidenceRow(new CaptureFrameSnapshot
        {
            Sequence = 13, ProtocolMode = "104", ProtocolName = "IEC-104",
            ProtocolTraceTitle = "IEC104 I-frame details",
            Time = "12:34:56.789", RawHex = "68 04 43 00 00 00"
        });
        Assert.Equal("IEC104 I-frame details", row.ProtocolTraceHeader);
        Assert.Equal("12:34:56.789", row.Time);
    }
}
