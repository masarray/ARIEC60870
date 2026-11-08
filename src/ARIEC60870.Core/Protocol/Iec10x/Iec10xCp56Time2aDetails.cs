// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>
/// Canonical IEC-101/104 CP56Time2a interpretation. RES1 is the
/// genuine/substituted-time indicator, IV is timestamp validity, and SU is
/// the summer-time flag. The 7 wire octets do not encode a time zone.
/// Immutable decoder evidence; presenters must never reinterpret timestamp hex.
/// </summary>
public sealed record Iec10xCp56Time2aDetails
{
    public int MillisecondsInMinute { get; init; }
    public int Minute { get; init; }
    public int Hour { get; init; }
    public int Day { get; init; }
    public int Month { get; init; }
    public int Year { get; init; }
    public int DayOfWeek { get; init; }
    public bool InvalidFlag { get; init; }
    public bool SubstitutedFlag { get; init; }
    public bool SummerTimeFlag { get; init; }
    public int Reserved2 { get; init; }
    public int Reserved3 { get; init; }
    public int Reserved4 { get; init; }

    public bool CalendarInRange => MillisecondsInMinute is >= 0 and < 60000 &&
        Minute is >= 0 and < 60 && Hour is >= 0 and <= 23 &&
        Month is >= 1 and <= 12 && Day >= 1 &&
        Day <= DateTime.DaysInMonth(Year, Math.Clamp(Month, 1, 12));
    public bool IsValid => !InvalidFlag && CalendarInRange;
    public bool UnexpectedReservedBits => Reserved2 != 0 || Reserved3 != 0 || Reserved4 != 0;
    public string DayOfWeekText => DayOfWeek == 0 ? "Not supplied" :
        ((System.DayOfWeek)DayOfWeek).ToString();

    /// <summary>Display is wall-clock time as supplied; never invent UTC or TZ.</summary>
    public string TimestampText =>
        $"{Year:0000}-{Month:00}-{Day:00} {Hour:00}:{Minute:00}:" +
        $"{MillisecondsInMinute / 1000:00}.{MillisecondsInMinute % 1000:000}" +
        (IsValid ? string.Empty : " invalid-time");

    public bool? DayOfWeekMatchesCalendar
    {
        get
        {
            if (!CalendarInRange || DayOfWeek == 0) return null;
            var actual = new DateTime(Year, Month, Day).DayOfWeek;
            var asIso = actual == System.DayOfWeek.Sunday ? 7 : (int)actual;
            return asIso == DayOfWeek;
        }
    }

    public static Iec10xCp56Time2aDetails? Decode(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != 7) return null;
        return new Iec10xCp56Time2aDetails
        {
            MillisecondsInMinute = raw[0] | (raw[1] << 8),
            Minute = raw[2] & 0x3F,
            InvalidFlag = (raw[2] & 0x80) != 0,
            SubstitutedFlag = (raw[2] & 0x40) != 0, // RES1 / GEN
            Hour = raw[3] & 0x1F,
            SummerTimeFlag = (raw[3] & 0x80) != 0,
            Reserved2 = (raw[3] >> 5) & 0x03,
            Day = raw[4] & 0x1F,
            DayOfWeek = (raw[4] >> 5) & 0x07,
            Month = raw[5] & 0x0F,
            Reserved3 = (raw[5] >> 4) & 0x0F,
            Year = 2000 + (raw[6] & 0x7F),
            Reserved4 = (raw[6] >> 7) & 0x01
        };
    }

    /// <summary>
    /// Only these IEC-10x types physically carry a complete 7-octet time
    /// at the documented element-relative offset. CP24 tags are distinct.
    /// </summary>
    public static Iec10xCp56Time2aDetails? FromInformationElement(byte type, byte[] element)
    {
        var offset = type switch
        {
            30 or 31 or 58 or 59 or 60 => 1,
            32 => 2,
            33 or 36 or 37 or 63 => 5,
            34 or 35 or 61 or 62 => 3,
            64 => 4,
            103 => 0,
            _ => -1
        };
        return offset >= 0 && element.Length >= offset + 7
            ? Decode(element.AsSpan(offset, 7))
            : null;
    }
}
