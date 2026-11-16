// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>IEC101 GI acceptance requires same link epoch, CA, OA, QOI and ACTCON.</summary>
public sealed class Iec101GiLifecycleTrackerTests
{
    private static readonly Iec10xAsduDecoder Decoder = new(2, 2, 3);

    private static Iec10xAsduDecode Gi(int cot, int ca = 1, int qoi = 20,
        int oa = 0, bool negative = false, bool test = false, int ioa = 0)
        => Decoder.Decode(new byte[]
        {
            100, 1, (byte)(cot | (negative ? 0x40 : 0) | (test ? 0x80 : 0)),
            (byte)oa, (byte)ca, (byte)(ca >> 8),
            (byte)ioa, (byte)(ioa >> 8), (byte)(ioa >> 16), (byte)qoi
        });

    [Theory]
    [InlineData(20, 1)]
    [InlineData(21, 1)]
    [InlineData(36, 105)]
    public void MatchedActivationAndActtermOnlyCompleteExactStationOrGroup(int qoi, int ca)
    {
        var tracker = new Iec101GiLifecycleTracker();
        Assert.True(tracker.ObserveTransmitted(Gi(6, ca, qoi)));
        Assert.Equal(ca, tracker.PendingCommonAddress);
        Assert.Equal(Iec101GiLifecycleObservation.PositiveActcon,
            tracker.ObserveReceived(Gi(7, ca, qoi)));
        Assert.True(tracker.HasAcceptedActivation);
        Assert.Equal(Iec101GiLifecycleObservation.MatchedActterm,
            tracker.ObserveReceived(Gi(10, ca, qoi)));
        Assert.Null(tracker.PendingCommonAddress);
        Assert.Equal(Iec101GiLifecycleObservation.Unmatched,
            tracker.ObserveReceived(Gi(10, ca, qoi)));
    }

    [Fact]
    public void ValidButUnrelatedCaQoiAndOriginatorNeverCompleteCurrentGi()
    {
        var tracker = new Iec101GiLifecycleTracker();
        tracker.ObserveTransmitted(Gi(6, ca: 5, qoi: 20, oa: 2));
        Assert.Equal(Iec101GiLifecycleObservation.Unmatched,
            tracker.ObserveReceived(Gi(7, ca: 6, oa: 2)));
        Assert.Equal(Iec101GiLifecycleObservation.Unmatched,
            tracker.ObserveReceived(Gi(7, ca: 5, qoi: 21, oa: 2)));
        Assert.Equal(Iec101GiLifecycleObservation.Unmatched,
            tracker.ObserveReceived(Gi(7, ca: 5, qoi: 20, oa: 1)));
        Assert.False(tracker.HasAcceptedActivation);
        Assert.Equal(Iec101GiLifecycleObservation.IncompleteActterm,
            tracker.ObserveReceived(Gi(10, ca: 5, qoi: 20, oa: 2)));
        Assert.Equal(Iec101GiLifecycleObservation.PositiveActcon,
            tracker.ObserveReceived(Gi(7, ca: 5, qoi: 20, oa: 2)));
        Assert.Equal(Iec101GiLifecycleObservation.Unmatched,
            tracker.ObserveReceived(Gi(10, ca: 6, qoi: 20, oa: 2)));
        Assert.Equal(Iec101GiLifecycleObservation.MatchedActterm,
            tracker.ObserveReceived(Gi(10, ca: 5, qoi: 20, oa: 2)));
    }

    [Fact]
    public void NegativeActconNeverAuthorizesCompletionAndAllowsNewIntent()
    {
        var tracker = new Iec101GiLifecycleTracker();
        tracker.ObserveTransmitted(Gi(6));
        Assert.Equal(Iec101GiLifecycleObservation.NegativeActcon,
            tracker.ObserveReceived(Gi(7, negative: true)));
        Assert.False(tracker.HasAcceptedActivation);
        Assert.Null(tracker.PendingCommonAddress);
        Assert.Equal(Iec101GiLifecycleObservation.Unmatched, tracker.ObserveReceived(Gi(10)));
        Assert.True(tracker.ObserveTransmitted(Gi(6)));
        Assert.Equal(Iec101GiLifecycleObservation.PositiveActcon, tracker.ObserveReceived(Gi(7)));
        Assert.Equal(Iec101GiLifecycleObservation.MatchedActterm, tracker.ObserveReceived(Gi(10)));
    }

    [Fact]
    public void TestAndUnsolicitedResponsesAreEvidenceNotVerifiedGi()
    {
        var tracker = new Iec101GiLifecycleTracker();
        Assert.Equal(Iec101GiLifecycleObservation.Unmatched, tracker.ObserveReceived(Gi(10)));
        tracker.ObserveTransmitted(Gi(6));
        Assert.Equal(Iec101GiLifecycleObservation.None,
            tracker.ObserveReceived(Gi(7, test: true)));
        Assert.Equal(Iec101GiLifecycleObservation.IncompleteActterm,
            tracker.ObserveReceived(Gi(10)));
        Assert.Equal(Iec101GiLifecycleObservation.PositiveActcon, tracker.ObserveReceived(Gi(7)));
        Assert.Equal(Iec101GiLifecycleObservation.None,
            tracker.ObserveReceived(Gi(10, test: true)));
        Assert.Equal(Iec101GiLifecycleObservation.MatchedActterm, tracker.ObserveReceived(Gi(10)));
    }

    [Fact]
    public void SameTargetOverlappingGiFailsClosedUntilFreshEpoch()
    {
        var tracker = new Iec101GiLifecycleTracker();
        tracker.ObserveTransmitted(Gi(6));
        tracker.ObserveTransmitted(Gi(6));
        Assert.Equal(Iec101GiLifecycleObservation.Ambiguous, tracker.ObserveReceived(Gi(7)));
        Assert.Equal(Iec101GiLifecycleObservation.Ambiguous, tracker.ObserveReceived(Gi(10)));
        Assert.False(tracker.HasAcceptedActivation);
        tracker.Reset();
        tracker.ObserveTransmitted(Gi(6));
        Assert.Equal(Iec101GiLifecycleObservation.PositiveActcon, tracker.ObserveReceived(Gi(7)));
        Assert.Equal(Iec101GiLifecycleObservation.MatchedActterm, tracker.ObserveReceived(Gi(10)));
    }

    [Fact]
    public void DifferentGroupRetryAndReopenedPortCannotBorrowOldConfirmation()
    {
        var tracker = new Iec101GiLifecycleTracker();
        tracker.ObserveTransmitted(Gi(6, qoi: 20));
        tracker.ObserveTransmitted(Gi(6, qoi: 21));
        Assert.Equal(Iec101GiLifecycleObservation.Unmatched,
            tracker.ObserveReceived(Gi(7, qoi: 20)));
        Assert.Equal(Iec101GiLifecycleObservation.PositiveActcon,
            tracker.ObserveReceived(Gi(7, qoi: 21)));
        tracker.Reset(); // COM reopened or owner promoted.
        Assert.Equal(Iec101GiLifecycleObservation.Unmatched,
            tracker.ObserveReceived(Gi(10, qoi: 21)));
        tracker.ObserveTransmitted(Gi(6, qoi: 20));
        Assert.Equal(Iec101GiLifecycleObservation.IncompleteActterm,
            tracker.ObserveReceived(Gi(10, qoi: 20)));
    }

    [Fact]
    public void DualPhysicalLinksKeepIndependentApplicationAuthority()
    {
        var linkA = new Iec101GiLifecycleTracker();
        var linkB = new Iec101GiLifecycleTracker();
        linkA.ObserveTransmitted(Gi(6));
        Assert.Equal(Iec101GiLifecycleObservation.Unmatched, linkB.ObserveReceived(Gi(7)));
        Assert.Equal(Iec101GiLifecycleObservation.PositiveActcon, linkA.ObserveReceived(Gi(7)));
        Assert.Equal(Iec101GiLifecycleObservation.Unmatched, linkB.ObserveReceived(Gi(10)));
        Assert.Equal(Iec101GiLifecycleObservation.MatchedActterm, linkA.ObserveReceived(Gi(10)));
    }

    [Fact]
    public void ImageTrackerCannotTurnReadyFromUncorrelatedOrNegativeGi()
    {
        var tracker = new Iec101ApplicationImageTracker();
        tracker.MarkGiStarted(DateTime.UtcNow);
        tracker.Observe(Gi(10), DateTime.UtcNow);
        Assert.NotEqual(Iec101ApplicationImageState.Ready, tracker.State);
        tracker.Observe(Gi(7, negative: true), DateTime.UtcNow);
        Assert.NotEqual(Iec101ApplicationImageState.Ready, tracker.State);
        tracker.Observe(Gi(10), DateTime.UtcNow, matchedGiCompletion: true);
        Assert.Equal(Iec101ApplicationImageState.Ready, tracker.State);
    }
}
