// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class EventOriginalFrameRetentionTests
{
    [Fact]
    public void EventRetainsSourceFrameEvenWhenTracePreviewNoLongerContainsIt()
    {
        var original = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 501,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            PhysicalSource = "Link A",
            PhysicalStreamId = 1,
            RawStreamOffset = 1724,
            RawHex = "68 0C 0C 68 08 69 2E 01 47 00 69 00 AE 0B 01 82 16"
        };
        var entry = new Iec103RelayEventLogEntry
        {
            EvidenceSequenceNumber = original.SequenceNumber,
            ProtocolMode = original.ProtocolMode,
            PhysicalSource = original.PhysicalSource,
            EvidenceReference = original.EvidenceReference,
            SignalName = "Control command",
            NewValue = "RTU negative confirmation",
            RawHex = original.RawHex
        };
        var eventRow = new RelayEventRow(entry, original);
        var liveTrace = new BoundedRingBuffer<Iec103MasterEvidenceEvent>(2);
        liveTrace.Add(new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 900, ProtocolMode = original.ProtocolMode,
            Direction = FrameDirection.SlaveToMaster, PhysicalSource = original.PhysicalSource,
            PhysicalStreamId = 1, RawStreamOffset = 2000
        });
        liveTrace.Add(new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 901, ProtocolMode = original.ProtocolMode,
            Direction = FrameDirection.SlaveToMaster, PhysicalSource = original.PhysicalSource,
            PhysicalStreamId = 1, RawStreamOffset = 2010
        });
        Assert.DoesNotContain(liveTrace.Snapshot(), x =>
            Iec10xObservationIdentity.SamePhysicalFrame(x, eventRow.EvidenceReference,
                eventRow.PhysicalSource, original.ProtocolMode, eventRow.Sequence));
        Assert.Same(original, eventRow.OriginalFrameEvidence);
        Assert.True(Iec10xObservationIdentity.SamePhysicalFrame(eventRow.OriginalFrameEvidence!,
            eventRow.EvidenceReference, eventRow.PhysicalSource, original.ProtocolMode,
            eventRow.Sequence));
        Assert.False(Iec10xObservationIdentity.SamePhysicalFrame(original,
            "101|stream:0|byte:1724|object:0", "Link A", original.ProtocolMode, 501));
        Assert.False(Iec10xObservationIdentity.SamePhysicalFrame(original,
            eventRow.EvidenceReference, "Link B", original.ProtocolMode, 501));
    }

    [Fact]
    public void MultiObjectEventKeepsItsPhysicalParentIdentity()
    {
        var original = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = 202,
            PhysicalFrameSequenceNumber = 200,
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            Direction = FrameDirection.SlaveToMaster,
            PhysicalSource = "SINGLE",
            RawHex = "68 04 01 00 00 00"
        };
        var row = new RelayEventRow(new Iec103RelayEventLogEntry
        {
            EvidenceSequenceNumber = 202,
            ProtocolMode = original.ProtocolMode,
            PhysicalSource = original.PhysicalSource,
            EvidenceReference = original.EvidenceReference
        }, original);
        Assert.Contains("|seq:200|", row.EvidenceReference);
        Assert.True(Iec10xObservationIdentity.SamePhysicalFrame(row.OriginalFrameEvidence!,
            row.EvidenceReference, row.PhysicalSource, original.ProtocolMode, row.Sequence));
    }
}
