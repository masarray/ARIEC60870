// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Bounded dispatcher-owned WPF projection. Routine live changes emit precise
/// single-item notifications; Reset is reserved for unrelated scope replacement.
/// </summary>
public sealed class ObservableRangeCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification;

    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        // ListCollectionView support for multi-item change payloads varies:
        // WPF-safe single Add notifications preserve realized row containers.
        foreach (var item in items is IList<T> list ? list : items.ToArray())
            Add(item);
    }

    public void ReplaceRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var desired = items as IList<T> ?? items.ToArray();
        if (Count == desired.Count && Enumerable.Range(0, Count)
            .All(i => EqualityComparer<T>.Default.Equals(this[i], desired[i])))
            return;
        _suppressNotification = true;
        try
        {
            Items.Clear();
            foreach (var item in desired) Items.Add(item);
        }
        finally { _suppressNotification = false; }
        RaiseReset();
    }

    /// <summary>Overlap of current suffix and desired prefix in O(n) using KMP.
    /// Normal rolling frame advancement removes only expired rows and appends
    /// only new ones. No collection-wide Reset on continuous trace traffic.</summary>
    public void ReconcileTail(IReadOnlyList<T> desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (Count == 0) { ReplaceRange(desired); return; }
        if (desired.Count == 0) { Clear(); return; }

        var eq = EqualityComparer<T>.Default;
        var prefix = new int[desired.Count];
        for (int i = 1, j = 0; i < desired.Count; i++)
        {
            while (j > 0 && !eq.Equals(desired[i], desired[j])) j = prefix[j - 1];
            if (eq.Equals(desired[i], desired[j])) j++;
            prefix[i] = j;
        }
        var matched = 0;
        for (int i = 0; i < Count; i++)
        {
            while (matched > 0 && (matched == desired.Count ||
                   !eq.Equals(this[i], desired[matched])))
                matched = prefix[matched - 1];
            if (matched < desired.Count && eq.Equals(this[i], desired[matched])) matched++;
            if (matched == desired.Count && i != Count - 1)
                matched = prefix[matched - 1];
        }

        if (matched == 0) { ReplaceRange(desired); return; }
        var oldPrefix = Count - matched;
        for (var i = 0; i < oldPrefix; i++) RemoveAt(0);
        for (var i = matched; i < desired.Count; i++) Add(desired[i]);
    }

    /// <summary>Preserve unchanged objects when sorted Value or Case keys
    /// remain stable; only genuinely changed rows raise Replace notifications.
    /// Structural reorder is explicitly handled as a new scope snapshot.</summary>
    public void ReconcileByKey<TKey>(IReadOnlyList<T> desired,
        Func<T, TKey> key, IEqualityComparer<TKey>? comparer = null) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(key);
        comparer ??= EqualityComparer<TKey>.Default;
        if (Count == 0) { ReplaceRange(desired); return; }
        // Newest-first casebooks often prepend a small number of cases while
        // evicting the oldest. Keep the common sequence and avoid a WPF Reset.
        var eqKey = comparer;
        if (desired.Count > 0 && Count > 0 &&
            !eqKey.Equals(key(this[0]), key(desired[0])))
        {
            var prepend = -1;
            for (var j = 1; j < desired.Count; j++)
            {
                if (!eqKey.Equals(key(this[0]), key(desired[j]))) continue;
                var matching = Math.Min(Count, desired.Count - j);
                var aligned = true;
                for (var k = 0; k < matching; k++)
                {
                    if (eqKey.Equals(key(this[k]), key(desired[j + k]))) continue;
                    aligned = false;
                    break;
                }
                if (aligned) { prepend = j; break; }
            }
            if (prepend > 0)
                for (var i = 0; i < prepend; i++) Insert(i, desired[i]);
        }
        var common = Math.Min(Count, desired.Count);
        for (var i = 0; i < common; i++
        {
            if (!comparer.Equals(key(this[i]), key(desired[i])))
            {
                ReplaceRange(desired);
                return;
            }
        }
        for (var i = 0; i < common; i++)
            if (!EqualityComparer<T>.Default.Equals(this[i], desired[i]))
                this[i] = desired[i];
        while (Count > desired.Count) RemoveAt(Count - 1);
        for (var i = common; i < desired.Count; i++) Add(desired[i]);
    }

    public int TrimStart(int maxCount)
    {
        var count = Math.Max(0, Count - Math.Max(0, maxCount));
        for (var i = 0; i < count; i++) RemoveAt(0);
        return count;
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification) base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_suppressNotification) base.OnPropertyChanged(e);
    }

    private void RaiseReset()
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
