// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;
using Xunit.Abstractions;

namespace ARIEC60870.Core.Tests;

public sealed class Iec104ApciConformanceTests
{
    private readonly ITestOutputHelper _output;

    public Iec104ApciConformanceTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("680400000000")] // I-format without ASDU
    [InlineData("680501000000AA")] // S-format cannot carry an ASDU
    [InlineData("680401010000")] // S-format reserved control octet
    [InlineData("680401000100")] // S-format N(R) reserved bit
    [InlineData("680403000000")] // Undefined U operation
    [InlineData("68040F000000")] // Multiple U function bits
    [InlineData("68040B010000")] // U-format reserved control octet
    [InlineData("68050B00000000")] // U-format with trailing payload
    [InlineData("68050000010000")] // I-format N(R) reserved bit
    [InlineData("680300000000")] // Declared length shorter than control field
    [InlineData("68FE00000000")] // Declared length over 253
    [InlineData("680501000000")] // Declared/actual mismatch
    public void InvalidApciIsRetainedButNeverAccepted(string hex)
    {
        var raw = Convert.FromHexString(hex);
        Assert.False(Iec104ApciWireLayout.TryValidate(raw, out var issue));
        Assert.False(string.IsNullOrWhiteSpace(issue));
        var decoded = new Iec104ApduParser().Decode(raw);
        Assert.False(decoded.IsValid);
        Assert.NotEmpty(decoded.Issues);
        Assert.Equal(raw, decoded.RawBytes);
    }

    [Theory]
    [InlineData("680407000000", "STARTDT act")]
    [InlineData("68040B000000", "STARTDT con")]
    [InlineData("680413000000", "STOPDT act")]
    [InlineData("680423000000", "STOPDT con")]
    [InlineData("680443000000", "TESTFR act")]
    [InlineData("680483000000", "TESTFR con")]
    public void SixLegalUFormatsAreAccepted(string hex, string operation)
    {
        var raw = Convert.FromHexString(hex);
        Assert.True(Iec104ApciWireLayout.TryValidate(raw, out var issue), issue);
        var decoded = new Iec104ApduParser().Decode(raw);
        Assert.True(decoded.IsValid);
        Assert.Equal("U", decoded.Format);
        Assert.Equal(operation, decoded.UFormatName);
    }

    [Fact]
    public void ModularSequenceBoundaryAndValidAsduRemainAccepted()
    {
        var s = Iec104FrameBuilderShim.S(32767);
        Assert.True(Iec104ApciWireLayout.TryValidate(s, out var issue), issue);
        Assert.Equal(32767, new Iec104ApduParser().Decode(s).ReceiveSequence);
        byte[] asdu = [1, 1, 3, 0, 1, 0, 7, 0, 0, 1];
        var i = new byte[] { 0x68, (byte)(asdu.Length + 4), 0xFE, 0xFF, 0xFE, 0xFF }
            .Concat(asdu).ToArray();
        Assert.True(Iec104ApciWireLayout.TryValidate(i, out issue), issue);
        var decoded = new Iec104ApduParser().Decode(i);
        Assert.True(decoded.IsValid);
        Assert.Equal(32767, decoded.SendSequence);
        Assert.Equal(32767, decoded.ReceiveSequence);
        Assert.Equal(7, decoded.Asdu!.FirstObject!.InformationObjectAddress);
    }

    [Fact]
    public void FiftyThousandMixedApciCandidatesAreAccountedForWithoutGrowingState()
    {
        const int iterations = 50_000;
        byte[] validS = [0x68, 4, 1, 0, 0, 0];
        byte[] validU = [0x68, 4, 0x43, 0, 0, 0];
        byte[] invalidS = [0x68, 4, 1, 1, 0, 0];
        byte[] invalidU = [0x68, 4, 0x0F, 0, 0, 0];
        var observedValid = 0;
        var observedInvalid = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (var index = 0; index < iterations; index++)
        {
            var raw = (index % 4) switch { 0 => validS, 1 => validU, 2 => invalidS, _ => invalidU };
            if (Iec104ApciWireLayout.TryValidate(raw, out _)) observedValid++;
            else observedInvalid++;
        }

        clock.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        _output.WriteLine($"P0-C2 50k mixed APCI structural checks: {clock.Elapsed.TotalMilliseconds:F2}ms, " +
                          $"thread allocations={allocated}B; expected 25k invalid diagnostic strings.");
        Assert.Equal(25_000, observedValid);
        Assert.Equal(25_000, observedInvalid);
    }

    private static class Iec104FrameBuilderShim
    {
        public static byte[] S(int nr)
        {
            var value = nr << 1;
            return [0x68, 4, 1, 0, (byte)value, (byte)(value >> 8)];
        }
    }
}
