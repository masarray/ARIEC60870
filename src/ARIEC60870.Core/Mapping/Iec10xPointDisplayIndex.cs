// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Mapping;

/// <summary>
/// Immutable, bounded display-name index for an operator-selected IEC-101/104
/// signal list. It cannot alter raw frame identity, command targets or GI
/// coverage. Rebuild this index whenever the active profile changes.
/// </summary>
public sealed class Iec10xPointDisplayIndex
{
    private readonly Dictionary<int, List<Iec10xPointMappingEntry>> _pointsByIoa = new();
    private readonly int? _profileCa;
    private readonly int? _singleStationCa;

    public Iec10xPointDisplayIndex(Iec10xPointMappingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profileCa = profile.CommonAddress;
        if (profile.CommonAddress is int ca && profile.Points.Count > 0 &&
            profile.Points.All(p => (p.Ca ?? profile.CommonAddress) == ca))
            _singleStationCa = ca;

        foreach (var point in profile.Points)
        {
            if (!_pointsByIoa.TryGetValue(point.Ioa, out var candidates))
                _pointsByIoa.Add(point.Ioa, candidates = new List<Iec10xPointMappingEntry>());
            candidates.Add(point);
        }
    }

    public readonly record struct Match(
        Iec10xPointMappingEntry? Point, bool NameOnly,
        bool CommonAddressMismatch, bool TypeMismatch);

    /// <summary>
    /// Strict CA/IOA/TI-family match first. With allowNameOnly, a single
    /// station's unique *process* IOA can supply its label when the wire CA/TI
    /// differs. Never use a name-only match for a command address, decoder
    /// selection, GI success or physical output proof.
    /// </summary>
    public Match Resolve(int? ca, int? ioa, int? typeId, bool allowNameOnly = false)
    {
        if (ca is null || ioa is null || typeId is null ||
            !_pointsByIoa.TryGetValue(ioa.Value, out var candidates))
            return default;

        Iec10xPointMappingEntry? exact = null;
        Iec10xPointMappingEntry? family = null;
        var exactCount = 0;
        var familyCount = 0;
        foreach (var point in candidates)
        {
            var mappedCa = point.Ca ?? _profileCa;
            if (mappedCa.HasValue && mappedCa.Value != ca.Value) continue;
            if (point.TypeId is null || point.TypeId.Value == typeId.Value)
            {
                exact = point;
                exactCount++;
            }
            else if (Iec10xPointTypeFamily.SameObservationFamily(point.TypeId.Value, typeId.Value))
            {
                family = point;
                familyCount++;
            }
        }
        if (exactCount > 1) return default;
        if (exactCount == 1) return new(exact, false, false, false);
        if (familyCount > 1) return default;
        if (familyCount == 1) return new(family, false, false, false);

        if (!allowNameOnly || !_singleStationCa.HasValue || candidates.Count != 1)
            return default;
        var candidate = candidates[0];
        if (candidate.TypeId is not int expected ||
            !Iec10xPointTypeFamily.SameProcessKind(expected, typeId.Value) ||
            candidate.TypeId is >= 45 and <= 64 ||
            !string.Equals(candidate.CommandPolicy, "MonitorOnly", StringComparison.OrdinalIgnoreCase))
            return default;
        return new(candidate, true, ca.Value != _singleStationCa.Value,
            !Iec10xPointTypeFamily.SameObservationFamily(expected, typeId.Value));
    }
}
