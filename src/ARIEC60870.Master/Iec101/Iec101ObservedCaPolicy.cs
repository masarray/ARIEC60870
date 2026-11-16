// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Master.Iec101;

/// <summary>
/// Session-scoped, bounded evidence policy for IEC-101 application CA discovery.
/// A valid secondary link reply does not by itself prove process-station identity.
/// Observations are advisory for a single bounded GI retry, NEVER command routing.
/// </summary>
public sealed class Iec101ObservedCaPolicy
{
    private int? _singleObservedCa;
    private int _hits;
    private bool _conflictingStations;

    public bool HasConflictingStations => _conflictingStations;

    /// <summary>
    /// A CA is usable for a GI retry only after two fully decoded process ASDUs
    /// from the same station, with no second station seen in this session.
    /// </summary>
    public int? GiRetryCandidate => !_conflictingStations && _hits >= 2 ? _singleObservedCa : null;

    public void Reset()
    {
        _singleObservedCa = null;
        _hits = 0;
        _conflictingStations = false;
    }

    public void Observe(Iec10xAsduDecode? asdu)
    {
        if (!IsValidProcessEvidence(asdu) || _conflictingStations)
        {
            return;
        }

        var ca = asdu!.CommonAddress;
        if (!_singleObservedCa.HasValue)
        {
            _singleObservedCa = ca;
            _hits = 1;
        }
        else if (_singleObservedCa.Value != ca)
        {
            // Fail closed on a multi-station bus. Never pick a majority CA by
            // arrival order, frequency, or the current poller's timing.
            _conflictingStations = true;
            _singleObservedCa = null;
            _hits = 0;
        }
        else if (_hits < 2)
        {
            _hits++;
        }
    }

    public static bool IsValidProcessEvidence(Iec10xAsduDecode? asdu)
    {
        if (asdu is null || !asdu.IsHeaderValid || asdu.CommonAddress <= 0 ||
            asdu.IsTest || asdu.IsNegativeConfirm || asdu.IsControlCommand ||
            asdu.IsOpaqueVendorSpecific || asdu.Issues.Count != 0 ||
            asdu.Objects.Count == 0 || asdu.Objects.Count != asdu.ObjectCount)
        {
            return false;
        }

        // Reject malformed/truncated object payloads, not just broken headers.
        foreach (var obj in asdu.Objects)
        {
            if (obj.Issues.Count != 0)
            {
                return false;
            }
        }

        // Only standard process measurements and spontaneous/interrogated
        // process value causes can contribute to address discovery.
        if (asdu.CauseOfTransmission is not (1 or 2 or 3 or 5 or >= 20 and <= 36))
        {
            return false;
        }

        return asdu.TypeId is 1 or 2 or 3 or 4 or 9 or 10 or 11 or 12 or
            13 or 14 or 30 or 31 or 34 or 35 or 36;
    }
}
