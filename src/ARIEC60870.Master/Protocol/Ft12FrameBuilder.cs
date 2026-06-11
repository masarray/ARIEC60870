// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol;

public static class Ft12FrameBuilder
{
    public static byte BuildPrimaryControl(int functionCode, bool fcv = false, bool fcb = false)
    {
        if (functionCode < 0 || functionCode > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(functionCode), "IEC FT1.2 primary function code must be 0..15.");
        }

        var value = 0x40 | (functionCode & 0x0F);
        if (fcb) value |= 0x20;
        if (fcv) value |= 0x10;
        return (byte)value;
    }

    public static byte[] Fixed(byte control, byte linkAddress) => Fixed(control, linkAddress, 1);

    public static byte[] Fixed(byte control, int linkAddress, int linkAddressSize = 1)
    {
        linkAddressSize = NormalizeAddressSize(linkAddressSize);
        var frame = new byte[3 + linkAddressSize + 1];
        frame[0] = 0x10;
        frame[1] = control;
        WriteLe(frame, 2, linkAddress, linkAddressSize);

        var sum = control;
        for (var i = 0; i < linkAddressSize; i++) sum += frame[2 + i];
        frame[2 + linkAddressSize] = (byte)(sum & 0xFF);
        frame[3 + linkAddressSize] = 0x16;
        return frame;
    }

    public static byte[] Variable(byte control, byte linkAddress, IReadOnlyList<byte> asdu) => Variable(control, linkAddress, asdu, 1);

    public static byte[] Variable(byte control, int linkAddress, IReadOnlyList<byte> asdu, int linkAddressSize = 1)
    {
        asdu ??= Array.Empty<byte>();
        linkAddressSize = NormalizeAddressSize(linkAddressSize);
        var length = checked((byte)(1 + linkAddressSize + asdu.Count));
        var frame = new byte[4 + length + 2];
        frame[0] = 0x68;
        frame[1] = length;
        frame[2] = length;
        frame[3] = 0x68;
        frame[4] = control;
        WriteLe(frame, 5, linkAddress, linkAddressSize);

        for (var i = 0; i < asdu.Count; i++)
        {
            frame[5 + linkAddressSize + i] = asdu[i];
        }

        var sum = 0;
        for (var i = 4; i < 4 + length; i++)
        {
            sum += frame[i];
        }

        frame[4 + length] = (byte)(sum & 0xFF);
        frame[5 + length] = 0x16;
        return frame;
    }

    private static int NormalizeAddressSize(int size)
    {
        if (size is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "IEC-101 FT1.2 link address size must be 0, 1 or 2 octets; IEC-103 uses 1 octet.");
        }
        return size;
    }

    private static void WriteLe(byte[] frame, int offset, int value, int count)
    {
        var max = count == 0 ? 0 : count == 1 ? 0xFF : 0xFFFF;
        if (value < 0 || value > max)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Link address {value} does not fit in {count} octet(s).");
        }

        for (var i = 0; i < count; i++)
        {
            frame[offset + i] = (byte)((value >> (8 * i)) & 0xFF);
        }
    }
}
