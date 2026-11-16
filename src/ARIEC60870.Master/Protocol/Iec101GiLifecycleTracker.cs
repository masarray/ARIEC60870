// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Master.Protocol;

/// <summary>
/// Per-physical IEC-101 COM-epoch GI application lifecycle gate. Correlates only
/// actual, decoded TI100 COT6 TX with compatible RTU ACTCON/ACTTERM by CA,
/// originator and QOI. A link ACK, unrelated GI or ACTTERM without ACTCON
/// never establishes completed application interrogation.
/// </summary>
public sealed class Iec101GiLifecycleTracker
{
    private int? _ca;
    private int _originator;
    private int _qoi;
    private bool _actcon;
    private bool _ambiguous;

    public int? PendingCommonAddress => _ca;
    public bool HasAcceptedActivation => _ca.HasValue && _actcon && !_ambiguous;

    public void Reset()
    {
        _ca = null;
        _originator = 0;
        _qoi = 0;
        _actcon = false;
        _ambiguous = false;
    }

    /// <summary>
    /// Call only after a physical IEC-101 TX write succeeded. This does not
    /// imply a link ACK or an RTU ACTCON.
    /// </summary>
    public bool ObserveTransmitted(Iec10xAsduDecode? asdu)
    {
        if (!ValidGi(asdu) || asdu!.CauseOfTransmission != 6 ||
            asdu.IsTest || asdu.IsNegativeConfirm)
            return false;

        var qoi = asdu.CommandDetails!.QualifierValue!.Value;
        var overlapping = _ca == asdu.CommonAddress &&
                          _originator == asdu.OriginatorAddress && _qoi == qoi;
        _ambiguous = overlapping;
        _ca = asdu.CommonAddress;
        _originator = asdu.OriginatorAddress;
        _qoi = qoi;
        _actcon = false;
        return true;
    }

    /// <summary>Inspect only a service-validated secondary FT1.2 user-data ASDU.</summary>
    public Iec101GiLifecycleObservation ObserveReceived(Iec10xAsduDecode? asdu)
    {
        if (!ValidGi(asdu) || asdu!.IsTest ||
            asdu.CauseOfTransmission is not (7 or 10))
            return Iec101GiLifecycleObservation.None;

        if (!_ca.HasValue || asdu.CommonAddress != _ca.Value ||
            asdu.OriginatorAddress != _originator ||
            asdu.CommandDetails!.QualifierValue!.Value != _qoi)
            return Iec101GiLifecycleObservation.Unmatched;

        if (_ambiguous)
            return Iec101GiLifecycleObservation.Ambiguous;

        if (asdu.CauseOfTransmission == 7)
        {
            if (asdu.IsNegativeConfirm)
            {
                Reset();
                return Iec101GiLifecycleObservation.NegativeActcon;
            }
            _actcon = true;
            return Iec101GiLifecycleObservation.PositiveActcon;
        }

        if (asdu.IsNegativeConfirm || !_actcon)
            return Iec101GiLifecycleObservation.IncompleteActterm;

        Reset();
        return Iec101GiLifecycleObservation.MatchedActterm;
    }

    private static bool ValidGi(Iec10xAsduDecode? asdu)
    {
        if (asdu is null) return false;
        return asdu.IsHeaderValid && asdu.TypeId == 100 &&
               asdu.ObjectCount == 1 && asdu.Issues.Count == 0 &&
               asdu.InformationObjectAddress == 0 && asdu.Objects.Count == 1 &&
               asdu.Objects[0].Issues.Count == 0 &&
               asdu.CommandDetails?.QualifierValue is >= 20 and <= 36;
    }
}

public enum Iec101GiLifecycleObservation
{
    None,
    PositiveActcon,
    NegativeActcon,
    MatchedActterm,
    IncompleteActterm,
    Unmatched,
    Ambiguous
}
