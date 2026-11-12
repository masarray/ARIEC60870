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

    private static Iec101DualLinkRedundancyOptions ProfileOptions() => new()
    {
        BaseSettings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            LinkAddressSize = 1,
            CauseOfTransmissionSize = 2,
            CommonAddressSize = 2,
            InformationObjectAddressSize = 3,
            TransmissionMode = "Unbalanced",
            LinkAddress = 105,
            CommonAddress = 1,
            UseSimulatedSlave = true
        }
    };

    [Fact]
    public void GenericIec101PreservesExistingPostSwitchGiButNucDoesNotMandateIt()
    {
        var options = ProfileOptions();
        var generic = Iec10xInteroperabilityProfile.Generic(Iec60870ProtocolMode.Iec101);
        Assert.Equal("OnFailover", generic.GiAfterSwitch);
        options.ApplyInteroperabilityProfile(generic);
        Assert.Equal(Iec101PostSwitchGiPolicy.Required, options.PostSwitchGiPolicy);

        var historical = Iec10xInteroperabilityProfile.HistoricalNuc101();
        Assert.Equal("Disabled", historical.GiAfterSwitch);
        options.ApplyInteroperabilityProfile(historical);
        Assert.Equal(Iec101PostSwitchGiPolicy.Disabled, options.PostSwitchGiPolicy);
    }

    [Theory]
    [InlineData("Disabled", Iec101PostSwitchGiPolicy.Disabled)]
    [InlineData("Manual", Iec101PostSwitchGiPolicy.ManualOnly)]
    [InlineData("OnFailover", Iec101PostSwitchGiPolicy.Required)]
    [InlineData("ProjectRequired", Iec101PostSwitchGiPolicy.Required)]
    public void ExplicitProjectGiPolicyControlsActualRuntime(string choice, Iec101PostSwitchGiPolicy expected)
    {
        var options = ProfileOptions();
        var profile = Iec10xInteroperabilityProfile.Generic(Iec60870ProtocolMode.Iec101) with
        {
            Id = "project-norwegian-substation", Edition = "project-rev1",
            Convention = "Project-approved IEC101 interoperability", GiAfterSwitch = choice
        };
        options.ApplyInteroperabilityProfile(Iec10xInteroperabilityProfile.ImportProject(profile.Export()));
        Assert.Equal(expected, options.PostSwitchGiPolicy);
    }

    [Fact]
    public void UnsupportedOrMismatchedNucProfileFailsClosedBeforeChangingRuntime()
    {
        var options = ProfileOptions();
        options.ApplyInteroperabilityProfile(Iec10xInteroperabilityProfile.HistoricalNuc101());
        var unchanged = options.PostSwitchGiPolicy;
        var wrongWidth = Iec10xInteroperabilityProfile.HistoricalNuc101() with { LinkWidth = 2 };
        Assert.Throws<InvalidOperationException>(() => options.ApplyInteroperabilityProfile(wrongWidth));
        Assert.Equal(unchanged, options.PostSwitchGiPolicy);

        var balanced = Iec10xInteroperabilityProfile.HistoricalNuc101() with { LinkMode = "Balanced" };
        Assert.Throws<InvalidOperationException>(() => options.ApplyInteroperabilityProfile(balanced));
        Assert.Equal(unchanged, options.PostSwitchGiPolicy);

        Assert.Throws<InvalidOperationException>(() =>
            options.ApplyInteroperabilityProfile(Iec10xInteroperabilityProfile.HistoricalNuc104()));
        Assert.Equal(unchanged, options.PostSwitchGiPolicy);
    }

    [Theory]
    [InlineData(1, 256)]
    [InlineData(1, -1)]
    [InlineData(2, 65536)]
    public void DualLinkAddressWidthRejectsUnrepresentableStandbyStationBeforeTx(int width, int badAddress)
    {
        var options = ProfileOptions();
        options.BaseSettings.LinkAddressSize = width;
        options.LinkB.LinkAddress = badAddress;
        var error = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("link address exceeds", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => options.ApplyInteroperabilityProfile(
            Iec10xInteroperabilityProfile.HistoricalNuc101() with { LinkWidth = width }));
    }

    [Fact]
    public void DualLinkTwoOctetAddressesRemainIndependentAndAreNotTruncated()
    {
        var options = ProfileOptions();
        options.BaseSettings.LinkAddressSize = 2;
        options.LinkA.LinkAddress = 105;
        options.LinkB.LinkAddress = 300;
        options.ApplyInteroperabilityProfile(
            Iec10xInteroperabilityProfile.HistoricalNuc101() with { LinkWidth = 2 });
        Assert.Equal(300, options.LinkB.ApplyTo(options.BaseSettings).LinkAddress);
        Assert.Equal(Iec101PostSwitchGiPolicy.Disabled, options.PostSwitchGiPolicy);
    }
}

