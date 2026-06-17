// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using ARIEC60870.Core.Mapping;
using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Reporting;
using ARIEC60870.Master.Transport;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabControl))
        {
            return;
        }

        RefreshActiveTraceSnapshot();
        if (IsReportPreviewTabActive())
        {
            Dispatcher.BeginInvoke(new Action(EnsureReportPreviewVisible), DispatcherPriority.Background);
        }
        UpdateAutoScrollLatestRailVisual();
        UpdateSegmentedNav(false);
    }

    private void SegmentedNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is null)
        {
            return;
        }

        if (int.TryParse(button.Tag.ToString(), out var index) && index >= 0 && index < MainTabControl.Items.Count)
        {
            MainTabControl.SelectedIndex = index;
        }
    }

    private Button[] GetSegmentedNavButtons()
        => new[]
        {
            NavDualLinkButton,
            NavFrameButton,
            NavValueButton,
            NavEventButton,
            NavReportButton,
            NavOperatorButton,
            NavFindingsButton,
            NavDiagnosticsButton,
            NavNotesButton,
            NavTriggersButton
        };

    private void ApplyWorkspaceNavigationProfile()
    {
        if (!IsLoaded || MainTabControl is null)
        {
            return;
        }

        var isDual101 = IsIec101DualLinkModeSelected();

        NavDualLinkButton.Content = "Redundancy";
        NavFrameButton.Content = "Trace";
        NavValueButton.Content = "Values";
        NavEventButton.Content = "Events";
        NavReportButton.Content = "Report";

        NavDualLinkButton.Visibility = isDual101 ? Visibility.Visible : Visibility.Collapsed;
        NavFrameButton.Visibility = Visibility.Visible;
        NavValueButton.Visibility = Visibility.Visible;
        NavEventButton.Visibility = Visibility.Visible;
        NavReportButton.Visibility = Visibility.Visible;

        // Advanced/supporting workspaces remain available in code and export logic,
        // but no longer compete with the primary commissioning path.
        NavOperatorButton.Visibility = Visibility.Collapsed;
        NavFindingsButton.Visibility = Visibility.Collapsed;
        NavDiagnosticsButton.Visibility = Visibility.Collapsed;
        NavNotesButton.Visibility = Visibility.Collapsed;
        NavTriggersButton.Visibility = Visibility.Collapsed;

        if (isDual101 && MainTabControl.SelectedIndex is 0 or 4 or 5 or 7 or 8)
        {
            MainTabControl.SelectedIndex = 9;
        }
        else if (!isDual101 && (MainTabControl.SelectedIndex == 9 || MainTabControl.SelectedIndex is 0 or 4 or 5 or 7 or 8))
        {
            MainTabControl.SelectedIndex = 1;
        }

        UpdateSegmentedNav(false);
    }

    private void UpdateSegmentedNav(bool animated)
    {
        if (!IsLoaded || MainTabControl is null)
        {
            return;
        }

        if (SegmentSlider is not null)
        {
            SegmentSlider.BeginAnimation(WidthProperty, null);
            SegmentSlider.Visibility = Visibility.Collapsed;
        }

        var inactiveBrush = (Brush)FindResource("Ink600Brush");
        var activeForegroundBrush = (Brush)FindResource("Ink900Brush");
        var activeBackgroundBrush = (Brush)FindResource("AccentSoftBrush");
        var activeBorderBrush = (Brush)FindResource("AccentBrush");
        var transparentBrush = Brushes.Transparent;
        var selectedIndex = MainTabControl.SelectedIndex;

        foreach (var button in GetSegmentedNavButtons())
        {
            var isActive = button.Tag is not null
                           && int.TryParse(button.Tag.ToString(), out var tagIndex)
                           && tagIndex == selectedIndex;

            button.Background = isActive ? activeBackgroundBrush : transparentBrush;
            button.BorderBrush = isActive ? activeBorderBrush : transparentBrush;
            button.Foreground = isActive ? activeForegroundBrush : inactiveBrush;
            button.FontWeight = isActive ? FontWeights.Medium : FontWeights.Normal;
        }
    }
























    private void FrameTraceGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        var pointer = e.GetPosition(listBox);
        if (IsScrollChromeInput(e.OriginalSource as DependencyObject) || IsPointerInsideVerticalScrollbarZone(listBox, pointer))
        {
            return;
        }

        var index = GetProtocolTraceIndexFromInput(listBox, e.OriginalSource as DependencyObject, pointer);
        if (index < 0 || index >= FrameTraceRows.Count)
        {
            return;
        }

        EnterLineMonitorHoldForUserSelection();
        BeginProtocolTraceSelectionBatch();
        ApplyProtocolTraceSelectionGesture(listBox, index, Keyboard.Modifiers);
        _isProtocolTraceDragSelecting = true;
        FocusProtocolTraceContainer(listBox, index);
        UpdateLineMonitorStatus();
        e.Handled = true;
    }

    private void FrameTraceGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isProtocolTraceDragSelecting || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (sender is not ListBox listBox)
        {
            return;
        }

        var pointer = e.GetPosition(listBox);
        if (IsScrollChromeInput(e.OriginalSource as DependencyObject) || IsPointerInsideVerticalScrollbarZone(listBox, pointer))
        {
            return;
        }

        var index = GetProtocolTraceIndexFromInput(listBox, e.OriginalSource as DependencyObject, pointer);
        if (index < 0 || index >= FrameTraceRows.Count)
        {
            return;
        }

        ExtendProtocolTraceSelectionToIndex(listBox, index);
        e.Handled = true;
    }

    private void FrameTraceLineItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_isProtocolTraceDragSelecting || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (sender is not ListBoxItem item || item.DataContext is not EvidenceRow row)
        {
            return;
        }

        if (ItemsControl.ItemsControlFromItemContainer(item) is not ListBox listBox)
        {
            return;
        }

        var index = FrameTraceRows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        ExtendProtocolTraceSelectionToIndex(listBox, index);
        e.Handled = true;
    }

    private void FrameTraceGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isProtocolTraceDragSelecting = false;

        if (sender is ListBox listBox)
        {
            EndProtocolTraceSelectionBatch(listBox);
        }
        else
        {
            EndProtocolTraceSelectionBatch(FrameTraceGrid);
        }
    }

    private void FrameTraceGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        var index = GetProtocolTraceIndexFromInput(listBox, e.OriginalSource as DependencyObject, e.GetPosition(listBox));
        if (index < 0 || index >= FrameTraceRows.Count)
        {
            return;
        }

        EnterLineMonitorHoldForUserSelection();
        var row = FrameTraceRows[index];
        if (!listBox.SelectedItems.Contains(row))
        {
            listBox.SelectedItems.Clear();
            listBox.SelectedItems.Add(row);
            _protocolTraceSelectionAnchorIndex = index;
        }

        FocusProtocolTraceContainer(listBox, index);
    }

    private void BeginProtocolTraceSelectionBatch()
    {
        _isProtocolTraceSelectionBatching = true;
        _pendingProtocolTraceSelectionInspectorRefresh = false;
    }

    private void EndProtocolTraceSelectionBatch(ListBox? listBox)
    {
        _isProtocolTraceSelectionBatching = false;

        if (!_pendingProtocolTraceSelectionInspectorRefresh)
        {
            return;
        }

        _pendingProtocolTraceSelectionInspectorRefresh = false;
        var row = listBox?.SelectedItems
            .OfType<EvidenceRow>()
            .OrderBy(item => item.Sequence)
            .LastOrDefault();

        ApplySelectedEvidenceRowToInspector(row);
    }

    private void ApplyProtocolTraceSelectionGesture(ListBox listBox, int index, ModifierKeys modifiers)
    {
        var row = FrameTraceRows[index];
        var shift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var ctrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (shift)
        {
            var anchor = GetProtocolTraceSelectionAnchorIndex(listBox, index);
            SelectProtocolTraceRange(listBox, anchor, index, additive: ctrl);
            return;
        }

        if (ctrl)
        {
            if (listBox.SelectedItems.Contains(row))
            {
                listBox.SelectedItems.Remove(row);
            }
            else
            {
                listBox.SelectedItems.Add(row);
            }

            _protocolTraceSelectionAnchorIndex = index;
            return;
        }

        listBox.SelectedItems.Clear();
        listBox.SelectedItems.Add(row);
        _protocolTraceSelectionAnchorIndex = index;
    }

    private void ExtendProtocolTraceSelectionToIndex(ListBox listBox, int index)
    {
        if (index < 0 || index >= FrameTraceRows.Count)
        {
            return;
        }

        if (_protocolTraceSelectionAnchorIndex < 0 || _protocolTraceSelectionAnchorIndex >= FrameTraceRows.Count)
        {
            _protocolTraceSelectionAnchorIndex = index;
        }

        SelectProtocolTraceRange(listBox, _protocolTraceSelectionAnchorIndex, index, additive: false);
        FocusProtocolTraceContainer(listBox, index);
    }

    private void SelectAllVisibleTraceRows_Click(object sender, RoutedEventArgs e)
    {
        FrameTraceGrid.SelectedItems.Clear();

        foreach (var row in FrameTraceRows)
        {
            FrameTraceGrid.SelectedItems.Add(row);
        }

        _protocolTraceSelectionAnchorIndex = FrameTraceRows.Count > 0 ? 0 : -1;
        ApplySelectedEvidenceRowToInspector(FrameTraceRows.LastOrDefault());
    }

    private void ClearProtocolTraceSelection_Click(object sender, RoutedEventArgs e)
    {
        ResumeProtocolTraceLiveView();
    }

    private void ResumeProtocolTraceLiveView_Click(object sender, RoutedEventArgs e)
    {
        ResumeProtocolTraceLiveView();
    }

    private int GetProtocolTraceSelectionAnchorIndex(ListBox listBox, int fallbackIndex)
    {
        if (_protocolTraceSelectionAnchorIndex >= 0 && _protocolTraceSelectionAnchorIndex < FrameTraceRows.Count)
        {
            return _protocolTraceSelectionAnchorIndex;
        }

        if (listBox.SelectedItems.Count > 0)
        {
            var selectedIndex = listBox.SelectedItems
                .OfType<EvidenceRow>()
                .Select(row => FrameTraceRows.IndexOf(row))
                .Where(index => index >= 0)
                .OrderBy(index => index)
                .FirstOrDefault(-1);

            if (selectedIndex >= 0)
            {
                _protocolTraceSelectionAnchorIndex = selectedIndex;
                return selectedIndex;
            }
        }

        _protocolTraceSelectionAnchorIndex = fallbackIndex;
        return fallbackIndex;
    }


    private static bool IsScrollChromeInput(DependencyObject? source)
    {
        for (var current = source; current is not null; current = GetVisualOrLogicalParent(current))
        {
            if (current is ScrollBar or Thumb or Track or RepeatButton)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointerInsideVerticalScrollbarZone(FrameworkElement owner, Point point)
    {
        // Guard against template hit-test quirks: clicks in the right rail belong to the ScrollBar, not row selection.
        return point.X >= Math.Max(0, owner.ActualWidth - 28);
    }

    private static DependencyObject? GetVisualOrLogicalParent(DependencyObject current)
    {
        if (current is Visual or Visual3D)
        {
            return VisualTreeHelper.GetParent(current);
        }

        return LogicalTreeHelper.GetParent(current);
    }

    private int GetProtocolTraceIndexFromInput(ListBox listBox, DependencyObject? originalSource, Point point)
    {
        var sourceItem = originalSource is null
            ? null
            : ItemsControl.ContainerFromElement(listBox, originalSource) as ListBoxItem;

        if (sourceItem?.DataContext is EvidenceRow sourceRow)
        {
            var index = FrameTraceRows.IndexOf(sourceRow);
            if (index >= 0)
            {
                return index;
            }
        }

        return GetProtocolTraceIndexFromPoint(listBox, point);
    }

    private int GetProtocolTraceIndexFromPoint(ListBox listBox, Point point)
    {
        if (FrameTraceRows.Count == 0)
        {
            return -1;
        }

        var directHit = VisualTreeHelper.HitTest(listBox, point)?.VisualHit as DependencyObject;
        var directItem = ItemsControl.ContainerFromElement(listBox, directHit) as ListBoxItem
                         ?? FindVisualParent<ListBoxItem>(directHit);
        if (directItem?.DataContext is EvidenceRow directRow)
        {
            var directIndex = FrameTraceRows.IndexOf(directRow);
            if (directIndex >= 0)
            {
                return directIndex;
            }
        }

        var bestIndex = -1;
        var bestDistance = double.MaxValue;
        var firstVisibleIndex = -1;
        var lastVisibleIndex = -1;
        var firstTop = double.MaxValue;
        var lastBottom = double.MinValue;

        for (var i = 0; i < FrameTraceRows.Count; i++)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container || !container.IsVisible)
            {
                continue;
            }

            var top = container.TransformToAncestor(listBox).Transform(new Point(0, 0)).Y;
            var height = Math.Max(1.0, container.ActualHeight);
            var bottom = top + height;

            if (firstVisibleIndex < 0 || top < firstTop)
            {
                firstVisibleIndex = i;
                firstTop = top;
            }

            if (lastVisibleIndex < 0 || bottom > lastBottom)
            {
                lastVisibleIndex = i;
                lastBottom = bottom;
            }

            if (point.Y >= top && point.Y <= bottom)
            {
                return i;
            }

            var distance = Math.Abs(point.Y - (top + height / 2.0));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        if (firstVisibleIndex >= 0 && point.Y < firstTop)
        {
            return firstVisibleIndex;
        }

        if (lastVisibleIndex >= 0 && point.Y > lastBottom)
        {
            return lastVisibleIndex;
        }

        return bestIndex;
    }

    private void SelectProtocolTraceRange(ListBox listBox, int firstIndex, int lastIndex, bool additive)
    {
        if (FrameTraceRows.Count == 0)
        {
            return;
        }

        if (!additive)
        {
            listBox.SelectedItems.Clear();
        }

        var start = Math.Clamp(Math.Min(firstIndex, lastIndex), 0, FrameTraceRows.Count - 1);
        var end = Math.Clamp(Math.Max(firstIndex, lastIndex), 0, FrameTraceRows.Count - 1);

        for (var i = start; i <= end; i++)
        {
            var row = FrameTraceRows[i];
            if (!listBox.SelectedItems.Contains(row))
            {
                listBox.SelectedItems.Add(row);
            }
        }
    }

    private void FocusProtocolTraceContainer(ListBox listBox, int index)
    {
        if (index < 0 || index >= FrameTraceRows.Count)
        {
            return;
        }

        if (listBox.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item)
        {
            item.Focus();
        }
    }



    private void EvidenceSummaryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        if (IsScrollChromeInput(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var index = GetEvidenceSummaryIndexFromInput(listBox, e.OriginalSource as DependencyObject, e.GetPosition(listBox));
        if (index < 0 || index >= EvidenceRows.Count)
        {
            return;
        }

        EnterLineMonitorHoldForUserSelection();
        ApplyEvidenceSummarySelectionGesture(listBox, index, Keyboard.Modifiers);
        _isEvidenceSummaryDragSelecting = true;
        FocusEvidenceSummaryContainer(listBox, index);
        UpdateLineMonitorStatus();
        e.Handled = true;
    }

    private void EvidenceSummaryList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isEvidenceSummaryDragSelecting || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (sender is not ListBox listBox)
        {
            return;
        }

        if (IsScrollChromeInput(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var index = GetEvidenceSummaryIndexFromInput(listBox, e.OriginalSource as DependencyObject, e.GetPosition(listBox));
        if (index < 0 || index >= EvidenceRows.Count)
        {
            return;
        }

        ExtendEvidenceSummarySelectionToIndex(listBox, index);
        e.Handled = true;
    }

    private void EvidenceSummaryLineItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_isEvidenceSummaryDragSelecting || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (sender is not ListBoxItem item || item.DataContext is not EvidenceRow row)
        {
            return;
        }

        if (ItemsControl.ItemsControlFromItemContainer(item) is not ListBox listBox)
        {
            return;
        }

        var index = EvidenceRows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        ExtendEvidenceSummarySelectionToIndex(listBox, index);
        e.Handled = true;
    }

    private void EvidenceSummaryList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isEvidenceSummaryDragSelecting = false;
    }

    private void EvidenceSummaryList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        var index = GetEvidenceSummaryIndexFromInput(listBox, e.OriginalSource as DependencyObject, e.GetPosition(listBox));
        if (index < 0 || index >= EvidenceRows.Count)
        {
            return;
        }

        EnterLineMonitorHoldForUserSelection();
        var row = EvidenceRows[index];
        if (!listBox.SelectedItems.Contains(row))
        {
            listBox.SelectedItems.Clear();
            listBox.SelectedItems.Add(row);
            _evidenceSummarySelectionAnchorIndex = index;
        }

        FocusEvidenceSummaryContainer(listBox, index);
    }

    private void ApplyEvidenceSummarySelectionGesture(ListBox listBox, int index, ModifierKeys modifiers)
    {
        var row = EvidenceRows[index];
        var shift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var ctrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (shift)
        {
            var anchor = GetEvidenceSummarySelectionAnchorIndex(listBox, index);
            SelectEvidenceSummaryRange(listBox, anchor, index, additive: ctrl);
            return;
        }

        if (ctrl)
        {
            if (listBox.SelectedItems.Contains(row))
            {
                listBox.SelectedItems.Remove(row);
            }
            else
            {
                listBox.SelectedItems.Add(row);
            }

            _evidenceSummarySelectionAnchorIndex = index;
            return;
        }

        listBox.SelectedItems.Clear();
        listBox.SelectedItems.Add(row);
        _evidenceSummarySelectionAnchorIndex = index;
    }

    private void ExtendEvidenceSummarySelectionToIndex(ListBox listBox, int index)
    {
        if (index < 0 || index >= EvidenceRows.Count)
        {
            return;
        }

        if (_evidenceSummarySelectionAnchorIndex < 0 || _evidenceSummarySelectionAnchorIndex >= EvidenceRows.Count)
        {
            _evidenceSummarySelectionAnchorIndex = index;
        }

        SelectEvidenceSummaryRange(listBox, _evidenceSummarySelectionAnchorIndex, index, additive: false);
        FocusEvidenceSummaryContainer(listBox, index);
    }

    private void SelectAllEvidenceSummaryRows_Click(object sender, RoutedEventArgs e)
    {
        EvidenceSummaryList.SelectedItems.Clear();

        foreach (var row in EvidenceRows)
        {
            EvidenceSummaryList.SelectedItems.Add(row);
        }

        _evidenceSummarySelectionAnchorIndex = EvidenceRows.Count > 0 ? 0 : -1;
    }

    private void ClearEvidenceSummarySelection_Click(object sender, RoutedEventArgs e)
    {
        ResumeEvidenceSummaryLiveView();
    }

    private void ResumeEvidenceSummaryLiveView_Click(object sender, RoutedEventArgs e)
    {
        ResumeEvidenceSummaryLiveView();
    }

    private int GetEvidenceSummarySelectionAnchorIndex(ListBox listBox, int fallbackIndex)
    {
        if (_evidenceSummarySelectionAnchorIndex >= 0 && _evidenceSummarySelectionAnchorIndex < EvidenceRows.Count)
        {
            return _evidenceSummarySelectionAnchorIndex;
        }

        if (listBox.SelectedItems.Count > 0)
        {
            var selectedIndex = listBox.SelectedItems
                .OfType<EvidenceRow>()
                .Select(row => EvidenceRows.IndexOf(row))
                .Where(index => index >= 0)
                .OrderBy(index => index)
                .FirstOrDefault(-1);

            if (selectedIndex >= 0)
            {
                _evidenceSummarySelectionAnchorIndex = selectedIndex;
                return selectedIndex;
            }
        }

        _evidenceSummarySelectionAnchorIndex = fallbackIndex;
        return fallbackIndex;
    }

    private int GetEvidenceSummaryIndexFromInput(ListBox listBox, DependencyObject? originalSource, Point point)
    {
        var sourceItem = originalSource is null
            ? null
            : ItemsControl.ContainerFromElement(listBox, originalSource) as ListBoxItem;

        if (sourceItem?.DataContext is EvidenceRow sourceRow)
        {
            var index = EvidenceRows.IndexOf(sourceRow);
            if (index >= 0)
            {
                return index;
            }
        }

        return GetEvidenceSummaryIndexFromPoint(listBox, point);
    }

    private int GetEvidenceSummaryIndexFromPoint(ListBox listBox, Point point)
    {
        if (EvidenceRows.Count == 0)
        {
            return -1;
        }

        var directHit = VisualTreeHelper.HitTest(listBox, point)?.VisualHit as DependencyObject;
        var directItem = ItemsControl.ContainerFromElement(listBox, directHit) as ListBoxItem
                         ?? FindVisualParent<ListBoxItem>(directHit);
        if (directItem?.DataContext is EvidenceRow directRow)
        {
            var directIndex = EvidenceRows.IndexOf(directRow);
            if (directIndex >= 0)
            {
                return directIndex;
            }
        }

        var bestIndex = -1;
        var bestDistance = double.MaxValue;
        var firstVisibleIndex = -1;
        var lastVisibleIndex = -1;
        var firstTop = double.MaxValue;
        var lastBottom = double.MinValue;

        for (var i = 0; i < EvidenceRows.Count; i++)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container || !container.IsVisible)
            {
                continue;
            }

            var top = container.TransformToAncestor(listBox).Transform(new Point(0, 0)).Y;
            var height = Math.Max(1.0, container.ActualHeight);
            var bottom = top + height;

            if (firstVisibleIndex < 0 || top < firstTop)
            {
                firstVisibleIndex = i;
                firstTop = top;
            }

            if (lastVisibleIndex < 0 || bottom > lastBottom)
            {
                lastVisibleIndex = i;
                lastBottom = bottom;
            }

            if (point.Y >= top && point.Y <= bottom)
            {
                return i;
            }

            var distance = Math.Abs(point.Y - (top + height / 2.0));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        if (firstVisibleIndex >= 0 && point.Y < firstTop)
        {
            return firstVisibleIndex;
        }

        if (lastVisibleIndex >= 0 && point.Y > lastBottom)
        {
            return lastVisibleIndex;
        }

        return bestIndex;
    }

    private void SelectEvidenceSummaryRange(ListBox listBox, int firstIndex, int lastIndex, bool additive)
    {
        if (EvidenceRows.Count == 0)
        {
            return;
        }

        if (!additive)
        {
            listBox.SelectedItems.Clear();
        }

        var start = Math.Clamp(Math.Min(firstIndex, lastIndex), 0, EvidenceRows.Count - 1);
        var end = Math.Clamp(Math.Max(firstIndex, lastIndex), 0, EvidenceRows.Count - 1);

        for (var i = start; i <= end; i++)
        {
            var row = EvidenceRows[i];
            if (!listBox.SelectedItems.Contains(row))
            {
                listBox.SelectedItems.Add(row);
            }
        }
    }

    private void FocusEvidenceSummaryContainer(ListBox listBox, int index)
    {
        if (index < 0 || index >= EvidenceRows.Count)
        {
            return;
        }

        if (listBox.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item)
        {
            item.Focus();
        }
    }
}
