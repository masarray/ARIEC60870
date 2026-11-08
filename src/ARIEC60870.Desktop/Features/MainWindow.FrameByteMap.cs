// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ARIEC60870.Desktop.ViewModels;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private FrameByteMapWindow? _frameByteMapWindow;

    private void FrameTraceGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only a genuine frame-row double click opens a byte diagram; a
        // scrollbar, header or empty trace background must do nothing.
        if (e.ChangedButton != MouseButton.Left ||
            e.OriginalSource is not DependencyObject origin ||
            ItemsControl.ContainerFromElement(FrameTraceGrid, origin) is not ListBoxItem item ||
            item.DataContext is not EvidenceRow row)
            return;
        OpenPhysicalByteMap(row);
        e.Handled = true;
    }

    private void TraceFrameHexByteDetail_Click(object sender, RoutedEventArgs e)
    {
        var row = FrameTraceGrid.SelectedItem as EvidenceRow ??
            FrameTraceGrid.SelectedItems.OfType<EvidenceRow>()
                .OrderBy(x => x.Sequence).LastOrDefault() ??
            _selectedFrameRow;
        if (row is null) return;
        OpenPhysicalByteMap(row);
    }

    private void OpenPhysicalByteMap(EvidenceRow row)
    {
        // A single modeless native detail window follows the explicitly
        // chosen source frame; no parallel hidden inspector or live updates.
        if (_frameByteMapWindow is { IsVisible: true })
            _frameByteMapWindow.Close();
        var window = new FrameByteMapWindow(row) { Owner = this };
        _frameByteMapWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_frameByteMapWindow, window))
                _frameByteMapWindow = null;
        };
        window.Show();
        window.Activate();
    }
}
