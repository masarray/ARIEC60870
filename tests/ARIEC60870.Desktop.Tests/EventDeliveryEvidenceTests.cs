// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class EventDeliveryEvidenceTests
{
    private static readonly DateTime ArrivalUtc = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    private static Iec103RelayEventLogEntry Entry(int cot, string time = "", bool invalid = false) =>
        new()
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            CauseDetails = new Iec10xCauseDetails((byte)cot, cot, false, false, 0),
            RelayTimeText = time,
            RelayTimeInvalid = invalid,
            ArrivalTimeUtc = ArrivalUtc
        };

    private static string DeviceLocal(DateTime utc) =>
        utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(1, "Cyclic")]
    [InlineData(2, "Background")]
    [InlineData(11, "Return info")]
    [InlineData(12, "Return info")]
    [InlineData(20, "GI data")]
    [InlineData(36, "GI data")]
    public void StandardCotClassesDoNotImplyBuffered(int cot, string label)
    {
        Assert.Equal(label, EventDeliveryEvidenceClassifier.Classify(Entry(cot)).Label);
    }

    [Fact]
    public void LargeAbsoluteClockGapAloneIsNeverBufferEvidence()
    {
        var result = EventDeliveryEvidenceClassifier.Classify(Entry(3, DeviceLocal(ArrivalUtc.AddMinutes(-2))));
        Assert.Equal("Spontaneous", result.Label);
        Assert.Contains("sequence", result.Basis, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpontaneousNearPCArrivalIsNeverAdvertisedAsProvenLive()
    {
        var result = EventDeliveryEvidenceClassifier.Classify(Entry(3, DeviceLocal(ArrivalUtc.AddSeconds(-1))));
        Assert.Equal("Spontaneous", result.Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("PC 2026-11-21 12:00:00.000")]
    [InlineData("no timestamp")]
    public void UntimedOrPCTimeDoesNotBecomeProvenBufferedOrLive(string time)
    {
        Assert.Equal("Spontaneous", EventDeliveryEvidenceClassifier.Classify(Entry(3, time)).Label);
    }

    [Fact]
    public void InvalidTimestampAndNegativeConfirmationRemainUnverified()
    {
        Assert.Equal("Spontaneous",
            EventDeliveryEvidenceClassifier.Classify(Entry(3, DeviceLocal(ArrivalUtc.AddHours(-2)), true)).Label);
        var negative = new Iec103RelayEventLogEntry
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            CauseDetails = new Iec10xCauseDetails(0x43, 3, true, false, 0),
            ArrivalTimeUtc = ArrivalUtc,
            RelayTimeText = DeviceLocal(ArrivalUtc.AddDays(-1))
        };
        Assert.Equal("Unverified", EventDeliveryEvidenceClassifier.Classify(negative).Label);
    }

    [Fact]
    public void EventCotDoesNotInferBufferedFromOneOldTimestamp()
    {
        var item = Entry(3, DeviceLocal(ArrivalUtc.AddMinutes(-2)));
        var row = new RelayEventRow(item);
        Assert.Equal("3 · spontaneous", row.CotLabel);
        Assert.Equal(3, item.CauseDetails?.Code);
        Assert.Contains("sequence", row.DeliveryEvidence.Basis, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NearTimeOrInvalidDateNeverAddsBufferedCue()
    {
        var near = new RelayEventRow(Entry(3, DeviceLocal(ArrivalUtc.AddSeconds(-1))));
        Assert.DoesNotContain("BUFFERED", near.CotLabel);
        var invalid = new RelayEventRow(Entry(3, DeviceLocal(ArrivalUtc.AddMinutes(-2)), invalid: true));
        Assert.DoesNotContain("BUFFERED", invalid.CotLabel);
        var gi = new RelayEventRow(Entry(20, DeviceLocal(ArrivalUtc.AddHours(-2))));
        Assert.DoesNotContain("BUFFERED", gi.CotLabel);
    }


    private static Iec103RelayEventLogEntry Timed(long frame, DateTime host, DateTime device,
        string source = "Link A", int ca = 1, int cot = 3, bool invalid = false,
        Iec60870ProtocolMode protocol = Iec60870ProtocolMode.Iec101) => new()
        {
            EvidenceSequenceNumber = frame,
            PhysicalSource = source,
            CommonAddress = ca,
            ProtocolMode = protocol,
            CauseDetails = new Iec10xCauseDetails((byte)cot, cot, false, false, 0),
            RelayTimeText = device.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            RelayTimeInvalid = invalid,
            ArrivalTimeUtc = host
        };

    [Fact]
    public void StableSevenHourOffsetWithNormalRateNeverShowsBuffer()
    {
        var detector = new EventDeliverySequenceClassifier();
        var rtu = new DateTime(2026, 11, 21, 2, 43, 34);
        for (var i = 1; i <= 1000; i++)
        {
            var source = Timed(i, ArrivalUtc.AddMilliseconds(i * 100d),
                rtu.AddMilliseconds(i * 100d));
            var result = detector.Observe(source);
            Assert.Equal("Spontaneous", result.Label);
            Assert.DoesNotContain("(buffer)", new RelayEventRow(source,
                deliveryEvidence: result).CotLabel);
        }
    }

    [Fact]
    public void ReplayCatchupShowsBufferThenClearsOnFirstNormalInterval()
    {
        var detector = new EventDeliverySequenceClassifier();
        var rtu = new DateTime(2026, 11, 21, 2, 43, 34);
        var first = Timed(10, ArrivalUtc, rtu);
        Assert.Equal("Spontaneous", detector.Observe(first).Label);
        Assert.Equal("Spontaneous", detector.Observe(
            Timed(11, ArrivalUtc.AddSeconds(1), rtu.AddMinutes(1))).Label);

        var third = Timed(12, ArrivalUtc.AddSeconds(2), rtu.AddMinutes(2));
        var catchup = detector.Observe(third);
        Assert.Equal("Buffer", catchup.Label);
        var bufferedRow = new RelayEventRow(third, deliveryEvidence: catchup);
        Assert.Equal("3 · spontaneous (buffer)", bufferedRow.CotLabel);
        Assert.Contains("not a standard IEC", catchup.Basis, StringComparison.OrdinalIgnoreCase);

        // Actual Class 1 polling continues, but RTU clock advances at normal
        // observation rate. The NEXT row must immediately lose the suffix.
        var live = Timed(13, ArrivalUtc.AddSeconds(3),
            rtu.AddMinutes(2).AddSeconds(1));
        var normal = detector.Observe(live);
        Assert.Equal("Spontaneous", normal.Label);
        Assert.Equal("3 · spontaneous", new RelayEventRow(live,
            deliveryEvidence: normal).CotLabel);
    }

    [Fact]
    public void LinkAndCaAreIndependentAndOneAsduCannotFakeThreeObservations()
    {
        var detector = new EventDeliverySequenceClassifier();
        var t = new DateTime(2026, 11, 21, 2, 43, 34);
        detector.Observe(Timed(101, ArrivalUtc, t));
        detector.Observe(Timed(102, ArrivalUtc.AddSeconds(1), t.AddMinutes(1)));
        Assert.Equal("Buffer", detector.Observe(
            Timed(103, ArrivalUtc.AddSeconds(2), t.AddMinutes(2))).Label);

        var sibling = Timed(103, ArrivalUtc.AddSeconds(2), t.AddMinutes(2).AddHours(1));
        Assert.Equal("Buffer", detector.Observe(sibling).Label);
        Assert.Equal("Spontaneous", detector.Observe(
            Timed(104, ArrivalUtc.AddSeconds(3), t.AddMinutes(2).AddSeconds(1),
                source: "Link B")).Label);
        Assert.Equal("Spontaneous", detector.Observe(
            Timed(105, ArrivalUtc.AddSeconds(4), t.AddMinutes(2).AddSeconds(2),
                ca: 105)).Label);
        Assert.Equal("Spontaneous", detector.Observe(
            Timed(106, ArrivalUtc.AddSeconds(5), t.AddMinutes(2).AddSeconds(3),
                protocol: Iec60870ProtocolMode.Iec104)).Label);
    }

    [Fact]
    public void SeparateObjectSequenceNumbersFromSamePhysicalFrameCountOnce()
    {
        var detector = new EventDeliverySequenceClassifier();
        var t = new DateTime(2026, 11, 21, 2, 43, 34);
        detector.Observe(Timed(500, ArrivalUtc, t), physicalFrameSequence: 500);
        detector.Observe(Timed(501, ArrivalUtc.AddSeconds(1), t.AddMinutes(1)),
            physicalFrameSequence: 501);
        var oneFrame = detector.Observe(Timed(502, ArrivalUtc.AddSeconds(2),
            t.AddMinutes(2)), physicalFrameSequence: 502);
        Assert.Equal("Buffer", oneFrame.Label);
        var sibling = detector.Observe(Timed(503, ArrivalUtc.AddSeconds(2),
            t.AddMinutes(3)), physicalFrameSequence: 502);
        Assert.Equal("Buffer", sibling.Label);
        // Different object sequence number within parent frame #502 must not
        // reset or advance the clock series; next normal frame ends the cue.
        var normal = detector.Observe(Timed(504, ArrivalUtc.AddSeconds(3),
            t.AddMinutes(2).AddSeconds(1)), physicalFrameSequence: 504);
        Assert.Equal("Spontaneous", normal.Label);
    }

    [Fact]
    public void RestartAndClockRollbackDoNotCarryBufferIntoNewSequence()
    {
        var detector = new EventDeliverySequenceClassifier();
        var t = new DateTime(2026, 11, 21, 2, 43, 34);
        detector.Observe(Timed(1, ArrivalUtc, t));
        detector.Observe(Timed(2, ArrivalUtc.AddSeconds(1), t.AddMinutes(1)));
        Assert.Equal("Buffer", detector.Observe(
            Timed(3, ArrivalUtc.AddSeconds(2), t.AddMinutes(2))).Label);
        Assert.Equal("Spontaneous", detector.Observe(
            Timed(4, ArrivalUtc.AddSeconds(3), t.AddMinutes(1))).Label);
        detector.Reset();
        Assert.Equal("Spontaneous", detector.Observe(
            Timed(1, ArrivalUtc.AddSeconds(4), t.AddMinutes(3))).Label);
    }

    [Fact]
    public void GiBackgroundInvalidAndUnpacedFramesDoNotClaimBuffer()
    {
        var detector = new EventDeliverySequenceClassifier();
        var t = new DateTime(2026, 11, 21, 2, 43, 34);
        var observations = new[]
        {
            Timed(1, ArrivalUtc, t, cot: 20),
            Timed(2, ArrivalUtc.AddSeconds(1), t.AddMinutes(1), cot: 2),
            Timed(3, ArrivalUtc.AddSeconds(2), t.AddMinutes(2), invalid: true),
            Timed(4, ArrivalUtc.AddSeconds(3), t.AddMinutes(3)),
            Timed(5, ArrivalUtc.AddSeconds(4), t.AddMinutes(4))
        };
        Assert.Equal("GI data", detector.Observe(observations[0]).Label);
        Assert.Equal("Background", detector.Observe(observations[1]).Label);
        Assert.Equal("Spontaneous", detector.Observe(observations[2]).Label);
        Assert.Equal("Spontaneous", detector.Observe(observations[3]).Label);
        Assert.Equal("Spontaneous", detector.Observe(observations[4]).Label);
        Assert.Equal("Spontaneous", detector.Observe(
            Timed(6, ArrivalUtc.AddMinutes(2), t.AddHours(1))).Label);
    }

    private static Iec103MasterEvidenceEvent Wire(
        long sequence, bool primary, bool acd = false,
        string source = "Link A", byte? controlOverride = null, long? parent = null)
    {
        const byte link = 105;
        var control = controlOverride ?? (primary ? (byte)0x5A :
            acd ? (byte)0x28 : (byte)0x08);
        byte[] wire;
        if (primary)
        {
            wire = [0x10, control, link, (byte)((control + link) & 0xFF), 0x16];
        }
        else
        {
            // Real FT1.2 variable frame: C=0x28 ACD=1 / C=0x08 ACD=0.
            // Type30, VSQ1, COT3, CA1, IOA7, SPS and CP56Time2a.
            byte[] asdu =
                [30, 1, 3, 0, 1, 0, 7, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0];
            var length = asdu.Length + 2;
            wire = new byte[length + 6];
            wire[0] = 0x68;
            wire[1] = (byte)length;
            wire[2] = (byte)length;
            wire[3] = 0x68;
            wire[4] = control;
            wire[5] = link;
            Array.Copy(asdu, 0, wire, 6, asdu.Length);
            wire[4 + length] = (byte)(wire.AsSpan(4, length).ToArray()
                .Sum(x => (int)x) & 0xFF);
            wire[5 + length] = 0x16;
        }

        var parsed = Ft12Parser.ForIec101(1).Decode(wire);
        Assert.True(parsed.IsLengthValid);
        Assert.True(parsed.IsChecksumValid);
        return new Iec103MasterEvidenceEvent
        {
            SequenceNumber = sequence,
            PhysicalFrameSequenceNumber = parent,
            TimestampUtc = ArrivalUtc.AddMilliseconds(sequence),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = source,
            Direction = primary ? FrameDirection.MasterToSlave :
                FrameDirection.SlaveToMaster,
            LinkAddress = link,
            RawHex = parsed.Hex,
            Frame = parsed
        };
    }

    [Fact]
    public void MatchedFc10AcdQueuePendingLabelsBufferAndClearsWithAcdZero()
    {
        var tracker = new EventFrameExchangeTracker();
        var detector = new EventDeliverySequenceClassifier();
        var device = new DateTime(2026, 11, 21, 3, 20, 50);
        for (var i = 0; i < 6; i++)
        {
            var poll = Wire(1000 + i * 2, primary: true);
            var reply = Wire(1001 + i * 2, primary: false, acd: i < 4);
            tracker.Observe(poll);
            tracker.Observe(reply);
            var matched = tracker.RequestForEvent(reply);
            Assert.Same(poll, matched);
            var pending = EventDeliverySequenceClassifier
                .VerifiedIec101Class1Pending(reply, matched);
            Assert.Equal(i < 4, pending);
            var source = Timed(reply.SequenceNumber, ArrivalUtc.AddMilliseconds(i * 100),
                device.AddMilliseconds(i * 100));
            var result = detector.Observe(source,
                physicalFrameSequence: reply.SequenceNumber,
                verifiedClass1Pending: pending);
            Assert.Equal(i < 4 ? "Buffer" : "Spontaneous", result.Label);
            var cot = new RelayEventRow(source, reply,
                observedRequestFrame: poll, deliveryEvidence: result).CotLabel;
            Assert.Equal(i < 4 ? "3 · spontaneous (buffer)" : "3 · spontaneous", cot);
        }
    }

    [Fact]
    public void ACDObservationNeedsExactValidFc10Fc8PairNotNearbyPolling()
    {
        var poll = Wire(200, primary: true);
        var acd = Wire(201, primary: false, acd: true);
        Assert.True(EventDeliverySequenceClassifier
            .VerifiedIec101Class1Pending(acd, poll));
        Assert.Null(EventDeliverySequenceClassifier
            .VerifiedIec101Class1Pending(acd, null));
        Assert.Null(EventDeliverySequenceClassifier
            .VerifiedIec101Class1Pending(acd,
                Wire(199, primary: true, controlOverride: 0x5B)));
        Assert.Null(EventDeliverySequenceClassifier
            .VerifiedIec101Class1Pending(acd,
                Wire(199, primary: true, source: "Link B")));
        Assert.Null(EventDeliverySequenceClassifier
            .VerifiedIec101Class1Pending(Wire(202, primary: false,
                acd: true, source: "Link B"), poll));
        Assert.Null(EventDeliverySequenceClassifier
            .VerifiedIec101Class1Pending(Wire(203, primary: false,
                acd: true, controlOverride: 0x29), poll));

        var invalid = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 204,
            TimestampUtc = ArrivalUtc.AddSeconds(1),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = "Link A",
            Direction = FrameDirection.SlaveToMaster,
            LinkAddress = 105,
            RawHex = "10 28 69 00 16",
            Frame = new Ft12FrameDecode
            {
                IsLengthValid = true,
                IsChecksumValid = false,
                LinkAddress = 105,
                LinkControl = new LinkControlInfo
                {
                    Prm = false,
                    FunctionCode = 8,
                    Acd = true
                }
            }
        };
        Assert.Null(EventDeliverySequenceClassifier
            .VerifiedIec101Class1Pending(invalid, poll));
    }

    [Fact]
    public void SamePhysicalResponseSiblingSharesWireDecisionAndSessionReset()
    {
        var tracker = new EventFrameExchangeTracker();
        var detector = new EventDeliverySequenceClassifier();
        var tx = Wire(300, primary: true);
        var rx = Wire(301, primary: false, acd: true, parent: 301);
        var sibling = Wire(302, primary: false, acd: true, parent: 301);
        tracker.Observe(tx);
        tracker.Observe(rx);
        var matched = tracker.RequestForEvent(rx);
        Assert.Same(tx, tracker.RequestForEvent(sibling));
        var pending = EventDeliverySequenceClassifier
            .VerifiedIec101Class1Pending(rx, matched);
        var first = Timed(301, ArrivalUtc, new DateTime(2026, 11, 21, 3, 20, 50));
        var second = Timed(302, ArrivalUtc, new DateTime(2026, 11, 21, 3, 20, 51));
        Assert.Equal("Buffer", detector.Observe(first,
            physicalFrameSequence: 301, verifiedClass1Pending: pending).Label);
        Assert.Equal("Buffer", detector.Observe(second,
            physicalFrameSequence: 301, verifiedClass1Pending: pending).Label);
        detector.Reset();
        Assert.Equal("Spontaneous", detector.Observe(first,
            physicalFrameSequence: 301).Label);
    }

    [Fact]
    public void CommandAndGiAreTransactionsNotRealtimeOrBufferClaims()
    {
        var cmd = new Iec103RelayEventLogEntry
        {
            TransactionKind = "Command",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            CauseDetails = new Iec10xCauseDetails(3, 3, false, false, 0),
            ArrivalTimeUtc = ArrivalUtc
        };
        Assert.Equal("Transaction", EventDeliveryEvidenceClassifier.Classify(cmd).Label);
    }
}
