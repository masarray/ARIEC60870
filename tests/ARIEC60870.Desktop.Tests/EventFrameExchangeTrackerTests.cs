// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class EventFrameExchangeTrackerTests
{
    private static readonly DateTime Time = new(2026, 11, 13, 7, 12, 0, DateTimeKind.Utc);

    private static Iec103MasterEvidenceEvent Frame(long sequence, byte control,
        int link = 105, string source = "Link A", long? parent = null,
        int delayMs = 0, int? stream = null, long? offset = null)
    {
        var raw = new byte[]
        {
            0x10, control, (byte)link, (byte)((control + link) & 0xFF), 0x16
        };
        var decoded = Ft12Parser.ForIec101(1).Decode(raw);
        Assert.True(decoded.IsChecksumValid);
        Assert.True(decoded.IsLengthValid);
        return new Iec103MasterEvidenceEvent
        {
            SequenceNumber = sequence,
            PhysicalFrameSequenceNumber = parent,
            PhysicalStreamId = stream,
            RawStreamOffset = offset,
            TimestampUtc = Time.AddMilliseconds(delayMs),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = source,
            Direction = (control & 0x40) != 0
                ? FrameDirection.MasterToSlave
                : FrameDirection.SlaveToMaster,
            LinkAddress = link,
            RawHex = decoded.Hex,
            Frame = decoded
        };
    }

    [Fact]
    public void TwoDistinctResponsesCannotBothClaimTheSameMasterPoll()
    {
        var tracker = new EventFrameExchangeTracker();
        var poll = Frame(100, 0x5A);
        var first = Frame(101, 0x08, delayMs: 80);
        var second = Frame(102, 0x08, delayMs: 140);
        tracker.Observe(poll);
        tracker.Observe(first);
        Assert.Same(poll, tracker.RequestForEvent(first));
        tracker.Observe(second);
        Assert.Null(tracker.RequestForEvent(second));
        Assert.Null(tracker.RequestForEvent(first)); // an older RX no longer owns live state
    }

    [Fact]
    public void MultipleIoaProjectionsOfOnePhysicalRxKeepTheirSingleRealTx()
    {
        var tracker = new EventFrameExchangeTracker();
        var poll = Frame(200, 0x5A);
        var firstObject = Frame(201, 0x08, parent: 201, delayMs: 90);
        var nextObject = Frame(202, 0x08, parent: 201, delayMs: 90);
        tracker.Observe(poll);
        tracker.Observe(firstObject);
        Assert.Same(poll, tracker.RequestForEvent(firstObject));
        tracker.Observe(nextObject);
        Assert.Same(poll, tracker.RequestForEvent(nextObject));
        var nextRx = Frame(203, 0x08, parent: 203, delayMs: 150);
        Assert.Null(tracker.RequestForEvent(nextRx));
    }

    [Fact]
    public void SecondaryNoDataAndAckConsumeCandidateWithoutInventingEventReply()
    {
        var tracker = new EventFrameExchangeTracker();
        var poll = Frame(300, 0x5A);
        tracker.Observe(poll);
        tracker.Observe(Frame(301, 0x09, delayMs: 50)); // secondary FC9: NO DATA
        Assert.Null(tracker.RequestForEvent(Frame(302, 0x08, delayMs: 110)));
        tracker.Observe(Frame(303, 0x5A, delayMs: 140));
        tracker.Observe(Frame(304, 0x00, delayMs: 160)); // secondary FC0: ACK
        Assert.Null(tracker.RequestForEvent(Frame(305, 0x08, delayMs: 200)));
    }

    [Fact]
    public void UnrelatedCommandOrInvalidRxEndsOldPollEligibility()
    {
        var tracker = new EventFrameExchangeTracker();
        tracker.Observe(Frame(400, 0x5A));
        tracker.Observe(Frame(401, 0x53, delayMs: 20)); // master FC3 command
        Assert.Null(tracker.RequestForEvent(Frame(402, 0x08, delayMs: 60)));
        tracker.Observe(Frame(403, 0x5B, delayMs: 80)); // new Class 2 request
        var bad = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 404, TimestampUtc = Time.AddMilliseconds(90),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            PhysicalSource = "Link A", LinkAddress = 105,
            RawHex = "10 08 69 70 16",
            Frame = new Ft12FrameDecode
            {
                LinkAddress = 105, IsLengthValid = true, IsChecksumValid = false,
                LinkControl = new LinkControlInfo { Prm = false, FunctionCode = 8 }
            }
        };
        tracker.Observe(bad);
        Assert.Null(tracker.RequestForEvent(bad));
        Assert.Null(tracker.RequestForEvent(Frame(405, 0x08, delayMs: 120)));
    }

    [Fact]
    public void SameLinkAddressOnDifferentPhysicalSourceIsIndependent()
    {
        var tracker = new EventFrameExchangeTracker();
        var a = Frame(500, 0x5A, source: "Link A");
        var b = Frame(501, 0x5A, source: "Link B", delayMs: 10);
        tracker.Observe(a);
        tracker.Observe(b);
        Assert.Same(b, tracker.RequestForEvent(
            Frame(502, 0x08, source: "Link B", delayMs: 35)));
        Assert.Same(a, tracker.RequestForEvent(
            Frame(503, 0x08, source: "Link A", delayMs: 45)));
    }

    [Fact]
    public void PassiveSiblingObjectsSharePhysicalStreamOffsetNotObjectSequence()
    {
        var tracker = new EventFrameExchangeTracker();
        var poll = Frame(600, 0x5A, stream: 0, offset: 100);
        var rx1 = Frame(601, 0x08, stream: 1, offset: 200, delayMs: 65);
        var rx2 = Frame(602, 0x08, stream: 1, offset: 200, delayMs: 65);
        tracker.Observe(poll);
        Assert.Same(poll, tracker.RequestForEvent(rx1));
        Assert.Same(poll, tracker.RequestForEvent(rx2));
        Assert.Null(tracker.RequestForEvent(
            Frame(603, 0x08, stream: 1, offset: 240, delayMs: 150)));
    }

    [Fact]
    public void UnaddressedE5DoesNotLeaveStaleClassPollsOnThatPhysicalCircuit()
    {
        var tracker = new EventFrameExchangeTracker();
        var a1 = Frame(640, 0x5A, link: 11, source: "Link A");
        var a2 = Frame(641, 0x5A, link: 12, source: "Link A");
        var otherSource = Frame(642, 0x5A, link: 11, source: "Link B");
        tracker.Observe(a1);
        tracker.Observe(a2);
        tracker.Observe(otherSource);
        var single = Ft12Parser.ForIec101(1).Decode([0xE5]);
        Assert.True(single.IsSingleCharacterAck);
        tracker.Observe(new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 643, TimestampUtc = Time.AddMilliseconds(40),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            PhysicalSource = "Link A",
            Frame = single,
            RawHex = single.Hex
            // No link address exists in E5; matching one by guess is forbidden.
        });
        Assert.Null(tracker.RequestForEvent(
            Frame(644, 0x08, link: 11, source: "Link A", delayMs: 75)));
        Assert.Null(tracker.RequestForEvent(
            Frame(645, 0x08, link: 12, source: "Link A", delayMs: 80)));
        Assert.Same(otherSource, tracker.RequestForEvent(
            Frame(646, 0x08, link: 11, source: "Link B", delayMs: 85)));
    }

    [Fact]
    public void TrackedLinkCountIsBoundedAndOldestSourceIsSafelyEvicted()
    {
        var tracker = new EventFrameExchangeTracker(maxTrackedLinks: 2);
        tracker.Observe(Frame(700, 0x5A, link: 11));
        tracker.Observe(Frame(701, 0x5A, link: 12));
        tracker.Observe(Frame(702, 0x5A, link: 13));
        Assert.Equal(2, tracker.TrackedLinkCount);
        Assert.Null(tracker.RequestForEvent(
            Frame(703, 0x08, link: 11, delayMs: 80)));
        Assert.Equal(2, tracker.TrackedLinkCount);
        tracker.Reset();
        Assert.Equal(0, tracker.TrackedLinkCount);
    }
}
