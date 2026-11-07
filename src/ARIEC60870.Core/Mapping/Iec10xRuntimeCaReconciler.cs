// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Mapping;

/// <summary>
/// Session-scoped, read-only CA adaptation of signal NAMES only.
/// The caller supplies accepted active IEC-101 RX process facts, never
/// service confirmations, active commands, invalid frames or passive taps.
/// Physical CA/IOA keys, raw evidence and operator settings are immutable.
/// </summary>
public sealed class Iec10xRuntimeCaReconciler
{
    private readonly Iec10xPointMappingProfile _profile;
    private readonly int? _singleProfileCa;
    // Pre-index the approved single-station monitor IOAs once. Duplicates
    // deliberately become ambiguous rather than claiming an arbitrary name.
    private readonly Dictionary<int, Iec10xPointMappingEntry> _uniqueMonitorIoas = new();
    private readonly HashSet<int> _ambiguousMonitorIoas = new();
    private readonly Dictionary<string, SourceState> _sources = new(StringComparer.OrdinalIgnoreCase);
    private sealed class SourceState
    {
        public int Ca { get; init; }
        public bool Conflict { get; set; }
        public bool Reported { get; set; }
    }
    public Iec10xRuntimeCaReconciler(Iec10xPointMappingProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        if (profile.CommonAddress is int ca && ca is >= 0 and <= 65535 &&
            profile.Points.Count > 0 &&
            profile.Points.All(p => (p.Ca ?? profile.CommonAddress) == ca))
        {
            _singleProfileCa = ca;
            foreach (var point in profile.Points)
            {
                if (point.TypeId is not int ti ||
                    !Iec10xPointTypeFamily.SameProcessKind(ti, ti) ||
                    IsControlProfilePoint(point) ||
                    _ambiguousMonitorIoas.Contains(point.Ioa))
                    continue;
                if (!_uniqueMonitorIoas.TryAdd(point.Ioa, point))
                {
                    _uniqueMonitorIoas.Remove(point.Ioa);
                    _ambiguousMonitorIoas.Add(point.Ioa);
                }
            }
        }
    }

    private static bool IsControlProfilePoint(Iec10xPointMappingEntry p)
    {
        if (p.TypeId is >= 45 and <= 64) return true;
        var policy = p.CommandPolicy ?? string.Empty;
        return policy.Contains("Command", StringComparison.OrdinalIgnoreCase) ||
               policy.Contains("RemoteOnly", StringComparison.OrdinalIgnoreCase) ||
               policy.Contains("Control", StringComparison.OrdinalIgnoreCase) ||
               policy.Contains("Setpoint", StringComparison.OrdinalIgnoreCase) ||
               policy.Contains("Regulating", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsSingleStation => _singleProfileCa.HasValue;

    public Result Observe(string? source, int ca, int ioa, int typeId)
    {
        if (ca is < 0 or > 65535 || ioa is < 0 or > 0xFFFFFF || typeId is < 0 or > 255)
            return default;
        var strict = _profile.ResolveObservedSignal(ca, ioa, typeId);
        if (!_singleProfileCa.HasValue) return new(strict, false, false, false);
        var channel = string.IsNullOrWhiteSpace(source) ? "SINGLE" : source.Trim();
        if (!_sources.TryGetValue(channel, out var state))
        {
            if (_sources.Count >= 8) return new(strict, false, false, false);
            state = new SourceState { Ca = ca };
            _sources.Add(channel, state);
        }
        else if (state.Ca != ca)
        {
            var firstConflict = !state.Conflict;
            state.Conflict = true;
            return new(strict, false, false, firstConflict);
        }
        if (state.Conflict || strict is not null)
            return new(strict, false, false, false);

        // An exact (CA, IOA, TI-family) match always wins. Name-only fallback
        // is permitted only for a unique project monitor IOA within one known
        // station, for a compatible process category (SP vs DP, scaled vs float).
        // Preserve the original TI's semantics and display a type mismatch.
        if (!_uniqueMonitorIoas.TryGetValue(ioa, out var candidate) ||
            candidate.TypeId is not int profileTi ||
            !Iec10xPointTypeFamily.SameProcessKind(profileTi, typeId))
            return default;

        var caAlias = ca != _singleProfileCa;
        var first = caAlias && !state.Reported;
        if (caAlias) state.Reported = true;
        var tiMismatch = !Iec10xPointTypeFamily.SameObservationFamily(profileTi, typeId);
        return new(candidate, caAlias, first, false, tiMismatch);
    }
    public readonly record struct Result(
        Iec10xPointMappingEntry? Point, bool ReadOnlyAlias,
        bool FirstAlias, bool SourceCaConflict, bool TypeMismatch = false);
}
