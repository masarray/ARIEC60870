// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Presentation-only bridge for Master-owned CommandCase.TypeId; avoids adding
/// WPF dependencies or duplicate type-classification rules to the casebook.
/// </summary>
public sealed class IecSignalTypeBadgeConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int typeId ? IecSignalTypeBadge.FromDecoded("101", typeId) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
