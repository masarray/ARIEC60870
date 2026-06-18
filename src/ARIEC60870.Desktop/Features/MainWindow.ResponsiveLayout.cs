// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private void UpdateResponsiveHeaderLayout()
    {
        if (!IsLoaded)
        {
            return;
        }

        var compact = ActualWidth < 1340;
        var secondaryVisibility = compact ? Visibility.Collapsed : Visibility.Visible;

        SetVisible(NoDataSeparator, secondaryVisibility);
        SetVisible(NoDataChipStack, secondaryVisibility);
        SetVisible(EventSeparator, secondaryVisibility);
        SetVisible(EventChipStack, secondaryVisibility);
        SetVisible(IssuesSeparator, secondaryVisibility);
        SetVisible(IssuesChipStack, secondaryVisibility);

        if (HeaderIndicatorChip is not null)
        {
            HeaderIndicatorChip.MinWidth = compact ? 408 : 662;
            HeaderIndicatorChip.Padding = compact ? new Thickness(10, 0, 10, 0) : new Thickness(14, 0, 14, 0);
        }
    }

    private static void SetVisible(UIElement? element, Visibility visibility)
    {
        if (element is not null)
        {
            element.Visibility = visibility;
        }
    }
}
