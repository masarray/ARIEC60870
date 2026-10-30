// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec10xGiCasebookTests
{
    private static long _sequence;
    private static Iec103MasterEvidenceEvent Decode(byte[] asduBytes, bool rtu,
        string source = "A", int link = 105)
    {
        var d = new Iec10xAsduDecoder(2, 2, 3).Decode(asduBytes);
        Assert.Empty(d.Issues);
        return new Iec103MasterEvidenceEvent
        {
            SequenceNumber = System.Threading.Interlocked.Increment(ref _sequence),
            TimestampUtc = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = rtu ? FrameDirection.SlaveToMaster : FrameDirection.MasterToSlave,
            PhysicalSource = source, LinkAddress = link, TypeId = d.TypeId,
            CommonAddressNumber = d.CommonAddress, InformationObjectAddress = d.InformationObjectAddress,
            AsduDetails = d, CauseDetails = d.CauseDetails, CommandDetails = d.CommandDetails,
            RawHex = Convert.ToHexString(asduBytes)
        };
    }

    private static Iec103MasterEvidenceEvent Gi(int cot, bool rtu = false, int ca = 1,
        int qoi = 20, bool negative = false, string source = "A", int link = 105)
        => Decode(new byte[] {
            100, 1, (byte)(cot | (negative ? 0x40 : 0)), 0,
            (byte)ca, 0, 0, 0, 0, (byte)qoi
        }, rtu, source, link);

    private static Iec103MasterEvidenceEvent TwoObjects(int ca = 1, int cot = 20,
        string source = "A", int link = 105, byte quality = 0)
        => Decode(new byte[] {
            1, 0x82, (byte)cot, 0, (byte)ca, 0, 100, 0, 0,
            (byte)(1 | quality), 0
        }, true, source, link);

    [Fact]
    public void FullGiCountsDecodedSq1ObjectsWithoutClaimingCoverage()
    {
        var b = new Iec10xGiCasebook();
        Assert.Equal("GI request", b.Observe(Gi(6))!.Phase);
        Assert.Equal("Positive GI ACTCON", b.Observe(Gi(7, true))!.Phase);
        var data = TwoObjects();
        Assert.Null(b.Observe(data));
        Assert.Null(b.Observe(data)); // same physical frame twice must not inflate counts
        // The active Master may emit one additional event per information object.
        // Those events share the decoder instance but can have fresh sequence numbers.
        var child = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = System.Threading.Interlocked.Increment(ref _sequence),
            TimestampUtc = data.TimestampUtc,
            ProtocolMode = data.ProtocolMode,
            Direction = data.Direction,
            PhysicalSource = data.PhysicalSource,
            LinkAddress = data.LinkAddress,
            TypeId = data.TypeId,
            AsduDetails = data.AsduDetails,
            RawHex = data.RawHex
        };
        Assert.Null(b.Observe(child));
        var done = b.Observe(Gi(10, true))!;
        Assert.Equal("ACTTERM observed · coverage unknown", done.Case.Status);
        Assert.Equal(1, done.Case.DataFrames);
        Assert.Equal(2, done.Case.DataObjects);
        Assert.Equal(2, done.Case.UniqueIoas);
        Assert.Contains("Unknown", done.Case.Coverage);
        Assert.Equal(0, b.ActiveCount);
        Assert.Equal(4, done.Case.Stages.Count);
    }

    [Fact]
    public void GiSourceCaAndLinkCannotBorrowUnrelatedDataOrReply()
    {
        var b = new Iec10xGiCasebook();
        b.Observe(Gi(6));
        b.Observe(Gi(7, true, ca: 2));
        b.Observe(Gi(7, true, source: "B"));
        b.Observe(Gi(7, true, link: 106));
        b.Observe(TwoObjects(ca: 2));
        b.Observe(TwoObjects(source: "B"));
        b.Observe(TwoObjects(link: 106));
        var done = b.Observe(Gi(10, true))!;
        Assert.Equal(0, done.Case.DataFrames);
        Assert.Equal("ACTTERM without observed ACTCON · incomplete", done.Case.Status);
        Assert.Equal(4, b.Snapshot().Count);
    }

    [Fact]
    public void NegativeAndMissingRequestStayExplicit()
    {
        var b = new Iec10xGiCasebook();
        Assert.Contains("Unmatched", b.Observe(Gi(10, true))!.Case.Status);
        b.Observe(Gi(6));
        Assert.Equal("GI rejected", b.Observe(Gi(7, true, negative: true))!.Case.Status);
        Assert.Equal(0, b.ActiveCount);
        b.Observe(Gi(6));
        b.EndObservation();
        Assert.Contains("Observed incomplete", b.Snapshot().Last().Status);
    }

    [Fact]
    public void GroupGiOnlyCountsItsMatchingGroupCot()
    {
        var b = new Iec10xGiCasebook();
        b.Observe(Gi(6, qoi: 21));
        b.Observe(Gi(7, true, qoi: 21));
        b.Observe(TwoObjects(cot: 20));
        b.Observe(TwoObjects(cot: 21, quality: 0x80));
        var done = b.Observe(Gi(10, true, qoi: 21))!;
        Assert.Equal(1, done.Case.DataFrames);
        // SQ=1 has two information elements; only the first has IV set.
        Assert.Equal(1, done.Case.InvalidQualityObjects);
        Assert.Contains("Group GI 1", done.Case.Description);
    }

    [Fact]
    public void DataRetentionLimitNeverPreventsActtermOrNegativeConfirmation()
    {
        var b = new Iec10xGiCasebook(seenFrameLimit: 2);
        b.Observe(Gi(6));
        b.Observe(Gi(7, true));
        Assert.Null(b.Observe(TwoObjects())); // beyond budget, data counting stops
        var terminal = b.Observe(Gi(10, true))!;
        Assert.Equal("ACTTERM observed · coverage unknown", terminal.Case.Status);
        Assert.True(terminal.Case.CountLimited);
        Assert.Equal(0, terminal.Case.DataFrames);
        Assert.Equal(0, b.ActiveCount);
        Assert.Equal("GI request", b.Observe(Gi(6))!.Phase);
    }

    [Fact]
    public void TwoDifferentConcurrentQoiGroupsAlsoBecomeAmbiguous()
    {
        var b = new Iec10xGiCasebook();
        b.Observe(Gi(6, qoi: 20));
        var other = b.Observe(Gi(6, qoi: 21))!;
        Assert.Contains("Ambiguous", other.Case.Status);
        Assert.All(b.Snapshot(), x => Assert.Contains("Ambiguous", x.Status));
        b.Observe(TwoObjects(cot: 20));
        Assert.All(b.Snapshot(), x => Assert.Equal(0, x.DataFrames));
    }

    [Fact]
    public void OverlapBecomesAmbiguousNeverLastWins()
    {
        var b = new Iec10xGiCasebook();
        b.Observe(Gi(6));
        b.Observe(Gi(6));
        Assert.Equal("Ambiguous", b.Observe(Gi(7, true))!.Case.Status);
        Assert.Equal("Ambiguous", b.Observe(Gi(10, true))!.Case.Status);
    }
}
