// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec10xObservationIdentityTests
{
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
