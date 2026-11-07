// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

public sealed record ProtocolFlagChip(string Text, string Tone);

/// <summary>
/// One immutable, bounded presentation per canonical kind/raw quality byte.
/// IEC-101 and IEC-104 share this visual policy. The raw typed decoder remains
/// authoritative: unknown or inapplicable quality bits are never invented.
/// </summary>
public sealed record ProtocolQualityPresentation(
    IReadOnlyList<ProtocolFlagChip> Chips,
    string Tooltip, string CleanRawLabel, bool IsInvalid);

public static class ProtocolFlagPresentation
{
    private static readonly IReadOnlyList<ProtocolFlagChip> Good = [new("Good", "Good")];
    private static readonly IReadOnlyList<ProtocolFlagChip> Unknown = [new("Unknown", "Neutral")];
    private static readonly IReadOnlyList<ProtocolFlagChip> None = [];
    private static readonly ConcurrentDictionary<int, ProtocolQualityPresentation> QualityCache = new();

    public static ProtocolQualityPresentation QualityState(Iec10xQualityDetails? q, string? legacy, bool iec10x)
    {
        if (q is not null)
            return QualityCache.GetOrAdd(((int)q.Kind << 8) | q.Raw, _ => BuildQuality(q));
        if (iec10x || string.IsNullOrWhiteSpace(legacy) || legacy == "-")
            return new(Unknown, "Quality byte not present in the decoded evidence — not assumed Good.", "", false);
        // IEC-103 quality metadata is not an IEC-10x SIQ/DIQ/QDS/BCR byte.
        return new([new ProtocolFlagChip(legacy.Length > 20 ? "Info" : legacy, "Neutral")],
            legacy, "", false);
    }

    public static IReadOnlyList<ProtocolFlagChip> Quality(Iec10xQualityDetails? q, string? legacy, bool iec10x)
        => QualityState(q, legacy, iec10x).Chips;

    public static IReadOnlyList<ProtocolFlagChip> Cot(Iec10xCauseDetails? cot)
    {
        if (cot is null || (!cot.Negative && !cot.Test)) return None;
        if (cot.Negative && cot.Test)
            return [new ProtocolFlagChip("NEG", "Critical"), new ProtocolFlagChip("TEST", "Warning")];
        return cot.Negative
            ? [new ProtocolFlagChip("NEG", "Critical")]
            : [new ProtocolFlagChip("TEST", "Warning")];
    }

    private static ProtocolQualityPresentation BuildQuality(Iec10xQualityDetails q)
    {
        // Severity order guarantees IV and OV stay visible even when five
        // independent flags are active. +N is presentation-only; tooltip
        // retains *every* applicable raw field without a lossy flag decode.
        var active = new List<ProtocolFlagChip>(7);
        if (q.Invalid) active.Add(new("IV", "Critical"));
        if (q.Overflow == true) active.Add(new("OV", "Warning"));
        if (q.Carry == true) active.Add(new("CY", "Warning"));
        if (q.NotTopical == true) active.Add(new("NT", "Warning"));
        if (q.Blocked == true) active.Add(new("BL", "Warning"));
        if (q.Substituted == true) active.Add(new("SB", "Warning"));
        if (q.Adjusted == true) active.Add(new("CA", "Warning"));

        IReadOnlyList<ProtocolFlagChip> chips;
        if (active.Count == 0) chips = Good;
        else if (active.Count <= 3) chips = active.ToArray();
        else chips = [active[0], active[1], new ProtocolFlagChip($"+{active.Count - 2}", "Warning")];

        static int Bit(bool? flag) => flag == true ? 1 : 0;
        var type = q.Kind.ToString().ToUpperInvariant();
        string flags = q.Kind switch
        {
            Iec10xQualityKind.Qds =>
                $"OV={Bit(q.Overflow)} · BL={Bit(q.Blocked)} · SB={Bit(q.Substituted)} · NT={Bit(q.NotTopical)} · IV={Bit(q.Invalid)}",
            Iec10xQualityKind.Siq or Iec10xQualityKind.Diq =>
                $"BL={Bit(q.Blocked)} · SB={Bit(q.Substituted)} · NT={Bit(q.NotTopical)} · IV={Bit(q.Invalid)}",
            Iec10xQualityKind.Bcr =>
                $"SQ={q.CounterSequence} · CY={Bit(q.Carry)} · CA={Bit(q.Adjusted)} · IV={Bit(q.Invalid)}",
            _ => ""
        };
        return new(chips, $"{type} 0x{q.Raw:X2}\n{flags}",
            active.Count == 0 ? $"{type} 0x{q.Raw:X2}" : "", q.Invalid);
    }
}
