// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class PassiveIec101ControlIndexerTests
{
    private readonly PassiveIec101ControlIndexer _index = new(1, 2, 2, 3);
    private static readonly DateTime Utc = new(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ValidNegativeActconKeepsRequestOffsetAndTypedEvidence()
    {
        Assert.True(_index.TryObserve(Frame(0, 20, 6, 0x82), out var select));
        Assert.Contains("MASTER SELECT", select!.Explanation);
        Assert.True(_index.TryObserve(Frame(1, 44, 0x47, 0x82), out var rejected));
        Assert.True(rejected!.NeedsAttention);
        Assert.Contains("NEGATIVE CONFIRMATION", rejected.Explanation);
        Assert.Contains("request RX offset=20", rejected.Explanation);
        Assert.Equal(44, rejected.StreamOffset);
    }

    [Fact]
    public void TwoLinksAndTwoLinkAddressesCannotCrossConfirm()
    {
        Assert.True(_index.TryObserve(Frame(0, 1, 6, 0x82, link: 105), out _));
        Assert.True(_index.TryObserve(Frame(1, 2, 7, 0x82, link: 106), out var wrongAddress));
        Assert.Contains("not observed", wrongAddress!.Explanation);
        Assert.True(_index.TryObserve(Frame(3, 3, 7, 0x82, link: 105), out var wrongLink));
        Assert.Contains("not observed", wrongLink!.Explanation);
        Assert.True(_index.TryObserve(Frame(1, 4, 7, 0x82, link: 105), out var matched));
        Assert.Contains("POSITIVE ACTCON", matched!.Explanation);
    }

    [Fact]
    public void BadChecksumTestBitAndMiswiredDirectionNeverBecomeVerifiedControl()
    {
        Assert.False(_index.TryObserve(Frame(0, 0, 6, 0x82) with { ChecksumValid = false }, out _));
        Assert.False(_index.TryObserve(Frame(0, 0, 6, 0x82) with { StreamId = 1 }, out _)); // master PRM on RTU tap
        Assert.True(_index.TryObserve(Frame(0, 1, 0x86, 0x82), out var test));
        Assert.Contains("TEST bit set", test!.Explanation);
        Assert.Equal(0, _index.PendingCount);
    }

    [Fact]
    public void LinkAckAndOrdinaryGiAreNotIndexedAsCommands()
    {
        Assert.False(_index.TryObserve(new PassiveObservedFrame(1, 0, Utc,
            "Single", true, new byte[] { 0xE5 }), out _));
        Assert.False(_index.TryObserve(Frame(0, 0, 6, 0x82, type: 100), out _));
    }

    internal static PassiveObservedFrame Frame(byte stream, long offset, byte cot,
        byte qualifier, byte link = 105, byte type = 46)
    {
        var asdu = new byte[]
        {
            type, 1, cot, 0, 5, 0, 123, 0, 0, qualifier
        };
        var body = new byte[] { (byte)((stream & 1) == 0 ? 0x53 : 0x08), link }
            .Concat(asdu).ToArray();
        var raw = new byte[] { 0x68, (byte)body.Length, (byte)body.Length, 0x68 }
            .Concat(body)
            .Concat(new byte[] { (byte)(body.Sum(x => x) & 0xFF), 0x16 }).ToArray();
        return new PassiveObservedFrame(stream, offset, Utc.AddMilliseconds(offset),
            "Variable", true, raw);
    }
    [Fact]
    public void SecondaryCOT6WithValidChecksumIsFlaggedAsDirectionAnomaly()
    {
        Assert.True(_index.TryObserve(Frame(0, 20, 6, 0x82), out _));
        Assert.True(_index.TryObserve(Frame(1, 30, 6, 0x82), out var anomaly));
        Assert.True(anomaly!.NeedsAttention);
        Assert.True(anomaly.DirectionCotMismatch);
        Assert.Contains("DIRECTION/COT ANOMALY", anomaly.Explanation);
        Assert.Equal(1, _index.PendingCount);
        Assert.True(_index.TryObserve(Frame(1, 40, 7, 0x82), out var confirmation));
        Assert.Contains("POSITIVE ACTCON", confirmation!.Explanation);
    }

}
