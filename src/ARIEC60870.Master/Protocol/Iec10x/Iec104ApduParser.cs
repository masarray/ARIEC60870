// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol.Iec10x;

public sealed class Iec104ApduParser
{
    private readonly Iec10xAsduDecoder _asduDecoder;

    public Iec104ApduParser(int cotSize = 2, int caSize = 2, int ioaSize = 3)
    {
        _asduDecoder = new Iec10xAsduDecoder(cotSize, caSize, ioaSize);
    }

    public Iec104ApduDecode Decode(IReadOnlyList<byte> bytes)
    {
        var raw = bytes.ToArray();
        var issues = new List<string>();
        if (raw.Length < 6)
        {
            return Malformed(raw, "APDU too short.");
        }

        if (raw[0] != 0x68)
        {
            return Malformed(raw, $"Invalid IEC-104 start byte 0x{raw[0]:X2}; expected 0x68.");
        }

        var length = raw[1];
        if (length != raw.Length - 2)
        {
            issues.Add($"APDU length mismatch. Declared={length}, actual={raw.Length - 2}.");
        }

        var c0 = raw[2];
        var c1 = raw[3];
        var c2 = raw[4];
        var c3 = raw[5];

        if ((c0 & 0x01) == 0)
        {
            var ns = ((c1 << 8) | c0) >> 1;
            var nr = ((c3 << 8) | c2) >> 1;
            var asduBytes = raw.Skip(6).ToArray();
            var asdu = asduBytes.Length == 0 ? null : _asduDecoder.Decode(asduBytes);
            return new Iec104ApduDecode
            {
                RawBytes = raw,
                IsValid = issues.Count == 0,
                Format = "I",
                SendSequence = ns,
                ReceiveSequence = nr,
                Asdu = asdu,
                Issues = issues.ToArray()
            };
        }

        if ((c0 & 0x03) == 0x01)
        {
            var nr = ((c3 << 8) | c2) >> 1;
            return new Iec104ApduDecode
            {
                RawBytes = raw,
                IsValid = issues.Count == 0,
                Format = "S",
                ReceiveSequence = nr,
                Issues = issues.ToArray()
            };
        }

        if ((c0 & 0x03) == 0x03)
        {
            return new Iec104ApduDecode
            {
                RawBytes = raw,
                IsValid = issues.Count == 0,
                Format = "U",
                UFormatName = DecodeU(c0),
                Issues = issues.ToArray()
            };
        }

        return Malformed(raw, "Unsupported APCI control field.");
    }

    private static Iec104ApduDecode Malformed(byte[] raw, string issue) => new()
    {
        RawBytes = raw,
        IsValid = false,
        Format = "Malformed",
        Issues = new[] { issue }
    };

    public static string DecodeU(byte c0) => c0 switch
    {
        0x07 => "STARTDT act",
        0x0B => "STARTDT con",
        0x13 => "STOPDT act",
        0x23 => "STOPDT con",
        0x43 => "TESTFR act",
        0x83 => "TESTFR con",
        _ => $"U=0x{c0:X2}"
    };
}
