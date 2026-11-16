// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>Wire-level GI lifecycle exercised through both actual master schedulers.</summary>
public sealed class Iec101GiRuntimeIntegrationTests
{
    [Fact]
    public async Task SingleSimulatedGiHasMatchingAppActconAndCompletion()
    {
        var settings = Settings();
        await using var transport = new SimulatedIec101Transport(settings);
        var result = await new Iec101MasterSession(settings, transport)
            .RunForAsync(TimeSpan.FromMilliseconds(1000), CancellationToken.None);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.True(result.Counters.GiCommands >= 1);
        Assert.True(result.Counters.GiEndResponses >= 1);
        Assert.Contains(result.Events, e =>
            e.Direction == FrameDirection.SlaveToMaster &&
            e.TypeId == 100 && e.CauseOfTransmission == 7);
        Assert.Contains(result.Events, e =>
            e.Direction == FrameDirection.SlaveToMaster &&
            e.TypeId == 100 && e.CauseOfTransmission == 10);
    }

    [Fact]
    public async Task DualActiveGiCompletesImageOnlyAfterCorrelatedActterm()
    {
        var settings = Settings();
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint
                { Name = "Link A", PortName = "SIM-A", LinkAddress = 1 },
            LinkB = new Iec101DualLinkEndpoint
                { Name = "Link B", PortName = "SIM-B", LinkAddress = 1 },
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(250),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled
        };
        await using var a = new SimulatedIec101Transport(options.LinkA.ApplyTo(settings));
        await using var b = new SimulatedIec101Transport(options.LinkB.ApplyTo(settings));
        var session = new Iec101DualLinkRedundancySession(options, a, b);
        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(1200), CancellationToken.None);
        var image = session.CreateSnapshot();

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.True(result.Counters.GiCommands >= 1);
        Assert.True(result.Counters.GiEndResponses >= 1);
        Assert.Equal(Iec101ApplicationImageState.Ready, image.ApplicationImageState);
        Assert.NotNull(image.LastGiCompletedUtc);
        Assert.Contains(result.Events, e =>
            e.Direction == FrameDirection.SlaveToMaster && e.PhysicalSource == "Link A" &&
            e.TypeId == 100 && e.CauseOfTransmission == 7);
        Assert.Contains(result.Events, e =>
            e.Direction == FrameDirection.SlaveToMaster && e.PhysicalSource == "Link A" &&
            e.TypeId == 100 && e.CauseOfTransmission == 10);
        Assert.DoesNotContain(result.Events, e =>
            e.Direction == FrameDirection.MasterToSlave && e.PhysicalSource == "Link B" &&
            e.TypeId == 100);
    }

    private static Iec103MasterSettings Settings() => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec101,
        UseSimulatedSlave = true,
        LinkAddress = 1,
        LinkAddressSize = 1,
        CommonAddress = 1,
        CommonAddressSize = 2,
        CauseOfTransmissionSize = 2,
        InformationObjectAddressSize = 3,
        Class2PollIntervalMs = 40,
        ResponseTimeoutMs = 150,
        MaxClass1DrainFrames = 12,
        SendGeneralInterrogationOnConnect = true,
        SendClockSyncOnConnect = false,
        ResetRemoteLinkOnConnect = false,
        ResetFcbOnConnect = false
    };
}
