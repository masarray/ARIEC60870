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
            LinkA = new Iec101DualLinkEndpoint { Name = "Link A", PortName = "SIM-A", LinkAddress = 1 },
            LinkB = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "SIM-B", LinkAddress = 1 },
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

    [Fact]
    public async Task FieldWidthLink105StartupGiAppearsInOriginalTxAndGiCasebookBeforeClass2()
    {
        // Regress the user's IEC-101 1200-bps Link/CA 105, 2/2/2/3 setup.
        // In addition to engine counters, pass each original evidence frame
        // through the exact typed GI casebook used by the WPF workspace.
        var settings = Settings();
        settings.LinkAddress = 105;
        settings.LinkAddressSize = 2;
        settings.CommonAddress = 105;
        settings.BaudRate = 1200;
        await using var transport = new SimulatedIec101Transport(settings);
        var result = await new Iec101MasterSession(settings, transport)
            .RunForAsync(TimeSpan.FromMilliseconds(1200), CancellationToken.None);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        var requests = result.Events.Where(e =>
            e.Direction == FrameDirection.MasterToSlave &&
            e.TypeId == 100 && e.CauseOfTransmission == 6).ToArray();
        Assert.Single(requests);
        Assert.Equal(105, requests[0].CommonAddressNumber);
        Assert.Equal(105, requests[0].LinkAddress);
        Assert.False(string.IsNullOrWhiteSpace(requests[0].RawHex));

        var casebook = new Iec10xGiCasebook();
        foreach (var evidence in result.Events.OrderBy(e => e.SequenceNumber))
            casebook.Observe(evidence);
        Assert.Contains(casebook.Snapshot(), c =>
            c.CommonAddress == 105 && c.LinkAddress == 105 &&
            c.Stages.Any(stage => stage.Phase == "GI request"));

        var background = result.Events.FirstOrDefault(e =>
            e.Direction == FrameDirection.MasterToSlave &&
            e.Frame?.LinkControl?.FunctionCode == 11);
        if (background is not null)
            Assert.True(requests[0].SequenceNumber < background.SequenceNumber,
                "First Connect GI must precede normal Class 2 polling.");
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
