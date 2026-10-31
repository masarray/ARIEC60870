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
    public void ReconcileTail(IReadOnlyList<T> desired, bool allowResetForUnrelated = true)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (Count == 0)
        {
            if (allowResetForUnrelated) ReplaceRange(desired);
            else AddRange(desired);
            return;
        }
        if (desired.Count == 0)
        {
            if (allowResetForUnrelated) Clear();
            else while (Count > 0) RemoveAt(Count - 1);
            return;
        }

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

        if (matched == 0)
        {
            if (allowResetForUnrelated) ReplaceRange(desired);
            else
            {
                // Even a complete live-window rollover is not a new scope:
                // a WPF Reset destroys selection and realized row containers.
                while (Count > 0) RemoveAt(Count - 1);
                AddRange(desired);
            }
            return;
        }
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

        if (!AreKeysAligned(desired, key, comparer))
            PrependMatchingCasePrefix(desired, key, comparer);
        if (!AreKeysAligned(desired, key, comparer))
        {
            ReplaceRange(desired);
            return;
        }
        ApplyAlignedChanges(desired);
    }

    private bool AreKeysAligned<TKey>(IReadOnlyList<T> desired,
        Func<T, TKey> key, IEqualityComparer<TKey> comparer) where TKey : notnull
    {
        for (var i = 0; i < Math.Min(Count, desired.Count); i++)
            if (!comparer.Equals(key(this[i]), key(desired[i]))) return false;
        return true;
    }

    private void PrependMatchingCasePrefix<TKey>(IReadOnlyList<T> desired,
        Func<T, TKey> key, IEqualityComparer<TKey> comparer) where TKey : notnull
    {
        // Newest-first casebooks prepend new cases; do not reset 240 rows.
        if (desired.Count == 0 || Count == 0) return;
        for (var start = 1; start < desired.Count; start++)
        {
            if (!comparer.Equals(key(this[0]), key(desired[start]))) continue;
            var common = Math.Min(Count, desired.Count - start);
            var matches = true;
            for (var i = 0; i < common; i++)
            {
                if (comparer.Equals(key(this[i]), key(desired[start + i]))) continue;
                matches = false;
                break;
            }
            if (!matches) continue;
            for (var i = 0; i < start; i++) Insert(i, desired[i]);
            return;
        }
    }

    private void ApplyAlignedChanges(IReadOnlyList<T> desired)
    {
        var common = Math.Min(Count, desired.Count);
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
