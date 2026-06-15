// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using Xunit;

namespace ARIEC60870.Core.Tests;

public sealed class Ft12ParserRegressionTests
{
    [Theory]
    [InlineData(0xE5, "ACK")]
    [InlineData(0xA2, "NACK")]
    public void SingleCharacterResponsesStayVisible(byte value, string expectedName)
    {
        var frame = new Ft12Parser().Decode(new[] { value });

        Assert.Equal(Ft12FrameFormat.SingleCharacter, frame.Format);
        Assert.True(frame.IsLengthValid);
        Assert.True(frame.IsChecksumValid);
        Assert.Equal(expectedName, frame.SingleCharacterName);
    }

    [Fact]
    public void FixedSecondaryNoDataDecodesLinkStatusBits()
    {
        var raw = Fixed(control: 0x09, linkAddress: 1);
        var frame = new Ft12Parser().Decode(raw);

        Assert.Equal(Ft12FrameFormat.FixedLength, frame.Format);
        Assert.True(frame.IsChecksumValid);
        Assert.False(frame.LinkControl!.Prm);
        Assert.Equal(9, frame.LinkControl.FunctionCode);
        Assert.True(frame.LinkControl.IsSecondaryNoData);
        Assert.Equal("PRM=0, ACD=0, DFC=0, FC=9", frame.LinkControl.BitSummary);
    }

    [Fact]
    public void FixedFrameChecksumMismatchIsReportedAsEvidence()
    {
        var raw = Fixed(control: 0x09, linkAddress: 1);
        raw[^2] ^= 0x55;

        var frame = new Ft12Parser().Decode(raw);

        Assert.Equal(Ft12FrameFormat.FixedLength, frame.Format);
        Assert.False(frame.IsChecksumValid);
        Assert.Contains(frame.Issues, issue => issue.Contains("Checksum mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VariableFrameDecodesIec103GeneralInterrogationEnd()
    {
        var asdu = new byte[] { 0x08, 0x01, 0x0A, 0x01, 0x00, 0x00, 0x00 };
        var raw = Variable(control: 0x08, linkAddress: 1, asdu);

        var frame = new Ft12Parser().Decode(raw);

        Assert.Equal(Ft12FrameFormat.VariableLength, frame.Format);
        Assert.True(frame.IsChecksumValid);
        Assert.True(frame.LinkControl!.IsSecondaryUserData);
        Assert.NotNull(frame.Asdu);
        Assert.Equal(8, frame.Asdu!.TypeId);
        Assert.Equal(10, frame.Asdu.CauseOfTransmission);
        Assert.Equal("General Interrogation End", frame.Asdu.TypeName);
    }

    [Fact]
    public void TwoOctetLinkAddressIsDecodedLittleEndianForIec101StyleFt12()
    {
        var raw = Fixed(control: 0x7B, linkAddress: 0x1234, linkAddressSize: 2);
        var frame = new Ft12Parser(linkAddressSize: 2).Decode(raw);

        Assert.Equal(Ft12FrameFormat.FixedLength, frame.Format);
        Assert.True(frame.IsChecksumValid);
        Assert.Equal(0x1234, frame.LinkAddress);
        Assert.True(frame.LinkControl!.IsPrimaryRequestClass2);
    }

    [Fact]
    public void MalformedStartByteDoesNotThrowAndKeepsRawEvidence()
    {
        var raw = new byte[] { 0x99, 0x01, 0x02 };
        var frame = new Ft12Parser().Decode(raw);

        Assert.Equal(Ft12FrameFormat.Malformed, frame.Format);
        Assert.Equal(raw, frame.RawBytes);
        Assert.Contains(frame.Issues, issue => issue.Contains("Unsupported start byte", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] Fixed(byte control, int linkAddress, int linkAddressSize = 1)
    {
        var frame = new byte[3 + linkAddressSize + 1];
        frame[0] = 0x10;
        frame[1] = control;
        WriteLe(frame, 2, linkAddress, linkAddressSize);
        var sum = control;
        for (var i = 0; i < linkAddressSize; i++) sum += frame[2 + i];
        frame[2 + linkAddressSize] = (byte)(sum & 0xFF);
        frame[3 + linkAddressSize] = 0x16;
        return frame;
    }

    private static byte[] Variable(byte control, int linkAddress, IReadOnlyList<byte> asdu, int linkAddressSize = 1)
    {
        var length = checked((byte)(1 + linkAddressSize + asdu.Count));
        var frame = new byte[4 + length + 2];
        frame[0] = 0x68;
        frame[1] = length;
        frame[2] = length;
        frame[3] = 0x68;
        frame[4] = control;
        WriteLe(frame, 5, linkAddress, linkAddressSize);
        for (var i = 0; i < asdu.Count; i++) frame[5 + linkAddressSize + i] = asdu[i];
        var sum = 0;
        for (var i = 4; i < 4 + length; i++) sum += frame[i];
        frame[4 + length] = (byte)(sum & 0xFF);
        frame[5 + length] = 0x16;
        return frame;
    }

    private static void WriteLe(byte[] buffer, int offset, int value, int count)
    {
        for (var i = 0; i < count; i++) buffer[offset + i] = (byte)((value >> (8 * i)) & 0xFF);
    }
}
