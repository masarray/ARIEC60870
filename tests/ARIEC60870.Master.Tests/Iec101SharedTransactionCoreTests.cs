// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Protocol;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>IEC101-R01/R02/R03/R04, T01-T06: same per-link core used by single and A/B.</summary>
public sealed class Iec101SharedTransactionCoreTests
{
    private static readonly Ft12Parser Parser = Ft12Parser.ForIec101(2);

    [Theory]
    [InlineData(3, "100B69007416", false)] // delayed status FC11 after GI FC3
    [InlineData(11, "100B69007416", false)] // delayed status after Class2
    [InlineData(3, "100169006A16", false)] // FC1 negative confirmation
    [InlineData(3, "100069006916", true)]  // positive FC0
    [InlineData(10, "100969007216", true)] // Class1 no-data
    [InlineData(11, "100869007116", true)] // Class2 FC8 user data
    public void SameResponseAndFcbRulesForSingleAndBothRedundancyChannels(
        int primaryFc, string rx, bool expectedAdvance)
    {
        foreach (var source in new[] { "single", "A", "B" })
        {
            var engine = new Iec101PerLinkTransactionEngine();
            engine.StartConnectionEpoch();
            using var pending = engine.BeginFixed(primaryFc, true, 105, 2, source);
            Assert.Equal(source, pending.PhysicalSource);
            engine.MarkTransmitAttempt(pending);
            engine.MarkAwaitingResponse(pending);
            var validation = engine.ValidateCandidate(pending, Parser.Decode(Convert.FromHexString(rx)));
            var outcome = engine.Complete(pending);
            Assert.Equal(expectedAdvance, outcome.FrameCountAdvanced);
            Assert.Equal(expectedAdvance, engine.FrameCountBit);
            Assert.False(engine.HasPendingExchange);
            Assert.Equal(expectedAdvance, validation.IsValid && validation.AdvancesFcb);
        }
    }

    [Fact]
    public void FcbIsNotAdvancedByBusyOrUncorrelatedE5StatusOrLostAck()
    {
        var engine = new Iec101PerLinkTransactionEngine();
        engine.StartConnectionEpoch();
        using (var command = engine.BeginVariable(new byte[] { 100, 1, 6, 0, 105, 0, 0, 0, 0, 20 }, 105, 2, "A"))
        {
            engine.MarkTransmitAttempt(command);
            engine.MarkAwaitingResponse(command);
            var bad = engine.ValidateCandidate(command, Parser.Decode(Convert.FromHexString("100B69007416")));
            Assert.False(bad.IsValid);
            var unknown = engine.Complete(command);
            Assert.Equal(Iec101ExchangeVerdict.UnknownDelivery, unknown.Verdict);
            Assert.False(unknown.FrameCountAdvanced);
        }
        using (var status = engine.BeginFixed(9, false, 105, 2, "A"))
        {
            engine.MarkTransmitAttempt(status);
            engine.MarkAwaitingResponse(status);
            Assert.False(engine.ValidateCandidate(status, Parser.Decode(new byte[] { 0xE5 })).IsValid);
            Assert.Equal(Iec101ExchangeVerdict.Unconfirmed, engine.Complete(status).Verdict);
        }
        using (var poll = engine.BeginFixed(11, true, 105, 2, "A"))
        {
            engine.MarkTransmitAttempt(poll);
            engine.MarkAwaitingResponse(poll);
            var busy = engine.ValidateCandidate(poll, Parser.Decode(Convert.FromHexString("101969008216")));
            Assert.True(busy.IsValid);
            Assert.Equal(Iec101ExchangeVerdict.Busy, engine.Complete(poll).Verdict);
        }
        Assert.False(engine.FrameCountBit);
    }

    [Fact]
    public void AllowsOnlyOneInFlightAndRejectsStaleResponseAfterReconnect()
    {
        var engine = new Iec101PerLinkTransactionEngine();
        engine.StartConnectionEpoch();
        using var old = engine.BeginFixed(11, true, 105, 2, "A");
        Assert.Throws<InvalidOperationException>(() => engine.BeginFixed(9, false, 105, 2, "A"));
        engine.MarkTransmitAttempt(old);
        engine.MarkAwaitingResponse(old);
        engine.StartConnectionEpoch();
        Assert.Equal(Iec101ExchangeState.Unknown, old.State);
        Assert.Throws<InvalidOperationException>(() =>
            engine.ValidateCandidate(old, Parser.Decode(Convert.FromHexString("100969007216"))));
        using var current = engine.BeginFixed(9, false, 105, 2, "A");
        Assert.NotEqual(old.LinkEpoch, current.LinkEpoch);
        Assert.NotEqual(old.ExchangeId, current.ExchangeId);
    }


    [Theory]
    [InlineData(9, false, "100B69007416")]
    [InlineData(10, true, "100969007216")]
    [InlineData(11, true, "100969007216")]
    public void T02SilentReadRetryKeepsOriginalBytesAndFcbAndOnlyOnce(
        int functionCode, bool usesFcv, string confirmedSecondary)
    {
        foreach (var source in new[] { "single", "A", "B" })
        {
            var engine = new Iec101PerLinkTransactionEngine();
            engine.StartConnectionEpoch();
            using var pending = engine.BeginFixed(functionCode, usesFcv, 105, 2, source);
            var original = pending.OriginalTransmit.ToArray();
            var originalFcb = pending.FrameCountBit;
            engine.MarkTransmitAttempt(pending);
            engine.MarkAwaitingResponse(pending);
            Assert.True(engine.TryAuthorizeUnchangedReadRetransmission(pending));
            Assert.False(engine.TryAuthorizeUnchangedReadRetransmission(pending));
            Assert.False(engine.FrameCountBit);
            Assert.Throws<InvalidOperationException>(() =>
                engine.BeginFixed(11, true, 105, 2, source));
            engine.MarkTransmitAttempt(pending);
            Assert.Equal(1, pending.ReadRetransmissions);
            Assert.Equal(originalFcb, pending.FrameCountBit);
            Assert.Equal(original, pending.OriginalTransmit);
            engine.MarkAwaitingResponse(pending);
            Assert.False(engine.TryAuthorizeUnchangedReadRetransmission(pending));
            if (functionCode == 9)
            {
                var status = engine.ValidateCandidate(pending,
                    Parser.Decode(Convert.FromHexString(confirmedSecondary)));
                Assert.True(status.IsValid);
            }
            else
            {
                var received = engine.ValidateCandidate(pending,
                    Parser.Decode(Convert.FromHexString(confirmedSecondary)));
                Assert.True(received.IsValid);
            }
            var outcome = engine.Complete(pending);
            Assert.Equal(usesFcv, outcome.FrameCountAdvanced);
            Assert.Equal(usesFcv, engine.FrameCountBit);
            Assert.False(engine.HasPendingExchange);
        }
    }

    [Fact]
    public void T02RejectsMutatingOutOfContextAndOldEpochRetry()
    {
        var engine = new Iec101PerLinkTransactionEngine();
        engine.StartConnectionEpoch();
        using (var control = engine.BeginVariable(
            new byte[] { 45, 1, 6, 0, 1, 0, 1, 0, 0, 0x81 }, 105, 2, "A"))
        {
            engine.MarkTransmitAttempt(control);
            engine.MarkAwaitingResponse(control);
            Assert.False(engine.TryAuthorizeUnchangedReadRetransmission(control));
            Assert.Equal(Iec101ExchangeVerdict.UnknownDelivery, engine.Complete(control).Verdict);
        }
        using (var read = engine.BeginFixed(10, true, 105, 2, "A"))
        {
            engine.MarkTransmitAttempt(read);
            engine.MarkAwaitingResponse(read);
            Assert.Throws<InvalidOperationException>(() => engine.MarkTransmitAttempt(read));
            var invalid = engine.ValidateCandidate(read,
                Parser.Decode(Convert.FromHexString("100B69007416")));
            Assert.False(invalid.IsValid);
            Assert.Equal(1, read.RejectedCandidates);
            Assert.False(engine.TryAuthorizeUnchangedReadRetransmission(read));
            Assert.Equal(Iec101ExchangeVerdict.Unconfirmed, engine.Complete(read).Verdict);
        }
        using var old = engine.BeginFixed(11, true, 105, 2, "A");
        engine.MarkTransmitAttempt(old);
        engine.MarkAwaitingResponse(old);
        engine.StartConnectionEpoch();
        Assert.Throws<InvalidOperationException>(() =>
            engine.TryAuthorizeUnchangedReadRetransmission(old));
        Assert.False(engine.FrameCountBit);
    }

    [Fact]
    public void ReadPollingMayPreserveOriginalFrameButMutatingUnknownMustNotReplay()
    {
        var engine = new Iec101PerLinkTransactionEngine();
        engine.StartConnectionEpoch();
        using (var read = engine.BeginFixed(11, true, 105, 2, "single"))
        {
            Assert.True(read.CanRetryUnchangedReadFrame);
            engine.MarkTransmitAttempt(read);
            engine.MarkAwaitingResponse(read);
            Assert.Equal(Iec101ExchangeVerdict.Unconfirmed, engine.Complete(read).Verdict);
        }
        using (var control = engine.BeginVariable(new byte[] { 45, 1, 6, 0, 105, 0, 1, 0, 0, 0x81 }, 105, 2, "single"))
        {
            Assert.False(control.CanRetryUnchangedReadFrame);
            engine.MarkTransmitAttempt(control);
            Assert.Equal(Iec101ExchangeState.Transmitted, control.State);
        }
        Assert.False(engine.FrameCountBit);
    }
}
