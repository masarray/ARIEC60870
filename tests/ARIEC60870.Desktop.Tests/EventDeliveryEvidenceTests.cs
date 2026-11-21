// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
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
    public void SpontaneousWithKnownLargeLagIsOnlySuspectedDelayed()
    {
        var result = EventDeliveryEvidenceClassifier.Classify(Entry(3, DeviceLocal(ArrivalUtc.AddMinutes(-2))));
        Assert.Equal("Delayed?", result.Label);
        Assert.Contains("clock", result.Basis, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpontaneousNearPCArrivalIsNeverAdvertisedAsProvenLive()
    {
        var result = EventDeliveryEvidenceClassifier.Classify(Entry(3, DeviceLocal(ArrivalUtc.AddSeconds(-1))));
        Assert.Equal("Near-live?", result.Label);
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
