// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class ArchivedEvidenceRowTests
{
    [Fact]
    public void NewCaptureRehydratesExactFrameCotQualityAndObjectSpan()
    {
        var asdu = new byte[] { 1, 1, 3, 0, 1, 0, 7, 0, 0, 0x81 };
        var raw = SerialVariable(0x08, 0x69, asdu);
        var row = new EvidenceRow(new CaptureFrameSnapshot
        {
            Sequence = 7, ProtocolMode = "101", Direction = "RX",
            CaptureArrivalUtc = new DateTime(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc),
            PhysicalSource = "Link A", PhysicalStreamId = 1, RawStreamOffset = 149, SelectedObjectIndex = 1,
            RawHex = Convert.ToHexString(raw),
            DecoderSchema = "IEC10X-OBJECT-V1",
            LinkWidth = 1, CotWidth = 2, CaWidth = 2, IoaWidth = 3,
            Quality = "Invalid", Cot = "spontaneous", CotCode = "3"
        });

        Assert.Equal("0x01", row.Vsq);
        Assert.Equal("Link A", row.Source.PhysicalSource);
        Assert.Equal(149, row.Source.RawStreamOffset);
        Assert.Equal(2026, row.Source.TimestampUtc.Year);
        Assert.Equal("1", row.ObjectCount);
        Assert.Equal("3", row.CotCode);
        Assert.NotNull(row.Source.AsduDetails);
        Assert.True(row.Source.QualityDetails!.Invalid);
        Assert.Equal(7, row.Source.AsduDetails!.FirstObject!.InformationObjectAddress);
        Assert.Equal(9, row.Source.AsduDetails.FirstObject.InformationElementOffset);
        var fields = Iec101FrameByteInspector.Explain(row.Source);
        Assert.Contains(fields, f => f.Key == "cot" && f.Hex == "03 00");
    }

    [Fact]
    public void LegacyCaptureRetainsRawWithoutInventingHeaderWidths()
    {
        var row = new EvidenceRow(new CaptureFrameSnapshot
        {
            ProtocolMode = "101", Direction = "RX",
            RawHex = "10 29 69 92 16", Quality = "-",
            CotCode = "-", ProtocolTraceTitle = "Legacy frame"
        });
        Assert.Null(row.Source.AsduDetails);
        Assert.Equal(DateTime.UnixEpoch, row.Source.TimestampUtc);
        Assert.Equal("-", row.Vsq);
        Assert.Equal("-", row.ObjectCount);
        Assert.Equal("10 29 69 92 16", row.RawHex);
    }

    [Fact]
    public void InvalidChecksumNeverAcquiresTypedQuality()
    {
        var asdu = new byte[] { 1, 1, 3, 0, 1, 0, 7, 0, 0, 0x81 };
        var raw = SerialVariable(0x08, 0x69, asdu);
        raw[^2] ^= 1;
        var row = new EvidenceRow(new CaptureFrameSnapshot
        {
            ProtocolMode = "101", Direction = "RX", RawHex = Convert.ToHexString(raw),
            DecoderSchema = "IEC10X-OBJECT-V1", LinkWidth = 1, CotWidth = 2,
            CaWidth = 2, IoaWidth = 3, Quality = "Good"
        });
        Assert.Null(row.Source.AsduDetails);
        Assert.Null(row.Source.QualityDetails);
        Assert.Equal("Invalid frame", row.Quality);
    }

    [Fact]
    public void NewIec103ArchiveReplaysTrueLinkFrameAndFunInfWithoutSynthesizingIoa()
    {
        var asdu = new byte[] { 1, 1, 3, 1, 7, 2, 0x81 };
        var raw = SerialVariable(0x08, 0x69, asdu);
        var restored = new EvidenceRow(new CaptureFrameSnapshot
        {
            Sequence = 91, ProtocolMode = "103", Direction = "RX",
            RawHex = Convert.ToHexString(raw), DecoderSchema = "IEC103-FT12-V1",
            LinkWidth = 1, CaptureArrivalUtc = new DateTime(2026, 10, 31, 16, 0, 0, DateTimeKind.Utc),
            PhysicalSource = "Relay 103", PhysicalStreamId = 1, RawStreamOffset = 209
        });

        Assert.NotNull(restored.Source.Frame);
        Assert.True(restored.Source.Frame.IsChecksumValid);
        Assert.Equal(0x69, restored.Source.LinkAddress);
        Assert.Equal(7, restored.Source.FunctionType);
        Assert.Equal(2, restored.Source.InformationNumber);
        Assert.Null(restored.Source.AsduDetails);
        Assert.Null(restored.TypeBadge); // IEC-103 is NOT IEC-101 Type 1 SPS.
        var fields = IecForensicByteInspector.Explain(restored.Source);
        Assert.Equal("07", fields.Single(x => x.Key == "fun").Hex);
        Assert.Equal("02", fields.Single(x => x.Key == "inf").Hex);
        Assert.Equal(209, restored.Source.RawStreamOffset);
    }

    [Fact]
    public void LegacyIec103CaptureWithoutExplicitProfileDoesNotInventLinkOrFunInf()
    {
        var asdu = new byte[] { 1, 1, 3, 1, 7, 2, 0x81 };
        var restored = new EvidenceRow(new CaptureFrameSnapshot
        {
            ProtocolMode = "103", RawHex = Convert.ToHexString(SerialVariable(0x08, 0x69, asdu)),
            LinkWidth = 1 // Historical capture: no explicit IEC103 decoder schema
        });
        Assert.Null(restored.Source.Frame);
        Assert.Empty(IecForensicByteInspector.Explain(restored.Source));
    }

    private static byte[] SerialVariable(byte control, byte address, byte[] asdu)
    {
        var payload = new byte[] { control, address }.Concat(asdu).ToArray();
        return new byte[] { 0x68, (byte)payload.Length, (byte)payload.Length, 0x68 }
            .Concat(payload).Concat(new byte[] { (byte)(payload.Sum(x => x) & 0xFF), 0x16 })
            .ToArray();
    }
}
