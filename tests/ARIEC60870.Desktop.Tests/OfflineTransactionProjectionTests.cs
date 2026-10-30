// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class OfflineTransactionProjectionTests
{
    private static CaptureFrameSnapshot Capture(long seq, byte control, byte[] asdu, string direction)
    {
        var payload = new byte[] { control, 105 }.Concat(asdu).ToArray();
        var n = (byte)payload.Length;
        var checksum = (byte)(payload.Sum(x => x) & 0xff);
        var raw = new byte[] { 0x68, n, n, 0x68 }.Concat(payload)
            .Concat(new byte[] { checksum, 0x16 }).ToArray();
        return new CaptureFrameSnapshot
        {
            Sequence = seq,
            DecoderSchema = "IEC10X-OBJECT-V1",
            CotWidth = 2, CaWidth = 2, IoaWidth = 3, LinkWidth = 1,
            RawHex = Convert.ToHexString(raw),
            Direction = direction,
            ProtocolMode = "101", ProtocolName = "IEC-101",
            PhysicalSource = "Link A",
            CaptureArrivalUtc = new DateTime(2026, 10, 30, 1, 2, 3, DateTimeKind.Utc)
        };
    }

    [Fact]
    public void OfflineGiLifecycleReconstructsSameTypedMilestonesWithExactLinkAndSource()
    {
        static byte[] Gi(byte cot) => new byte[]
        { 100, 1, cot, 0, 1, 0, 0, 0, 0, 20 };
        var gi = new Iec10xGiCasebook();
        var request = new EvidenceRow(Capture(1, 0x53, Gi(6), "TX")).Source;
        var actcon = new EvidenceRow(Capture(2, 0x08, Gi(7), "RX")).Source;
        var terminal = new EvidenceRow(Capture(3, 0x08, Gi(10), "RX")).Source;
        Assert.Equal(105, request.LinkAddress);
        Assert.Equal(105, actcon.LinkAddress);
        Assert.NotNull(gi.Observe(request));
        Assert.Equal("Positive GI ACTCON", gi.Observe(actcon)!.Phase);
        var done = gi.Observe(terminal)!;
        var row = Iec10xGiEventProjection.Project(terminal, done);
        Assert.Equal("GI", row.TransactionKind);
        Assert.Equal(done.Case.CaseId, row.TransactionCaseId);
        Assert.Equal(105, done.Case.LinkAddress);
        Assert.Contains("coverage unknown", row.NewValue);
        Assert.Equal(0, gi.ActiveCount);
    }

    [Fact]
    public void OfflineCommandStagesHaveTypedCaseReferenceAndNoFakePhysicalSuccess()
    {
        static byte[] Control(byte cot) => new byte[]
        { 46, 1, cot, 0, 1, 0, 25, 0, 0, 0x82 };
        var ledger = new Iec10xCommandCasebook();
        var tx = new EvidenceRow(Capture(10, 0x53, Control(6), "TX")).Source;
        var rx = new EvidenceRow(Capture(11, 0x08, Control(7), "RX")).Source;
        Assert.True(ledger.Observe(tx));
        Assert.True(ledger.Observe(rx));
        var matched = ledger.FindByEvidence(rx);
        Assert.NotNull(matched);
        var eventRow = Iec10xCommandEventProjection.Project(rx, matched!.CaseId);
        Assert.NotNull(eventRow);
        Assert.Equal("Command", eventRow!.TransactionKind);
        Assert.Equal(matched.CaseId, eventRow.TransactionCaseId);
        Assert.Contains("accepted", matched.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("physical operation proved", matched.Interpretation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyWithoutDecoderMetadataDoesNotInventAGiCase()
    {
        var snapshot = new CaptureFrameSnapshot
        { Sequence = 33, RawHex = "680D0D68", ProtocolMode = "101", Direction = "RX" };
        var row = new EvidenceRow(snapshot);
        Assert.Null(row.Source.AsduDetails);
        Assert.Null(new Iec10xGiCasebook().Observe(row.Source));
    }
}
