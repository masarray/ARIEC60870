// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Master.Iec101;

/// <summary>
/// Bounded, fail-closed IEC-101 process-ASDU evidence for a *GI-only* alternative
/// Common Address. This never changes configured station identity or a control
/// destination. One ASDU counts as one observation regardless of object count.
/// Single-link and dual-link share this policy.
/// </summary>
public sealed class Iec101CommonAddressEvidence
{
    // A stray/duplicated ASDU must not re-address an interrogation. Observations
    // are saturated rather than retained in an unbounded per-station dictionary.
    public const int MinimumProcessAsdus = 3;

    private readonly int _configuredCommonAddress;
    private int _observedCommonAddress;
    private int _acceptedObservations;

    public Iec101CommonAddressEvidence(int configuredCommonAddress)
    {
        if (configuredCommonAddress <= 0 || configuredCommonAddress > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(configuredCommonAddress));
        }

        _configuredCommonAddress = configuredCommonAddress;
    }

    /// <summary>
    /// True when more than one station CA produced eligible process ASDUs.
    /// Once ambiguous, this session must never auto-select an alternative CA.
    /// </summary>
    public bool IsAmbiguous { get; private set; }

    public int? UniqueAlternativeForGi =>
        !IsAmbiguous && _acceptedObservations >= MinimumProcessAsdus &&
        _observedCommonAddress != _configuredCommonAddress
            ? _observedCommonAddress
            : null;

    public void Observe(Iec10xAsduDecode? asdu)
    {
        if (!IsEligibleProcessAsdu(asdu) || IsAmbiguous)
        {
            return;
        }

        var ca = asdu!.CommonAddress;
        if (_acceptedObservations == 0)
        {
            _observedCommonAddress = ca;
            _acceptedObservations = 1;
        }
        else if (_observedCommonAddress != ca)
        {
            // Even a valid frame from the configured CA is evidence against
            // attributing this physical link to one automatically learned CA.
            IsAmbiguous = true;
        }
        else if (_acceptedObservations < MinimumProcessAsdus)
        {
            _acceptedObservations++;
        }
    }

    public static bool IsEligibleProcessAsdu(Iec10xAsduDecode? asdu)
    {
        if (asdu is null || !asdu.IsHeaderValid || asdu.IsTest ||
            asdu.IsNegativeConfirm || asdu.IsControlCommand ||
            asdu.Issues.Count != 0 || asdu.ObjectCount <= 0 ||
            asdu.Objects.Count != asdu.ObjectCount)
        {
            return false;
        }

        // CA 0 and the all-ones global address are never station identities.
        var maxCa = asdu.CommonAddressWidth switch
        {
            1 => byte.MaxValue,
            2 => ushort.MaxValue,
            _ => 0
        };
        if (asdu.CommonAddress <= 0 || asdu.CommonAddress >= maxCa)
        {
            return false;
        }

        // No proprietary/opaque types, control ASDUs, TEST, negative confirmations
        // or unknown/partial objects may steer a transmitted GI. Positive process
        // values from periodic, background, spontaneous, initialization, request,
        // or GI responses are the only admissible source.
        var cot = asdu.CauseOfTransmission;
        if (!((cot >= 1 && cot <= 5) || (cot >= 20 && cot <= 36)))
        {
            return false;
        }

        return asdu.TypeId is 1 or 2 or 3 or 4 or 9 or 10 or 11 or 12 or
            13 or 14 or 30 or 31 or 34 or 35 or 36;
    }
}
