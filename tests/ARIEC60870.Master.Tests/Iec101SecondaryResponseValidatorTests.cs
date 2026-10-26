// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Protocol;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101SecondaryResponseValidatorTests
{
    private static readonly Ft12Parser Parser = new(2);

    [Fact]
    public void SicamEvidencePrimaryLinkStatusCannotProveOutstationHealth()
    {
        // RX evidence from 2026-10-26: 10 49 69 00 B2 16 (PRM=1).
        var received = Convert.FromHexString("10496900B216");
        var check = Iec101SecondaryResponseValidator.Validate(
            Parser.Decode(received), 105, received);

        Assert.False(check.IsValid);
        Assert.False(check.AdvancesFcb);
        Assert.Equal("IEC101-RX-TX-ECHO", check.Code);
    }

    [Fact]
    public void SicamCrossChannelPrimaryLink105CannotProveLink1Healthy()
    {
        var previousTxLink1 = Convert.FromHexString("104901004A16");
        var check = Iec101SecondaryResponseValidator.Validate(
            Parser.Decode(Convert.FromHexString("10496900B216")), 1, previousTxLink1);

        Assert.False(check.IsValid);
        Assert.False(check.AdvancesFcb);
        Assert.Equal("IEC101-RX-FOREIGN-PRIMARY", check.Code);
        Assert.Contains("expected Link=1", check.Explanation);
    }

    [Fact]
    public void PrimaryFromSameAddressButNotMatchingLastTxIsRejected()
    {
        var giTx = Convert.FromHexString("680D0D6853690064010600690000000014A416");
        var check = Iec101SecondaryResponseValidator.Validate(
            Parser.Decode(Convert.FromHexString("10496900B216")), 105, giTx);

        Assert.False(check.IsValid);
        Assert.Equal("IEC101-RX-UNEXPECTED-PRIMARY", check.Code);
    }

    [Fact]
    public void SecondaryWrongLinkIsRejectedEvenWithCorrectChecksum()
    {
        var check = Iec101SecondaryResponseValidator.Validate(
            Parser.Decode(Convert.FromHexString("100B69007416")), 1, Array.Empty<byte>());

        Assert.False(check.IsValid);
        Assert.Equal("IEC101-RX-LINK-ADDRESS-MISMATCH", check.Code);
    }

    [Fact]
    public void ValidSicamSecondaryStatusIsAccepted()
    {
        var frame = Parser.Decode(Convert.FromHexString("100B69007416"));
        var check = Iec101SecondaryResponseValidator.Validate(frame, 105, Array.Empty<byte>());

        Assert.True(check.IsValid);
        Assert.True(check.AdvancesFcb);
        Assert.True(Iec101SecondaryResponseValidator.CanAdvanceFcb(frame));
    }

    [Fact]
    public void CorruptChecksumNeverAdvancesFcb()
    {
        var frame = Parser.Decode(Convert.FromHexString("100B69007516"));
        var check = Iec101SecondaryResponseValidator.Validate(frame, 105, Array.Empty<byte>());

        Assert.False(check.IsValid);
        Assert.Equal("IEC101-RX-INTEGRITY", check.Code);
    }

    [Theory]
    [InlineData("E5", true)]
    [InlineData("A2", false)]
    [InlineData("100169006A16", false)] // Secondary NACK function 1.
    [InlineData("102B69009416", false)] // Secondary ACK with DFC=1 (0x2B).
    public void AckNackAndBusyPreserveFcbSemantics(string hex, bool advances)
    {
        var frame = Parser.Decode(Convert.FromHexString(hex));
        var check = Iec101SecondaryResponseValidator.Validate(frame, 105, Array.Empty<byte>());

        Assert.True(check.IsValid);
        Assert.Equal(advances, check.AdvancesFcb);
        Assert.Equal(advances, Iec101SecondaryResponseValidator.CanAdvanceFcb(frame));
    }
}
