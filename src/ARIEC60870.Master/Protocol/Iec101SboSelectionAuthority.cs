// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Master.Protocol;

/// <summary>
/// Per-physical-COM, per-owner-epoch SBO admission authority. Link ACK never
/// authorizes EXECUTE: only an exact, positively confirmed SELECT ASDU does.
/// Direct Operate is intentionally a distinct operator intent.
/// </summary>
public sealed class Iec101SboSelectionAuthority
{
    private static readonly TimeSpan MaximumSelectAge = TimeSpan.FromSeconds(30);
    private Selection? _selection;

    private sealed record Selection(string Identity, DateTime TransmittedUtc, bool Accepted, bool Ambiguous);

    public void Reset() => _selection = null;

    public bool CanExecuteSelected(Iec10xAsduDecode? execute, DateTime nowUtc)
    {
        var selected = _selection;
        return selected is { Accepted: true, Ambiguous: false } &&
               nowUtc >= selected.TransmittedUtc &&
               nowUtc - selected.TransmittedUtc <= MaximumSelectAge &&
               Iec10xCommandWireIdentity.TryCreate(execute, select: false, out var identity) &&
               string.Equals(selected.Identity, identity, StringComparison.Ordinal);
    }

    /// <summary>Invoke only AFTER transport.WriteAsync succeeds, not at queue admission.</summary>
    public void ObserveTransmitted(Iec10xAsduDecode? asdu, DateTime nowUtc)
    {
        if (!Iec10xCommandWireIdentity.TryCreate(asdu, select: true, out var identity))
        {
            // Any other valid on-wire control execution consumes previous SELECT.
            if (Iec10xCommandWireIdentity.TryCreate(asdu, select: false, out _)) Reset();
            return;
        }
        if (asdu!.CauseOfTransmission != 6) return;
        var old = _selection;
        // Two unresolved SELECTs cannot be uniquely paired to a subsequent ACTCON.
        if (old is not null && nowUtc >= old.TransmittedUtc &&
            nowUtc - old.TransmittedUtc <= MaximumSelectAge)
        {
            _selection = old with { Ambiguous = true };
            return;
        }
        _selection = new Selection(identity, nowUtc, Accepted: false, Ambiguous: false);
    }

    /// <summary>Accept only service-validated secondary user-data ASDU on this COM.</summary>
    public void ObserveReceived(Iec10xAsduDecode? asdu)
    {
        if (_selection is not { Ambiguous: false } selected ||
            !Iec10xCommandWireIdentity.TryCreate(asdu, select: true, out var identity) ||
            asdu!.CauseOfTransmission != 7 ||
            !string.Equals(selected.Identity, identity, StringComparison.Ordinal))
            return;

        if (asdu.IsNegativeConfirm)
        {
            Reset();
            return;
        }

        _selection = selected with { Accepted = true };
    }

}
