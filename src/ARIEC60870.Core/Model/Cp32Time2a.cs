// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Model;

public sealed class Cp32Time2a
{
    public int MillisecondOfMinute { get; init; }
    public int Second => MillisecondOfMinute / 1000;
    public int Millisecond => MillisecondOfMinute % 1000;
    public int Minute { get; init; }
    public int Hour { get; init; }
    public bool Invalid { get; init; }
    public bool SummerTime { get; init; }
    public byte[] RawBytes { get; init; } = Array.Empty<byte>();

    public string DisplayTime => $"{Hour:00}:{Minute:00}:{Second:00}.{Millisecond:000}";

    public override string ToString()
    {
        var flags = new List<string>();
        if (Invalid) flags.Add("invalid");
        if (SummerTime) flags.Add("summer-time");
        return flags.Count == 0 ? DisplayTime : $"{DisplayTime} ({string.Join(", ", flags)})";
    }
}
