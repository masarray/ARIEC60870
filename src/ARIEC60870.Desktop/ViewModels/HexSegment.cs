// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ARIEC60870.Desktop.ViewModels;

public sealed class HexSegment : INotifyPropertyChanged
{
    private bool _isActive;

    public HexSegment(string key, string hex, string title, string meaning)
    {
        Key = key;
        Hex = hex;
        Title = title;
        Meaning = meaning;
        DisplayMeaning = string.IsNullOrWhiteSpace(title) ? meaning : $"{title}: {meaning}";
    }

    public string Key { get; }
    public string Hex { get; }
    public string Title { get; }
    public string Meaning { get; }
    public string DisplayMeaning { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
