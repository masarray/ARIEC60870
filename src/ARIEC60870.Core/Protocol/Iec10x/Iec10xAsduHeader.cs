// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>
/// Canonical, allocation-free IEC-101/104 ASDU header. Field widths come from
/// the selected interoperability profile; never infer an unknown profile from
/// plausible Type/COT bytes alone.
/// </summary>
public readonly record struct Iec10xAsduHeader(
    byte TypeId, byte Vsq, byte CotRaw, int Cause, bool IsNegative,
    bool IsTest, int OriginatorAddress, int CommonAddress,
    int ObjectCount, bool IsSequence, int ObjectsOffset)
{
    public static bool TryParse(ReadOnlySpan<byte> asdu, int cotWidth, int caWidth,
        out Iec10xAsduHeader header)
    {
        header = default;
        if (cotWidth is < 1 or > 2 || caWidth is < 1 or > 2 ||
            asdu.Length < 2 + cotWidth + caWidth)
            return false;
        var vsq = asdu[1];
        var cot = asdu[2];
        var ca = 0;
        var caOffset = 2 + cotWidth;
        for (var i = 0; i < caWidth; i++)
            ca |= asdu[caOffset + i] << (8 * i);
        header = new Iec10xAsduHeader(asdu[0], vsq, cot, cot & 0x3F,
            (cot & 0x40) != 0, (cot & 0x80) != 0,
            cotWidth == 2 ? asdu[3] : 0, ca, vsq & 0x7F,
            (vsq & 0x80) != 0, caOffset + caWidth);
        return true;
    }
}
