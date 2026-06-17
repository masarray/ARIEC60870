// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101DualLinkRedundancyOptionsTests
{
    [Fact]
    public void DualLinkDefaultsKeepStandbyFromPollingApplicationQueues()
    {
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = new Iec103MasterSettings
            {
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                UseSimulatedSlave = true,
                LinkAddressSize = 1
            }
        };

        options.Validate();

        Assert.False(options.AllowStandbyClass1Polling);
        Assert.False(options.AllowStandbyClass2Polling);
        Assert.True(options.CommandOnActiveOnly);
        Assert.Equal(Iec101PostSwitchGiPolicy.Required, options.PostSwitchGiPolicy);
        Assert.Equal(Iec101DualLinkFailbackPolicy.ManualOnly, options.FailbackPolicy);
        Assert.True(options.StandbyRecoveryGoodResponseThreshold >= 1);
    }

    [Fact]
    public void DualLinkRejectsSameRealSerialPortForBothLinks()
    {
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = new Iec103MasterSettings
            {
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                UseSimulatedSlave = false,
                LinkAddressSize = 1
            },
            LinkA = new Iec101DualLinkEndpoint { Name = "Link A", PortName = "COM7", LinkAddress = 1 },
            LinkB = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "COM7", LinkAddress = 1 }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("two different serial ports", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndpointApplyToCreatesIndependentIec101LinkSettings()
    {
        var baseSettings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PortName = "COM1",
            LinkAddress = 1,
            LinkAddressSize = 2,
            CommonAddress = 7
        };
        var endpoint = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "COM2", LinkAddress = 22 };

        var result = endpoint.ApplyTo(baseSettings);

        Assert.Equal(Iec60870ProtocolMode.Iec101, result.ProtocolMode);
        Assert.Equal("COM2", result.PortName);
        Assert.Equal(22, result.LinkAddress);
        Assert.Equal(7, result.CommonAddress);
        Assert.Equal(2, result.LinkAddressSize);
    }
}

