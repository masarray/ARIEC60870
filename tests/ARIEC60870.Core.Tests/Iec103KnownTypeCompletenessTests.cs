// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using Xunit;

namespace ARIEC60870.Core.Tests;

public sealed class Iec103KnownTypeCompletenessTests
{
    [Theory]
    [InlineData(2, 0)]
    [InlineData(6, 0)]
    [InlineData(6, 3)]
    [InlineData(7, 0)]
    [InlineData(8, 0)]
    [InlineData(9, 0)]
    [InlineData(9, 1)]
    [InlineData(20, 0)]
    public void KnownTypeWithMissingMandatoryElementsIsNotOk(byte type, int bytes)
    {
        var raw = new byte[] { type, 1, 1, 1, 0xC0, 0x24 }
            .Concat(Enumerable.Repeat((byte)0, bytes)).ToArray();
        var result = new AsduDecoder().Decode(raw);
        Assert.Equal(DecodeStatus.Error, result.Status);
        Assert.Null(result.Dpi);
        Assert.Null(result.NumericValue);
        Assert.Null(result.Time);
        Assert.Equal(bytes, result.DataBytes.Count);
        Assert.Contains(result.Notes, x => x.Contains("Incomplete IEC-103", StringComparison.Ordinal));
    }

    [Fact]
    public void SignedMeasurandStillDecodesWithRealBytes()
    {
        byte[] raw = [9, 1, 1, 1, 0xC0, 0x24, 0, 0x80];
        var result = new AsduDecoder().Decode(raw);
        Assert.Equal(DecodeStatus.Ok, result.Status);
        Assert.Equal(-32768.0, result.NumericValue);
        Assert.Equal(new byte[] { 0, 0x80 }, result.DataBytes);
    }
}
