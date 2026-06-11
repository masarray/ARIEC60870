// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;

namespace ARIEC60870.Core.Parsing;

public static class Cp32Time2aDecoder
{
    public static Cp32Time2a? TryDecode(IReadOnlyList<byte> bytes)
    {
        if (bytes.Count < 4) return null;

        var ms = bytes[0] | (bytes[1] << 8);
        var minuteRaw = bytes[2];
        var hourRaw = bytes[3];

        return new Cp32Time2a
        {
            MillisecondOfMinute = ms,
            Minute = minuteRaw & 0x3F,
            Hour = hourRaw & 0x1F,
            Invalid = (minuteRaw & 0x80) != 0,
            SummerTime = (hourRaw & 0x80) != 0,
            RawBytes = bytes.Take(Math.Min(5, bytes.Count)).ToArray()
        };
    }
}
