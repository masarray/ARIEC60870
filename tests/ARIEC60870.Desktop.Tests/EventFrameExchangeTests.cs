// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class EventFrameExchangeTests
{
    private static readonly DateTime Utc = new(2026, 11, 13, 7, 12, 0, DateTimeKind.Utc);

    private static Iec103MasterEvidenceEvent Poll(string source = "Link A",
        int link = 105, int function = 10, long sequence = 30) =>
        new()
        {
            SequenceNumber = sequence,
            TimestampUtc = Utc,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.MasterToSlave,
            PhysicalSource = source,
            LinkAddress = link,
            RawHex = "10 5A 69 C3 16",
            Frame = new Ft12FrameDecode
            {
                LinkAddress = link,
                LinkControl = new LinkControlInfo { Prm = true, FunctionCode = function }
            }
        };

    private static Iec103MasterEvidenceEvent Reply(string source = "Link A",
        int link = 105, int delayMs = 80, long sequence = 31) =>
        new()
        {
            SequenceNumber = sequence,
            TimestampUtc = Utc.AddMilliseconds(delayMs),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            PhysicalSource = source,
            LinkAddress = link,
            RawHex = "10 08 69 71 16",
            Frame = new Ft12FrameDecode
            {
                LinkAddress = link,
                LinkControl = new LinkControlInfo { Prm = false, FunctionCode = 8 }
            }
        };

    [Fact]
    public void ValidClass1PollAndResponseAreLinkedWithoutInventingProcessCausality()
    {
        Assert.True(EventFrameExchange.IsObservedPollReply(Poll(), Reply()));
        Assert.True(EventFrameExchange.IsObservedPollReply(Poll(function: 11), Reply()));
    }

    [Fact]
    public void CrossLinkWrongAddressStaleOrReorderedTrafficCannotBePaired()
    {
        Assert.False(EventFrameExchange.IsObservedPollReply(Poll(), Reply(source: "Link B")));
        Assert.False(EventFrameExchange.IsObservedPollReply(Poll(), Reply(link: 106)));
        Assert.False(EventFrameExchange.IsObservedPollReply(Poll(), Reply(delayMs: 60000)));
        Assert.False(EventFrameExchange.IsObservedPollReply(Poll(), Reply(sequence: 29)));
        Assert.False(EventFrameExchange.IsObservedPollReply(Poll(function: 3), Reply()));
        var unsolicited = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 31,
            TimestampUtc = Utc.AddMilliseconds(80),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            PhysicalSource = "Link A",
            LinkAddress = 105,
            RawHex = "10 4B 69 B4 16",
            Frame = new Ft12FrameDecode
            {
                LinkAddress = 105,
                LinkControl = new LinkControlInfo { Prm = true, FunctionCode = 11 }
            }
        };
        Assert.False(EventFrameExchange.IsObservedPollReply(Poll(), unsolicited));
    }

    [Fact]
    public void ANewerNonPollTxCannotLeaveAnOlderPollFalselyPaired()
    {
        var poll = Poll();
        var unrelated = Poll(function: 3, sequence: 31);
        var reply = Reply(sequence: 32);
        Assert.Null(EventFrameExchange.FindLatestObservedPoll(reply, new[] { poll, unrelated }));
        Assert.Same(poll, EventFrameExchange.FindLatestObservedPoll(reply, new[] { poll }));
    }
}
