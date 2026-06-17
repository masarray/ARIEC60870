// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101DualLinkRedundancySessionTests
{
    [Fact]
    public async Task ManualFailoverPromotesStandbyAndProducesEvidence()
    {
        var settings = CreateFastSimulatedIec101Settings();
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "Link A", PortName = "SIM-A", LinkAddress = 1 },
            LinkB = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "SIM-B", LinkAddress = 1 },
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(100),
            RecoveryBackoff = TimeSpan.FromMilliseconds(100),
            AntiPingPongWindow = TimeSpan.FromSeconds(30),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Required
        };

        var session = new Iec101DualLinkRedundancySession(
            options,
            new SimulatedIec101Transport(options.LinkA.ApplyTo(settings)),
            new SimulatedIec101Transport(options.LinkB.ApplyTo(settings)));

        var runTask = session.RunForAsync(TimeSpan.FromMilliseconds(1500), CancellationToken.None);
        await Task.Delay(250);
        session.QueueManualFailover("unit test manual switchover proof");
        var result = await runTask;
        var snapshot = session.CreateSnapshot();

        Assert.Contains(session.FailoverJournal, x => x.Completed && x.FromLink == "Link A" && x.ToLink == "Link B");
        Assert.Contains(result.Events, x => x.Category == Iec101RedundancyEventKind.ManualFailoverRequested.ToString());
        Assert.Contains(result.Events, x => x.Category == Iec101RedundancyEventKind.FailoverCompleted.ToString() || x.Summary.Contains("failover completed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Link B", snapshot.ActiveLinkName);
        Assert.True(snapshot.ApplicationImageObjectCount > 0);
    }

    [Fact]
    public async Task ManualFailoverBypassesStabilizationGuardButStillRequiresPromotableStandby()
    {
        var settings = CreateFastSimulatedIec101Settings();
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "Link A", PortName = "SIM-A", LinkAddress = 1 },
            LinkB = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "SIM-B", LinkAddress = 1 },
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(100),
            AntiPingPongWindow = TimeSpan.FromMinutes(5),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled
        };

        var session = new Iec101DualLinkRedundancySession(
            options,
            new SimulatedIec101Transport(options.LinkA.ApplyTo(settings)),
            new SimulatedIec101Transport(options.LinkB.ApplyTo(settings)));

        var runTask = session.RunForAsync(TimeSpan.FromMilliseconds(1800), CancellationToken.None);
        await Task.Delay(250);
        session.QueueManualFailover("first manual switchover");
        await Task.Delay(300);
        session.QueueManualFailover("second manual switchover inside stabilization window");
        await runTask;

        Assert.True(session.FailoverJournal.Count(x => x.Completed) >= 2);
        Assert.DoesNotContain(session.FailoverJournal, x => !x.Completed && x.Detail.Contains("anti-ping-pong", StringComparison.OrdinalIgnoreCase));
    }

    private static Iec103MasterSettings CreateFastSimulatedIec101Settings()
        => new()
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            UseSimulatedSlave = true,
            TargetProfile = "IEC-101 dual-link simulated outstation",
            LinkAddress = 1,
            LinkAddressSize = 1,
            CommonAddress = 1,
            CommonAddressSize = 2,
            CauseOfTransmissionSize = 2,
            InformationObjectAddressSize = 3,
            ResponseTimeoutMs = 250,
            Class2PollIntervalMs = 60,
            Class1DrainDelayMs = 0,
            MaxClass1DrainFrames = 16,
            ResetRemoteLinkOnConnect = false,
            ResetFcbOnConnect = false,
            SendGeneralInterrogationOnConnect = false,
            SendClockSyncOnConnect = false,
            RequestClass2ImmediatelyAfterStartup = true,
            MaxRetainedEvidenceEvents = 2000
        };
}
