// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class CaptureContractRegressionTests
{
    [Fact]
    public void CaptureFrameSnapshotRoundTripsThroughJsonAndEvidenceRow()
    {
        var snapshot = new CaptureFrameSnapshot
        {
            Sequence = 42,
            Time = "12:34:56.789",
            Direction = "RX",
            ProtocolName = "IEC-104",
            ProtocolMode = "IEC-104",
            State = "OfflineCapture",
            Category = "Capture",
            DataClass = "Monitor",
            Service = "I-format APDU",
            Address = "CA=1, IOA=100",
            SignalOrAddress = "Feeder A CB",
            Value = "ON",
            Quality = "Good",
            AsduType = "M_SP_NA_1 single-point",
            TypeId = "1",
            Cot = "spontaneous",
            CotCode = "3",
            CommonAddress = "1",
            Ioa = "100",
            Meaning = "Single-point event decoded from capture",
            Detail = "Synthetic capture round-trip",
            RawHex = "68 0B 00 00 00 00 01 01 03 00 01 00 64 00 00 01",
            ProtocolTraceTitle = "RX I-format APDU | CA=1, IOA=100",
            ProtocolTraceMeaning = "IEC-104 single-point monitor value",
            ProtocolTraceRaw = "RAW 68 0B 00 00 00 00 01 01 03 00 01 00 64 00 00 01",
            ProtocolTraceMeta = "#42  12:34:56.789  IEC-104"
        };

        var json = JsonSerializer.Serialize(snapshot);
        var restored = JsonSerializer.Deserialize<CaptureFrameSnapshot>(json);

        Assert.NotNull(restored);
        var row = new EvidenceRow(restored!);

        Assert.Equal(42, row.Sequence);
        Assert.Equal("RX", row.Direction);
        Assert.Equal("IEC-104", row.ProtocolMode);
        Assert.Equal("I-format APDU", row.ProtocolService);
        Assert.Equal("CA=1, IOA=100", row.ProtocolAddress);
        Assert.Contains("single-point", row.ProtocolTraceMeaning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("68 0B", row.ProtocolTraceRaw, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureFrameSnapshotFallbacksKeepIncompleteCapturesReviewable()
    {
        var row = new EvidenceRow(new CaptureFrameSnapshot
        {
            Sequence = 7,
            ProtocolMode = "103",
            RawHex = "10 09 01 0A 16"
        });

        Assert.Equal(7, row.Sequence);
        Assert.Equal("STATE", row.Direction);
        Assert.Equal("103", row.ProtocolMode);
        Assert.Equal("ARIEC capture", row.ProtocolName);
        Assert.Equal("Offline capture frame", row.Summary);
        Assert.Equal("RAW 10 09 01 0A 16", row.ProtocolTraceRaw);
        Assert.Equal("offline-capture", row.PollingReason);
    }
}
