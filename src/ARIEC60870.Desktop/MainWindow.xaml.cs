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
using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Reporting;
using ARIEC60870.Master.Transport;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _sessionCancellation;
    private Iec103MasterRunResult? _lastResult;
    private int _txCount;
    private int _rxCount;
    private int _giCount;
    private int _class1Count;
    private int _class2Count;
    private int _noDataCount;
    private int _dpiCount;
    private long _visibleEvidenceDropped;
    private long _visibleLogLinesDropped;
    private Iec103SignalMappingProfile _mappingProfile = Iec103SignalMappingProfile.Empty;
    private Iec10xPointMappingProfile _ioaProfile = Iec10xPointMappingProfile.Empty;
    private IProtocolControlCommandSession? _activeControlSession;
    private Iec101DualLinkRedundancySession? _activeDualLinkSession;
    private bool _commandDockExpanded = true;
    private readonly BoundedRingBuffer<RelayEventRow> _relayEventStore = new(MaxVisibleRelayEventRows);
    private IByteTransport? _activeTransport;
    private IByteTransport? _activeBackupTransport;
    private bool _stopRequested;
    private string _selectedFrameExplanation = "Select a frame. This panel translates raw bytes into commissioning meaning.";
    private EvidenceRow? _selectedFrameRow;
    private string? _pinnedProtocolMapKey;
    private bool _statusHistoryExpanded;
    private bool _isApplyingSavedSetup;
    private bool _savedSetupPreferencesLoaded;
    private bool _defaultIoaSeedSettingsApplied;
    private bool _isProtocolTraceDragSelecting;
    private int _protocolTraceSelectionAnchorIndex = -1;
    private bool _isProtocolTraceSelectionBatching;
    private bool _pendingProtocolTraceSelectionInspectorRefresh;
    private bool _protocolTraceViewDirtyWhileFrozen;
    private long _protocolTraceRowsDeferredWhileFrozen;
    private bool _isEvidenceSummaryDragSelecting;
    private int _evidenceSummarySelectionAnchorIndex = -1;
    private bool _evidenceSummaryViewDirtyWhileFrozen;
    private long _evidenceSummaryRowsDeferredWhileFrozen;

    private const int MaxVisibleEvidenceRows = 260;
    private const int MaxVisibleFrameTraceRows = 1200;
    private const int MaxVisibleRelayEventRows = 420;
    private const int MaxVisibleFindingRows = 260;
    private const int MaxVisibleDiagnosticRows = 280;
    private const int MaxVisibleValueRows = 2200;
    private const int MaxVisibleSignalListRows = 360;
    private const int MaxVisibleDualLinkTimelineRows = 160;
    private const int MaxSessionLogLines = 280;
    private const int MaxUiFlushPerTick = 42;
    private const int MaxUiFlushBurstPerTick = 220;
    private const int MaxPendingEvidenceBacklog = 5000;
    private const int UiFlushSlowWarningMs = 120;
    private const int UiQueuePressureWarningDepth = 2500;
    private const int TriggerPreCaptureRows = 32;
    private const int TriggerPostCaptureRows = 24;
    private const int MaxConcurrentTriggerCaptures = 4;
    private const int MaxTriggerPreBufferRows = 80;


    private readonly ConcurrentQueue<Iec103MasterEvidenceEvent> _pendingEvidence = new();
    private readonly ConcurrentQueue<Iec103MasterFinding> _pendingFindings = new();
    private readonly Queue<string> _sessionLogLines = new();
    private readonly DispatcherTimer _uiFlushTimer;
    private readonly DispatcherTimer _ledDecayTimer;
    private readonly DispatcherTimer _valueHighlightTimer;
    private readonly Dictionary<FrameworkElement, DateTime> _ledPulseTimes = new();
    private readonly Dictionary<string, DateTime> _valueHighlightExpiryByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastDisplayedValueByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _evidenceSummarySignatureByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _evidenceSummaryLastUtcByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _evidenceSummaryLastAnalogValueByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _evidenceSummaryLastAnalogUtcByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly BoundedRingBuffer<EvidenceRow> _evidenceSummaryStore = new(MaxVisibleEvidenceRows);
    private readonly BoundedRingBuffer<EvidenceRow> _protocolTraceStore = new(MaxVisibleFrameTraceRows);
    private readonly List<EvidenceRow> _pendingEvidenceSummaryUiRows = new();
    private readonly List<EvidenceRow> _pendingProtocolTraceUiRows = new();
    private readonly List<FindingRow> _pendingFindingUiRows = new();
    private readonly List<DiagnosticRow> _pendingDiagnosticUiRows = new();
    private readonly BoundedRingBuffer<FindingRow> _findingStore = new(MaxVisibleFindingRows);
    private readonly BoundedRingBuffer<DiagnosticRow> _diagnosticStore = new(MaxVisibleDiagnosticRows);
    private readonly Queue<EvidenceRow> _triggerPreCaptureBuffer = new();
    private readonly List<ProtocolTriggerCapture> _activeProtocolTriggerCaptures = new();
    private readonly Dictionary<string, DateTime> _lastProtocolTriggerUtcByKey = new(StringComparer.OrdinalIgnoreCase);
    private int _protocolTriggerCaptureSequence;
    private long _protocolTriggerStartedCount;
    private long _protocolTriggerCompletedCount;

    private readonly Dictionary<string, ValueRow> _valueRowsByKey = new(StringComparer.OrdinalIgnoreCase);
    private bool _valueRowsDirty;
    private bool _relayEventRowsDirty;
    private long _backpressureDroppedEvents;
    private long _backpressureDroppedAckNoData;
    private long _backpressureDroppedBackgroundPoll;
    private long _backpressureDroppedTestFrames;
    private long _backpressureDroppedOtherLowValue;
    private long _traceVerbositySuppressedRows;
    private long _traceVerbositySuppressedRoutine;
    private long _traceVerbositySuppressedSupervisory;
    private int _backpressureNoticePending;
    private long _lastDropSummaryMarkerTotal;
    private long _maxPendingEvidenceDepth;
    private long _uiFlushTicks;
    private long _lastUiFlushMs;
    private long _maxUiFlushMs;
    private int _lastEvidenceProcessed;
    private int _lastFindingProcessed;
    private int _lastVisibleBatchRows;
    private int _lastFlushBudget = MaxUiFlushPerTick;
    private DateTime _lastBackpressureLogUtc = DateTime.MinValue;
    private DateTime _lastDispatcherPressureDiagnosticUtc = DateTime.MinValue;
    private DateTime _lastDispatcherSlowDiagnosticUtc = DateTime.MinValue;
    private readonly HashSet<string> _giExpectedValueKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _giReceivedValueKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _giCompletenessWatchActive;
    private bool _giCompletenessReported;
    private bool _giClass2CollectionWindowActive;
    private DateTime _giClass2CollectionUntilUtc = DateTime.MinValue;
    private int? _firstObservedRuntimeCa;
    private bool _runtimeCaMismatchReported;
    private DateTime _scanHealthSessionStartedUtc = DateTime.MinValue;
    private DateTime _scanHealthLastClass1RxUtc = DateTime.MinValue;
    private DateTime _scanHealthLastClass2RxUtc = DateTime.MinValue;
    private DateTime _scanHealthLastProcessRxUtc = DateTime.MinValue;
    private DateTime _scanHealthLastDigitalRxUtc = DateTime.MinValue;
    private DateTime _scanHealthAcdSinceUtc = DateTime.MinValue;
    private DateTime _proofFirstGiUtc = DateTime.MinValue;
    private DateTime _proofFirstProcessValueUtc = DateTime.MinValue;
    private DateTime _proofFirstDigitalUtc = DateTime.MinValue;
    private DateTime _proofFirstAnalogUtc = DateTime.MinValue;
    private DateTime _proofFirstCommandUtc = DateTime.MinValue;
    private DateTime _proofFirstCommandFeedbackUtc = DateTime.MinValue;
    private int _proofObservedCa = -1;
    private bool _proofGiObserved;
    private bool _proofGiCompleted;
    private bool _proofGiNegative;
    private bool _proofDigitalObserved;
    private bool _proofAnalogObserved;
    private bool _proofCommandObserved;
    private bool _proofCommandFeedbackObserved;
    private readonly HashSet<string> _protocolProofMarkers = new(StringComparer.OrdinalIgnoreCase);
    private int _lastMonitorExpectedCount;
    private int _lastMonitorReceivedCount;
    private int _lastDigitalExpectedCount;
    private int _lastDigitalReceivedCount;
    private int _lastAnalogExpectedCount;
    private int _lastAnalogReceivedCount;
    private int _lastOtherExpectedCount;
    private int _lastOtherReceivedCount;
    private int _lastCommandExpectedCount;
    private int _lastFeedbackMappedCommandCount;
    private int _lastMissingMonitorCount;
    private string _lastMissingMonitorPreview = "-";
    private readonly Dictionary<string, DateTime> _scanHealthLastDiagnosticUtcByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommandLedgerEntry> _commandLedgerByKey = new(StringComparer.OrdinalIgnoreCase);

    private sealed class CommandLedgerEntry
    {
        public string Key { get; init; } = string.Empty;
        public int? CommonAddress { get; init; }
        public int CommandIoa { get; init; }
        public int? CommandTypeId { get; init; }
        public int? FeedbackIoa { get; init; }
        public string Summary { get; init; } = string.Empty;
        public string Stage { get; set; } = "issued";
        public DateTime StartedUtc { get; init; } = DateTime.UtcNow;
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
        public bool ActConSeen { get; set; }
        public bool ActTermSeen { get; set; }
        public bool FeedbackSeen { get; set; }
        public bool NegativeSeen { get; set; }
        public bool TimeoutReported { get; set; }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        _uiFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _uiFlushTimer.Tick += (_, _) => FlushUiQueues();
        _uiFlushTimer.Start();
        _ledDecayTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(90)
        };
        _ledDecayTimer.Tick += (_, _) => DecayLedPulses();
        _ledDecayTimer.Start();
        _valueHighlightTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _valueHighlightTimer.Tick += (_, _) => ResetExpiredValueHighlights();
        _valueHighlightTimer.Start();
        RefreshPorts();
        LoadSetupPreferences();
        LoadDefaultIoaSeedProfile();
        ApplyProtocolUxProfile(GetSelectedProtocolMode());
        AppendSessionLog("ARIEC60870 Evidence Analyzer initialized. Ready for protocol-aware IEC-101 / IEC-103 / IEC-104 testing.");
        AppendSessionLog("Output model: Values, Events, Trace, and Report stay focused; advanced evidence ledger remains available without crowding the main workspace.");
        Loaded += (_, _) =>
        {
            ApplyProtocolUxProfile(GetSelectedProtocolMode());
            MainTabControl.SelectedIndex = IsIec101DualLinkModeSelected() ? 9 : 1;
            UpdateSegmentedNav(false);
            UpdateResponsiveHeaderLayout();
            UpdateAutoScrollLatestRailVisual();
            ApplyCommandDockLayout();
            UpdateCommandDockActionButtons();
            UpdateConnectToggleVisual(false);
            SetStatusHistoryPanelExpanded(false);
            RefreshReportPreview();
            RefreshCommandSignalOptions();
            AutoFillCommandTargetFromProfile();
        };
        SizeChanged += (_, _) =>
        {
            UpdateSegmentedNav(false);
            UpdateResponsiveHeaderLayout();
        };
        Closing += (_, _) => SaveSetupPreferencesFromUi(silent: true);
    }

    public ObservableRangeCollection<EvidenceRow> EvidenceRows { get; } = new();
    public ObservableRangeCollection<EvidenceRow> FrameTraceRows { get; } = new();
    public ObservableRangeCollection<FindingRow> FindingRows { get; } = new();
    public ObservableRangeCollection<FindingWorkspaceRow> FindingWorkspaceRows { get; } = new();
    public ObservableRangeCollection<ValueRow> ValueRows { get; } = new();
    public ObservableRangeCollection<RelayEventRow> RelayEventRows { get; } = new();
    public ObservableCollection<IoaMappingRow> IoaProfileRows { get; } = new();
    public ObservableCollection<CommandSignalOption> CommandSignalOptions { get; } = new();
    public ObservableCollection<AssessmentRow> AssessmentRows { get; } = new();
    public ObservableRangeCollection<DiagnosticRow> DiagnosticRows { get; } = new();
    public ObservableCollection<ProtocolMapLine> SelectedProtocolMapLines { get; } = new();
    public ObservableCollection<HexSegment> SelectedHexSegments { get; } = new();
    public ObservableCollection<StatusHistoryRow> StatusHistoryRows { get; } = new();
    public ObservableCollection<TriggerCaptureRow> TriggerCaptureRows { get; } = new();
    public ObservableRangeCollection<DualLinkTimelineRow> DualLinkTimelineRows { get; } = new();

    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void OpenSetup_Click(object sender, RoutedEventArgs e)
    {
        SetupOverlay.Visibility = Visibility.Visible;
    }

    private void CloseSetup_Click(object sender, RoutedEventArgs e)
    {
        SaveSetupPreferencesFromUi(silent: true);
        SetupOverlay.Visibility = Visibility.Collapsed;
    }

    private void CloseSetupAndConnect_Click(object sender, RoutedEventArgs e)
    {
        SaveSetupPreferencesFromUi(silent: true);
        SetupOverlay.Visibility = Visibility.Collapsed;
        if (_sessionCancellation is null)
        {
            Start_Click(sender, e);
        }
    }

    private void RefreshPorts()
    {
        var previous = PortComboBox.SelectedItem as string;
        PortComboBox.Items.Clear();

        var ports = SerialPort.GetPortNames()
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ports.Length == 0)
        {
            PortComboBox.Items.Add("COM1");
        }
        else
        {
            foreach (var port in ports)
            {
                PortComboBox.Items.Add(port);
            }
        }

        PortComboBox.SelectedItem = !string.IsNullOrWhiteSpace(previous) && PortComboBox.Items.Contains(previous)
            ? previous
            : PortComboBox.Items[0];

        if (BackupPortComboBox is not null)
        {
            var previousBackup = BackupPortComboBox.SelectedItem as string;
            BackupPortComboBox.Items.Clear();
            foreach (var port in PortComboBox.Items.OfType<string>())
            {
                BackupPortComboBox.Items.Add(port);
            }

            if (!string.IsNullOrWhiteSpace(previousBackup) && BackupPortComboBox.Items.Contains(previousBackup))
            {
                BackupPortComboBox.SelectedItem = previousBackup;
            }
            else if (BackupPortComboBox.Items.Count > 1)
            {
                BackupPortComboBox.SelectedIndex = 1;
            }
            else if (BackupPortComboBox.Items.Count > 0)
            {
                BackupPortComboBox.SelectedIndex = 0;
            }
        }
    }



    private static string SetupPreferencesPath => ARIEC60870.Desktop.Services.LocalWorkspacePaths.SetupPreferencesFile;

    private static string BundledUtilityFatProfilePath => Path.Combine(
        AppContext.BaseDirectory,
        "profiles",
        "utility_fat_iec10x_default_profile.json");

    private static string SourceTreeUtilityFatProfilePath => Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "profiles",
        "utility_fat_iec10x_default_profile.json");

    private void LoadDefaultIoaSeedProfile()
    {
        if (_ioaProfile.HasPoints)
        {
            return;
        }

        var candidates = new[]
        {
            BundledUtilityFatProfilePath,
            Path.GetFullPath(SourceTreeUtilityFatProfilePath)
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                _ioaProfile = Iec10xPointMappingProfile.LoadFromFile(path);
                if (GetSelectedProtocolMode() != Iec60870ProtocolMode.Iec103 && string.IsNullOrWhiteSpace(MappingProfilePathBox.Text))
                {
                    MappingProfilePathBox.Text = path;
                }
                MappingProfileStatusText.Text = $"Default IOA seed available: {_ioaProfile.ProfileName} ({_ioaProfile.Points.Count} points). Copy/edit JSON for project-specific IOA database.";
                RefreshIoaProfileRows();
                if (GetSelectedProtocolMode() != Iec60870ProtocolMode.Iec103)
                {
                    ApplyIoaProfileDefaultsToUi(_ioaProfile, onlyWhenUiLooksDefault: !_savedSetupPreferencesLoaded);
                }
                AppendSessionLog($"Default IOA seed profile loaded: {_ioaProfile.ProfileName} ({_ioaProfile.Points.Count} points).");
                return;
            }
            catch (Exception ex)
            {
                AddUiDiagnostic("Warning", "Mapping", "IEC10X-IOA-SEED-LOAD", "Default IOA seed could not be loaded", ex.Message, "The app will continue with raw IOA labels. Check profiles/utility_fat_iec10x_default_profile.json.", ex);
            }
        }
    }


    private void RefreshIoaProfileRows()
    {
        IoaProfileRows.Clear();
        var ordered = _ioaProfile.Points
            .OrderBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Ioa)
            .ThenBy(x => x.TypeId ?? 0)
            .ToList();

        foreach (var point in ordered.Take(MaxVisibleSignalListRows))
        {
            IoaProfileRows.Add(new IoaMappingRow(point, ordered));
        }

        if (ordered.Count > 0)
        {
            var suffix = ordered.Count > IoaProfileRows.Count
                ? $" Showing first {IoaProfileRows.Count} rows in cached preview; use the Signal List popup for the full database."
                : string.Empty;
            AppendSessionLog($"IOA signal list loaded: {ordered.Count} points from {_ioaProfile.ProfileName}.{suffix}");
        }

        RefreshCommandSignalOptions();
    }






}
