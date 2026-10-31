// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec10xObservationIdentityTests
{
    [Theory]
    [InlineData(1, 3, true)]
    [InlineData(3, 11, true)]
    [InlineData(13, 3, true)]
    [InlineData(13, 12, true)]
    [InlineData(15, 3, true)]
    [InlineData(34, 3, true)]
    [InlineData(37, 3, true)]
    [InlineData(13, 20, false)]
    [InlineData(13, 2, false)]
    [InlineData(45, 3, false)]
    [InlineData(200, 3, false)]
    public void SourceTypedProcessFactsAreNotGuessedFromDisplayLabels(int type, int cot, bool expected)
    {
        var e = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            Direction = FrameDirection.SlaveToMaster,
            TypeId = type, CauseOfTransmission = cot,
            CommonAddressNumber = 105, InformationObjectAddress = 1001
        };
        Assert.Equal(expected, Iec10xObservationIdentity.IsReceivedProcessEvent(e));
    }

    [Fact]
    public void MalformedObjectOrAsduIsNotPromotedToProcessEvent()
    {
        foreach (var type in new[] { 1, 13, 15, 31 })
        {
            var asduIssues = new Iec103MasterEvidenceEvent
            {
                Direction = FrameDirection.SlaveToMaster,
                ProtocolMode = Iec60870ProtocolMode.Iec104,
                TypeId = type, CauseOfTransmission = 3,
                InformationObjectAddress = 1001,
                AsduDetails = new Iec10xAsduDecode { Issues = ["Truncated information element"] }
            };
            Assert.False(Iec10xObservationIdentity.IsReceivedProcessEvent(asduIssues));
            Assert.False(Iec10xObservationIdentity.ShouldRecordProcessEvent(asduIssues, true));

            var objectIssues = new Iec103MasterEvidenceEvent
            {
                Direction = FrameDirection.SlaveToMaster,
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                TypeId = type, CauseOfTransmission = 3,
                InformationObjectAddress = 1001,
                InformationObjectDetails = new Iec10xInformationObject
                {
                    InformationObjectAddress = 1001, Issues = ["Incomplete raw value"]
                }
            };
            Assert.False(Iec10xObservationIdentity.IsReceivedProcessEvent(objectIssues));
            Assert.False(Iec10xObservationIdentity.ShouldRecordProcessEvent(objectIssues, true));
        }
    }

    [Fact]
    public void FiftyThousandMixedDigitalAnalogCounterObservationsKeepSourceIdentity()
    {
        const int count = 50_000;
        var facts = 0;
        var refs = new HashSet<string>();
        for (var i = 0; i < count; i++)
        {
            var e = new Iec103MasterEvidenceEvent
            {
                ProtocolMode = i % 2 == 0 ? Iec60870ProtocolMode.Iec101 : Iec60870ProtocolMode.Iec104,
                Direction = FrameDirection.SlaveToMaster,
                TypeId = (i % 4) switch { 0 => 1, 1 => 3, 2 => 13, _ => 15 },
                CauseOfTransmission = i % 11 == 0 ? 20 : 3,
                CommonAddressNumber = 105 + i % 3,
                InformationObjectAddress = 1000 + i % 300,
                PhysicalSource = i % 2 == 0 ? "Link A" : "Link B",
                SequenceNumber = i + 1
            };
            var isEvent = Iec10xObservationIdentity.IsReceivedProcessEvent(e);
            if (isEvent != (e.CauseOfTransmission == 3))
                throw new Xunit.Sdk.XunitException(
                    $"Unexpected process classification i={i}, type={e.TypeId}, cot={e.CauseOfTransmission}, " +
                    $"protocol={e.ProtocolMode}, direction={e.Direction}, category={e.Category}, " +
                    $"frame={(e.Frame is null ? "none" : "provided")}, ioa={e.InformationObjectAddress}, " +
                    $"digital={Iec10xObservationIdentity.IsDigitalProcessType(e.TypeId)}, " +
                    $"analog={Iec10xObservationIdentity.IsMeasurementOrCounterType(e.TypeId)}.");
            if (!isEvent) continue;
            facts++;
            refs.Add(e.EvidenceReference + "|" + e.PhysicalSource);
        }
        Assert.Equal(count - (count + 10) / 11, facts);
        Assert.Equal(facts, refs.Count);
    }

    [Fact]
    public void PhysicalKeySeparatesProtocolChannelCommonAddressAndIoa()
    {
        var a = Iec10xObservationIdentity.ValueKey(Iec60870ProtocolMode.Iec101, "Link A", 105, 77);
        Assert.Equal(a, Iec10xObservationIdentity.ValueKey(Iec60870ProtocolMode.Iec101, " link a ", 105, 77));
        Assert.NotEqual(a, Iec10xObservationIdentity.ValueKey(Iec60870ProtocolMode.Iec101, "Link B", 105, 77));
        Assert.NotEqual(a, Iec10xObservationIdentity.ValueKey(Iec60870ProtocolMode.Iec101, "Link A", 106, 77));
        Assert.NotEqual(a, Iec10xObservationIdentity.ValueKey(Iec60870ProtocolMode.Iec101, "Link A", 105, 78));
        Assert.NotEqual(a, Iec10xObservationIdentity.ValueKey(Iec60870ProtocolMode.Iec104, "Link A", 105, 77));
        Assert.NotEqual(a, Iec10xObservationIdentity.ValueKey(Iec60870ProtocolMode.Iec101, "Link A", null, 77));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(20, false)]
    public void SpontaneousAndReturnInformationAreFactsEvenWithoutTransition(int cot, bool expected)
    {
        var e = new Iec103MasterEvidenceEvent {
            Direction = FrameDirection.SlaveToMaster, ProtocolMode = Iec60870ProtocolMode.Iec101,
            TypeId = 31, InformationObjectAddress = 77, CommonAddressNumber = 105,
            CauseOfTransmission = cot, CauseName = "observed"
        };
        Assert.Equal(expected, Iec10xObservationIdentity.IsReceivedDigitalEvent(e));
        if (expected) {
            Assert.Contains("first observation", Iec10xObservationIdentity.EventReason(e, false, false));
            Assert.Contains("repeat observation", Iec10xObservationIdentity.EventReason(e, true, false));
        }
    }

    [Fact]
    public void GiSnapshotOnlyEntersSoeForKnownChangeAndNeverForTx()
    {
        var receivedGi = new Iec103MasterEvidenceEvent {
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            TypeId = 31, InformationObjectAddress = 77,
            CommonAddressNumber = 105, CauseOfTransmission = 20
        };
        Assert.False(Iec10xObservationIdentity.ShouldRecordDigitalEvent(receivedGi, false));
        Assert.True(Iec10xObservationIdentity.ShouldRecordDigitalEvent(receivedGi, true));
        var sentGi = new Iec103MasterEvidenceEvent {
            Direction = FrameDirection.MasterToSlave,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            TypeId = 31, InformationObjectAddress = 77, CauseOfTransmission = 20
        };
        Assert.False(Iec10xObservationIdentity.ShouldRecordDigitalEvent(sentGi, true));

        var spontaneous = new Iec103MasterEvidenceEvent {
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            TypeId = 31, InformationObjectAddress = 77, CauseOfTransmission = 3
        };
        Assert.True(Iec10xObservationIdentity.ShouldRecordDigitalEvent(spontaneous, false));
    }

    [Fact]
    public void CommandAndOutboundEventsAreNotFabricatedAsSpontaneous()
    {
        foreach (var direction in new[] { FrameDirection.MasterToSlave, FrameDirection.Unknown }) {
            var e = new Iec103MasterEvidenceEvent {
                Direction = direction, ProtocolMode = Iec60870ProtocolMode.Iec101,
                TypeId = 31, InformationObjectAddress = 1, CauseOfTransmission = 3
            };
            Assert.False(Iec10xObservationIdentity.IsReceivedDigitalEvent(e));
        }
        var command = new Iec103MasterEvidenceEvent {
            Direction = FrameDirection.SlaveToMaster, ProtocolMode = Iec60870ProtocolMode.Iec104,
            TypeId = 45, InformationObjectAddress = 1, CauseOfTransmission = 3
        };
        Assert.False(Iec10xObservationIdentity.IsReceivedDigitalEvent(command));
    }

    [Fact]
    public void PassiveReferenceUsesOriginalStreamOffsetAndObjectIndex()
    {
        var obj = new Iec10xInformationObject { Index = 2, InformationObjectAddress = 77 };
        var e = new Iec103MasterEvidenceEvent {
            ProtocolMode = Iec60870ProtocolMode.Iec101, SequenceNumber = 88,
            PhysicalSource = "Link B", PhysicalStreamId = 3, RawStreamOffset = 12500,
            InformationObjectDetails = obj
        };
        Assert.Contains("stream:3|byte:12500|object:2", e.EvidenceReference);
        var active = new Iec103MasterEvidenceEvent { SequenceNumber = 88 };
        Assert.Contains("seq:88|object:0", active.EvidenceReference);
    }
}
