// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec10xArchivedFrameDecoderTests
{
    [Fact]
    public void SerialFrameReplaysSameCotObjectAndQualityFromOriginalBytes()
    {
        byte[] asdu = { 1, 1, 3, 0, 1, 0, 7, 0, 0, 0x81 };
        var raw = SerialVariable(0x08, 0x69, asdu);
        var decoded = Iec10xArchivedFrameDecoder.Decode(Iec60870ProtocolMode.Iec101, raw, 1, 2, 2, 3);
        Assert.True(decoded.IsIntegrityValid);
        Assert.NotNull(decoded.SerialFrame);
        Assert.Equal(3, decoded.Asdu!.CauseOfTransmission);
        Assert.Equal(1, decoded.Asdu.ObjectCount);
        Assert.Equal(7, decoded.Asdu.FirstObject!.InformationObjectAddress);
        Assert.True(decoded.Asdu.FirstObject.QualityDetails!.Invalid);
        Assert.Equal(9, decoded.Asdu.FirstObject.InformationElementOffset);
    }

    [Fact]
    public void AbsentLegacyProfileNeverGuessesAsduWidths()
    {
        byte[] asdu = { 1, 1, 3, 0, 1, 0, 7, 0, 0, 1 };
        var decoded = Iec10xArchivedFrameDecoder.Decode(Iec60870ProtocolMode.Iec101,
            SerialVariable(0x08, 0x69, asdu), 1, null, null, null);
        Assert.True(decoded.IsIntegrityValid);
        Assert.Null(decoded.Asdu);
    }

    [Fact]
    public void MutatedChecksumNeverYieldsTrustedAsdu()
    {
        byte[] asdu = { 1, 1, 3, 0, 1, 0, 7, 0, 0, 1 };
        var raw = SerialVariable(0x08, 0x69, asdu);
        raw[^2] ^= 1;
        var decoded = Iec10xArchivedFrameDecoder.Decode(Iec60870ProtocolMode.Iec101, raw, 1, 2, 2, 3);
        Assert.False(decoded.IsIntegrityValid);
        Assert.Null(decoded.Asdu);
        Assert.NotNull(decoded.SerialFrame);
    }

    [Fact]
    public void TcpFrameReplaysExactApciAndAsduWithRecordedWidths()
    {
        byte[] asdu = { 46, 1, 7, 0, 1, 0, 123, 0, 0, 0x82 };
        var raw = new byte[] { 0x68, (byte)(4 + asdu.Length), 0, 0, 0, 0 }.Concat(asdu).ToArray();
        var d = Iec10xArchivedFrameDecoder.Decode(Iec60870ProtocolMode.Iec104, raw, null, 2, 2, 3);
        Assert.True(d.IsIntegrityValid);
        Assert.Equal("I", d.TcpFrame!.Format);
        Assert.Equal(7, d.Asdu!.CauseOfTransmission);
        Assert.Equal("SELECT", d.Asdu.CommandDetails!.Operation);
    }

    private static byte[] SerialVariable(byte control, byte linkAddress, byte[] asdu)
    {
        var data = new byte[] { control, linkAddress }.Concat(asdu).ToArray();
        var length = (byte)data.Length;
        var checksum = (byte)(data.Sum(x => x) & 0xFF);
        return new byte[] { 0x68, length, length, 0x68 }
            .Concat(data).Concat(new byte[] { checksum, 0x16 }).ToArray();
    }
}
