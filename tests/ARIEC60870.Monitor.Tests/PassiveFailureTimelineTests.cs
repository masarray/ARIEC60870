// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class PassiveFailureTimelineTests
{
    private static readonly DateTime Origin = new(2026, 10, 28, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NormalStandbyLinkStatusAndClassPollsNeverCreateFalseFailure()
    {
        var timeline = new PassiveIec101FailureTimeline(Origin);
        var windows = new List<PassiveTimelineWindow>();
        timeline.ObserveFrame(Fixed(0, 0, 0, 0x49), windows.Add); // FC9 link status
        timeline.ObserveFrame(Fixed(1, 0, 100, 0x0B), windows.Add); // FC11 link reply
        timeline.ObserveFrame(Fixed(0, 5, 250, 0x5B), windows.Add); // class2
        timeline.ObserveFrame(Single(1, 5, 360, 0xE5), windows.Add); // short no-data
        timeline.ObserveFrame(Fixed(0, 10, 5100, 0x5A), windows.Add); // class1
        timeline.Complete(windows.Add);

        Assert.Equal(2, windows.Count);
        Assert.Equal(4, windows[0].Frames);
        Assert.Equal(2, windows[0].Source0Frames);
        Assert.Equal(2, windows[0].Source1Frames);
        Assert.Equal(1, windows[0].LinkStatusPolls);
        Assert.Equal(1, windows[0].SecondaryLinkStatus);
        Assert.Equal(1, windows[0].ShortE5Responses);
        Assert.Equal(1, windows[0].Class2Polls);
        Assert.Equal(1, windows[1].Class1Polls);
        Assert.All(windows, x => Assert.False(x.HasAnomaly));
    }

    [Fact]
    public void ThirtySecondTraceRetainsTrafficGapsAndAnchoredFailureEvidence()
    {
        var timeline = new PassiveIec101FailureTimeline(Origin);
        var windows = new List<PassiveTimelineWindow>();
        timeline.ObserveFrame(Fixed(0, 0, 0, 0x5B), windows.Add);
        timeline.ObserveTransaction(Transaction(PassiveTransactionKind.RetryObserved, 0, 0, 2000));
        timeline.AdvanceTime(Origin.AddSeconds(30), windows.Add);
        timeline.ObserveTransaction(Transaction(PassiveTransactionKind.ResponseTimeout, 0, 0, 30000));
        timeline.ObserveFrame(Fixed(1, 0, 30100, 0x0B), windows.Add);
        timeline.Complete(windows.Add);

        Assert.Equal(7, windows.Count); // 0, 5, 10, 15, 20, 25, 30 seconds
        Assert.Equal(1, windows[0].Retries);
        Assert.Equal(0, windows[0].FirstAnomalyOffset);
        Assert.All(windows.Skip(1).Take(5), x =>
        {
            Assert.Equal(0, x.Frames);
            Assert.False(x.HasAnomaly);
        });
        Assert.Equal(1, windows[^1].ResponseTimeouts);
        Assert.True(windows[^1].HasAnomaly);
        Assert.Equal(0, windows[^1].FirstAnomalyOffset);
        Assert.Equal(1, windows[^1].Source1Frames);
        Assert.DoesNotContain(windows, x => x.ToString().Contains("Switching", StringComparison.Ordinal));
    }

    [Fact]
    public void LargeOfflineGapsCompressWithoutUnboundedEmptyWindows()
    {
        var timeline = new PassiveIec101FailureTimeline(Origin,
            new PassiveTimelineOptions(WindowSeconds: 5, MaxIndividualEmptyWindows: 3));
        var windows = new List<PassiveTimelineWindow>();
        timeline.ObserveFrame(Fixed(0, 0, 0, 0x49), windows.Add);
        timeline.ObserveFrame(Fixed(0, 5, 3600000, 0x49), windows.Add);
        timeline.Complete(windows.Add);

        Assert.Equal(3, windows.Count);
        Assert.Equal(719, windows[1].CompressedEmptyWindows);
        Assert.Equal(TimeSpan.FromSeconds(5), windows[1].StartUtc - Origin);
        Assert.Equal(TimeSpan.FromHours(1), windows[1].EndUtc - Origin);
        Assert.Equal(719, timeline.CompressedEmptyWindowCount);
        Assert.False(windows[1].HasAnomaly);
    }

    [Fact]
    public void UsbReadTimestampRegressionIsFlaggedNotReorderedOrSilentlyDiscarded()
    {
        var timeline = new PassiveIec101FailureTimeline(Origin);
        var windows = new List<PassiveTimelineWindow>();
        timeline.ObserveFrame(Fixed(0, 0, 6000, 0x49), windows.Add);
        timeline.ObserveFrame(Fixed(1, 0, 100, 0x0B), windows.Add);
        timeline.Complete(windows.Add);
        var window = Assert.Single(windows);
        Assert.Equal(2, window.Frames);
        Assert.True(window.HostTimestampOrderUncertain);
    }

    [Fact]
    public void UnknownPrimaryFunctionAndSecondaryFc15CarryRawAnomalyReferences()
    {
        var timeline = new PassiveIec101FailureTimeline(Origin);
        var windows = new List<PassiveTimelineWindow>();
        timeline.ObserveFrame(Fixed(0, 12, 0, 0x42), windows.Add);
        timeline.ObserveFrame(Fixed(1, 8, 50, 0x0F), windows.Add);
        timeline.Complete(windows.Add);
        var window = Assert.Single(windows);
        Assert.Equal(1, window.UnknownPrimaryFunctions);
        Assert.Equal(1, window.ServiceNotImplementedReplies);
        Assert.Equal((int?)0, window.FirstAnomalyStream);
        Assert.Equal(12, window.FirstAnomalyOffset);
    }

    [Fact]
    public void GiLifecycleAndFaultCountersStayBoundedInFiveSecondIntervals()
    {
        var timeline = new PassiveIec101FailureTimeline(Origin);
        var windows = new List<PassiveTimelineWindow>();
        timeline.ObserveFrame(Fixed(0, 0, 0, 0x53), windows.Add);
        timeline.ObserveGi(Gi(PassiveGiKind.Activation, 0, 0));
        timeline.ObserveFrame(Fixed(1, 0, 100, 0x08), windows.Add);
        timeline.ObserveGi(Gi(PassiveGiKind.Completed, 1, 0));
        timeline.ObserveFrame(Fixed(0, 5, 5100, 0x53), windows.Add);
        timeline.ObserveGi(Gi(PassiveGiKind.NegativeConfirmation, 0, 5));
        timeline.Complete(windows.Add);
        Assert.Equal(1, windows[0].GiActivations);
        Assert.Equal(1, windows[0].GiCompleted);
        Assert.Equal(1, windows[1].GiExceptions);
        Assert.True(windows[1].HasAnomaly);
    }

    [Fact]
    public void OptionsRejectUnboundedOrInvalidConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PassiveIec101FailureTimeline(Origin, new(WindowSeconds: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PassiveIec101FailureTimeline(Origin, new(MaxIndividualEmptyWindows: 999)));
        Assert.Throws<ArgumentException>(() =>
            new PassiveIec101FailureTimeline(DateTime.SpecifyKind(Origin, DateTimeKind.Local)));
    }

    private static PassiveObservedFrame Fixed(byte stream, long offset, int ms, byte control)
    {
        var raw = new byte[] { 0x10, control, 0x69, (byte)((control + 0x69) & 0xFF), 0x16 };
        return new(stream, offset, Origin.AddMilliseconds(ms), "Fixed", true, raw);
    }

    private static PassiveObservedFrame Single(byte stream, long offset, int ms, byte b)
        => new(stream, offset, Origin.AddMilliseconds(ms), "Single", true, new[] { b });

    private static PassiveTransactionEvent Transaction(PassiveTransactionKind kind, byte stream, long offset, int ms)
        => new(kind, Origin.AddMilliseconds(ms), 1, stream, offset,
            0, 0, 11, 9, false, true, false, false, 1, null, "Test observation");

    private static PassiveGiEvent Gi(PassiveGiKind kind, byte stream, long offset)
        => new(kind, Origin, stream, offset, 0, 0, 1, 0, 20, 1, false, "Test GI");
}
