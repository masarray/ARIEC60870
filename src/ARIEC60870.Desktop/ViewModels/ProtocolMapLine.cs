// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ARIEC60870.Desktop.ViewModels;

public sealed class ProtocolMapLine : INotifyPropertyChanged
{
    private bool _isActive;

    public ProtocolMapLine(string key, string title, string meaning, string evidence)
    {
        Key = key;
        Title = title;
        Meaning = meaning;
        Evidence = evidence;
        IsCriticalData = key.Equals("object", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("payload", StringComparison.OrdinalIgnoreCase);
    }

    public string Key { get; }
    public string Title { get; }
    public string Meaning { get; }
    public string Evidence { get; }
    public bool IsCriticalData { get; }

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
