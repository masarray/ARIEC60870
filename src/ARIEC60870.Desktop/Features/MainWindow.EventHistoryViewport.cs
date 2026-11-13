// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ARIEC60870.Desktop.ViewModels;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private bool _eventHistoryProgrammaticScroll;
    private bool _eventHistoryPointerScrolling;
    private bool _eventHistoryTailResumeRunning;
    private Task? _eventHistoryTailResumeTask;
    private bool _eventHistoryTailResumeDeferred;
    private int _eventHistoryProgrammaticScrollGeneration;
    private long _eventHistoryRecentScrollInputTick = long.MinValue;

    // Operator scrolling is different from row recycling, layout, and
    // ScrollIntoView. Only deliberate input changes the follow policy.
    private void MarkEventHistoryUserScroll()
        => _eventHistoryRecentScrollInputTick = Environment.TickCount64;

    private bool HasRecentEventHistoryUserScroll()
        => _eventHistoryPointerScrolling ||
           Environment.TickCount64 - _eventHistoryRecentScrollInputTick is >= 0 and <= 900;

    private ScrollViewer? EventHistoryScrollViewer()
        => RelayEventGrid?.Template.FindName("DG_ScrollViewer", RelayEventGrid) as ScrollViewer;

    private bool IsEventHistoryViewportAtBottom()
    {
        var viewer = EventHistoryScrollViewer();
        return viewer is not null && viewer.VerticalOffset >= viewer.ScrollableHeight - 0.5;
    }

    private void GuardEventHistoryProgrammaticScroll()
    {
        _eventHistoryProgrammaticScroll = true;
        var generation = ++_eventHistoryProgrammaticScrollGeneration;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_eventHistoryProgrammaticScrollGeneration == generation)
                _eventHistoryProgrammaticScroll = false;
        }), DispatcherPriority.ContextIdle);
    }

    private void ResetEventHistoryViewportTracking()
    {
        _eventHistoryPointerScrolling = false;
        _eventHistoryTailResumeDeferred = false;
        _eventHistoryTailResumeTask = null;
        _eventHistoryRecentScrollInputTick = long.MinValue;
        _eventHistoryProgrammaticScroll = false;
        _eventHistoryProgrammaticScrollGeneration++;
    }

    private void EventHistoryGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _eventHistoryPointerScrolling = true;
        MarkEventHistoryUserScroll();
    }

    private void EventHistoryGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _eventHistoryPointerScrolling = false;
        MarkEventHistoryUserScroll();
    }

    private void EventHistoryGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End))
            return;
        MarkEventHistoryUserScroll();
        if (e.Key is Key.Up or Key.PageUp or Key.Home)
            _eventHistoryFollowingLatest = false;
        else if (IsEventHistoryViewportAtBottom())
            FollowEventHistoryTailFromViewport();
    }

    private void ObserveEventHistoryWheelInput(int delta)
    {
        if (delta == 0 || !IsEventsTabActive()) return;
        MarkEventHistoryUserScroll();
        if (delta > 0)
            _eventHistoryFollowingLatest = false;
        else if (!EventHistoryPagination.CanResumeFromVisiblePage(
                     _eventHistoryPageStart, _eventHistoryQuery.Count))
            _eventHistoryFollowingLatest = false;
        else if (IsEventHistoryViewportAtBottom())
            FollowEventHistoryTailFromViewport();
        // Normal WPF scrolling happens after PreviewMouseWheel. ScrollChanged
        // detects the precise moment the viewport reaches its bottom.
    }

    private void EventHistoryGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!IsEventsTabActive() || _eventHistoryProgrammaticScroll ||
            _eventHistoryPageBusy || !HasRecentEventHistoryUserScroll() ||
            e.VerticalChange == 0 ||
            !ReferenceEquals(e.OriginalSource, EventHistoryScrollViewer()))
            return;

        if (e.VerticalChange > 0 && IsEventHistoryViewportAtBottom() &&
            EventHistoryPagination.CanResumeFromVisiblePage(
                _eventHistoryPageStart, _eventHistoryQuery.Count))
            FollowEventHistoryTailFromViewport();
        else
            _eventHistoryFollowingLatest = false;
    }

    private void FollowEventHistoryTailFromViewport()
    {
        // A page-bottom is not the end of the Event Journal. Reaching row 200
        // on page 2/9 must NEVER jump to event 801 on page 9.
        // Only the actual last page may opt back into automatic tail following.
        if (!IsEventsTabActive() || !EventHistoryPagination.CanResumeFromVisiblePage(
                _eventHistoryPageStart, _eventHistoryQuery.Count)) return;
        _eventHistoryFollowingLatest = true;
        RelayEventGrid.SelectedItem = null;
        if (_eventHistoryTailResumeRunning) return;
        if (_eventHistoryPageBusy || _eventHistoryQueryBusy || _eventHistoryRefreshBusy)
        {
            _eventHistoryTailResumeDeferred = true;
            return;
        }
        if (_eventHistoryPageStart == LastHistoryPageStart() &&
            (_eventHistoryJournal?.Count ?? 0) <= _eventHistoryQuery.Ceiling)
            return;

        _eventHistoryTailResumeRunning = true;
        _eventHistoryTailResumeTask = ResumeEventHistoryTailFromViewportAsync();
    }

    private async Task ResumeEventHistoryTailFromViewportAsync()
    {
        try
        {
            await JumpEventHistoryToLatestAsync();
        }
        finally
        {
            _eventHistoryTailResumeRunning = false;
            FinishDeferredEventHistoryTailResume();
        }
    }

    private void FinishDeferredEventHistoryTailResume()
    {
        if (!_eventHistoryTailResumeDeferred ||
            _eventHistoryPageBusy || _eventHistoryQueryBusy ||
            _eventHistoryRefreshBusy || _eventHistoryTailResumeRunning)
            return;
        _eventHistoryTailResumeDeferred = false;
        if (_eventHistoryFollowingLatest && IsEventsTabActive())
            FollowEventHistoryTailFromViewport();
    }
}
