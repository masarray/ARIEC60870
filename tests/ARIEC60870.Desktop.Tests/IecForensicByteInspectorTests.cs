// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class IecForensicByteInspectorTests
{
    [Fact]
    public void Iec104SqOneListsEveryObjectWithoutFabricatingAnIoaByte()
    {
        // COT=1 octet, CA=1 octet, IOA=1 octet. SQ=1 sends IOA once.
        byte[] asduBytes = { 1, 0x82, 3, 1, 7, 1, 0 };
        var wire = Apdu(asduBytes);
        var asdu = new Iec10xAsduDecoder(1, 1, 1).Decode(asduBytes);
        Assert.Empty(asdu.Issues);
        var fields = IecForensicByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            RawHex = Convert.ToHexString(wire),
            ApciFormat = "I", SendSequence = 0, ReceiveSequence = 0,
            AsduDetails = asdu
        });

        Assert.Equal("68 0B", fields.Single(x => x.Key == "envelope").Hex);
        Assert.Equal("00 00 00 00", fields.Single(x => x.Key == "apci").Hex);
        Assert.Equal("03", fields.Single(x => x.Key == "cot").Hex);
        Assert.Equal(8, fields.Single(x => x.Key == "cot").Offset);
        Assert.Equal("01", fields.Single(x => x.Key == "ca").Hex);
        Assert.Equal("07", fields.Single(x => x.Key == "object-1").Hex);
        Assert.Equal(10, fields.Single(x => x.Key == "object-1").Offset);
        Assert.DoesNotContain(fields, x => x.Key == "object-2");
        var implied = fields.Single(x => x.Key == "implicit-ioa-2");
        Assert.Equal(-1, implied.Offset);
        Assert.Equal(0, implied.Count);
        Assert.Contains("NO address octets", implied.Meaning);
        Assert.Equal("01", fields.Single(x => x.Key == "value-1").Hex);
        Assert.Equal("00", fields.Single(x => x.Key == "value-2").Hex);
        Assert.All(fields.Where(x => x.Count > 0), x => Assert.Equal(
            string.Join(" ", wire.Skip(x.Offset).Take(x.Count).Select(b => b.ToString("X2"))), x.Hex));
    }

    [Fact]
    public void Iec104ControlAndMalformedFramesDoNotInventAsdu()
    {
        var s = IecForensicByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            RawHex = "68 04 01 00 00 00",
            ApciFormat = "S", ReceiveSequence = 0
        });
        Assert.Equal(new[] { "envelope", "apci" }, s.Select(x => x.Key));
        Assert.Contains("acknowledgement", s.Single(x => x.Key == "apci").Meaning);

        var u = IecForensicByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            RawHex = "68 04 07 00 00 00",
            ApciFormat = "U", UFormatName = "STARTDT act"
        });
        Assert.Contains("STARTDT act", u.Single(x => x.Key == "apci").Meaning);

        var malformed = IecForensicByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            RawHex = "68 06 00 00 00 00"
        });
        Assert.Single(malformed);
        Assert.Equal("raw", malformed[0].Key);
        Assert.DoesNotContain(malformed, f => f.Key == "asdu-type");

        // Opaque raw bytes or mismatched decoded ASDU must never be treated
        // as a verified COT/CA/IOA profile.
        byte[] original = { 1, 1, 3, 0, 1, 0, 7, 0, 0, 1 };
        var tampered = (byte[])original.Clone();
        tampered[^1] ^= 1;
        var mismatched = IecForensicByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            RawHex = Convert.ToHexString(Apdu(tampered)),
            ApciFormat = "I", SendSequence = 0, ReceiveSequence = 0,
            AsduDetails = new Iec10xAsduDecoder().Decode(original)
        });
        Assert.Contains(mismatched, x => x.Key == "asdu");
        Assert.DoesNotContain(mismatched, x => x.Key == "cot" || x.Key == "object-1");

        var staleApci = IecForensicByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            RawHex = "68 04 01 00 00 00",
            ApciFormat = "S", ReceiveSequence = 42 // Contradicts physical bytes
        });
        Assert.Single(staleApci);
        Assert.Equal("raw", staleApci[0].Key);
    }

    [Fact]
    public void Iec103ReadsOriginalFt12FunInfAndNeverCallsThemIec101Ioa()
    {
        byte[] asdu = { 1, 1, 3, 1, 7, 2, 0x81 };
        var raw = SerialVariable(0x08, 0x69, asdu);
        var frame = new Ft12Parser(1).Decode(raw);
        Assert.True(frame.IsChecksumValid);
        var fields = IecForensicByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec103, Frame = frame, RawHex = Convert.ToHexString(raw)
        });
        Assert.Equal("07", fields.Single(x => x.Key == "fun").Hex);
        Assert.Equal(10, fields.Single(x => x.Key == "fun").Offset);
        Assert.Equal("02", fields.Single(x => x.Key == "inf").Hex);
        Assert.Equal(11, fields.Single(x => x.Key == "inf").Offset);
        Assert.Equal("81", fields.Single(x => x.Key == "payload").Hex);
        Assert.DoesNotContain(fields, f => f.Key.StartsWith("object-", StringComparison.Ordinal));
        Assert.Equal("03", fields.Single(x => x.Key == "cot").Hex);
        Assert.Equal("01", fields.Single(x => x.Key == "ca").Hex);

        raw[^2] ^= 1;
        var invalid = IecForensicByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec103, Frame = new Ft12Parser(1).Decode(raw)
        });
        Assert.Single(invalid);
        Assert.Equal("raw", invalid[0].Key);
        Assert.DoesNotContain(invalid, x => x.Key == "fun");
    }

    [Fact]
    public void Iec101UsesActualConfiguredWidthsRatherThanGuessingAbsentObjects()
    {
        byte[] asduBytes = { 13, 1, 3, 1, 7, 0, 0, 0x80, 0x3F, 0 };
        var raw = SerialVariable(0x08, 0x69, asduBytes);
        var decoder = new Iec10xAsduDecoder(1, 1, 1);
        var asdu = decoder.Decode(asduBytes);
        Assert.Empty(asdu.Issues);
        var fields = IecForensicByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Frame = new Ft12Parser(1).Decode(raw),
            AsduDetails = asdu
        });
        Assert.Equal(1, asdu.CotWidth);
        Assert.Equal(1, asdu.CommonAddressWidth);
        Assert.Equal(1, asdu.InformationObjectAddressWidth);
        Assert.Equal("03", fields.Single(x => x.Key == "cot").Hex);
        Assert.Equal("01", fields.Single(x => x.Key == "ca").Hex);
        Assert.Equal("07", fields.Single(x => x.Key == "object-1").Hex);
        Assert.Equal(1, fields.Single(x => x.Key == "ca").Count);

        var mismatch = IecForensicByteInspector.Explain(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Frame = new Ft12Parser(1).Decode(raw),
            AsduDetails = new Iec10xAsduDecoder(2, 2, 3).Decode(asduBytes)
        });
        Assert.DoesNotContain(mismatch, f => f.Key == "cot" || f.Key == "ca");
    }

    private static byte[] Apdu(byte[] asdu) =>
        new byte[] { 0x68, (byte)(asdu.Length + 4), 0, 0, 0, 0 }.Concat(asdu).ToArray();

    private static byte[] SerialVariable(byte control, byte address, byte[] asdu)
    {
        var body = new byte[] { control, address }.Concat(asdu).ToArray();
        return new byte[] { 0x68, (byte)body.Length, (byte)body.Length, 0x68 }
            .Concat(body).Concat(new byte[] { (byte)(body.Sum(b => b) & 0xFF), 0x16 }).ToArray();
    }
}
