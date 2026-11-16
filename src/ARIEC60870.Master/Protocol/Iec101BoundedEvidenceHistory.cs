// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;

namespace ARIEC60870.Master.Protocol;

/// <summary>
/// Bounded O(1) rolling evidence retention for IEC-101 Master session results.
/// Original evidence still reaches subscribers before it can be evicted from
/// this report snapshot. This is not a persistent raw-wire archive.
/// </summary>
public sealed class Iec101BoundedEvidenceHistory
{
    private readonly Queue<Iec103MasterEvidenceEvent> _items = new();

    public Iec101BoundedEvidenceHistory(int configuredCapacity)
        => Capacity = Math.Max(100, configuredCapacity);

    public int Capacity { get; }
    public int Count => _items.Count;
    public long Evicted { get; private set; }

    /// <returns>Number evicted from the bounded in-memory report, 0 or 1.</returns>
    public int Append(Iec103MasterEvidenceEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Enqueue(item);
        if (_items.Count <= Capacity) return 0;
        _items.Dequeue();
        Evicted++;
        return 1;
    }

    public Iec103MasterEvidenceEvent[] Snapshot() => _items.ToArray();
}
