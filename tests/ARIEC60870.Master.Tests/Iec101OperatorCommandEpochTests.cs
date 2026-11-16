// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>IEC101 P0: bounded command admission and physical owner-epoch safety.</summary>
public sealed class Iec101OperatorCommandEpochTests
{
    [Fact]
    public void BoundedQueuePreservesOrderAndRefusesOverflow()
    {
        var q = new Iec101BoundedFifo<int>(2);
        Assert.True(q.TryEnqueue(10));
        Assert.True(q.TryEnqueue(20));
        Assert.False(q.TryEnqueue(30));
        Assert.Equal(2, q.Count);
        Assert.True(q.TryDequeue(out var a));
        Assert.Equal(10, a);
        Assert.True(q.TryEnqueue(40));
        Assert.True(q.TryDequeue(out var b));
        Assert.Equal(20, b);
        Assert.True(q.TryDequeue(out var c));
        Assert.Equal(40, c);
        Assert.False(q.TryDequeue(out _));
    }

    [Fact]
    public void StopClearsUnsentIntentsRatherThanReplaying()
    {
        var q = new Iec101BoundedFifo<string>(2);
        Assert.True(q.TryEnqueue("SELECT"));
        Assert.True(q.TryEnqueue("EXECUTE"));
        Assert.Equal(2, q.Clear());
        Assert.False(q.TryDequeue(out _));
    }

    [Fact]
    public void ActiveOwnerStampRejectsAtoBtoAAndSameComReopen()
    {
        var original = new Iec101OwnerStamp("A", 4, 7);
        Assert.True(original.Matches("A", 4, 7));
        Assert.False(original.Matches("B", 4, 8));
        Assert.False(original.Matches("A", 4, 9));
        Assert.False(original.Matches("A", 5, 7));
    }

    [Fact]
    public void SingleQueueAdmissionReportsCapacityWithoutEvictingOldRequests()
    {
        var settings = Settings();
        using var transport = new SimulatedIec101Transport(settings);
        var session = new Iec101MasterSession(settings, transport);
        for (var i = 0; i < 64; i++)
            Assert.True(session.TryQueueControlCommand(ReadOnlyGi()));
        Assert.False(session.TryQueueControlCommand(ReadOnlyGi()));
    }

    [Fact]
    public void DualQueueAdmissionReportsCapacityWithoutEvictingOldRequests()
    {
        var settings = Settings();
        var options = DualOptions(settings);
        using var a = new SimulatedIec101Transport(options.LinkA.ApplyTo(settings));
        using var b = new SimulatedIec101Transport(options.LinkB.ApplyTo(settings));
        var session = new Iec101DualLinkRedundancySession(options, a, b);
        for (var i = 0; i < 64; i++)
            Assert.True(session.TryQueueControlCommand(ReadOnlyGi()));
        Assert.False(session.TryQueueControlCommand(ReadOnlyGi()));
    }

    [Fact]
    public async Task SinglePreLinkPhysicalControlIsNeverTransmittedAfterVerification()
    {
        var settings = Settings();
        await using var transport = new SimulatedIec101Transport(settings);
        var session = new Iec101MasterSession(settings, transport);
        session.QueueControlCommand(SingleCommand());
        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(700), CancellationToken.None);
        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.Contains(result.Findings, x => x.Id == "IEC101-COMMAND-LINK-UNVERIFIED");
        Assert.DoesNotContain(result.Events, x =>
            x.Direction == FrameDirection.MasterToSlave && x.TypeId == 45);
    }

    [Fact]
    public async Task SingleVerifiedLivePhysicalControlRemainsAvailable()
    {
        var settings = Settings();
        await using var transport = new SimulatedIec101Transport(settings);
        var session = new Iec101MasterSession(settings, transport);
        var queued = false;
        session.EvidenceReceived += (_, e) =>
        {
            if (queued || e.Direction != FrameDirection.SlaveToMaster) return;
            queued = true;
            session.QueueControlCommand(SingleCommand());
        };
        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(700), CancellationToken.None);
        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.True(queued);
        Assert.Contains(result.Events, x =>
            x.Direction == FrameDirection.MasterToSlave && x.TypeId == 45);
    }

    [Fact]
    public async Task DualPreElectionPhysicalControlIsNotReplayedOnActiveLink()
    {
        var settings = Settings();
        var options = DualOptions(settings);
        await using var a = new SimulatedIec101Transport(options.LinkA.ApplyTo(settings));
        await using var b = new SimulatedIec101Transport(options.LinkB.ApplyTo(settings));
        var session = new Iec101DualLinkRedundancySession(options, a, b);
        session.QueueControlCommand(SingleCommand());
        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(750), CancellationToken.None);
        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.Contains(result.Findings, x => x.Id == "IEC101-DUAL-COMMAND-ACTIVE-UNVERIFIED");
        Assert.DoesNotContain(result.Events, x =>
            x.Direction == FrameDirection.MasterToSlave && x.TypeId == 45);
    }

    [Fact]
    public async Task DualVerifiedActiveStillDispatchesIntentionalPhysicalControl()
    {
        var settings = Settings();
        var options = DualOptions(settings);
        await using var a = new SimulatedIec101Transport(options.LinkA.ApplyTo(settings));
        await using var b = new SimulatedIec101Transport(options.LinkB.ApplyTo(settings));
        var session = new Iec101DualLinkRedundancySession(options, a, b);
        var queued = false;
        session.EvidenceReceived += (_, e) =>
        {
            if (queued || e.Direction != FrameDirection.SlaveToMaster ||
                e.PhysicalSource != "Link A") return;
            queued = true;
            session.QueueControlCommand(SingleCommand());
        };
        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(900), CancellationToken.None);
        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.True(queued);
        Assert.Contains(result.Events, x => x.Direction == FrameDirection.MasterToSlave &&
            x.TypeId == 45 && x.PhysicalSource == "Link A");
        Assert.DoesNotContain(result.Events, x =>
            x.Category == "TX" && string.IsNullOrWhiteSpace(x.PhysicalSource));
        Assert.DoesNotContain(result.Findings, x => x.Id == "IEC101-DUAL-COMMAND-STALE-OWNER");
    }

    [Fact]
    public async Task QueuedControlCannotCrossManualAtoBPromotionOrCauseSecondSwitch()
    {
        var settings = Settings();
        var options = DualOptions(settings);
        await using var a = new SimulatedIec101Transport(options.LinkA.ApplyTo(settings));
        await using var b = new SimulatedIec101Transport(options.LinkB.ApplyTo(settings));
        var session = new Iec101DualLinkRedundancySession(options, a, b);
        var queued = false;
        session.EvidenceReceived += (_, e) =>
        {
            if (queued || e.Direction != FrameDirection.SlaveToMaster ||
                e.PhysicalSource != "Link A") return;
            queued = true;
            session.QueueControlCommand(SingleCommand());
            session.QueueManualFailover("promote B before dispatching queued A command");
            session.QueueManualFailover("stale second switch must not ping-pong back to A");
        };

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(1600), CancellationToken.None);
        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.True(queued);
        Assert.Contains(session.FailoverJournal, x =>
            x.Completed && x.FromLink == "Link A" && x.ToLink == "Link B");
        Assert.DoesNotContain(session.FailoverJournal, x =>
            x.Completed && x.FromLink == "Link B" && x.ToLink == "Link A");
        Assert.Contains(result.Findings, x => x.Id == "IEC101-DUAL-COMMAND-STALE-OWNER");
        Assert.Contains(result.Findings, x => x.Id == "IEC101-DUAL-STALE-FAILOVER-REQUESTS");
        Assert.DoesNotContain(result.Events, x =>
            x.Direction == FrameDirection.MasterToSlave && x.TypeId == 45);
    }

    [Fact]
    public async Task SingleSboExecuteWithoutMatchedSelectIsNeverTransmitted()
    {
        var settings = Settings();
        await using var transport = new SimulatedIec101Transport(settings);
        var session = new Iec101MasterSession(settings, transport);
        var queued = false;
        session.EvidenceReceived += (_, e) =>
        {
            if (queued || e.Direction != FrameDirection.SlaveToMaster) return;
            queued = true;
            session.QueueControlCommand(new Iec60870ControlCommandRequest
            {
                Kind = Iec60870ControlCommandKind.DoubleCommand,
                InformationObjectAddress = 101,
                Value = 2,
                OperationMode = Iec60870ControlOperationMode.ExecuteSelected
            });
        };

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(700), CancellationToken.None);
        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.True(queued);
        Assert.Contains(result.Findings, x => x.Id == "IEC101-SBO-EXECUTE-NOT-SELECTED");
        Assert.DoesNotContain(result.Events, x =>
            x.Direction == FrameDirection.MasterToSlave && x.TypeId == 46);
    }

    [Fact]
    public async Task DualSboExecuteWithoutActiveSourceSelectIsNeverTransmitted()
    {
        var settings = Settings();
        var options = DualOptions(settings);
        await using var a = new SimulatedIec101Transport(options.LinkA.ApplyTo(settings));
        await using var b = new SimulatedIec101Transport(options.LinkB.ApplyTo(settings));
        var session = new Iec101DualLinkRedundancySession(options, a, b);
        var queued = false;
        session.EvidenceReceived += (_, e) =>
        {
            if (queued || e.Direction != FrameDirection.SlaveToMaster ||
                e.PhysicalSource != "Link A") return;
            queued = true;
            session.QueueControlCommand(new Iec60870ControlCommandRequest
            {
                Kind = Iec60870ControlCommandKind.DoubleCommand,
                InformationObjectAddress = 101,
                Value = 2,
                OperationMode = Iec60870ControlOperationMode.ExecuteSelected
            });
        };

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(900), CancellationToken.None);
        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.True(queued);
        Assert.Contains(result.Findings, x => x.Id == "IEC101-DUAL-SBO-EXECUTE-NOT-SELECTED");
        Assert.DoesNotContain(result.Events, x =>
            x.Direction == FrameDirection.MasterToSlave && x.TypeId == 46);
    }

    private static Iec60870ControlCommandRequest ReadOnlyGi() => new()
    {
        Kind = Iec60870ControlCommandKind.GeneralInterrogation
    };

    private static Iec60870ControlCommandRequest SingleCommand() => new()
    {
        Kind = Iec60870ControlCommandKind.SingleCommand,
        InformationObjectAddress = 101,
        Value = 1
    };

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
        ResponseTimeoutMs = 120,
        MaxClass1DrainFrames = 4,
        SendGeneralInterrogationOnConnect = false,
        SendClockSyncOnConnect = false,
        ResetRemoteLinkOnConnect = false,
        ResetFcbOnConnect = false
    };

    private static Iec101DualLinkRedundancyOptions DualOptions(Iec103MasterSettings settings) => new()
    {
        BaseSettings = settings,
        LinkA = new Iec101DualLinkEndpoint { Name = "Link A", PortName = "SIM-A", LinkAddress = 1 },
        LinkB = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "SIM-B", LinkAddress = 1 },
        StandbySupervisionInterval = TimeSpan.FromMilliseconds(200),
        PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled
    };
}
