// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Master.Iec101.Redundancy;

public sealed class Iec101ApplicationImageTracker
{
    private readonly HashSet<int> _observedIoas = new();

    public Iec101ApplicationImageState State { get; private set; } = Iec101ApplicationImageState.Empty;
    public DateTime? LastGiStartedUtc { get; private set; }
    public DateTime? LastGiCompletedUtc { get; private set; }
    public int ObjectCount => _observedIoas.Count;

    public bool IsFresh(TimeSpan maxAge)
        => State == Iec101ApplicationImageState.Ready
           && LastGiCompletedUtc.HasValue
           && DateTime.UtcNow - LastGiCompletedUtc.Value <= maxAge;

    public void MarkGiStarted(DateTime utcNow)
    {
        LastGiStartedUtc = utcNow;
        State = _observedIoas.Count == 0 ? Iec101ApplicationImageState.Building : Iec101ApplicationImageState.Partial;
    }

    public void MarkStale()
    {
        if (State != Iec101ApplicationImageState.Empty)
        {
            State = Iec101ApplicationImageState.Stale;
        }
    }

    public void Observe(Iec10xAsduDecode? asdu, DateTime utcNow)
    {
        if (asdu is null)
        {
            return;
        }

        foreach (var obj in asdu.Objects)
        {
            if (obj.InformationObjectAddress > 0)
            {
                _observedIoas.Add(obj.InformationObjectAddress);
            }
        }

        if (asdu.CauseOfTransmission == 10 && asdu.TypeId == 100)
        {
            LastGiCompletedUtc = utcNow;
            State = Iec101ApplicationImageState.Ready;
        }
        else if (_observedIoas.Count > 0 && State is Iec101ApplicationImageState.Empty or Iec101ApplicationImageState.Building)
        {
            State = Iec101ApplicationImageState.Partial;
        }
    }
}
