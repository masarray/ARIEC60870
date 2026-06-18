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
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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
    private static string ClassifyLowValueBackpressureBucket(Iec103MasterEvidenceEvent item)
    {
        if (IsDiagnosticEvidence(item) ||
            item.IsRelayEdgeEvent ||
            item.IsRelayValue ||
            item.IsMappedSignal ||
            IsIec10xProcessValue(item) ||
            IsIec10xDigitalType(item.TypeId) ||
            IsGeneralInterrogationActivity(item) ||
            item.CauseOfTransmission is 6 or 7 or 10 ||
            item.TypeId is 45 or 46 or 47 or 48 or 49 or 50 or 51)
        {
            return string.Empty;
        }

        var text = string.Join(" ", item.Summary, item.Detail, item.OperatorMessage, item.ProtocolMeaning, item.DataClass);
        if (ContainsAny(text, "ACK", "NACK", "single-character ACK", "single-character NACK", "no data", "NO DATA"))
        {
            return "ack/no-data";
        }

        if (ContainsAny(text, "Request Class 1", "Request Class 2", "Class 2 poll", "background poll"))
        {
            return "background-poll";
        }

        if (ContainsAny(text, "TESTFR", "S-frame", "STARTDT", "STOPDT"))
        {
            return "test/supervisory";
        }

        return ContainsAny(text, "poll", "routine", "idle", "keepalive")
            ? "other-low-value"
            : string.Empty;
    }

    private bool TryDropLowValueForBackpressure(Iec103MasterEvidenceEvent item)
    {
        var bucket = ClassifyLowValueBackpressureBucket(item);
        if (string.IsNullOrWhiteSpace(bucket))
        {
            return false;
        }

        System.Threading.Interlocked.Increment(ref _backpressureDroppedEvents);
        switch (bucket)
        {
            case "ack/no-data":
                System.Threading.Interlocked.Increment(ref _backpressureDroppedAckNoData);
                break;
            case "background-poll":
                System.Threading.Interlocked.Increment(ref _backpressureDroppedBackgroundPoll);
                break;
            case "test/supervisory":
                System.Threading.Interlocked.Increment(ref _backpressureDroppedTestFrames);
                break;
            default:
                System.Threading.Interlocked.Increment(ref _backpressureDroppedOtherLowValue);
                break;
        }

        System.Threading.Interlocked.Exchange(ref _backpressureNoticePending, 1);
        return true;
    }

    private void TrackPendingEvidenceDepth(int depth)
    {
        long current;
        while (depth > (current = System.Threading.Interlocked.Read(ref _maxPendingEvidenceDepth)))
        {
            if (System.Threading.Interlocked.CompareExchange(ref _maxPendingEvidenceDepth, depth, current) == current)
            {
                break;
            }
        }
    }

    private void EmitBackpressureNoticeIfNeeded()
    {
        if (System.Threading.Interlocked.Exchange(ref _backpressureNoticePending, 0) != 1)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastBackpressureLogUtc).TotalSeconds < 20)
        {
            System.Threading.Interlocked.Exchange(ref _backpressureNoticePending, 1);
            return;
        }

        _lastBackpressureLogUtc = now;
        var total = System.Threading.Interlocked.Read(ref _backpressureDroppedEvents);
        var ack = System.Threading.Interlocked.Read(ref _backpressureDroppedAckNoData);
        var poll = System.Threading.Interlocked.Read(ref _backpressureDroppedBackgroundPoll);
        var test = System.Threading.Interlocked.Read(ref _backpressureDroppedTestFrames);
        var other = System.Threading.Interlocked.Read(ref _backpressureDroppedOtherLowValue);
        var delta = total - _lastDropSummaryMarkerTotal;
        _lastDropSummaryMarkerTotal = total;

        AppendSessionLog($"UI backpressure active: dropped {total} routine low-value trace events (new {delta}; ack/no-data {ack}, poll {poll}, test/supervisory {test}, other {other}). Protected: diagnostics, digital/process values, mapped values, GI, command and ACTCON/ACTTERM.");

        AddUiDiagnostic(
            "Info",
            "UI Dispatcher",
            "ARIEC-UI-DROP-SUMMARY",
            "Low-value trace compression summary",
            $"Dropped routine low-value trace rows total={total}, new={delta}, ack/no-data={ack}, background-poll={poll}, test/supervisory={test}, other={other}.",
            "This is a UI pressure protection marker, not protocol data loss. Critical evidence remains protected by the priority router.");
    }


    private int GetAdaptiveFlushBudget(int queued)
    {
        if (queued >= MaxPendingEvidenceBacklog)
        {
            return MaxUiFlushBurstPerTick;
        }

        if (queued >= 3000)
        {
            return Math.Min(MaxUiFlushBurstPerTick, 160);
        }

        if (queued >= 1500)
        {
            return Math.Min(MaxUiFlushBurstPerTick, 96);
        }

        if (queued >= 600)
        {
            return Math.Min(MaxUiFlushBurstPerTick, 64);
        }

        return MaxUiFlushPerTick;
    }

    private bool ShouldApplyBackpressure(int queued)
    {
        var threshold = _lastUiFlushMs >= UiFlushSlowWarningMs
            ? MaxPendingEvidenceBacklog / 2
            : MaxPendingEvidenceBacklog;

        return queued > threshold;
    }

    private void EvaluateDispatcherHealthTelemetry(int queuedBeforeFlush)
    {
        var now = DateTime.UtcNow;

        if (queuedBeforeFlush >= UiQueuePressureWarningDepth &&
            (now - _lastDispatcherPressureDiagnosticUtc).TotalSeconds >= 30)
        {
            _lastDispatcherPressureDiagnosticUtc = now;
            AddUiDiagnostic(
                "Info",
                "UI Dispatcher",
                "ARIEC-UI-QUEUE-PRESSURE",
                "UI dispatcher queue pressure detected",
                $"Pending evidence queue reached {queuedBeforeFlush} items. Adaptive budget={_lastFlushBudget}, last flush={_lastUiFlushMs} ms, max flush={_maxUiFlushMs} ms, dropped low-value={_backpressureDroppedEvents} (ack/no-data={_backpressureDroppedAckNoData}, poll={_backpressureDroppedBackgroundPoll}, test={_backpressureDroppedTestFrames}, other={_backpressureDroppedOtherLowValue}).",
                "This is normally survivable. If it persists, reduce trace verbosity, keep Trace tab inactive during long tests, or increase polling interval for low-baud serial links.");
        }

        if (_lastUiFlushMs >= UiFlushSlowWarningMs &&
            (now - _lastDispatcherSlowDiagnosticUtc).TotalSeconds >= 30)
        {
            _lastDispatcherSlowDiagnosticUtc = now;
            AddUiDiagnostic(
                "Warning",
                "UI Dispatcher",
                "ARIEC-UI-SLOW-FLUSH",
                "UI flush cycle is slow",
                $"Last UI flush took {_lastUiFlushMs} ms. Queue={_pendingEvidence.Count}, processed={_lastEvidenceProcessed}, visible batch rows={_lastVisibleBatchRows}.",
                "The protocol engine continues to protect important evidence. For smoother UI, avoid leaving high-volume Trace visible during long IEC-101/104 polling sessions.");
        }
    }

    private void OnEvidenceReceived(object? sender, Iec103MasterEvidenceEvent item)
    {
        // Do not render one WPF row per protocol event immediately. High-volume polling can
        // produce thousands of frames; the UI consumes this queue in timed batches.
        var depth = _pendingEvidence.Count;
        TrackPendingEvidenceDepth(depth);

        if (ShouldApplyBackpressure(depth) && TryDropLowValueForBackpressure(item))
        {
            return;
        }

        _pendingEvidence.Enqueue(item);
    }

    private void OnFindingRaised(object? sender, Iec103MasterFinding finding)
    {
        _pendingFindings.Enqueue(finding);
    }

    private void FlushUiQueues()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var processed = 0;
        var queuedBeforeFlush = _pendingEvidence.Count;
        TrackPendingEvidenceDepth(queuedBeforeFlush);

        var flushBudget = GetAdaptiveFlushBudget(queuedBeforeFlush);
        _lastFlushBudget = flushBudget;

        while (processed < flushBudget && _pendingEvidence.TryDequeue(out var item))
        {
            ApplyEvidenceToUi(item);
            processed++;
        }

        var findingProcessed = 0;
        while (findingProcessed < 50 && _pendingFindings.TryDequeue(out var finding))
        {
            ApplyFindingToUi(finding);
            findingProcessed++;
        }

        EvaluateGiCollectionWindow();
        EvaluateScanHealthWindow();
        EvaluateCommandLedgerTimeouts();
        FlushVisibleUiBatches();
        EmitBackpressureNoticeIfNeeded();

        stopwatch.Stop();
        _lastEvidenceProcessed = processed;
        _lastFindingProcessed = findingProcessed;
        _lastUiFlushMs = stopwatch.ElapsedMilliseconds;
        _maxUiFlushMs = Math.Max(_maxUiFlushMs, _lastUiFlushMs);
        _uiFlushTicks++;

        EvaluateDispatcherHealthTelemetry(queuedBeforeFlush);
        UpdateBufferStatus();
    }

    private bool IsEvidenceSummaryTabActive()
        => MainTabControl?.SelectedIndex == 0;

    private bool IsProtocolTraceTabActive()
        => MainTabControl?.SelectedIndex == 1;




    private bool IsFollowLiveEnabled()
        => AutoScrollLatestToggleButton is null || AutoScrollLatestToggleButton.IsChecked == true;

    private void EnterLineMonitorHoldForUserSelection()
    {
        if (AutoScrollLatestToggleButton?.IsChecked == true)
        {
            AutoScrollLatestToggleButton.IsChecked = false;
        }

        _protocolTraceViewDirtyWhileFrozen = true;
        _evidenceSummaryViewDirtyWhileFrozen = true;
        UpdateLineMonitorStatus();
    }


    private bool IsLineMonitorHoldRequested()
        => !IsFollowLiveEnabled();

    private long GetActiveWorkspacePendingRows()
    {
        if (IsEvidenceSummaryTabActive())
        {
            return _evidenceSummaryRowsDeferredWhileFrozen;
        }

        if (IsProtocolTraceTabActive())
        {
            return _protocolTraceRowsDeferredWhileFrozen;
        }

        return _evidenceSummaryRowsDeferredWhileFrozen + _protocolTraceRowsDeferredWhileFrozen;
    }

    private int GetActiveWorkspaceSelectedRows()
    {
        if (IsEvidenceSummaryTabActive())
        {
            return EvidenceSummaryList?.SelectedItems.Count ?? 0;
        }

        if (IsProtocolTraceTabActive())
        {
            return FrameTraceGrid?.SelectedItems.Count ?? 0;
        }

        return 0;
    }

    private void UpdateLineMonitorStatus()
    {
        UpdateAutoScrollLatestRailVisual();
    }

    private void UpdateAutoScrollLatestRailVisual()
    {
        if (AutoScrollLatestToggleButton is null)
        {
            return;
        }

        var enabledForWorkspace = IsEvidenceSummaryTabActive() || IsProtocolTraceTabActive();
        AutoScrollLatestToggleButton.IsEnabled = enabledForWorkspace;

        var isOn = enabledForWorkspace && IsFollowLiveEnabled();
        if (AutoScrollOnIcon is not null)
        {
            AutoScrollOnIcon.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
            AutoScrollOnIcon.Stroke = (Brush)FindResource(isOn ? "AccentBrush" : "Ink500Brush");
        }

        if (AutoScrollOffIcon is not null)
        {
            AutoScrollOffIcon.Visibility = isOn ? Visibility.Collapsed : Visibility.Visible;
            AutoScrollOffIcon.Stroke = enabledForWorkspace ? (Brush)FindResource("Ink500Brush") : (Brush)FindResource("Ink500Brush");
        }

        if (AutoScrollCaption is not null)
        {
            AutoScrollCaption.Text = isOn ? "Auto" : "Hold";
            AutoScrollCaption.Foreground = enabledForWorkspace
                ? (Brush)FindResource(isOn ? "AccentBrush" : "Ink600Brush")
                : (Brush)FindResource("Ink500Brush");
        }
    }

    private void AutoScrollLatestToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (IsFollowLiveEnabled())
        {
            ResumeLiveViews(clearSelection: true, followLive: true);
        }
        else
        {
            _protocolTraceViewDirtyWhileFrozen = true;
            _evidenceSummaryViewDirtyWhileFrozen = true;
            UpdateLineMonitorStatus();
        }
    }

    private void ResumeLiveViews(bool clearSelection, bool followLive)
    {
        if (AutoScrollLatestToggleButton is not null)
        {
            AutoScrollLatestToggleButton.IsChecked = followLive;
        }

        if (clearSelection)
        {
            FrameTraceGrid?.SelectedItems.Clear();
                EvidenceSummaryList?.SelectedItems.Clear();
            _protocolTraceSelectionAnchorIndex = -1;
            _evidenceSummarySelectionAnchorIndex = -1;
            _isProtocolTraceDragSelecting = false;
            _isProtocolTraceSelectionBatching = false;
            _isEvidenceSummaryDragSelecting = false;
            _pendingProtocolTraceSelectionInspectorRefresh = false;
        }

        ApplyEvidenceSummarySnapshotNow();
        ApplyProtocolTraceSnapshotNow();
        ScrollActiveWorkspaceToLatest();
        ApplySelectedEvidenceRowToInspector(null);
        UpdateLineMonitorStatus();
        UpdateBufferStatus();
    }

    private void JumpActiveWorkspaceToLatest()
    {
        if (IsEvidenceSummaryTabActive())
        {
            ApplyEvidenceSummarySnapshotNow();
        }
        else if (IsProtocolTraceTabActive())
        {
            ApplyProtocolTraceSnapshotNow();
        }
        else
        {
            ApplyEvidenceSummarySnapshotNow();
            ApplyProtocolTraceSnapshotNow();
        }

        ScrollActiveWorkspaceToLatest();
        UpdateLineMonitorStatus();
        UpdateBufferStatus();
    }

    private void ApplyEvidenceSummarySnapshotNow()
    {
        EvidenceRows.ReplaceRange(_evidenceSummaryStore.Snapshot());
        _pendingEvidenceSummaryUiRows.Clear();
        _evidenceSummaryViewDirtyWhileFrozen = false;
        _evidenceSummaryRowsDeferredWhileFrozen = 0;
    }

    private void ApplyProtocolTraceSnapshotNow()
    {
        FrameTraceRows.ReplaceRange(_protocolTraceStore.Snapshot());
        _pendingProtocolTraceUiRows.Clear();
        _protocolTraceViewDirtyWhileFrozen = false;
        _protocolTraceRowsDeferredWhileFrozen = 0;
    }


    private void ScrollLatestEvidenceSummaryIfAutoScroll()
    {
        if (!IsFollowLiveEnabled() || !IsEvidenceSummaryTabActive() || EvidenceRows.Count == 0)
        {
            return;
        }

        EvidenceSummaryList?.ScrollIntoView(EvidenceRows[EvidenceRows.Count - 1]);
    }

    private void ScrollLatestProtocolTraceIfAutoScroll()
    {
        if (!IsFollowLiveEnabled() || !IsProtocolTraceTabActive() || FrameTraceRows.Count == 0)
        {
            return;
        }

        FrameTraceGrid?.ScrollIntoView(FrameTraceRows[FrameTraceRows.Count - 1]);
    }

    private void ScrollActiveWorkspaceToLatest()
    {
        if (IsEvidenceSummaryTabActive() && EvidenceRows.Count > 0)
        {
            EvidenceSummaryList?.ScrollIntoView(EvidenceRows[EvidenceRows.Count - 1]);
        }

        if (IsProtocolTraceTabActive() && FrameTraceRows.Count > 0)
        {
            FrameTraceGrid?.ScrollIntoView(FrameTraceRows[FrameTraceRows.Count - 1]);
        }
    }

    private bool IsEvidenceSummaryViewFrozen()
    {
        if (!IsEvidenceSummaryTabActive() || EvidenceSummaryList is null)
        {
            return false;
        }

        return IsLineMonitorHoldRequested()
               || _isEvidenceSummaryDragSelecting
               || EvidenceSummaryList.SelectedItems.Count > 0
               || EvidenceSummaryList.ContextMenu?.IsOpen == true;
    }

    private void ApplyDeferredEvidenceSummarySnapshotIfNeeded()
    {
        if (!_evidenceSummaryViewDirtyWhileFrozen || !IsEvidenceSummaryTabActive() || IsEvidenceSummaryViewFrozen())
        {
            return;
        }

        EvidenceRows.ReplaceRange(_evidenceSummaryStore.Snapshot());
        _evidenceSummaryViewDirtyWhileFrozen = false;
        _evidenceSummaryRowsDeferredWhileFrozen = 0;
    }

    private void ResumeEvidenceSummaryLiveView()
    {
        _isEvidenceSummaryDragSelecting = false;
        EvidenceSummaryList?.SelectedItems.Clear();
        _evidenceSummarySelectionAnchorIndex = -1;
        ApplyEvidenceSummarySnapshotNow();
        ApplySelectedEvidenceRowToInspector(null);
        ScrollActiveWorkspaceToLatest();
        UpdateLineMonitorStatus();
    }

    private bool IsProtocolTraceViewFrozen()
    {
        if (!IsProtocolTraceTabActive())
        {
            return false;
        }

        return IsLineMonitorHoldRequested()
               || _isProtocolTraceDragSelecting
               || _isProtocolTraceSelectionBatching
               || (FrameTraceGrid?.SelectedItems.Count ?? 0) > 0
               || FrameTraceGrid?.ContextMenu?.IsOpen == true;
    }

    private void ApplyDeferredProtocolTraceSnapshotIfNeeded()
    {
        if (!_protocolTraceViewDirtyWhileFrozen || !IsProtocolTraceTabActive() || IsProtocolTraceViewFrozen())
        {
            return;
        }

        FrameTraceRows.ReplaceRange(_protocolTraceStore.Snapshot());
        _protocolTraceViewDirtyWhileFrozen = false;
        _protocolTraceRowsDeferredWhileFrozen = 0;
    }

    private void ResumeProtocolTraceLiveView()
    {
        _isProtocolTraceDragSelecting = false;
        _isProtocolTraceSelectionBatching = false;
        _pendingProtocolTraceSelectionInspectorRefresh = false;
        FrameTraceGrid?.SelectedItems.Clear();
        _protocolTraceSelectionAnchorIndex = -1;
        ApplyProtocolTraceSnapshotNow();
        ApplySelectedEvidenceRowToInspector(null);
        ScrollActiveWorkspaceToLatest();
        UpdateLineMonitorStatus();
    }

    private void AddEvidenceSummaryRow(EvidenceRow row)
    {
        _evidenceSummaryStore.Add(row);
        if (IsEvidenceSummaryTabActive())
        {
            _pendingEvidenceSummaryUiRows.Add(row);
        }
    }

    private void AddProtocolTraceRow(EvidenceRow row)
    {
        _protocolTraceStore.Add(row);
        if (IsProtocolTraceTabActive())
        {
            _pendingProtocolTraceUiRows.Add(row);
        }
    }

    private void FlushVisibleUiBatches()
    {
        var batchRows = _pendingEvidenceSummaryUiRows.Count
                        + _pendingProtocolTraceUiRows.Count
                        + _pendingFindingUiRows.Count
                        + _pendingDiagnosticUiRows.Count;

        if (_pendingEvidenceSummaryUiRows.Count > 0)
        {
            if (IsEvidenceSummaryViewFrozen())
            {
                _evidenceSummaryViewDirtyWhileFrozen = true;
                _evidenceSummaryRowsDeferredWhileFrozen += _pendingEvidenceSummaryUiRows.Count;
                _pendingEvidenceSummaryUiRows.Clear();
            }
            else
            {
                ApplyDeferredEvidenceSummarySnapshotIfNeeded();
                EvidenceRows.AddRange(_pendingEvidenceSummaryUiRows);
                _pendingEvidenceSummaryUiRows.Clear();
                _visibleEvidenceDropped += EvidenceRows.TrimStart(MaxVisibleEvidenceRows);
                ScrollLatestEvidenceSummaryIfAutoScroll();
            }
        }
        else
        {
            ApplyDeferredEvidenceSummarySnapshotIfNeeded();
        }

        if (_pendingProtocolTraceUiRows.Count > 0)
        {
            if (IsProtocolTraceViewFrozen())
            {
                _protocolTraceViewDirtyWhileFrozen = true;
                _protocolTraceRowsDeferredWhileFrozen += _pendingProtocolTraceUiRows.Count;
                _pendingProtocolTraceUiRows.Clear();
            }
            else
            {
                ApplyDeferredProtocolTraceSnapshotIfNeeded();
                FrameTraceRows.AddRange(_pendingProtocolTraceUiRows);
                _pendingProtocolTraceUiRows.Clear();
                _visibleEvidenceDropped += FrameTraceRows.TrimStart(MaxVisibleFrameTraceRows);
                ScrollLatestProtocolTraceIfAutoScroll();
            }
        }
        else
        {
            ApplyDeferredProtocolTraceSnapshotIfNeeded();
        }

        if (_valueRowsDirty)
        {
            ValueRows.ReplaceRange(GetSortedValueRowsSnapshot());
            batchRows += ValueRows.Count;
            _valueRowsDirty = false;
        }

        if (_relayEventRowsDirty)
        {
            ApplyRelayEventFilter();
            batchRows += RelayEventRows.Count;
            _relayEventRowsDirty = false;
        }

        if (_pendingFindingUiRows.Count > 0)
        {
            FindingRows.ReplaceRange(_findingStore.Snapshot());
            _pendingFindingUiRows.Clear();
            FindingCountText.Text = FindingRows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (_pendingDiagnosticUiRows.Count > 0)
        {
            DiagnosticRows.ReplaceRange(_diagnosticStore.Snapshot());
            _pendingDiagnosticUiRows.Clear();
        }

        if (IsFindingsWorkspaceTabActive() && (batchRows > 0 || _pendingEvidence.Count == 0))
        {
            RefreshFindingsWorkspace();
        }

        _lastVisibleBatchRows = batchRows;
        UpdateLineMonitorStatus();
    }

    private void RefreshActiveTraceSnapshot()
    {
        if (IsEvidenceSummaryTabActive())
        {
            EvidenceRows.ReplaceRange(_evidenceSummaryStore.Snapshot());
        }
        else if (EvidenceRows.Count > 0)
        {
            EvidenceRows.Clear();
        }

        if (IsProtocolTraceTabActive())
        {
            FrameTraceRows.ReplaceRange(_protocolTraceStore.Snapshot());
        }
        else if (FrameTraceRows.Count > 0)
        {
            FrameTraceRows.Clear();
        }

        _pendingEvidenceSummaryUiRows.Clear();
        _pendingProtocolTraceUiRows.Clear();
        UpdateLineMonitorStatus();
    }


    private enum TraceVerbosityMode
    {
        Proof,
        Balanced,
        Full
    }

    private TraceVerbosityMode GetTraceVerbosityMode()
    {
        // The public-preview UX hides trace verbosity from the central toolbar to reduce visual noise.
        // Balanced remains the safe default until the advanced settings surface is reintroduced.
        return TraceVerbosityMode.Balanced;
    }

    private bool ShouldShowInFrameTrace(Iec103MasterEvidenceEvent item, EvidenceRow row)
    {
        if (row.RawHex == "-" ||
            (!row.Direction.Equals("TX", StringComparison.OrdinalIgnoreCase) &&
             !row.Direction.Equals("RX", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var mode = GetTraceVerbosityMode();
        if (mode == TraceVerbosityMode.Full)
        {
            return true;
        }

        if (IsProtectedTraceEvidence(item, row))
        {
            return true;
        }

        var combined = string.Join(" ", item.Category, item.State, item.Summary, item.Detail, item.OperatorMessage, item.OperatorAction, item.ProtocolMeaning, item.DataClass, row.ReadableMeaning);
        var routinePoll = ContainsAny(combined, "Request Class 1", "Request Class 2", "Class 2 poll", "background poll", "no data", "ACK", "single-character ACK");
        var supervisory = ContainsAny(combined, "TESTFR", "S-frame", "STARTDT", "STOPDT");

        if (mode == TraceVerbosityMode.Proof)
        {
            if (routinePoll || supervisory)
            {
                CountTraceVerbositySuppression(routinePoll ? "routine" : "supervisory");
                return false;
            }
        }

        if (mode == TraceVerbosityMode.Balanced)
        {
            if (supervisory)
            {
                CountTraceVerbositySuppression("supervisory");
                return false;
            }

            if (routinePoll && !IsProtocolTraceTabActive())
            {
                CountTraceVerbositySuppression("routine");
                return false;
            }
        }

        return true;
    }

    private static bool IsProtectedTraceEvidence(Iec103MasterEvidenceEvent item, EvidenceRow row)
    {
        if (IsDiagnosticEvidence(item) ||
            item.IsRelayValue ||
            item.IsRelayEdgeEvent ||
            item.IsMappedSignal ||
            IsIec10xProcessValue(item) ||
            IsIec10xDigitalType(item.TypeId) ||
            IsGeneralInterrogationActivity(item) ||
            item.CauseOfTransmission is 6 or 7 or 10 ||
            item.TypeId is 45 or 46 or 47 or 48 or 49 or 50 or 51)
        {
            return true;
        }

        var text = string.Join(" ", item.Summary, item.Detail, item.OperatorMessage, item.ProtocolMeaning, row.ReadableMeaning);
        return ContainsAny(text, "timeout", "failed", "error", "nack", "negative", "invalid", "busy", "DFC=1", "quality", "ACTCON", "ACTTERM", "command", "operate", "select");
    }

    private void CountTraceVerbositySuppression(string bucket)
    {
        _traceVerbositySuppressedRows++;
        if (bucket.Equals("supervisory", StringComparison.OrdinalIgnoreCase))
        {
            _traceVerbositySuppressedSupervisory++;
        }
        else
        {
            _traceVerbositySuppressedRoutine++;
        }
    }

    private void AddDualLinkTimelineRowIfImportant(Iec103MasterEvidenceEvent item)
    {
        if (item.ProtocolMode != Iec60870ProtocolMode.Iec101)
        {
            return;
        }

        var combined = string.Join(" ", item.Category, item.DataClass, item.SignalGroup, item.Summary, item.Detail, item.OperatorMessage, item.OperatorAction);
        var isDualLink = ContainsAny(combined, "IEC-101 Dual Link", "Redundancy", "Link A", "Link B", "Failover", "failback", "standby", "active link");
        if (!isDualLink)
        {
            return;
        }

        // Keep the Redundancy workspace quiet. Routine successful standby probes and
        // ordinary state chatter stay in Trace/Evidence Ledger; only decisions,
        // failures, recovery and operator actions belong in this timeline.
        var isImportant = ContainsAny(
            combined,
            "Failover", "failover", "Manual", "manual", "Recovery", "recovery",
            "blocked", "rejected", "timeout", "failed", "fault", "Stale", "Post-switch",
            "GI completed", "GI failed", "Application image", "ApplicationImage", "Command blocked", "CommandBlocked", "AutoFailback");

        if (!isImportant)
        {
            return;
        }

        DualLinkTimelineRows.Add(new DualLinkTimelineRow(item));
        DualLinkTimelineRows.TrimStart(MaxVisibleDualLinkTimelineRows);
    }

    private void ApplyEvidenceToUi(Iec103MasterEvidenceEvent item)
    {
        var row = new EvidenceRow(item, ResolveIoaPoint(item));
        ObserveIecProtocolTriggerWatch(item, row);
        AddDualLinkTimelineRowIfImportant(item);

        if (ShouldAddToEvidenceSummary(item, row, out var summaryKey, out var summarySignature))
        {
            AddEvidenceSummaryRow(row);
            if (!string.IsNullOrWhiteSpace(summaryKey))
            {
                _evidenceSummarySignatureByKey[summaryKey] = summarySignature;
                _evidenceSummaryLastUtcByKey[summaryKey] = DateTime.UtcNow;
            }
        }

        if (ShouldShowInFrameTrace(item, row))
        {
            AddProtocolTraceRow(row);
        }

        UpdateLiveCounters(item);
        ObserveScanHealth(item);
        ObserveProtocolProof(item);
        ObserveCommandBehaviour(item);
        ReportRuntimeCommonAddressMismatch(item);
        UpdateValueAndEventViews(item);
        if (IsDiagnosticEvidence(item))
        {
            PulseLed(DiagLed);
            AddDiagnosticRow(new DiagnosticRow(item));
            UpdateStableHeader("Attention", ChooseOperatorStatus(item));
        }

        // Do not push every protocol state into the top session card. High-volume
        // polling alternates Class 2/Class 1 states quickly and makes Auto-sized WPF
        // layouts appear to flicker. The header shows stable session phase only;
        // detailed per-frame state belongs in Evidence Ledger / Trace.

        if (item.Category == "Error" || item.Category == "Warning" || item.Category == "RX Warning" || IsImportantSessionNote(item))
        {
            AppendSessionLog($"#{item.SequenceNumber} {item.State}: {item.Summary} - {item.Detail}");
        }
    }


    private bool ShouldAddToEvidenceSummary(Iec103MasterEvidenceEvent item, EvidenceRow row, out string summaryKey, out string summarySignature)
    {
        summaryKey = BuildEvidenceSummaryKey(item, row);
        summarySignature = BuildEvidenceSummarySignature(item, row);

        if (IsDiagnosticEvidence(item))
        {
            return true;
        }

        var combined = string.Join(" ", item.Category, item.State, item.Summary, item.Detail, item.OperatorMessage, item.OperatorAction, item.ProtocolMeaning, item.CauseName, item.QualityText);
        var startupLinkNack = item.ProtocolMode == Iec60870ProtocolMode.Iec101
                              && item.DataClass.Equals("Link", StringComparison.OrdinalIgnoreCase)
                              && ContainsAny(combined, "NACK", "single-character NACK")
                              && ContainsAny(combined, "Startup", "Reset FCB", "Reset remote link", "synchronization");
        if (startupLinkNack)
        {
            return false;
        }

        var isIssue = ContainsAny(combined, "timeout", "failed", "error", "nack", "negative", "invalid", "not topical", "blocked", "DFC=1", "busy", "quality");
        var isGiMilestone = ContainsAny(combined, "General Interrogation", "ACTCON", "ACTTERM", "interrogation completed", "GI completed", "GI failed", "GI timeout");
        var isCommandMilestone = ContainsAny(combined, "command", "select", "operate", "activation confirmation", "activation termination", "feedback");
        var isClockOrResetMilestone = ContainsAny(combined, "clock sync", "time synchronization", "reset remote link", "reset FCB");
        var isSignalOutcome = item.IsRelayValue || item.IsRelayEdgeEvent || item.IsMappedSignal || item.InformationObjectAddress.HasValue;

        if (!isIssue && !isGiMilestone && !isCommandMilestone && !isClockOrResetMilestone && !isSignalOutcome)
        {
            return false;
        }

        // Do not pollute the summary with routine line traffic. Trace remains the source of truth for these.
        if (!isIssue && !isCommandMilestone && !isGiMilestone)
        {
            var routine = ContainsAny(combined, "Request Class 1", "Request Class 2", "ACK", "Class 2 poll", "background poll", "S-frame", "TESTFR");
            if (routine && !isSignalOutcome)
            {
                return false;
            }
        }

        if (item.IsRelayEdgeEvent)
        {
            if (!string.IsNullOrWhiteSpace(item.PreviousSignalValue) &&
                !string.IsNullOrWhiteSpace(item.SignalDisplayValue) &&
                string.Equals(NormalizeSummaryValue(item.PreviousSignalValue), NormalizeSummaryValue(item.SignalDisplayValue), StringComparison.OrdinalIgnoreCase) &&
                !isIssue)
            {
                return false;
            }

            return true;
        }

        if (isSignalOutcome && !isIssue)
        {
            // Analog measurement scan is high-volume. Values must stay live, but Evidence Ledger
            // should be proof-grade: first proof, quality/timestamp issue, significant drift, or slow heartbeat.
            if (IsAnalogMeasurementType(item.TypeId) && !string.IsNullOrWhiteSpace(summaryKey))
            {
                return ShouldShowAnalogMeasurementProof(item, summaryKey);
            }

            // Digital/SP/DP and command feedback must remain event-grade. Suppress exact duplicates only.
            if (!string.IsNullOrWhiteSpace(summaryKey) &&
                _evidenceSummarySignatureByKey.TryGetValue(summaryKey, out var previousSignature) &&
                string.Equals(previousSignature, summarySignature, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildEvidenceSummaryKey(Iec103MasterEvidenceEvent item, EvidenceRow row)
    {
        if (!string.IsNullOrWhiteSpace(item.SignalKey))
        {
            return $"{item.ProtocolMode}|signal|{item.SignalKey}";
        }

        if (item.CommonAddressNumber.HasValue || item.InformationObjectAddress.HasValue || item.TypeId.HasValue)
        {
            return $"{item.ProtocolMode}|ioa|{item.CommonAddressNumber}|{item.InformationObjectAddress}|{item.TypeId}";
        }

        var combined = string.Join(" ", item.Category, item.State, item.Summary, item.Detail, item.OperatorAction, item.ProtocolMeaning);
        if (ContainsAny(combined, "General Interrogation", "ACTCON", "ACTTERM", "GI completed"))
        {
            return $"{item.ProtocolMode}|gi|{item.State}|{item.CauseOfTransmission}|{item.Category}";
        }

        if (ContainsAny(combined, "command", "select", "operate", "activation"))
        {
            return $"{item.ProtocolMode}|cmd|{item.CommonAddressNumber}|{item.InformationObjectAddress}|{item.TypeId}|{item.CauseOfTransmission}|{item.State}";
        }

        if (ContainsAny(combined, "timeout", "failed", "error", "nack", "negative"))
        {
            return $"{item.ProtocolMode}|issue|{item.State}|{item.Category}|{item.Summary}";
        }

        return string.Empty;
    }

    private static string BuildEvidenceSummarySignature(Iec103MasterEvidenceEvent item, EvidenceRow row)
    {
        return string.Join("|",
            NormalizeSummaryValue(item.SignalDisplayValue),
            NormalizeSummaryValue(item.SignalRawValue),
            NormalizeSummaryValue(item.QualityText),
            item.RelayTimestampInvalid ? "time-invalid" : "time-ok",
            item.CauseOfTransmission?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            NormalizeSummaryValue(item.CauseName),
            NormalizeSummaryValue(item.Category),
            NormalizeSummaryValue(item.OperatorAction));
    }

    private static string NormalizeSummaryValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }


    private bool ShouldShowAnalogMeasurementProof(Iec103MasterEvidenceEvent item, string summaryKey)
    {
        var numeric = TryExtractFirstNumeric(item.SignalDisplayValue);
        if (!numeric.HasValue)
        {
            numeric = TryExtractFirstNumeric(item.ObjectSummary);
        }

        if (!numeric.HasValue)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        if (!_evidenceSummaryLastAnalogValueByKey.TryGetValue(summaryKey, out var previous))
        {
            _evidenceSummaryLastAnalogValueByKey[summaryKey] = numeric.Value;
            _evidenceSummaryLastAnalogUtcByKey[summaryKey] = now;
            return true;
        }

        var delta = Math.Abs(numeric.Value - previous);
        var threshold = Math.Max(Math.Abs(previous) * 0.02, 0.2);
        var heartbeatDue = !_evidenceSummaryLastAnalogUtcByKey.TryGetValue(summaryKey, out var lastUtc)
                           || (now - lastUtc).TotalSeconds >= 120;

        if (delta >= threshold || heartbeatDue)
        {
            _evidenceSummaryLastAnalogValueByKey[summaryKey] = numeric.Value;
            _evidenceSummaryLastAnalogUtcByKey[summaryKey] = now;
            return true;
        }

        return false;
    }

    private static bool IsAnalogMeasurementType(int? typeId)
        => typeId is 9 or 10 or 11 or 12 or 13 or 14 or 34 or 35 or 36;

    private static double? TryExtractFirstNumeric(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(value, @"[-+]?\d+(?:[.,]\d+)?");
        if (!match.Success)
        {
            return null;
        }

        var normalized = match.Value.Replace(',', '.');
        return double.TryParse(
            normalized,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var result)
            ? result
            : null;
    }

    private static bool ContainsAny(string text, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var token in tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsImportantSessionNote(Iec103MasterEvidenceEvent item)
    {
        var text = string.Join(" ", item.Summary, item.Detail, item.OperatorMessage);
        return text.Contains("General Interrogation", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("GI ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Fault", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Assessment", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFindingToUi(Iec103MasterFinding finding)
    {
        var row = new FindingRow(finding);
        _findingStore.Add(row);
        _pendingFindingUiRows.Add(row);
        FindingCountText.Text = Math.Min(MaxVisibleFindingRows, FindingRows.Count + _pendingFindingUiRows.Count).ToString(System.Globalization.CultureInfo.InvariantCulture);
        PulseLed(DiagLed);
        AddDiagnosticRow(new DiagnosticRow(finding));
        AppendSessionLog($"Finding [{finding.Severity}] {finding.Id}: {finding.Title}");
    }

    private static string ChooseOperatorStatus(Iec103MasterEvidenceEvent item)
    {
        if (!string.IsNullOrWhiteSpace(item.OperatorMessage))
        {
            return string.IsNullOrWhiteSpace(item.OperatorAction)
                ? item.OperatorMessage
                : item.OperatorMessage + " " + item.OperatorAction;
        }

        return string.IsNullOrWhiteSpace(item.Detail) ? item.Summary : item.Detail;
    }

    private static bool IsGeneralInterrogationActivity(Iec103MasterEvidenceEvent item)
    {
        if (item.CauseOfTransmission is >= 20 and <= 36)
        {
            return true;
        }

        var text = string.Join(" ",
            item.State.ToString(),
            item.Summary,
            item.Detail,
            item.OperatorMessage,
            item.ProtocolMeaning,
            item.Cot ?? string.Empty,
            item.AsduType ?? string.Empty,
            item.TypeName ?? string.Empty);

        return text.Contains("General Interrogation", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Interrogation", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("GI ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("GI-", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("GI_", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateLiveCounters(Iec103MasterEvidenceEvent item)
    {
        if (IsGeneralInterrogationActivity(item))
        {
            _giCount++;
            PulseLed(GiLed);
        }

        if (item.Direction == FrameDirection.MasterToSlave)
        {
            _txCount++;
            PulseLed(TxLed);
        }
        else if (item.Direction == FrameDirection.SlaveToMaster)
        {
            _rxCount++;
            PulseLed(RxLed);
        }

        if (item.ProtocolMode == Iec60870ProtocolMode.Iec104)
        {
            if (string.Equals(item.DataClass, "I", StringComparison.OrdinalIgnoreCase))
            {
                _class1Count++;
                PulseLed(Class1Led);
            }
            else if (string.Equals(item.DataClass, "S", StringComparison.OrdinalIgnoreCase))
            {
                _class2Count++;
                PulseLed(Class2Led);
            }
        }
        else
        {
            if (string.Equals(item.DataClass, "Class 1", StringComparison.OrdinalIgnoreCase) && item.Direction == FrameDirection.MasterToSlave)
            {
                _class1Count++;
                PulseLed(Class1Led);
            }

            if (string.Equals(item.DataClass, "Class 2", StringComparison.OrdinalIgnoreCase) && item.Direction == FrameDirection.MasterToSlave)
            {
                _class2Count++;
                PulseLed(Class2Led);
            }
        }

        if (item.Summary.Contains("NO DATA", StringComparison.OrdinalIgnoreCase) || item.Detail.Contains("NO DATA", StringComparison.OrdinalIgnoreCase))
        {
            _noDataCount++;
        }

        if (item.Frame?.Asdu?.TypeId == 1 || item.Frame?.Asdu?.TypeId == 2 || item.IsRelayValue)
        {
            _dpiCount++;
            PulseLed(EventLed);
        }

        TxRxText.Text = $"{_txCount} / {_rxCount}";
        ClassPollText.Text = $"{_giCount} / {_class1Count} / {_class2Count}";
        NoDataText.Text = _noDataCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        DpiText.Text = _dpiCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void PulseLed(FrameworkElement led)
    {
        if (led == null)
        {
            return;
        }

        led.Opacity = 1.0;
        _ledPulseTimes[led] = DateTime.UtcNow;
    }

    private void DecayLedPulses()
    {
        if (_ledPulseTimes.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var pair in _ledPulseTimes.ToArray())
        {
            if ((now - pair.Value).TotalMilliseconds >= 180)
            {
                pair.Key.Opacity = 0.28;
                _ledPulseTimes.Remove(pair.Key);
            }
        }
    }

    private void ApplyFinalResult(Iec103MasterRunResult result)
    {
        FlushUiQueues();
        TxRxText.Text = $"{result.Counters.TxFrames} / {result.Counters.RxFrames}";
        ClassPollText.Text = $"{result.Counters.GiCommands + result.Counters.GiEndResponses} / {result.Counters.Class1Requests} / {result.Counters.Class2Requests}";
        NoDataText.Text = result.Counters.NoDataResponses.ToString(System.Globalization.CultureInfo.InvariantCulture);
        DpiText.Text = result.Counters.DpiEvents.ToString(System.Globalization.CultureInfo.InvariantCulture);
        FindingCountText.Text = result.Findings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        UpdateStableHeader(result.CompletedNormally ? "Completed" : "Faulted",
            $"Assessment: {result.Assessment.OverallStatus} ({result.Assessment.Score}/100). {result.CompletionReason}");
        ExportReportButton.IsEnabled = true;

        AssessmentRows.Clear();
        foreach (var item in result.Assessment.Items)
        {
            AssessmentRows.Add(new AssessmentRow(item));
        }
        AppendSessionLog($"Assessment: {result.Assessment.OverallStatus} ({result.Assessment.Score}/100) - {result.Assessment.Summary}");

        if (result.ValuePoints.Count > 0)
        {
            _valueRowsByKey.Clear();
            foreach (var row in result.ValuePoints.Select(x => new ValueRow(x)))
            {
                _valueRowsByKey[row.Key] = row;
            }

            ValueRows.ReplaceRange(GetSortedValueRowsSnapshot());
            _valueRowsDirty = false;
        }

        if (result.EventLog.Count > 0)
        {
            _relayEventStore.Clear();
            foreach (var ev in result.EventLog.Select(x => new RelayEventRow(x)))
            {
                _relayEventStore.Add(ev);
            }

            ApplyRelayEventFilter();
            _relayEventRowsDirty = false;
        }

        foreach (var finding in result.Findings)
        {
            if (!FindingRows.Any(x => x.Id == finding.Id && x.Title == finding.Title))
            {
                var row = new FindingRow(finding);
                _findingStore.Add(row);
                _pendingFindingUiRows.Add(row);
            }
        }
        FlushVisibleUiBatches();
        EmitGiCoverageMatrixVerdict("Completed session result applied");
        EmitSessionProofVerdict("Completed session result applied");
    }
}
