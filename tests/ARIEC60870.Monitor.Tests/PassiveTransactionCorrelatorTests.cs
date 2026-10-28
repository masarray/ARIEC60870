// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class PassiveTransactionCorrelatorTests
{
    private static readonly DateTime Origin = new(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Class2RequestAndDataResponseAreMatchedWithOriginalOffsets()
    {
        var engine = new PassiveIec101TransactionCorrelator();
        var events = new List<PassiveTransactionEvent>();
        engine.Observe(Frame(0, 0, 0, 0x5B), events.Add); // FC11, FCV1
        engine.Observe(Frame(1, 0, 85, 0x08, variable: true), events.Add);
        var match = Assert.Single(events);
        Assert.Equal(PassiveTransactionKind.Matched, match.Kind);
        Assert.Equal(11, match.RequestFunction);
        Assert.Equal(8, match.ReplyFunction);
        Assert.Equal((byte)0, match.RequestStream);
        Assert.Equal(0, match.RequestOffset);
        Assert.Equal((byte)1, match.ObservedStream);
        Assert.Equal(0, match.ObservedOffset);
        Assert.Equal(85, match.HostDelayMilliseconds);
        Assert.Equal(1, engine.Matched);
        Assert.Equal(0, engine.PendingCount);
    }

    [Fact]
    public void E5MeansShortAckOrShortNoDataAccordingToPendingRequest()
    {
        var engine = new PassiveIec101TransactionCorrelator();
        var events = new List<PassiveTransactionEvent>();
        engine.Observe(Frame(0, 0, 0, 0x40), events.Add); // FC0 Reset Remote Link confirmation
        engine.Observe(Single(1, 0, 20, 0xE5), events.Add);
        var shortAck = Assert.Single(events);
        Assert.Equal(PassiveTransactionKind.Matched, shortAck.Kind);
        Assert.Null(shortAck.ReplyFunction); // E5 carries no actual secondary control byte.
        Assert.Contains("FC0 equivalent inferred", shortAck.Meaning, StringComparison.Ordinal);

        engine.Observe(Frame(0, 5, 40, 0x5B), events.Add);
        engine.Observe(Single(1, 1, 75, 0xE5), events.Add);
        var shortNoData = Assert.Single(events, x => x.RequestFunction == 11);
        Assert.Equal(PassiveTransactionKind.Matched, shortNoData.Kind);
        Assert.Null(shortNoData.ReplyFunction);
        Assert.Contains("FC9 equivalent inferred", shortNoData.Meaning, StringComparison.Ordinal);

        engine.Observe(Frame(0, 10, 90, 0x5A), events.Add);
        engine.Observe(Frame(1, 2, 120, 0x09), events.Add);
        Assert.Contains(events, x => x.ReplyFunction == 9 &&
            x.Meaning.Contains("no requested data (FC9)", StringComparison.Ordinal));
    }

    [Fact]
    public void A2IsNotPermittedIec101AndMustNeverSatisfyPendingLinkRequest()
    {
        var engine = new PassiveIec101TransactionCorrelator();
        var output = new List<PassiveTransactionEvent>();
        engine.Observe(Frame(0, 0, 0, 0x40), output.Add);
        engine.Observe(Single(1, 0, 20, 0xA2), output.Add);
        var invalid = Assert.Single(output);
        Assert.Equal(PassiveTransactionKind.InvalidFrame, invalid.Kind);
        Assert.Contains("not permitted", invalid.Meaning, StringComparison.Ordinal);
        Assert.Equal(1, engine.PendingCount);
        Assert.Equal(0, engine.Matched);
    }

    [Fact]
    public void E5CannotReplaceMandatoryFc11LinkStatusResponse()
    {
        var engine = new PassiveIec101TransactionCorrelator();
        var output = new List<PassiveTransactionEvent>();
        engine.Observe(Frame(0, 0, 0, 0x49), output.Add); // primary FC9
        engine.Observe(Single(1, 0, 30, 0xE5), output.Add);
        Assert.Equal(PassiveTransactionKind.IncompatibleReply, Assert.Single(output).Kind);
        Assert.Equal(1, engine.PendingCount);
        engine.Observe(Frame(1, 1, 50, 0x0B), output.Add);
        Assert.Equal(PassiveTransactionKind.Matched, output[^1].Kind);
    }

    [Fact]
    public void RepeatedFcbWithSameRawIsRetryNotNewTransaction()
    {
        var engine = new PassiveIec101TransactionCorrelator();
        var events = new List<PassiveTransactionEvent>();
        engine.Observe(Frame(0, 0, 0, 0x5A), events.Add);
        engine.Observe(Frame(0, 5, 250, 0x5A), events.Add);
        var retry = Assert.Single(events);
        Assert.Equal(PassiveTransactionKind.RetryObserved, retry.Kind);
        Assert.Equal(2, retry.Attempts);
        Assert.Equal(0, retry.RequestOffset);
        engine.Observe(Frame(1, 0, 280, 0x09), events.Add);
        var matched = Assert.Single(events, x => x.Kind == PassiveTransactionKind.Matched);
        Assert.Equal(2, matched.Attempts);
        Assert.Equal(1, engine.Retries);
    }

    [Fact]
    public void TimedOutRequestAndUnmatchedReplyRemainSeparateEvidence()
    {
        var engine = new PassiveIec101TransactionCorrelator(new(ResponseTimeoutMilliseconds: 1000));
        var events = new List<PassiveTransactionEvent>();
        engine.Observe(Frame(0, 0, 0, 0x5B), events.Add);
        engine.AdvanceTime(Origin.AddMilliseconds(1100), events.Add);
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.ResponseTimeout);
        engine.Observe(Frame(1, 0, 1200, 0x08), events.Add);
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.UnmatchedReply);
        Assert.DoesNotContain(events, x => x.Kind == PassiveTransactionKind.Matched);
    }

    [Fact]
    public void StandbyLinkStatusIsNormalTransactionNotFault()
    {
        var engine = new PassiveIec101TransactionCorrelator();
        var events = new List<PassiveTransactionEvent>();
        engine.Observe(Frame(0, 0, 0, 0x49), events.Add); // Request link status
        engine.Observe(Frame(1, 0, 90, 0x0B), events.Add); // Secondary link status
        Assert.Equal(PassiveTransactionKind.Matched, Assert.Single(events).Kind);
        Assert.Equal(11, events[0].ReplyFunction);
        Assert.Equal(0, engine.TimedOut);
    }

    [Fact]
    public void ACDAndDFCAreObservationsNotPositiveAcknowledgement()
    {
        var engine = new PassiveIec101TransactionCorrelator();
        var events = new List<PassiveTransactionEvent>();
        engine.Observe(Frame(0, 0, 0, 0x5A), events.Add);
        engine.Observe(Frame(1, 0, 55, 0x38), events.Add); // PRM0, ACD1 DFC1 FC8
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.DfcBusy);
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.AcdDemand);
        Assert.Equal(0, engine.Matched);
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.Matched);
    }

    [Fact]
    public void BadChecksumAndWrongAddressCannotSatisfyPendingRequest()
    {
        var engine = new PassiveIec101TransactionCorrelator();
        var events = new List<PassiveTransactionEvent>();
        engine.Observe(Frame(0, 0, 0, 0x5B), events.Add);
        engine.Observe(Frame(1, 0, 10, 0x08, valid: false), events.Add);
        engine.Observe(Frame(1, 5, 20, 0x08, address: 2), events.Add);
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.InvalidFrame);
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.UnmatchedReply);
        Assert.Equal(1, engine.PendingCount);
        Assert.DoesNotContain(events, x => x.Kind == PassiveTransactionKind.Matched);
    }

    [Fact]
    public void SingleAddresslessAckNeverPicksArbitraryOpenLink()
    {
        var engine = new PassiveIec101TransactionCorrelator();
        var events = new List<PassiveTransactionEvent>();
        engine.Observe(Frame(0, 0, 0, 0x49, address: 1), events.Add);
        engine.Observe(Frame(0, 5, 10, 0x49, address: 2), events.Add);
        engine.Observe(Single(1, 0, 35, 0xE5), events.Add);
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.AmbiguousSingleReply);
        Assert.Equal(2, engine.PendingCount);
    }

    [Fact]
    public void BalancedModeCanMatchPrimaryFromEitherDirection()
    {
        var engine = new PassiveIec101TransactionCorrelator(new(LinkMode: "Balanced"));
        var events = new List<PassiveTransactionEvent>();
        engine.Observe(Frame(1, 0, 0, 0x49), events.Add);
        engine.Observe(Frame(0, 0, 15, 0x0B), events.Add);
        Assert.Single(events);
        Assert.Equal(PassiveTransactionKind.Matched, events[0].Kind);
        Assert.DoesNotContain(events, x => x.Kind == PassiveTransactionKind.DirectionConflict);
    }

    [Fact]
    public void OppositePrimaryInUnbalancedProducesEvidenceNotFabricatedReply()
    {
        var engine = new PassiveIec101TransactionCorrelator(new(LinkMode: "Unbalanced"));
        var events = new List<PassiveTransactionEvent>();
        engine.Observe(Frame(1, 0, 0, 0x49), events.Add);
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.DirectionConflict);
        Assert.DoesNotContain(events, x => x.Kind == PassiveTransactionKind.Matched);
    }

    [Fact]
    public void ResourceBoundAndEndOfCaptureAreExplicit()
    {
        var engine = new PassiveIec101TransactionCorrelator(new(MaxTrackedLinks: 3));
        var events = new List<PassiveTransactionEvent>();
        for (var i = 1; i <= 20; i++)
            engine.Observe(Frame(0, (i - 1) * 5, i * 2, 0x49, address: (byte)i), events.Add);
        Assert.Equal(3, engine.TrackedLinks);
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.CapacityEviction);
        engine.Complete(events.Add);
        Assert.Equal(0, engine.PendingCount);
        Assert.Equal(3, events.Count(x => x.Kind == PassiveTransactionKind.PendingAtEnd));
    }

    [Fact]
    public void LinkWidth2AndFCBReuseAreHandledWithoutGuessingRemoteLink()
    {
        var engine = new PassiveIec101TransactionCorrelator(new(LinkAddressWidth: 2));
        var events = new List<PassiveTransactionEvent>();
        engine.Observe(Frame(0, 0, 0, 0x5A, address: 0x169, width: 2), events.Add);
        engine.Observe(Frame(1, 0, 50, 0x09, address: 0x169, width: 2), events.Add);
        engine.Observe(Frame(0, 6, 200, 0x5A, address: 0x169, width: 2), events.Add);
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.ReusedConfirmedFcb &&
            x.LinkAddress == 0x169);
        Assert.Equal(1, engine.Matched);
    }

    [Fact]
    public void FourStreamsNeverPairAddresslessE5OrFixedReplyAcrossCircuits()
    {
        var correlator = new PassiveIec101TransactionCorrelator();
        var events = new List<PassiveTransactionEvent>();
        correlator.Observe(Frame(0, 0, 0, 0x5B), events.Add); // A Class2 request
        correlator.Observe(Single(3, 0, 30, 0xE5), events.Add); // B RTU E5
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.UnmatchedReply &&
            x.ObservedStream == 3);
        Assert.Equal(1, correlator.PendingCount); // A unchanged
        correlator.Observe(Frame(2, 0, 50, 0x5B), events.Add); // B Class2 request
        correlator.Observe(Single(3, 1, 80, 0xE5), events.Add); // B E5
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.Matched &&
            x.ObservedStream == 3 && x.RequestStream == 2);
        Assert.Equal(1, correlator.PendingCount); // A still unchanged
        correlator.Observe(Frame(1, 0, 90, 0x09), events.Add); // A RTU fixed no-data
        Assert.Contains(events, x => x.Kind == PassiveTransactionKind.Matched &&
            x.ObservedStream == 1 && x.RequestStream == 0);
        Assert.Equal(0, correlator.PendingCount);
    }

    private static PassiveObservedFrame Frame(byte stream, long offset, int ms, byte control,
        int address = 1, int width = 1, bool variable = false, bool valid = true)
    {
        var payload = new byte[1 + width];
        payload[0] = control;
        for (var i = 0; i < width; i++) payload[i + 1] = (byte)(address >> (i * 8));
        byte[] wire;
        if (!variable)
        {
            wire = new byte[payload.Length + 3];
            wire[0] = 0x10;
            payload.CopyTo(wire, 1);
            wire[^2] = (byte)(payload.Sum(x => x) & 0xff);
            wire[^1] = 0x16;
        }
        else
        {
            wire = new byte[payload.Length + 6];
            wire[0] = 0x68; wire[1] = (byte)payload.Length;
            wire[2] = (byte)payload.Length; wire[3] = 0x68;
            payload.CopyTo(wire, 4);
            wire[^2] = (byte)(payload.Sum(x => x) & 0xff);
            wire[^1] = 0x16;
        }
        if (!valid) wire[^2] ^= 0x33;
        return new(stream, offset, Origin.AddMilliseconds(ms),
            variable ? "Variable" : "Fixed", valid, wire);
    }

    private static PassiveObservedFrame Single(byte stream, long offset, int ms, byte control)
        => new(stream, offset, Origin.AddMilliseconds(ms), "Single", true, new[] { control });
}
