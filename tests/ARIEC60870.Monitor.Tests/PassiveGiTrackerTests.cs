// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class PassiveGiTrackerTests
{
    private static readonly DateTime Origin = new(2026, 10, 28, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CompleteStationInterrogationLinksActivationActconDataAndActterm()
    {
        var tracker = new PassiveIec101GiTracker();
        var events = new List<PassiveGiEvent>();
        tracker.Observe(Frame(0, 0, 0, 100, 6, 1, 20), events.Add);
        tracker.Observe(Frame(1, 0, 30, 100, 7, 1, 20), events.Add);
        tracker.Observe(Frame(1, 18, 80, 1, 20, 1), events.Add);
        tracker.Observe(Frame(1, 33, 100, 1, 20, 1), events.Add);
        tracker.Observe(Frame(1, 48, 190, 100, 10, 1, 20), events.Add);
        Assert.Equal(new[] { PassiveGiKind.Activation, PassiveGiKind.ActivationConfirmed,
            PassiveGiKind.FirstInterrogatedData, PassiveGiKind.Completed },
            events.Select(x => x.Kind));
        Assert.Equal(2, events[^1].InterrogatedDataFrames);
        Assert.All(events, x => Assert.Equal(0, x.RequestOffset));
        Assert.Equal(0, tracker.PendingCount);
        tracker.Complete(events.Add);
        Assert.Equal(4, events.Count);
    }

    [Fact]
    public void GiReactivationWithoutTerminationIsVisibleNotSilentlyReset()
    {
        var tracker = new PassiveIec101GiTracker();
        var events = new List<PassiveGiEvent>();
        tracker.Observe(Frame(0, 0, 0, 100, 6, 7, 20), events.Add);
        tracker.Observe(Frame(0, 18, 200, 100, 6, 7, 20), events.Add);
        Assert.Contains(events, x => x.Kind == PassiveGiKind.RepeatedActivation);
        tracker.Observe(Frame(0, 36, 400, 100, 6, 7, 21), events.Add);
        Assert.Contains(events, x => x.Kind == PassiveGiKind.Superseded);
        Assert.Equal(2, events.Count(x => x.Kind == PassiveGiKind.Activation));
        Assert.Equal(1, tracker.PendingCount);
        tracker.Complete(events.Add);
        Assert.Equal(PassiveGiKind.PendingAtCaptureEnd, events[^1].Kind);
    }

    [Fact]
    public void NegativeActconAndUnmatchedActtermAreNotSuccess()
    {
        var tracker = new PassiveIec101GiTracker();
        var events = new List<PassiveGiEvent>();
        tracker.Observe(Frame(0, 0, 0, 100, 6, 1, 20), events.Add);
        tracker.Observe(Frame(1, 0, 10, 100, 7, 1, 20, negative: true), events.Add);
        Assert.Equal(PassiveGiKind.NegativeConfirmation, events[^1].Kind);
        Assert.Equal(0, tracker.PendingCount);
        tracker.Observe(Frame(1, 18, 20, 100, 10, 1, 20), events.Add);
        Assert.Equal(PassiveGiKind.UnmatchedResponse, events[^1].Kind);
        Assert.DoesNotContain(events, x => x.Kind == PassiveGiKind.Completed);
    }

    [Fact]
    public void MissingActtermTimeoutDiffersFromEndOfShortCapture()
    {
        var e1 = new List<PassiveGiEvent>();
        var tracker = new PassiveIec101GiTracker(new(TimeoutMilliseconds: 1000));
        tracker.Observe(Frame(0, 0, 0, 100, 6, 1, 20), e1.Add);
        tracker.Observe(Frame(1, 0, 30, 100, 7, 1, 20), e1.Add);
        tracker.AdvanceTime(Origin.AddMilliseconds(1200), e1.Add);
        Assert.Equal(PassiveGiKind.TimedOut, e1[^1].Kind);
        Assert.Contains("ACTTERM", e1[^1].Meaning, StringComparison.Ordinal);
        Assert.Equal(0, tracker.PendingCount);

        var e2 = new List<PassiveGiEvent>();
        var another = new PassiveIec101GiTracker();
        another.Observe(Frame(0, 0, 0, 100, 6, 1, 20), e2.Add);
        another.Complete(e2.Add);
        Assert.Equal(PassiveGiKind.PendingAtCaptureEnd, e2[^1].Kind);
        Assert.DoesNotContain(e2, x => x.Kind == PassiveGiKind.TimedOut);
    }

    [Fact]
    public void WrongCaWrongQualifierAndBadChecksumCannotCloseGi()
    {
        var tracker = new PassiveIec101GiTracker();
        var events = new List<PassiveGiEvent>();
        tracker.Observe(Frame(0, 0, 0, 100, 6, 1, 20), events.Add);
        tracker.Observe(Frame(1, 0, 20, 100, 7, 2, 20), events.Add);
        tracker.Observe(Frame(1, 18, 40, 100, 7, 1, 21), events.Add);
        tracker.Observe(Frame(1, 36, 60, 100, 7, 1, 20, valid: false), events.Add);
        tracker.Observe(Frame(1, 54, 80, 100, 10, 1, 20, valid: false), events.Add);
        Assert.Equal(1, tracker.PendingCount);
        Assert.Equal(2, events.Count(x => x.Kind == PassiveGiKind.UnmatchedResponse));
        Assert.DoesNotContain(events, x => x.Kind == PassiveGiKind.Completed);
    }

    [Fact]
    public void DifferentDecoderWidthsSupportTwoOctetLinkAddress()
    {
        var tracker = new PassiveIec101GiTracker(new(LinkAddressWidth: 2,
            CotWidth: 1, CommonAddressWidth: 1, InformationAddressWidth: 2));
        var events = new List<PassiveGiEvent>();
        tracker.Observe(Frame(0, 0, 0, 100, 6, 3, 21,
            linkWidth: 2, cotWidth: 1, caWidth: 1, ioaWidth: 2), events.Add);
        tracker.Observe(Frame(1, 0, 30, 100, 7, 3, 21,
            linkWidth: 2, cotWidth: 1, caWidth: 1, ioaWidth: 2), events.Add);
        tracker.Observe(Frame(1, 17, 70, 1, 21, 3,
            linkWidth: 2, cotWidth: 1, caWidth: 1, ioaWidth: 2), events.Add);
        tracker.Observe(Frame(1, 34, 130, 100, 10, 3, 21,
            linkWidth: 2, cotWidth: 1, caWidth: 1, ioaWidth: 2), events.Add);
        Assert.Equal(PassiveGiKind.Completed, events[^1].Kind);
        Assert.Equal(1, events[^1].InterrogatedDataFrames);
    }

    [Fact]
    public void ConcurrentGiSessionsAndBoundedCapacityNeverCrossMatch()
    {
        var tracker = new PassiveIec101GiTracker(new(MaxTrackedSessions: 2));
        var events = new List<PassiveGiEvent>();
        tracker.Observe(Frame(0, 0, 0, 100, 6, 1, 20), events.Add);
        tracker.Observe(Frame(0, 18, 5, 100, 6, 2, 20), events.Add);
        tracker.Observe(Frame(0, 36, 10, 100, 6, 3, 20), events.Add);
        Assert.Contains(events, x => x.Kind == PassiveGiKind.CapacityEviction);
        Assert.Equal(2, tracker.PendingCount);
        tracker.Observe(Frame(1, 0, 30, 100, 10, 1, 20), events.Add);
        Assert.Equal(PassiveGiKind.UnmatchedResponse, events[^1].Kind);
        tracker.Observe(Frame(1, 18, 60, 100, 10, 3, 20), events.Add);
        Assert.Equal(PassiveGiKind.Completed, events[^1].Kind);
        Assert.Equal(1, tracker.PendingCount);
    }

    [Fact]
    public void FakeGiInFixedFrameOrIncorrectObjectCountIsIgnored()
    {
        var tracker = new PassiveIec101GiTracker();
        var events = new List<PassiveGiEvent>();
        var frame = Frame(0, 0, 0, 100, 6, 1, 20);
        tracker.Observe(frame with { RawBytes = frame.RawBytes[..^2] }, events.Add);
        tracker.Observe(Frame(0, 18, 20, 100, 6, 1, 20) with
        {
            RawBytes = Frame(0, 18, 20, 100, 6, 1, 20).RawBytes
                .Select((x, i) => i == 7 ? (byte)2 : x).ToArray()
        }, events.Add);
        Assert.Empty(events);
        Assert.Equal(0, tracker.PendingCount);
    }

    private static PassiveObservedFrame Frame(byte stream, long offset, int ms,
        byte type, int cot, int ca, int qoi = 20, bool negative = false,
        bool valid = true, int linkWidth = 1, int cotWidth = 2, int caWidth = 2, int ioaWidth = 3)
    {
        var asdu = new List<byte> { type, 1, (byte)(cot | (negative ? 0x40 : 0)) };
        if (cotWidth == 2) asdu.Add(0);
        for (var i = 0; i < caWidth; i++) asdu.Add((byte)(ca >> (8 * i)));
        for (var i = 0; i < ioaWidth; i++) asdu.Add(0);
        asdu.Add(type == 100 ? (byte)qoi : (byte)1);
        var payload = new List<byte> { (byte)(stream == 0 ? 0x53 : 0x08) };
        for (var i = 0; i < linkWidth; i++) payload.Add((byte)(0x169 >> (8 * i)));
        payload.AddRange(asdu);
        var length = (byte)payload.Count;
        var raw = new byte[] { 0x68, length, length, 0x68 }
            .Concat(payload)
            .Concat(new byte[] { (byte)(payload.Sum(x => x) & 0xff), 0x16 }).ToArray();
        return new PassiveObservedFrame(stream, offset, Origin.AddMilliseconds(ms),
            "Variable", valid, raw);
    }
}
