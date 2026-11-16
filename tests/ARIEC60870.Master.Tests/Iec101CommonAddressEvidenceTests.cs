// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Iec101;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101CommonAddressEvidenceTests
{
    [Fact]
    public void ThreeCompleteProcessAsdusAreRequiredBeforeGiOnlyAlternative()
    {
        var evidence = new Iec101CommonAddressEvidence(configuredCommonAddress: 105);
        var point = Process(ca: 1, cot: 3);
        Assert.True(Iec101CommonAddressEvidence.IsEligibleProcessAsdu(point));
        Assert.Null(evidence.UniqueAlternativeForGi);

        evidence.Observe(point);
        evidence.Observe(point);
        Assert.Null(evidence.UniqueAlternativeForGi);

        evidence.Observe(point);
        Assert.Equal(1, evidence.UniqueAlternativeForGi);
        Assert.False(evidence.IsAmbiguous);
    }

    [Fact]
    public void NegativeTestCommandUnknownCauseAndPartialAsdusNeverContribute()
    {
        var evidence = new Iec101CommonAddressEvidence(105);
        var disallowed = new Iec10xAsduDecode?[]
        {
            Process(ca: 1, cot: 3, negative: true),
            Process(ca: 1, cot: 3, test: true),
            Process(ca: 1, cot: 7), // confirmation, not process evidence
            Process(ca: 1, cot: 3, type: 100), // GI command/control ASDU
            Process(ca: 1, cot: 3, type: 127), // unknown standard type
            Process(ca: ushort.MaxValue, cot: 3), // global/broadcast CA
            Process(ca: 1, cot: 3, truncate: true),
            new Iec10xAsduDecoder().Decode(new byte[] { 1, 1, 3 }), // truncated header
            null
        };
        foreach (var asdu in disallowed)
        {
            Assert.False(Iec101CommonAddressEvidence.IsEligibleProcessAsdu(asdu));
            evidence.Observe(asdu);
        }

        Assert.False(evidence.IsAmbiguous);
        Assert.Null(evidence.UniqueAlternativeForGi);
        evidence.Observe(Process(ca: 1, cot: 20));
        evidence.Observe(Process(ca: 1, cot: 20));
        Assert.Null(evidence.UniqueAlternativeForGi);
        evidence.Observe(Process(ca: 1, cot: 20));
        Assert.Equal(1, evidence.UniqueAlternativeForGi);
    }

    [Fact]
    public void MoreThanOneValidStationFailsClosedEvenAfterThreshold()
    {
        var evidence = new Iec101CommonAddressEvidence(105);
        for (var i = 0; i < Iec101CommonAddressEvidence.MinimumProcessAsdus; i++)
        {
            evidence.Observe(Process(ca: 1, cot: 3));
        }
        Assert.Equal(1, evidence.UniqueAlternativeForGi);
        evidence.Observe(Process(ca: 2, cot: 3));
        Assert.True(evidence.IsAmbiguous);
        Assert.Null(evidence.UniqueAlternativeForGi);
        for (var i = 0; i < 100; i++)
        {
            evidence.Observe(Process(ca: 1, cot: 3));
        }
        Assert.Null(evidence.UniqueAlternativeForGi);
    }

    [Fact]
    public void ObservingConfiguredAndAlternativeCaIsAlsoAmbiguous()
    {
        var evidence = new Iec101CommonAddressEvidence(105);
        evidence.Observe(Process(ca: 105, cot: 3));
        for (var i = 0; i < 5; i++)
        {
            evidence.Observe(Process(ca: 1, cot: 3));
        }
        Assert.True(evidence.IsAmbiguous);
        Assert.Null(evidence.UniqueAlternativeForGi);
    }

    [Fact]
    public void ConfiguredCaNeverBecomesAnAlternative()
    {
        var evidence = new Iec101CommonAddressEvidence(105);
        for (var i = 0; i < 10; i++)
        {
            evidence.Observe(Process(ca: 105, cot: 3));
        }
        Assert.Null(evidence.UniqueAlternativeForGi);
        Assert.False(evidence.IsAmbiguous);
    }

    private static Iec10xAsduDecode Process(int ca, int cot, bool negative = false,
        bool test = false, byte type = 1, bool truncate = false)
    {
        byte cotOctet = (byte)(cot | (negative ? 0x40 : 0) | (test ? 0x80 : 0));
        byte[] raw =
        [
            type, 0x01, cotOctet, 0x00, // type, one object, COT and OA
            (byte)ca, (byte)(ca >> 8), // two-octet CA
            0x64, 0x00, 0x00, // IOA 100
            0x01 // SP=ON, or command qualifier for control types
        ];
        return new Iec10xAsduDecoder().Decode(truncate ? raw[..^1] : raw);
    }
}
