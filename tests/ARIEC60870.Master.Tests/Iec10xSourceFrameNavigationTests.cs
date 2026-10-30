// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec10xSourceFrameNavigationTests
{
    private static Iec103MasterEvidenceEvent Frame(
        Iec60870ProtocolMode protocol, string source, long sequence,
        int? stream = null, long? offset = null, int objectIndex = 0)
    {
        return new Iec103MasterEvidenceEvent
        {
            ProtocolMode = protocol, PhysicalSource = source, SequenceNumber = sequence,
            PhysicalStreamId = stream, RawStreamOffset = offset,
            Direction = ARIEC60870.Core.Model.FrameDirection.SlaveToMaster
        };
    }

    [Fact]
    public void PassiveSiblingObjectReferencesMatchOnePhysicalFrameButNeverAnotherSource()
    {
        var frame = Frame(Iec60870ProtocolMode.Iec101, "Link A", 11, 1, 512);
        var sibling = frame.EvidenceReference.Replace("|object:0", "|object:2", StringComparison.Ordinal);
        Assert.True(Iec10xObservationIdentity.SamePhysicalFrame(frame, sibling,
            "Link A", Iec60870ProtocolMode.Iec101, 19));
        Assert.False(Iec10xObservationIdentity.SamePhysicalFrame(frame, sibling,
            "Link B", Iec60870ProtocolMode.Iec101, 19));
        Assert.False(Iec10xObservationIdentity.SamePhysicalFrame(frame, sibling,
            "Link A", Iec60870ProtocolMode.Iec104, 19));
        Assert.False(Iec10xObservationIdentity.SamePhysicalFrame(
            Frame(Iec60870ProtocolMode.Iec101, "Link A", 11, 1, 513), sibling,
            "Link A", Iec60870ProtocolMode.Iec101, 19));
    }

    [Fact]
    public void ActiveSiblingObjectUsesOriginalPhysicalFrameSequenceNotObjectEventSequence()
    {
        var physical = Frame(Iec60870ProtocolMode.Iec101, "Link A", 200);
        var sibling = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101, PhysicalSource = "Link A",
            SequenceNumber = 201, PhysicalFrameSequenceNumber = 200,
            Direction = ARIEC60870.Core.Model.FrameDirection.SlaveToMaster
        };
        Assert.Contains("|seq:200|", sibling.EvidenceReference);
        Assert.True(Iec10xObservationIdentity.SamePhysicalFrame(physical, sibling.EvidenceReference,
            sibling.PhysicalSource, sibling.ProtocolMode, sibling.SequenceNumber));
        Assert.False(Iec10xObservationIdentity.SamePhysicalFrame(
            Frame(Iec60870ProtocolMode.Iec101, "Link A", 201),
            sibling.EvidenceReference, sibling.PhysicalSource, sibling.ProtocolMode, sibling.SequenceNumber));
    }

    [Fact]
    public void ActiveSequenceMustMatchEvenIfAddressOrRawBytesWouldMatch()
    {
        var original = Frame(Iec60870ProtocolMode.Iec104, "", 100);
        var unrelated = Frame(Iec60870ProtocolMode.Iec104, "", 101);
        Assert.True(Iec10xObservationIdentity.SamePhysicalFrame(original, original.EvidenceReference,
            "SINGLE", Iec60870ProtocolMode.Iec104, 100));
        Assert.False(Iec10xObservationIdentity.SamePhysicalFrame(unrelated, original.EvidenceReference,
            "SINGLE", Iec60870ProtocolMode.Iec104, 100));
        Assert.False(Iec10xObservationIdentity.SamePhysicalFrame(original, "",
            "SINGLE", Iec60870ProtocolMode.Iec104, 100));
    }
}
