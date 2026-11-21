// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class SmartRedundancyUxTests
{
    [Fact]
    public void NewStandbyFollowsLinkAUntilEngineerExplicitlyOverrides()
    {
        Assert.False(BackupLinkAddressPolicy.IsExplicit(null, 105, 105));
        Assert.Equal(105, BackupLinkAddressPolicy.InitialAddress(105, 1, explicitlyConfigured: false));
        Assert.Equal(105, BackupLinkAddressPolicy.InitialAddress(105, 105, explicitlyConfigured: false));
        Assert.False(BackupLinkAddressPolicy.IsDifferent(105, 105));
    }

    [Fact]
    public void LegacySavedMismatchIsPreservedAndNeverReinterpretedAsHealthy()
    {
        Assert.True(BackupLinkAddressPolicy.IsExplicit(null, 105, 1));
        Assert.Equal(1, BackupLinkAddressPolicy.InitialAddress(105, 1, explicitlyConfigured: true));
        Assert.True(BackupLinkAddressPolicy.IsDifferent(105, 1));
        Assert.True(BackupLinkAddressPolicy.IsExplicit(true, 105, 105));
        Assert.False(BackupLinkAddressPolicy.IsExplicit(false, 105, 1));
    }

    [Fact]
    public void MismatchTimelineUsesTypedExpectedAndReceivedAddressAndKeepsFullEvidence()
    {
        const string evidence = "COM=COM2; channel=Link B/Standby; expected Link=1; actual Link=105; PRM=0; checksum=True; last TX=10 49 01 00 4A 16; RX=10 2B 69 00 94 16.";
        var row = new DualLinkTimelineRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Category = "Warning",
            Summary = "Link B IEC-101 RX rejected [IEC101-RX-LINK-ADDRESS-MISMATCH]",
            Detail = evidence,
            PollingReason = "IEC101-RX-LINK-ADDRESS-MISMATCH",
            ExpectedLinkAddress = 1,
            Frame = new Ft12FrameDecode { LinkAddress = 105 }
        });
        Assert.Equal("Link B", row.Link);
        Assert.Equal("Expected 1 · Received 105", row.Reason);
        Assert.Contains("Rejected", row.Result);
        Assert.Equal(evidence, row.FullResult);
        Assert.Contains("IEC101-RX-LINK-ADDRESS-MISMATCH", row.FullReason);
    }

    [Fact]
    public void DifferentAddressesWithoutValidatedMismatchAreNotAutomaticallyRejectedByPresentation()
    {
        var row = new DualLinkTimelineRow(new Iec103MasterEvidenceEvent
        {
            Summary = "Standby health probe accepted",
            PollingReason = "Link supervision",
            ExpectedLinkAddress = 1,
            Frame = new Ft12FrameDecode { LinkAddress = 1 }
        });
        Assert.DoesNotContain("Rejected", row.Result);
        Assert.Equal("Standby health probe accepted", row.Reason);
    }

    [Fact]
    public void HealthyDualLinkSnapshotProducesUsefulTimelineOnceNotOneRowPerPoll()
    {
        var observer = new DualLinkStatusTimelineProjection();
        var initial = HealthySnapshot(12, 8);
        var first = observer.Observe(initial);
        Assert.Contains(first, x => x.Event == "Session observed");
        Assert.Contains(first, x => x.Event == "Active assigned" && x.Link == "Link A");
        Assert.Contains(first, x => x.Event == "Link healthy" && x.Link == "Link A");
        Assert.Contains(first, x => x.Event == "Link healthy" && x.Link == "Link B");
        Assert.Contains(first, x => x.Event == "Image empty");
        Assert.All(first, x => Assert.False(string.IsNullOrWhiteSpace(x.Result)));

        // The RX counters and last supervision timestamp change on every
        // healthy standby FC9 probe: this must NOT grow the timeline.
        var afterPoll = HealthySnapshot(30, 26);
        Assert.Empty(observer.Observe(afterPoll));
        var statusRow = new DualLinkTimelineRow(first.Single(x => x.Event == "Link healthy" && x.Link == "Link B"));
        Assert.Contains("Validated RTU", statusRow.Reason);
        Assert.Contains("COM2", statusRow.FullResult);
        Assert.Equal(0, statusRow.Sequence);

        var degraded = new Iec101RedundancySessionSnapshot
        {
            ControllerState = Iec101RedundancyControllerState.Degraded,
            ActiveLinkName = "Link A",
            StandbyLinkName = "Link B",
            ApplicationImageState = Iec101ApplicationImageState.Empty,
            LinkA = afterPoll.LinkA,
            LinkB = new Iec101RedundancyChannelSnapshot
            {
                Name = "Link B", PortName = "COM2", LinkAddress = 105,
                Role = Iec101RedundancyChannelRole.Standby,
                IsHealthy = false,
                State = Iec101RedundancyChannelState.FailedLatched,
                ConsecutiveTimeouts = 2
            }
        };
        var transitions = observer.Observe(degraded);
        Assert.Contains(transitions, x => x.Event == "Link degraded" && x.Link == "Link B");
        Assert.Contains(transitions, x => x.Event == "State changed");
        Assert.Empty(observer.Observe(degraded));
        observer.Reset();
        Assert.Contains(observer.Observe(initial), x => x.Event == "Link healthy" && x.Link == "Link A");
    }

    private static Iec101RedundancySessionSnapshot HealthySnapshot(int aRx, int bRx) => new()
    {
        ControllerState = Iec101RedundancyControllerState.Healthy,
        ActiveLinkName = "Link A",
        StandbyLinkName = "Link B",
        ApplicationImageState = Iec101ApplicationImageState.Empty,
        LastStandbySupervisionUtc = DateTime.UtcNow,
        LinkA = new Iec101RedundancyChannelSnapshot
        {
            Name = "Link A", PortName = "COM1", LinkAddress = 105,
            Role = Iec101RedundancyChannelRole.Active,
            State = Iec101RedundancyChannelState.ActivePolling,
            IsHealthy = true, RxFrames = aRx
        },
        LinkB = new Iec101RedundancyChannelSnapshot
        {
            Name = "Link B", PortName = "COM2", LinkAddress = 105,
            Role = Iec101RedundancyChannelRole.Standby,
            State = Iec101RedundancyChannelState.StandbySupervising,
            IsHealthy = true, RxFrames = bRx
        }
    };

    [Fact]
    public void TypedSignalShowsOneIoaAndItsOwnRtuTimeWithoutRepeatingInContext()
    {
        const string deviceTime = "2026-11-09 07:29:16.200";
        var asdu = new Iec10xAsduDecode
        {
            IsHeaderValid = true,
            TypeId = 31,
            CommonAddress = 1,
            CauseOfTransmission = 20,
            InformationObjectAddressWidth = 3,
            ObjectCount = 1,
            Objects = [new Iec10xInformationObject
            {
                Index = 1, InformationObjectAddress = 16712704,
                ValueText = "DP=ON", TimestampText = deviceTime
            }]
        };
        var trace = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            LinkAddress = 105,
            AsduDetails = asdu
        });
        Assert.Contains("IOA 16712704", trace.TimelineSignalDigest);
        Assert.Equal(1, trace.TimelineSignalDigest.Split("IOA 16712704").Length - 1);
        Assert.Contains("TI-31", trace.TimelineSignalDigest);
        Assert.Contains("RTU partial time: " + deviceTime, trace.TimelineSignalDigest);
        Assert.DoesNotContain("TI ", trace.TimelineEngineeringIdentity);
        Assert.Contains("Link 105", trace.TimelineEngineeringIdentity);
        Assert.Contains("Cause Of Transmission: 20", trace.TimelineEngineeringCause);
    }
}
