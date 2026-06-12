// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Fixed-capacity circular buffer used as the backing store for high-volume UI evidence.
/// Appends are O(1); snapshot is ordered oldest-to-newest for UI replacement.
/// </summary>
public sealed class BoundedRingBuffer<T>
{
    private readonly T[] _items;
    private int _start;
    private int _count;

    public BoundedRingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Ring buffer capacity must be greater than zero.");
        }

        _items = new T[capacity];
    }

    public int Capacity => _items.Length;
    public int Count => _count;

    public void Add(T item)
    {
        var index = (_start + _count) % _items.Length;
        if (_count == _items.Length)
        {
            _items[_start] = item;
            _start = (_start + 1) % _items.Length;
        }
        else
        {
            _items[index] = item;
            _count++;
        }
    }

    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            Add(item);
        }
    }

    public IReadOnlyList<T> Snapshot()
    {
        var result = new List<T>(_count);
        for (var i = 0; i < _count; i++)
        {
            result.Add(_items[(_start + i) % _items.Length]);
        }

        return result;
    }

    public void Clear()
    {
        Array.Clear(_items, 0, _items.Length);
        _start = 0;
        _count = 0;
    }
}
