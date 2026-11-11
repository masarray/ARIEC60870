// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class PassiveFramePreparationTests
{

    [Fact]
    public void WireFirstUnknownAsduWithGoodFt12ChecksumIsNeverAProcessValue()
    {
        byte[] asdu = [0xFA, 0x01, 0x03, 0x00, 0x69, 0x00, 0x01, 0x00, 0x00, 0x42];
        var body = new byte[] { 0x08, 0x69 }.Concat(asdu).ToArray();
        byte[] wire = [0x68, (byte)body.Length, (byte)body.Length, 0x68,
            .. body, (byte)(body.Sum(x => x) & 255), 0x16];
        var item = new PassiveObservedFrame(1, 905, DateTime.UtcNow,
            "Variable", true, wire);
        var result = Assert.Single(PassiveFramePreparation.PrepareBatch(
            [item], 1, 2, 2, 3));
        Assert.Equal(PassiveWirePresentation.Kind.VendorSpecific, result.Wire.Status);
        Assert.True(result.Wire.TrustedWire);
        Assert.False(result.Wire.TrustedProcess);
        Assert.False(result.Wire.NeedsAttention);
        Assert.Contains("Vendor-specific IEC-101 TI 250", result.Wire.Message);
        Assert.Contains("raw ASDU CA 0x0069", result.Wire.Message);
        Assert.Equal(Convert.ToHexString(wire), result.RawHex);
        Assert.NotNull(result.Decode);
    }

    [Fact]
    public void WireFirstTruncatedKnownTypeHasValidEnvelopeButNoTrustedObject()
    {
        // Legal FT1.2 enclosing a Type 1 with one declared object but no DPI.
        byte[] asdu = [0x01, 0x01, 0x03, 0x00, 0x69, 0x00, 0x65, 0x00, 0x00];
        var body = new byte[] { 0x08, 0x69 }.Concat(asdu).ToArray();
        byte[] wire = [0x68, (byte)body.Length, (byte)body.Length, 0x68,
            .. body, (byte)(body.Sum(x => x) & 255), 0x16];
        var observed = new PassiveObservedFrame(1, 88, DateTime.UtcNow,
            "Variable", true, wire);
        var item = Assert.Single(PassiveFramePreparation.PrepareBatch(
            [observed], 1, 2, 2, 3));
        Assert.Equal(PassiveWirePresentation.Kind.UnknownAsdu, item.Wire.Status);
        Assert.False(item.Wire.TrustedProcess);
        Assert.NotEmpty(item.Decode!.Issues);
        Assert.Equal(Convert.ToHexString(wire), item.RawHex);
    }

    [Fact]
    public void WireFirstAllInvalidAndPartialStatesKeepVerbatimRawWithoutFakeDecode()
    {
        PassiveObservedFrame[] samples =
        [
            new(0, 1, DateTime.UtcNow, "Noise", false, [0x51, 0x52, 0x53]),
            new(0, 4, DateTime.UtcNow, "Partial", false, [0x68, 0x0C]),
            new(0, 6, DateTime.UtcNow, "Malformed", false, [0x10, 0x32, 0x16]),
            new(0, 9, DateTime.UtcNow, "Single", true, [0xA2])
        ];
        var kinds = new[]
        {
            PassiveWirePresentation.Kind.Noise,
            PassiveWirePresentation.Kind.Partial,
            PassiveWirePresentation.Kind.InvalidFrame,
            PassiveWirePresentation.Kind.InvalidFrame
        };
        var items = PassiveFramePreparation.PrepareBatch(samples, 1, 2, 2, 3);
        for (var i = 0; i < samples.Length; i++)
        {
            Assert.Equal(kinds[i], items[i].Wire.Status);
            Assert.False(items[i].Wire.TrustedProcess);
            Assert.False(items[i].Wire.TrustedWire);
            Assert.Equal(Convert.ToHexString(samples[i].RawBytes), items[i].RawHex);
            Assert.Null(items[i].Decode);
        }
    }

    [Fact]
    public void PreparedBatchRetainsSourceOrderingRawHexAndTypedMultiObject()
    {
        // Public synthetic IEC-101 type 1, VSQ=2, SQ=0, CA=105.
        byte[] wire =
        [
            0x68, 0x10, 0x10, 0x68, 0x08, 0x69,
            0x01, 0x02, 0x14, 0x00, 0x69, 0x00,
            0x65, 0x00, 0x00, 0x01, 0x66, 0x00, 0x00, 0x00,
            0xBD, 0x16 // verified checksum (was incorrectly 00 in the old fixture)
        ];
        var observations = Enumerable.Range(0, 800).Select(i =>
            new PassiveObservedFrame((byte)(i % 2), i * 22L,
                DateTime.UtcNow.AddMilliseconds(i), "Variable", true, wire)).ToArray();
        var prepared = PassiveFramePreparation.PrepareBatch(observations, 1, 2, 2, 3);
        Assert.Equal(800, prepared.Count);
        for (var i = 0; i < prepared.Count; i++)
        {
            Assert.Same(observations[i], prepared[i].Frame);
            Assert.Equal(Convert.ToHexString(wire), prepared[i].RawHex);
            Assert.Equal<byte?>((byte)1, prepared[i].Decode?.TypeId);
            Assert.Equal(2, prepared[i].Decode?.Objects.Count);
            Assert.Equal(101, prepared[i].Decode?.Objects[0].InformationObjectAddress);
            Assert.Equal(102, prepared[i].Decode?.Objects[1].InformationObjectAddress);
        }
    }


    [Fact]
    public void FieldSicamClassOneNineteenTimestampedEventsDecodeWithObservedLinkWidth()
    {
        // Faithful layout to field RX: 68 DA DA 68 28 69 00 1F 13 03 00 69 00...
        // One Type 31 ASDU contains 19 DP events, not Type 0 / 31 objects.
        var payload = new List<byte> { 0x28, 0x69, 0x00, 0x1F, 0x13, 0x03, 0x00, 0x69, 0x00 };
        for (var i = 0; i < 19; i++)
        {
            var ms = 0xAB2A + i * 100;
            payload.AddRange([0xF6, 0x03, 0xFF, (byte)(i % 2 == 0 ? 2 : 0),
                (byte)ms, (byte)(ms >> 8), 0x04, 0x11, 0x85, 0x0B, 0x1A]);
        }
        Assert.Equal(218, payload.Count);
        var wire = new byte[] { 0x68, 0xDA, 0xDA, 0x68 }.Concat(payload)
            .Concat(new byte[] { (byte)(payload.Sum(x => x) & 0xFF), 0x16 }).ToArray();
        Assert.Equal(224, wire.Length);
        Assert.Equal(0xB0, wire[^2]); // exact observed field checksum
        var observed = new PassiveObservedFrame(1, 0, DateTime.UtcNow,
            "Variable", true, wire, DetectedLinkAddressWidth: 2);
        var decoded = Assert.Single(PassiveFramePreparation.PrepareBatch(
            [observed], linkAddressWidth: 1, cotWidth: 2, caWidth: 2, ioaWidth: 3)).Decode;
        Assert.NotNull(decoded);
        Assert.Equal(31, decoded.TypeId);
        Assert.Equal(19, decoded.ObjectCount);
        Assert.Equal(19, decoded.Objects.Count);
        Assert.Equal(3, decoded.CauseOfTransmission);
        Assert.Equal(105, decoded.CommonAddress);
        Assert.All(decoded.Objects, o => Assert.Equal(0xFF03F6, o.InformationObjectAddress));
        Assert.Contains("2026-11-05", decoded.Objects[0].TimestampText);
        Assert.Empty(decoded.Issues);
    }
    [Fact]
    public void FalseTrustedFlagCannotPromoteTamperedPayloadToEvent()
    {
        byte[] payload = [0x08, 0x69, 31, 1, 3, 0, 105, 0, 100, 0, 0, 2,
            0x2A, 0xAB, 0x04, 0x11, 0x85, 0x0B, 0x1A];
        var raw = new byte[] { 0x68, (byte)payload.Length, (byte)payload.Length, 0x68 }
            .Concat(payload).Concat([(byte)(payload.Sum(x => x) & 255), (byte)0x16]).ToArray();
        raw[12] ^= 1; // false-positive external checksum flag must not be trusted
        var source = new PassiveObservedFrame(1, 0, DateTime.UtcNow, "Variable", true, raw);
        var batch = Assert.Single(PassiveFramePreparation.PrepareBatch([source], 1, 2, 2, 3));
        Assert.Null(batch.Decode);
        Assert.Empty(batch.AsduRaw);
        Assert.Equal(Convert.ToHexString(raw), batch.RawHex);
    }

    [Fact]
    public void BrokenFramesKeepExactRawWithoutFabricatingDecodedPoints()
    {
        var broken = new PassiveObservedFrame(1, 0, DateTime.UtcNow,
            "Variable", true, [0x68, 0x20, 0x20, 0x68, 0x08, 0x69]);
        var invalid = new PassiveObservedFrame(1, 1, DateTime.UtcNow,
            "Variable", false,
            [0x68, 0x0A, 0x0A, 0x68, 0x08, 0x69, 0x01, 0x01, 0x03, 0x00, 0x69, 0x00, 0x65, 0x00, 0x00, 0x01, 0x00, (byte)0x16]);
        var result = PassiveFramePreparation.PrepareBatch([broken, invalid], 1, 2, 2, 3);
        Assert.Null(result[0].Decode);
        Assert.Empty(result[0].AsduRaw);
        Assert.Equal("682020680869", result[0].RawHex);
        Assert.Null(result[1].Decode);
        Assert.Equal(Convert.ToHexString(invalid.RawBytes), result[1].RawHex);
    }
}
