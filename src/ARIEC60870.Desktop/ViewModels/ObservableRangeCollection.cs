// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// ObservableCollection with coarse-grained range operations. WPF collection views are
/// most stable with Reset notifications for multi-item changes, so batch updates use
/// one Reset instead of hundreds of Add/Remove notifications.
/// </summary>
public sealed class ObservableRangeCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification;

    public void AddRange(IEnumerable<T> items)
    {
        if (items is null)
        {
            return;
        }

        var list = items as IList<T> ?? items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        _suppressNotification = true;
        try
        {
            foreach (var item in list)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotification = false;
        }

        RaiseReset();
    }

    public void ReplaceRange(IEnumerable<T> items)
    {
        _suppressNotification = true;
        try
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotification = false;
        }

        RaiseReset();
    }

    public int TrimStart(int maxCount)
    {
        if (maxCount < 0)
        {
            maxCount = 0;
        }

        var removeCount = Count - maxCount;
        if (removeCount <= 0)
        {
            return 0;
        }

        _suppressNotification = true;
        try
        {
            for (var i = 0; i < removeCount; i++)
            {
                Items.RemoveAt(0);
            }
        }
        finally
        {
            _suppressNotification = false;
        }

        RaiseReset();
        return removeCount;
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
        {
            base.OnCollectionChanged(e);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_suppressNotification)
        {
            base.OnPropertyChanged(e);
        }
    }

    private void RaiseReset()
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
