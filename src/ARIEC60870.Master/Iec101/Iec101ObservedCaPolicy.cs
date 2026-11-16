// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Master.Iec101;

/// <summary>
/// Session-scoped, bounded and thread-safe evidence for a GI-only alternative CA.
/// Link ACK and passive data never authorize a new interrogation or control target.
/// </summary>
public sealed class Iec101ObservedCaPolicy
{
    // One ASDU counts once regardless of VSQ. Conservative advisory evidence
    // is not independent physical station identity proof.
    public const int MinimumProcessAsdus = 3;

    private readonly object _gate = new();
    private int? _singleObservedCa;
    private int _hits;
    private bool _conflictingStations;

    public bool HasConflictingStations
    {
        get { lock (_gate) return _conflictingStations; }
    }

    /// <summary>
    /// Eligible only after three fully decoded process ASDUs identify one
    /// station, with no contradictory station in the current session epoch.
    /// </summary>
    public int? GiRetryCandidate
    {
        get
        {
            lock (_gate)
                return !_conflictingStations && _hits >= MinimumProcessAsdus
                    ? _singleObservedCa : null;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _singleObservedCa = null;
            _hits = 0;
            _conflictingStations = false;
        }
    }

    public void Observe(Iec10xAsduDecode? asdu)
    {
        if (!IsValidProcessEvidence(asdu)) return;
        lock (_gate)
        {
            if (_conflictingStations) return;
            var ca = asdu!.CommonAddress;
            if (!_singleObservedCa.HasValue)
            {
                _singleObservedCa = ca;
                _hits = 1;
            }
            else if (_singleObservedCa.Value != ca)
            {
                // Fail closed for the rest of the session; never majority-vote.
                _conflictingStations = true;
                _singleObservedCa = null;
                _hits = 0;
            }
            else if (_hits < MinimumProcessAsdus)
            {
                _hits++;
            }
        }
    }

    public static bool IsValidProcessEvidence(Iec10xAsduDecode? asdu)
    {
        if (asdu is null || !asdu.IsHeaderValid ||
            asdu.IsTest || asdu.IsNegativeConfirm || asdu.IsControlCommand ||
            asdu.IsOpaqueVendorSpecific || asdu.Issues.Count != 0 ||
            asdu.ObjectCount <= 0 || asdu.Objects.Count != asdu.ObjectCount)
            return false;

        // CA=0 and all-ones are not unique process-station addresses.
        var broadcastCa = asdu.CommonAddressWidth switch
        {
            1 => byte.MaxValue,
            2 => ushort.MaxValue,
            _ => 0
        };
        if (asdu.CommonAddress <= 0 || asdu.CommonAddress >= broadcastCa)
            return false;

        foreach (var obj in asdu.Objects)
        {
            if (obj.Issues.Count != 0) return false;
        }

        if (asdu.CauseOfTransmission is not (1 or 2 or 3 or 4 or 5 or >= 20 and <= 36))
            return false;

        return asdu.TypeId is 1 or 2 or 3 or 4 or 9 or 10 or 11 or 12 or
            13 or 14 or 30 or 31 or 34 or 35 or 36;
    }
}
