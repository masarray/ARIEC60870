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
            _singleProfileCa = ca;
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
        if (state.Conflict || strict is not null || ca == _singleProfileCa)
            return new(strict, false, false, false);
        // No cross-station IOA-only alias. Enforce one unique CA + typed
        // point family; actual source CA is never re-keyed.
        if (_profile.FindUniqueMismatchedCommonAddress(ca, ioa, typeId) != _singleProfileCa)
            return default;
        var candidate = _profile.ResolveObservedSignal(_singleProfileCa, ioa, typeId);
        if (candidate is null) return default;
        var first = !state.Reported;
        state.Reported = true;
        return new(candidate, true, first, false);
    }
    public readonly record struct Result(
        Iec10xPointMappingEntry? Point, bool ReadOnlyAlias,
        bool FirstAlias, bool SourceCaConflict);
}
