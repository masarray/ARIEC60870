// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol;

/// <summary>
/// Shared bounded FIFO for single-link and A/B operator intent. It refuses new
/// commands when full; it never silently drops, replaces or replays a control.
/// </summary>
public sealed class Iec101BoundedFifo<T>
{
    private readonly object _gate = new();
    private readonly Queue<T> _items = new();

    public Iec101BoundedFifo(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    public bool TryEnqueue(T value)
    {
        lock (_gate)
        {
            if (_items.Count >= Capacity) return false;
            _items.Enqueue(value);
            return true;
        }
    }

    public bool TryDequeue(out T value)
    {
        lock (_gate)
        {
            if (_items.TryDequeue(out var item))
            {
                value = item;
                return true;
            }
        }
        value = default!;
        return false;
    }

    public int Clear()
    {
        lock (_gate)
        {
            var discarded = _items.Count;
            _items.Clear();
            return discarded;
        }
    }
}
