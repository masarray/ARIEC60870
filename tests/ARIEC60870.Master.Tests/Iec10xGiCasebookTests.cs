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
        Assert.Equal(2, done.Case.InvalidQualityObjects);
        Assert.Contains("Group GI 1", done.Case.Description);
    }

    [Fact]
    public void OverlapBecomesAmbiguousNeverLastWins()
    {
        var b = new Iec10xGiCasebook();
        b.Observe(Gi(6));
        b.Observe(Gi(6));
        Assert.Equal("Ambiguous · overlapping GI", b.Observe(Gi(7, true))!.Case.Status);
        Assert.Equal("Ambiguous", b.Observe(Gi(10, true))!.Case.Status);
    }
}
