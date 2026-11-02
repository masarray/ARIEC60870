// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class PassiveFramePreparationTests
{
    [Fact]
    public void PreparedBatchRetainsSourceOrderingRawHexAndTypedMultiObject()
    {
        // Public synthetic IEC-101 type 1, VSQ=2, SQ=0, CA=105.
        byte[] wire =
        [
            0x68, 0x10, 0x10, 0x68, 0x08, 0x69,
            0x01, 0x02, 0x14, 0x00, 0x69, 0x00,
            0x65, 0x00, 0x00, 0x01, 0x66, 0x00, 0x00, 0x00,
            0x00, 0x16
        ];
        var observations = Enumerable.Range(0, 800).Select(i =>
            new PassiveObservedFrame((byte)(i % 2), i * 22L,
                DateTime.UtcNow.AddMilliseconds(i), "Variable", true, wire)).ToArray();
        var prepared = PassiveFramePreparation.PrepareBatch(observations, 1, 2, 2, 3);
        Assert.Equal(800, prepared.Count);
        for (var i = 0; i < prepared.Count; i++)
        {
            Assert.Same(observations[i], prepared[i].Frame);
            Assert.Equal(Convert.ToHexString(wire), prepared[i].RawHex);
            Assert.Equal<byte?>((byte)1, prepared[i].Decode?.TypeId);
            Assert.Equal(2, prepared[i].Decode?.Objects.Count);
            Assert.Equal(101, prepared[i].Decode?.Objects[0].InformationObjectAddress);
            Assert.Equal(102, prepared[i].Decode?.Objects[1].InformationObjectAddress);
        }
    }

    [Fact]
    public void BrokenFramesKeepExactRawWithoutFabricatingDecodedPoints()
    {
        var broken = new PassiveObservedFrame(1, 0, DateTime.UtcNow,
            "Variable", true, [0x68, 0x20, 0x20, 0x68, 0x08, 0x69]);
        var invalid = new PassiveObservedFrame(1, 1, DateTime.UtcNow,
            "Variable", false,
            [0x68, 0x0A, 0x0A, 0x68, 0x08, 0x69, 0x01, 0x01, 0x03, 0x00, 0x69, 0x00, 0x65, 0x00, 0x00, 0x01, 0x00, 0x16]);
        var result = PassiveFramePreparation.PrepareBatch([broken, invalid], 1, 2, 2, 3);
        Assert.Null(result[0].Decode);
        Assert.Empty(result[0].AsduRaw);
        Assert.Equal("682020680869", result[0].RawHex);
        Assert.Null(result[1].Decode);
        Assert.Equal(Convert.ToHexString(invalid.RawBytes), result[1].RawHex);
    }
}
