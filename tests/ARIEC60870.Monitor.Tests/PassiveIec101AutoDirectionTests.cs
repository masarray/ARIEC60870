// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class PassiveIec101AutoDirectionTests
{
    private static readonly DateTime At = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

    private static PassiveObservedFrame Fixed(byte stream, byte control, int width = 1)
    {
        var body = width == 2
            ? new byte[] { control, 105, 0 }
            : new byte[] { control, 105 };
        var checksum = (byte)(body.Sum(x => x) & 255);
        return new PassiveObservedFrame(stream, 10, At, "Fixed", true,
            [0x10, .. body, checksum, 0x16]);
    }

    private static PassiveObservedFrame Variable(byte stream, byte control, byte[] asdu, int width = 1)
    {
        byte[] body = width == 2
            ? [control, 105, 0, .. asdu]
            : [control, 105, .. asdu];
        byte l = (byte)body.Length;
        return new PassiveObservedFrame(stream, 11, At, "Variable", true,
            [0x68, l, l, 0x68, .. body, (byte)(body.Sum(x => x) & 255), 0x16]);
    }

    [Fact]
    public void ReversedTwoComPortsAreDetectedFromValidatedPrimaryAndSecondary()
    {
        var roles = new PassiveIec101PortRoles();
        var request = Fixed(1, 0x5B); // Master on the port labeled RTU
        var response = Variable(0, 0x08, [1, 1, 20, 0, 105, 0, 101, 0, 0, 1]);
        Assert.Equal(PassiveWireRole.Primary, roles.Observe(request, 1));
        Assert.Contains("partial capture", roles.DescribeAll());
        Assert.Equal(PassiveWireRole.Secondary, roles.Observe(response, 1));
        Assert.Contains("A COM roles auto-reversed", roles.DescribeAll());
        Assert.Equal(PassiveWireRole.Secondary, roles.KnownRole(0));
        Assert.Equal(PassiveWireRole.Primary, roles.KnownRole(1));
        Assert.Equal(PassiveWireRole.Secondary, PassiveIec101FrameRoles.Classify(response, 1));
        roles.Reset();
        Assert.Equal(PassiveWireRole.Unknown, roles.KnownRole(0));
    }

    [Fact]
    public void FourComCircuitsCanHaveIndependentOrientations()
    {
        var roles = new PassiveIec101PortRoles();
        roles.Observe(Fixed(0, 0x49), 1);
        roles.Observe(Fixed(1, 0x08), 1);
        roles.Observe(Fixed(3, 0x49), 1);
        roles.Observe(Fixed(2, 0x08), 1);
        Assert.Contains("A COM roles confirmed", roles.DescribeAll());
        Assert.Contains("B COM roles auto-reversed", roles.DescribeAll());
    }

    [Theory]
    [InlineData(0x49, PassiveWireRole.Primary)]
    [InlineData(0x08, PassiveWireRole.Secondary)]
    public void OneAndTwoOctetLinkAddressUsePrmBit(byte control, PassiveWireRole expected)
    {
        Assert.Equal(expected, PassiveIec101FrameRoles.Classify(Fixed(0, control, 1), 1));
        Assert.Equal(expected, PassiveIec101FrameRoles.Classify(Fixed(1, control, 2), 2));
    }

    [Fact]
    public void PartialNoiseBadChecksumAndA2CannotInventSender()
    {
        var primary = Fixed(1, 0x49);
        Assert.Equal(PassiveWireRole.Unknown,
            PassiveIec101FrameRoles.Classify(primary with { ChecksumValid = false }, 1));
        Assert.Equal(PassiveWireRole.Unknown,
            PassiveIec101FrameRoles.Classify(primary with { RawBytes = [0x10, 0x49, 0x69, 0, 0x16] }, 1));
        Assert.Equal(PassiveWireRole.Unknown,
            PassiveIec101FrameRoles.Classify(primary with { Format = "Noise" }, 1));
        Assert.Equal(PassiveWireRole.Secondary,
            PassiveIec101FrameRoles.Classify(new PassiveObservedFrame(0, 0, At, "Single", true, [0xE5]), 1));
        Assert.Equal(PassiveWireRole.Unknown,
            PassiveIec101FrameRoles.Classify(new PassiveObservedFrame(0, 0, At, "Single", true, [0xA2]), 1));
    }
}
