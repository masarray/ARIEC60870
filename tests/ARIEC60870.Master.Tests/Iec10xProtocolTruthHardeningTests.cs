// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec10xProtocolTruthHardeningTests
{
    private static byte[] Asdu(byte type, byte vsq, int ioa, int width, params byte[] elements)
    {
        byte[] header = [type, vsq, 0x03, 0x00, 0x01, 0x00];
        return header.Concat(Enumerable.Range(0, width)
            .Select(i => (byte)(ioa >> (8 * i)))).Concat(elements).ToArray();
    }

    [Theory]
    [InlineData(1, 255)]
    [InlineData(2, 65535)]
    [InlineData(3, 16777215)]
    public void Sq1PreventsOverflowWithoutLosingOriginalBytes(int width, int largest)
    {
        var d = new Iec10xAsduDecoder(2, 2, width);
        var raw = Asdu(1, 0x82, largest, width, 1, 0);
        var overflow = d.Decode(raw);
        Assert.True(overflow.IsHeaderValid);
        Assert.Equal(raw, overflow.RawBytes);
        Assert.Equal(2, overflow.ObjectCount);
        Assert.Single(overflow.Objects);
        Assert.Equal(largest, overflow.Objects[0].InformationObjectAddress);
        Assert.Contains(overflow.Issues, x => x.Contains("implied IOA", StringComparison.Ordinal));
        var legal = d.Decode(Asdu(1, 0x82, largest - 1, width, 1, 0));
        Assert.Empty(legal.Issues);
        Assert.Equal(new[] { largest - 1, largest }, legal.Objects.Select(o => o.InformationObjectAddress));
        Assert.Empty(d.Decode(Asdu(1, 1, largest, width, 1)).Issues);
    }

    [Fact]
    public void MalformedHeaderHasNoTrustedNumericFields()
    {
        byte[] raw = [1, 1, 3, 0, 1];
        var decoded = new Iec10xAsduDecoder().Decode(raw);
        Assert.False(decoded.IsHeaderValid);
        Assert.Equal("Malformed ASDU", decoded.TypeName);
        Assert.Equal(raw, decoded.RawBytes);
        Assert.Contains("header not decoded", decoded.ShortMeaning);
        Assert.DoesNotContain("COT=0", decoded.ShortMeaning);
    }

    [Fact]
    public void Ti21IsNormalizedWithoutQualityOctet()
    {
        var d = new Iec10xAsduDecoder();
        var value = d.Decode(Asdu(21, 1, 513, 3, 0, 0x80));
        Assert.Empty(value.Issues);
        Assert.Equal("M_ME_ND_1 normalized measured value without QDS", value.TypeName);
        Assert.Equal("Normalized=-1", value.ValueText);
        Assert.Null(value.FirstObject!.QualityDetails);
        Assert.True(Iec10xProcessEventPolicy.IsMeasurementOrCounterType(21));
        var sq1 = d.Decode(Asdu(21, 0x82, 513, 3, 0, 0x80, 0xFF, 0x7F));
        Assert.Empty(sq1.Issues);
        Assert.Equal(new[] { 513, 514 }, sq1.Objects.Select(o => o.InformationObjectAddress));
        Assert.All(sq1.Objects, o => Assert.Null(o.QualityDetails));
        Assert.NotEmpty(d.Decode(Asdu(21, 1, 513, 3, 0)).Issues);
    }

    [Fact]
    public void Ti70AndTi106CarryTypedValuesAndOriginalOctets()
    {
        var d = new Iec10xAsduDecoder();
        var initialization = d.Decode(Asdu(70, 1, 0, 3, 0x81));
        Assert.Empty(initialization.Issues);
        Assert.Contains("LPC=1", initialization.ValueText);
        Assert.Null(initialization.FirstObject!.QualityDetails);
        var delay = d.Decode(Asdu(106, 1, 0, 3, 0x34, 0x12));
        Assert.Empty(delay.Issues);
        Assert.Equal("C_CD_NA_1 delay acquisition", delay.TypeName);
        Assert.Contains("4660 ms", delay.ValueText);
        Assert.Equal(new byte[]{ 0x34, 0x12 }, delay.FirstObject!.RawBytes);
    }

    [Fact]
    public void FloatUsesRoundtripPrecisionAndRejectsNonFinite()
    {
        var d = new Iec10xAsduDecoder();
        var normal = d.Decode(Asdu(13, 1, 5, 3,
            BitConverter.GetBytes(0.12345679f).Concat(new byte[]{ 0 }).ToArray()));
        Assert.Empty(normal.Issues);
        Assert.Equal("Float=0.12345679", normal.ValueText);
        var nan = d.Decode(Asdu(13, 1, 5, 3,
            BitConverter.GetBytes(float.NaN).Concat(new byte[]{ 0 }).ToArray()));
        Assert.Contains(nan.Issues, x => x.Contains("non-finite", StringComparison.Ordinal));
        Assert.Equal(5, nan.FirstObject!.RawBytes.Count);
    }

    [Fact]
    public void MissingOrPartialAsduCannotBecomeTrustedProcessEvidence()
    {
        var d = new Iec10xAsduDecoder();
        var complete = d.Decode(Asdu(1, 1, 5, 3, 1));
        var partial = d.Decode(Asdu(1, 1, 5, 3));
        Assert.True(Iec10xObservationIdentity.HasTrustedDecodedPayload(
            new Iec103MasterEvidenceEvent { AsduDetails = complete, Category = "RX" }));
        Assert.False(Iec10xObservationIdentity.HasTrustedDecodedPayload(
            new Iec103MasterEvidenceEvent { Category = "RX" }));
        Assert.False(Iec10xObservationIdentity.HasTrustedDecodedPayload(
            new Iec103MasterEvidenceEvent { AsduDetails = partial, Category = "RX" }));
    }

    [Fact]
    public void OnlyValidTi100Cot10CanEndGi()
    {
        var d = new Iec10xAsduDecoder();
        var valid = Asdu(100, 1, 0, 3, 20); valid[2] = 10;
        Assert.True(Iec10xGiTermination.IsObserved(d.Decode(valid)));
        var command = Asdu(45, 1, 0, 3, 1); command[2] = 10;
        Assert.False(Iec10xGiTermination.IsObserved(d.Decode(command)));
        foreach (var flags in new byte[]{ 0x4A, 0x8A })
        {
            var changed = valid.ToArray(); changed[2] = flags;
            Assert.False(Iec10xGiTermination.IsObserved(d.Decode(changed)));
        }
        Assert.False(Iec10xGiTermination.IsObserved(d.Decode(valid[..^1])));
        var wrongIoa = Asdu(100, 1, 1, 3, 20); wrongIoa[2] = 10;
        Assert.False(Iec10xGiTermination.IsObserved(d.Decode(wrongIoa)));
    }
}
