// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;

namespace ARIEC60870.Core.Parsing;

public sealed class Ft12Parser
{
    private readonly AsduDecoder _asduDecoder = new();
    private readonly int _linkAddressSize;

    public Ft12Parser(int linkAddressSize = 1)
    {
        _linkAddressSize = Math.Clamp(linkAddressSize, 0, 2);
    }

    public Ft12FrameDecode Decode(IReadOnlyList<byte> bytes)
    {
        var issues = new List<string>();
        if (bytes.Count == 0)
        {
            return Malformed(bytes, "Empty frame.");
        }

        if (bytes.Count == 1 && (bytes[0] == 0xE5 || bytes[0] == 0xA2))
        {
            return new Ft12FrameDecode
            {
                Format = Ft12FrameFormat.SingleCharacter,
                RawBytes = bytes.ToArray(),
                IsLengthValid = true,
                IsChecksumValid = true,
                LinkAddressSize = _linkAddressSize
            };
        }

        if (bytes[0] == 0x10)
        {
            return DecodeFixed(bytes, issues);
        }

        if (bytes[0] == 0x68)
        {
            return DecodeVariable(bytes, issues);
        }

        return Malformed(bytes, $"Unsupported start byte 0x{bytes[0]:X2}.");
    }

    private Ft12FrameDecode DecodeFixed(IReadOnlyList<byte> bytes, List<string> issues)
    {
        var expectedCount = 3 + _linkAddressSize + 1;
        if (bytes.Count != expectedCount)
        {
            issues.Add($"Fixed frame must contain exactly {expectedCount} bytes for configured link-address size={_linkAddressSize}, actual={bytes.Count}.");
            return BuildFrame(Ft12FrameFormat.Malformed, bytes, issues, null, null, null, null, false, false, null, Array.Empty<byte>(), null);
        }

        var endIndex = 2 + _linkAddressSize + 1;
        var endOk = bytes[endIndex] == 0x16;
        if (!endOk) issues.Add($"Invalid fixed frame end byte 0x{bytes[endIndex]:X2}; expected 0x16.");

        var control = bytes[1];
        var address = ReadLe(bytes, 2, _linkAddressSize);
        var checksum = bytes[2 + _linkAddressSize];
        var sum = control;
        for (var i = 0; i < _linkAddressSize; i++) sum += bytes[2 + i];
        var calculated = (byte)(sum & 0xFF);
        var checksumOk = checksum == calculated;
        if (!checksumOk) issues.Add($"Checksum mismatch. Calculated 0x{calculated:X2}, received 0x{checksum:X2}.");

        var link = LinkControlDecoder.Decode(control);
        return BuildFrame(endOk ? Ft12FrameFormat.FixedLength : Ft12FrameFormat.Malformed, bytes, issues, control, address, checksum, calculated, checksumOk, endOk, link, Array.Empty<byte>(), null);
    }

    private Ft12FrameDecode DecodeVariable(IReadOnlyList<byte> bytes, List<string> issues)
    {
        if (bytes.Count < 5 + _linkAddressSize + 1)
        {
            return Malformed(bytes, "Variable frame is too short for configured link-address size.");
        }

        var length1 = bytes[1];
        var length2 = bytes[2];
        var repeatedLengthOk = length1 == length2;
        if (!repeatedLengthOk) issues.Add($"Length bytes differ: L1={length1}, L2={length2}.");

        var secondStartOk = bytes[3] == 0x68;
        if (!secondStartOk) issues.Add($"Invalid second start byte 0x{bytes[3]:X2}; expected 0x68.");

        var expectedCount = 4 + length1 + 2;
        var lengthOk = bytes.Count == expectedCount;
        if (!lengthOk) issues.Add($"Variable frame length mismatch. Declared={length1}, expected total={expectedCount}, actual={bytes.Count}.");

        if (bytes.Count < Math.Min(expectedCount, 5 + _linkAddressSize + 1))
        {
            return BuildFrame(Ft12FrameFormat.Malformed, bytes, issues, null, null, null, null, false, false, null, Array.Empty<byte>(), null, length1);
        }

        var checksumIndex = Math.Min(4 + length1, bytes.Count - 2);
        var endIndex = checksumIndex + 1;
        var endOk = endIndex < bytes.Count && bytes[endIndex] == 0x16;
        if (!endOk) issues.Add("Invalid or missing variable frame end byte 0x16.");

        var control = bytes.Count > 4 ? bytes[4] : (byte?)null;
        int? address = bytes.Count >= 5 + _linkAddressSize ? ReadLe(bytes, 5, _linkAddressSize) : null;
        var checksum = checksumIndex < bytes.Count ? bytes[checksumIndex] : (byte?)null;
        byte? calculated = null;
        var checksumOk = false;

        if (bytes.Count >= 5 + _linkAddressSize && checksum.HasValue && bytes.Count >= 4 + length1)
        {
            var sum = 0;
            for (var i = 4; i < 4 + length1 && i < bytes.Count; i++) sum += bytes[i];
            calculated = (byte)(sum & 0xFF);
            checksumOk = calculated == checksum;
            if (!checksumOk) issues.Add($"Checksum mismatch. Calculated 0x{calculated:X2}, received 0x{checksum:X2}.");
        }

        var asduBytes = Array.Empty<byte>();
        AsduDecode? asdu = null;
        LinkControlInfo? link = null;

        if (control.HasValue)
        {
            link = LinkControlDecoder.Decode(control.Value);
        }

        var headerInsideLength = 1 + _linkAddressSize;
        if (length1 >= headerInsideLength && bytes.Count >= 5 + _linkAddressSize)
        {
            var asduLength = Math.Max(0, length1 - headerInsideLength);
            asduBytes = bytes.Skip(5 + _linkAddressSize).Take(asduLength).ToArray();
            if (asduBytes.Length > 0)
            {
                asdu = _asduDecoder.Decode(asduBytes);
            }
        }

        var format = repeatedLengthOk && secondStartOk && lengthOk && endOk ? Ft12FrameFormat.VariableLength : Ft12FrameFormat.Malformed;
        return BuildFrame(format, bytes, issues, control, address, checksum, calculated, checksumOk, repeatedLengthOk && secondStartOk && lengthOk && endOk, link, asduBytes, asdu, length1);
    }

    private Ft12FrameDecode BuildFrame(
        Ft12FrameFormat format,
        IReadOnlyList<byte> raw,
        IReadOnlyList<string> issues,
        byte? control,
        int? address,
        byte? checksum,
        byte? calculated,
        bool checksumOk,
        bool lengthOk,
        LinkControlInfo? link,
        IReadOnlyList<byte> asduBytes,
        AsduDecode? asdu,
        int? declaredLength = null)
    {
        return new Ft12FrameDecode
        {
            Format = format,
            RawBytes = raw.ToArray(),
            Control = control,
            LinkAddress = address,
            LinkAddressSize = _linkAddressSize,
            Checksum = checksum,
            CalculatedChecksum = calculated,
            IsChecksumValid = checksumOk,
            IsLengthValid = lengthOk,
            DeclaredLength = declaredLength,
            LinkControl = link,
            AsduBytes = asduBytes.ToArray(),
            Asdu = asdu,
            Issues = issues.ToArray()
        };
    }

    private Ft12FrameDecode Malformed(IReadOnlyList<byte> bytes, string issue)
    {
        return new Ft12FrameDecode
        {
            Format = Ft12FrameFormat.Malformed,
            RawBytes = bytes.ToArray(),
            IsChecksumValid = false,
            IsLengthValid = false,
            LinkAddressSize = _linkAddressSize,
            Issues = new[] { issue }
        };
    }

    private static int ReadLe(IReadOnlyList<byte> raw, int offset, int count)
    {
        var value = 0;
        for (var i = 0; i < count && offset + i < raw.Count; i++)
        {
            value |= raw[offset + i] << (8 * i);
        }

        return value;
    }
}
