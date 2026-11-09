// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
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
        Assert.Contains("RTU " + deviceTime, trace.TimelineSignalDigest);
        Assert.DoesNotContain("TI ", trace.TimelineEngineeringIdentity);
        Assert.Contains("Link 105", trace.TimelineEngineeringIdentity);
        Assert.Contains("COT 20", trace.TimelineEngineeringCause);
    }
}
