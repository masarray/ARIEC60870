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
    string Tooltip, bool IsInvalid);

public static class ProtocolFlagPresentation
{
    private static readonly IReadOnlyList<ProtocolFlagChip> Good = [new("Good", "Good")];
    private static readonly IReadOnlyList<ProtocolFlagChip> Unknown = [new("Unknown", "Neutral")];
    private static readonly IReadOnlyList<ProtocolFlagChip> NotApplicable = [new("—", "Neutral")];
    private static readonly IReadOnlyList<ProtocolFlagChip> None = [];
    private static readonly ConcurrentDictionary<int, ProtocolQualityPresentation> QualityCache = new();

    // IEC-101/104 Type ID is necessary for deciding whether a quality
    // descriptor SHOULD exist. In particular, a GI/command has no QDS;
    // missing QDS in a process value such as TI13 must NOT be marked Good.
    // Preserve source-backed quality flags first, even if a caller labels the
    // ASDU as a service transaction.
    public static ProtocolQualityPresentation QualityState(
        Iec10xQualityDetails? q, string? legacy, bool iec10x,
        int? typeId = null, string? transactionKind = null)
    {
        if (q is not null)
            return QualityCache.GetOrAdd(((int)q.Kind << 8) | q.Raw, _ => BuildQuality(q));
        if (iec10x && QualityDescriptorNotApplicable(typeId, transactionKind))
            return new(NotApplicable, ExplainNotApplicable(typeId, transactionKind), false);
        if (iec10x || string.IsNullOrWhiteSpace(legacy) || legacy == "-")
            return new(Unknown,
                typeId.HasValue
                    ? $"TI{typeId.Value}: no quality descriptor in accepted evidence. Do not infer Good; inspect the decoded information element and raw ASDU."
                    : "Quality descriptor not available in decoded evidence — not assumed Good.",
                false);
        // IEC-103 quality metadata is not an IEC-10x SIQ/DIQ/QDS/BCR byte.
        return new([new ProtocolFlagChip(legacy.Length > 20 ? "Info" : legacy, "Neutral")],
            legacy, false);
    }

    public static IReadOnlyList<ProtocolFlagChip> Quality(Iec10xQualityDetails? q, string? legacy,
        bool iec10x, int? typeId = null, string? transactionKind = null)
        => QualityState(q, legacy, iec10x, typeId, transactionKind).Chips;

    private static bool QualityDescriptorNotApplicable(int? typeId, string? transactionKind) =>
        typeId switch
        {
            21 => true, // M_ME_ND_1: normalized value WITHOUT quality descriptor
            >= 45 and <= 70 => true, // control/setpoint/parameter and end of initialization
            >= 100 and <= 127 => true, // system: GI, counter GI, read, clock sync, reset, tests, ...
            null => transactionKind is "GI" or "Command",
            _ => false
        };

    private static string ExplainNotApplicable(int? typeId, string? transactionKind) =>
        typeId switch
        {
            100 => "TI100 · C_IC_NA_1 general interrogation: no SIQ/DIQ/QDS/BCR quality descriptor. Evaluate COT (ACT, ACTCON, ACTTERM, P/N, TEST), QOI and GI sequence/coverage separately.",
            101 => "TI101 · C_CI_NA_1 counter interrogation: no process quality descriptor. Evaluate COT and QCC.",
            102 => "TI102 · C_RD_NA_1 read command: no process quality descriptor. Evaluate response and COT.",
            103 => "TI103 · C_CS_NA_1 clock synchronization: no process quality descriptor. Evaluate COT and CP56Time2a time validity separately.",
            21 => "TI21 · M_ME_ND_1: normalized measured value explicitly carries no QDS. N/A does not mean Good.",
            >= 45 and <= 70 => $"TI{typeId}: control, parameter or initialization ASDU; quality descriptor is not part of this service payload. Evaluate COT and the corresponding command/response fields.",
            >= 100 and <= 127 => $"TI{typeId}: system ASDU without process quality descriptor. Evaluate COT, qualifier and transaction outcome.",
            _ => transactionKind is "GI" or "Command"
                ? $"{transactionKind} transaction without a decoded process quality descriptor. Evaluate its COT and protocol evidence."
                : "Quality descriptor not applicable to this ASDU type."
        };

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
        return new(chips, $"{type} 0x{q.Raw:X2}\n{flags}", q.Invalid);
    }
}
