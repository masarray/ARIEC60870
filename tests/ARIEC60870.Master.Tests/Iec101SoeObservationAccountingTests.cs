// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101SoeObservationAccountingTests
{
    private static Iec103MasterEvidenceEvent[] DecodeTwoObjects(byte cot = 3,
        bool test = false)
    {
        var raw = new byte[] {
            3, 2, (byte)(cot | (test ? 0x80 : 0)), 0, 105, 0,
            201, 0, 0, 2,
            202, 0, 0, 1
        };
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(raw);
        Assert.Empty(asdu.Issues);
        return asdu.Objects.Select((obj, i) => new Iec103MasterEvidenceEvent
        {
            SequenceNumber = i + 1,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            Category = i == 0 ? "RX" : "RX Object",
            TypeId = asdu.TypeId,
            CauseDetails = asdu.CauseDetails,
            AsduDetails = asdu,
            InformationObjectDetails = obj,
            IsRelayValue = true,
            IsRelayEdgeEvent = true,
            CommonAddressNumber = asdu.CommonAddress,
            InformationObjectAddress = obj.InformationObjectAddress
        }).ToArray();
    }

    private static Iec103MasterEvidenceEvent Reproject(
        Iec103MasterEvidenceEvent source, long sequence, string? category = null)
        => new()
        {
            SequenceNumber = sequence,
            ProtocolMode = source.ProtocolMode,
            Direction = source.Direction,
            Category = category ?? source.Category,
            TypeId = source.TypeId,
            CauseDetails = source.CauseDetails,
            AsduDetails = source.AsduDetails,
            InformationObjectDetails = source.InformationObjectDetails,
            IsRelayValue = source.IsRelayValue,
            IsRelayEdgeEvent = source.IsRelayEdgeEvent,
            CommonAddressNumber = source.CommonAddressNumber,
            InformationObjectAddress = source.InformationObjectAddress
        };

    [Fact]
    public void MultiObjectAsduCountsEveryDigitalSoeObjectNotOneFrame()
    {
        var telegram = DecodeTwoObjects();
        Assert.Equal(2, telegram.Count(Iec101SoeObservationPolicy.IsAcceptedSpontaneousDigitalObject));
        Assert.Equal(new int?[] { 201, 202 },
            telegram.Select(x => x.InformationObjectAddress));
    }

    [Fact]
    public void TestBitGiOrRejectedRxCannotFabricateSpontaneousSoe()
    {
        Assert.All(DecodeTwoObjects(cot: 20), x =>
            Assert.False(Iec101SoeObservationPolicy.IsAcceptedSpontaneousDigitalObject(x)));
        Assert.All(DecodeTwoObjects(test: true), x =>
            Assert.False(Iec101SoeObservationPolicy.IsAcceptedSpontaneousDigitalObject(x)));
        var warning = Reproject(DecodeTwoObjects()[0], 1, "RX Warning");
        Assert.False(Iec101SoeObservationPolicy.IsAcceptedSpontaneousDigitalObject(warning));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(500)]
    [InlineData(2000)]
    public void BoundedEvidenceHistoryAccountsForEverySyntheticSoeAndEveryEviction(int count)
    {
        var fixture = DecodeTwoObjects()[0];
        var history = new Iec101BoundedEvidenceHistory(100);
        long observed = 0;
        for (var i = 1; i <= count; i++)
        {
            var eventFact = Reproject(fixture, i);
            if (Iec101SoeObservationPolicy.IsAcceptedSpontaneousDigitalObject(eventFact))
                observed++;
            history.Append(eventFact);
        }

        var snapshot = history.Snapshot();
        Assert.Equal(count, observed);
        Assert.Equal(Math.Min(100, count), snapshot.Length);
        Assert.Equal(Math.Max(0, count - 100), history.Evicted);
        Assert.Equal(Math.Max(1, count - 99), snapshot[0].SequenceNumber);
        Assert.Equal(count, snapshot[^1].SequenceNumber);
    }
}
