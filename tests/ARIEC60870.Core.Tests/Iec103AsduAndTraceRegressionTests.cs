// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Analysis;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using Xunit;

namespace ARIEC60870.Core.Tests;

public sealed class Iec103AsduAndTraceRegressionTests
{
    [Fact]
    public void Type1EventDecodesDpiAndRelayTimestamp()
    {
        var asduBytes = new byte[] { 0x01, 0x01, 0x01, 0x01, 0xC0, 0x24, 0x02, 0x39, 0x30, 0x22, 0x0E, 0x00 };
        var asdu = new AsduDecoder().Decode(asduBytes);

        Assert.Equal(DecodeStatus.Ok, asdu.Status);
        Assert.Equal(1, asdu.TypeId);
        Assert.Equal("Spontaneous", asdu.CauseName);
        Assert.Equal(0xC0, asdu.FunctionType);
        Assert.Equal(0x24, asdu.InformationNumber);
        Assert.Equal(2, asdu.Dpi);
        Assert.Equal("14:34:12.345", asdu.Time!.DisplayTime);
    }

    [Fact]
    public void UnknownPrivateAsduIsTransparentRatherThanDiscarded()
    {
        var asduBytes = new byte[] { 0xCD, 0x01, 0x01, 0x01, 0xC8, 0x0A, 0xAA, 0x55 };
        var asdu = new AsduDecoder().Decode(asduBytes);

        Assert.Equal(205, asdu.TypeId);
        Assert.Equal(DecodeStatus.Unknown, asdu.Status);
        Assert.Contains("Unknown", asdu.TypeName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new byte[] { 0xAA, 0x55 }, asdu.DataBytes);
        Assert.NotEmpty(asdu.Notes);
    }

    [Fact]
    public void TraceAnalyzerRaisesWarningWhenGiStartHasNoGiEnd()
    {
        var giStartFrame = Variable(control: 0x08, linkAddress: 1, new byte[] { 0x07, 0x01, 0x09, 0x01, 0x00, 0x00 });
        var text = $"12:00:00.000 COM1 <- GI start [{ToHex(giStartFrame)}]";

        var report = new Iec103TraceAnalyzer().AnalyzeText(text, "missing-gi-end.log");

        Assert.Equal(1, report.Summary.TotalFrames);
        Assert.Contains(report.Findings, finding =>
            finding.Id == "IEC103-ASDU-001" &&
            finding.Severity == FindingSeverity.Warning &&
            finding.Title.Contains("no GI END", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TraceExtractorKeepsDirectionTimestampAndRawHexFromComStyleLogs()
    {
        var raw = "12:00:00.536 COM1 <- No data [10 09 01 0A 16]";
        var entry = new HexTraceExtractor().TryParseLine(raw, 7);

        Assert.NotNull(entry);
        Assert.Equal(7, entry!.LineNumber);
        Assert.Equal(FrameDirection.SlaveToMaster, entry.Direction);
        Assert.Equal("12:00:00.536", entry.TimestampText);
        Assert.Equal(new byte[] { 0x10, 0x09, 0x01, 0x0A, 0x16 }, entry.RawBytes);
    }

    private static byte[] Variable(byte control, int linkAddress, IReadOnlyList<byte> asdu)
    {
        const int linkAddressSize = 1;
        var length = checked((byte)(1 + linkAddressSize + asdu.Count));
        var frame = new byte[4 + length + 2];
        frame[0] = 0x68;
        frame[1] = length;
        frame[2] = length;
        frame[3] = 0x68;
        frame[4] = control;
        frame[5] = (byte)linkAddress;
        for (var i = 0; i < asdu.Count; i++) frame[6 + i] = asdu[i];
        var sum = 0;
        for (var i = 4; i < 4 + length; i++) sum += frame[i];
        frame[4 + length] = (byte)(sum & 0xFF);
        frame[5 + length] = 0x16;
        return frame;
    }

    private static string ToHex(IEnumerable<byte> bytes) => string.Join(" ", bytes.Select(x => x.ToString("X2")));
}
