// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

public sealed record ProtocolFlagChip(string Text, string Tone);

/// <summary>Pure presentation of source-backed bits; bounded cache over 4x256 quality states.</summary>
public static class ProtocolFlagPresentation
{
    private static readonly IReadOnlyList<ProtocolFlagChip> Good = [new("Good", "Good")];
    private static readonly IReadOnlyList<ProtocolFlagChip> Unknown = [new("Unknown", "Neutral")];
    private static readonly IReadOnlyList<ProtocolFlagChip> None = [];
    private static readonly ConcurrentDictionary<int, IReadOnlyList<ProtocolFlagChip>> Cache = new();

    public static IReadOnlyList<ProtocolFlagChip> Quality(Iec10xQualityDetails? q, string? legacy, bool iec10x)
    {
        if (q is not null) return Cache.GetOrAdd(((int)q.Kind << 8) | q.Raw, _ => Build(q));
        if (iec10x || string.IsNullOrWhiteSpace(legacy) || legacy == "-") return Unknown;
        // IEC-103 legacy quality is not an IEC-10x QDS byte.
        return [new ProtocolFlagChip(legacy.Length > 20 ? "Info" : legacy, "Neutral")];
    }

    public static IReadOnlyList<ProtocolFlagChip> Cot(Iec10xCauseDetails? cot)
    {
        if (cot is null || (!cot.Negative && !cot.Test)) return None;
        if (cot.Negative && cot.Test)
            return [new ProtocolFlagChip("NEG", "Critical"), new ProtocolFlagChip("TEST", "Warning")];
        return cot.Negative
            ? [new ProtocolFlagChip("NEG", "Critical")]
            : [new ProtocolFlagChip("TEST", "Warning")];
    }

    private static IReadOnlyList<ProtocolFlagChip> Build(Iec10xQualityDetails q)
    {
        var active = q.ActiveBadges;
        if (active.Count == 1 && active[0] == "Good") return Good;
        var result = new ProtocolFlagChip[Math.Min(3, active.Count)];
        for (var i = 0; i < result.Length; i++)
        {
            if (i == 2 && active.Count > 3)
            {
                result[i] = new ProtocolFlagChip($"+{active.Count - 2}", "Neutral");
                continue;
            }
            var text = active[i];
            result[i] = new ProtocolFlagChip(text, text == "INVALID" ? "Critical" :
                text is "OVF" or "NT" or "CARRY" ? "Warning" : "Neutral");
        }
        return result;
    }
}
