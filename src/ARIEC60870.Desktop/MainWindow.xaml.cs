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
    private long _visibleRelayEventsDropped;
    private long _visibleLogLinesDropped;
    private long _visibleDiagnosticsDropped;
    private Iec103SignalMappingProfile _mappingProfile = Iec103SignalMappingProfile.Empty;
    private Iec10xPointMappingProfile _ioaProfile = Iec10xPointMappingProfile.Empty;
    private IProtocolControlCommandSession? _activeControlSession;
    private bool _commandDockExpanded = true;
    private readonly BoundedRingBuffer<RelayEventRow> _relayEventStore = new(MaxVisibleRelayEventRows);
    private IByteTransport? _activeTransport;
    private bool _stopRequested;
    private string _selectedFrameExplanation = "Select a frame. This panel translates raw bytes into commissioning meaning.";
    private EvidenceRow? _selectedFrameRow;
    private string? _pinnedProtocolMapKey;
    private bool _statusHistoryExpanded = true;
    private bool _isApplyingSavedSetup;
    private bool _savedSetupPreferencesLoaded;
    private bool _defaultIoaSeedSettingsApplied;

    private const int MaxVisibleEvidenceRows = 260;
    private const int MaxVisibleFrameTraceRows = 1200;
    private const int MaxVisibleRelayEventRows = 420;
    private const int MaxVisibleFindingRows = 260;
    private const int MaxVisibleDiagnosticRows = 280;
    private const int MaxVisibleValueRows = 2200;
    private const int MaxVisibleSignalListRows = 360;
    private const int MaxSessionLogLines = 280;
    private const int MaxUiFlushPerTick = 42;
    private const int MaxUiFlushBurstPerTick = 220;
    private const int MaxPendingEvidenceBacklog = 5000;
    private const int UiFlushSlowWarningMs = 120;
    private const int UiQueuePressureWarningDepth = 2500;

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
        AppendSessionLog("ARIEC60870 Protocol Lab initialized. Ready for protocol-aware IEC-101 / IEC-103 / IEC-104 testing.");
        AppendSessionLog("Output model: Value Viewer stays live; Evidence Summary is de-noised proof; raw hex remains available in Protocol Trace for protocol transparency.");
        Loaded += (_, _) =>
        {
            MainTabControl.SelectedIndex = 0;
            ApplyProtocolUxProfile(GetSelectedProtocolMode());
            UpdateSegmentedNav(false);
            ApplyCommandDockLayout();
            UpdateCommandDockActionButtons();
            UpdateConnectToggleVisual(false);
            RefreshCommandSignalOptions();
            AutoFillCommandTargetFromProfile();
        };
        SizeChanged += (_, _) => UpdateSegmentedNav(false);
        Closing += (_, _) => SaveSetupPreferencesFromUi(silent: true);
    }

    public ObservableRangeCollection<EvidenceRow> EvidenceRows { get; } = new();
    public ObservableRangeCollection<EvidenceRow> FrameTraceRows { get; } = new();
    public ObservableRangeCollection<FindingRow> FindingRows { get; } = new();
    public ObservableRangeCollection<ValueRow> ValueRows { get; } = new();
    public ObservableRangeCollection<RelayEventRow> RelayEventRows { get; } = new();
    public ObservableCollection<IoaMappingRow> IoaProfileRows { get; } = new();
    public ObservableCollection<CommandSignalOption> CommandSignalOptions { get; } = new();
    public ObservableCollection<AssessmentRow> AssessmentRows { get; } = new();
    public ObservableRangeCollection<DiagnosticRow> DiagnosticRows { get; } = new();
    public ObservableCollection<ProtocolMapLine> SelectedProtocolMapLines { get; } = new();
    public ObservableCollection<HexSegment> SelectedHexSegments { get; } = new();
    public ObservableCollection<StatusHistoryRow> StatusHistoryRows { get; } = new();

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
    }



    private static string SetupPreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ARIEC60870",
        "setup-preferences.json");

    private static string BundledPlnPusertifSeedPath => Path.Combine(
        AppContext.BaseDirectory,
        "profiles",
        "PLN_Pusertif_IEC101_default_seed.json");

    private static string SourceTreePlnPusertifSeedPath => Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "profiles",
        "PLN_Pusertif_IEC101_default_seed.json");

    private void LoadDefaultIoaSeedProfile()
    {
        if (_ioaProfile.HasPoints)
        {
            return;
        }

        var candidates = new[]
        {
            BundledPlnPusertifSeedPath,
            Path.GetFullPath(SourceTreePlnPusertifSeedPath)
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
                AddUiDiagnostic("Warning", "Mapping", "IEC10X-IOA-SEED-LOAD", "Default IOA seed could not be loaded", ex.Message, "The app will continue with raw IOA labels. Check profiles/PLN_Pusertif_IEC101_default_seed.json.", ex);
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





    private void ResetRuntimeHealthStores()
    {
        _scanHealthSessionStartedUtc = DateTime.MinValue;
        _scanHealthLastClass1RxUtc = DateTime.MinValue;
        _scanHealthLastClass2RxUtc = DateTime.MinValue;
        _scanHealthLastProcessRxUtc = DateTime.MinValue;
        _scanHealthLastDigitalRxUtc = DateTime.MinValue;
        _scanHealthAcdSinceUtc = DateTime.MinValue;
        ResetProtocolProofState();
        _scanHealthLastDiagnosticUtcByCode.Clear();
        _commandLedgerByKey.Clear();
    }

    private void ResetProtocolProofState()
    {
        _proofFirstGiUtc = DateTime.MinValue;
        _proofFirstProcessValueUtc = DateTime.MinValue;
        _proofFirstDigitalUtc = DateTime.MinValue;
        _proofFirstAnalogUtc = DateTime.MinValue;
        _proofFirstCommandUtc = DateTime.MinValue;
        _proofFirstCommandFeedbackUtc = DateTime.MinValue;
        _proofObservedCa = -1;
        _proofGiObserved = false;
        _proofGiCompleted = false;
        _proofGiNegative = false;
        _proofDigitalObserved = false;
        _proofAnalogObserved = false;
        _proofCommandObserved = false;
        _proofCommandFeedbackObserved = false;
        _lastMonitorExpectedCount = 0;
        _lastMonitorReceivedCount = 0;
        _lastDigitalExpectedCount = 0;
        _lastDigitalReceivedCount = 0;
        _lastAnalogExpectedCount = 0;
        _lastAnalogReceivedCount = 0;
        _lastOtherExpectedCount = 0;
        _lastOtherReceivedCount = 0;
        _lastCommandExpectedCount = 0;
        _lastFeedbackMappedCommandCount = 0;
        _lastMissingMonitorCount = 0;
        _lastMissingMonitorPreview = "-";
        _protocolProofMarkers.Clear();
    }

    private void ObserveScanHealth(Iec103MasterEvidenceEvent item)
    {
        if (_scanHealthSessionStartedUtc == DateTime.MinValue)
        {
            _scanHealthSessionStartedUtc = DateTime.UtcNow;
        }

        if (item.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var isRx = item.Direction == FrameDirection.SlaveToMaster;
        if (isRx && item.DataClass.Contains("Class 1", StringComparison.OrdinalIgnoreCase))
        {
            _scanHealthLastClass1RxUtc = now;
        }

        if (isRx && item.DataClass.Contains("Class 2", StringComparison.OrdinalIgnoreCase))
        {
            _scanHealthLastClass2RxUtc = now;
        }

        if (isRx && (item.IsRelayValue || item.InformationObjectAddress.HasValue))
        {
            _scanHealthLastProcessRxUtc = now;
            if (IsIec10xDigitalType(item.TypeId))
            {
                _scanHealthLastDigitalRxUtc = now;
            }
        }

        if (item.Acd == true)
        {
            if (_scanHealthAcdSinceUtc == DateTime.MinValue)
            {
                _scanHealthAcdSinceUtc = now;
            }
        }
        else if (item.Acd == false)
        {
            _scanHealthAcdSinceUtc = DateTime.MinValue;
        }

        if (item.Dfc == true)
        {
            AddRateLimitedDiagnostic(
                "IEC101-SCAN-DFC-BUSY",
                "Warning",
                "IEC-101",
                "Outstation busy / DFC=1 observed",
                "The controlled station reported DFC=1. Continue polling with backoff; do not interpret missing values as GI failure while the station is busy.",
                "Check RTU load, serial baudrate, class polling interval, and whether the master is over-polling a slow channel.",
                TimeSpan.FromSeconds(20));
        }

        if (item.ResponseTimeMs.HasValue && item.ResponseTimeMs.Value > 2500)
        {
            AddRateLimitedDiagnostic(
                "IEC101-SCAN-SLOW-RESPONSE",
                "Warning",
                "IEC-101",
                "Slow serial response observed",
                $"Response time {item.ResponseTimeMs.Value} ms is high for a polling scan. This can make GI/Class 2 observation look incomplete even when the RTU is only slow.",
                "Increase response timeout and Class 2 poll interval for low-baud links; avoid interpreting 1200 bps channels like Ethernet.",
                TimeSpan.FromSeconds(30));
        }
    }

    private void EvaluateScanHealthWindow()
    {
        if (_sessionCancellation is null || _scanHealthSessionStartedUtc == DateTime.MinValue)
        {
            return;
        }

        var mode = GetSelectedProtocolMode();
        if (mode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var sessionAge = now - _scanHealthSessionStartedUtc;
        if (sessionAge.TotalSeconds < 20)
        {
            return;
        }

        if (_scanHealthLastProcessRxUtc != DateTime.MinValue &&
            (now - _scanHealthLastProcessRxUtc).TotalSeconds > 30)
        {
            AddRateLimitedDiagnostic(
                "IEC10X-SCAN-PROCESS-STARVATION",
                "Warning",
                mode.ToString(),
                "No process value received recently",
                $"No process IOA update has been received for {(now - _scanHealthLastProcessRxUtc).TotalSeconds:0}s while the session is still running.",
                "Check link health, class polling, ASDU CA, and whether the RTU only sends data on GI/group interrogation or cyclic scan.",
                TimeSpan.FromSeconds(45));
        }

        if (_scanHealthLastClass2RxUtc != DateTime.MinValue &&
            (now - _scanHealthLastClass2RxUtc).TotalSeconds > 25)
        {
            AddRateLimitedDiagnostic(
                "IEC101-CLASS2-STARVATION",
                "Warning",
                "IEC-101",
                "Class 2/background scan appears stale",
                $"No Class 2 RX has been observed for {(now - _scanHealthLastClass2RxUtc).TotalSeconds:0}s.",
                "Verify class 2 request cadence, RTU response timeout, serial baudrate, and DFC/busy state.",
                TimeSpan.FromSeconds(45));
        }

        if (_scanHealthAcdSinceUtc != DateTime.MinValue &&
            (now - _scanHealthAcdSinceUtc).TotalSeconds > 15)
        {
            AddRateLimitedDiagnostic(
                "IEC101-ACD-STUCK-HIGH",
                "Warning",
                "IEC-101",
                "ACD remains high for a long period",
                $"ACD has been high for {(now - _scanHealthAcdSinceUtc).TotalSeconds:0}s. The outstation says Class 1 data is pending, but the pending condition is not clearing quickly.",
                "Drain Class 1 with bounded loops. If it stays high, check event queue load, link errors, or RTU class assignment.",
                TimeSpan.FromSeconds(30));
        }
    }

    private void AddRateLimitedDiagnostic(string code, string severity, string source, string message, string detail, string recommendation, TimeSpan interval)
    {
        var now = DateTime.UtcNow;
        if (_scanHealthLastDiagnosticUtcByCode.TryGetValue(code, out var last) && (now - last) < interval)
        {
            return;
        }

        _scanHealthLastDiagnosticUtcByCode[code] = now;
        AddUiDiagnostic(severity, source, code, message, detail, recommendation);
        AppendSessionLog($"{code}: {message}");
    }


    private void ObserveProtocolProof(Iec103MasterEvidenceEvent item)
    {
        if (item.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (item.CommonAddressNumber.HasValue && item.CommonAddressNumber.Value > 0 && _proofObservedCa < 0)
        {
            _proofObservedCa = item.CommonAddressNumber.Value;
            EmitProtocolProofMarker(
                "ARIEC-PROOF-CA-OBSERVED",
                "ASDU common address observed",
                $"First observed runtime ASDU CA={_proofObservedCa}. This separates link address from ASDU common address for IEC-101/104 proof.",
                "Use observed CA to validate GI/command addressing.");
        }

        var combined = string.Join(" ", item.State, item.Summary, item.Detail, item.OperatorMessage, item.ProtocolMeaning, item.CauseName, item.Cot, item.AsduType, item.TypeName);

        if (!_proofGiObserved && IsGeneralInterrogationActivity(item))
        {
            _proofGiObserved = true;
            _proofFirstGiUtc = now;
            EmitProtocolProofMarker(
                "ARIEC-PROOF-GI-SEEN",
                "General Interrogation activity observed",
                $"GI activity detected from {item.Direction} frame. COT={item.Cot ?? "-"}, CA={item.CommonAddressNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}, IOA={item.InformationObjectAddress?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}",
                "GI proof is stronger when followed by process values or ACTTERM.");
        }

        if (!_proofGiCompleted && ContainsAny(combined, "ACTTERM", "activation termination", "interrogation completed", "GI completed"))
        {
            _proofGiCompleted = true;
            EmitProtocolProofMarker(
                "ARIEC-PROOF-GI-COMPLETE",
                "General Interrogation completion observed",
                $"GI completion marker observed. COT={item.Cot ?? "-"}, Type={item.AsduType ?? item.TypeName ?? "-"}",
                "Compare expected vs received IOA list for completeness.");
        }

        if (!_proofGiNegative && ContainsAny(combined, "negative", "negative confirmation", "GI failed"))
        {
            _proofGiNegative = true;
            EmitProtocolProofMarker(
                "ARIEC-PROOF-GI-NEGATIVE",
                "General Interrogation negative confirmation observed",
                $"Negative confirmation observed around GI/control flow. CA={item.CommonAddressNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}",
                "Check ASDU CA, QOI, COT size, CA size, and whether the RTU accepts station/group GI.");
        }

        if (!_proofDigitalObserved && item.Direction == FrameDirection.SlaveToMaster && (item.IsRelayValue || item.InformationObjectAddress.HasValue) && IsIec10xDigitalType(item.TypeId))
        {
            _proofDigitalObserved = true;
            _proofFirstDigitalUtc = now;
            EmitProtocolProofMarker(
                "ARIEC-PROOF-DIGITAL-DATA",
                "Digital process data observed",
                $"First digital process value observed: TypeID={item.TypeId}, CA={item.CommonAddressNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}, IOA={item.InformationObjectAddress?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}, value={item.SignalDisplayValue ?? item.ObjectSummary ?? "-"}",
                "This proves SP/DP status path is alive.");
        }

        if (!_proofAnalogObserved && item.Direction == FrameDirection.SlaveToMaster && (item.IsRelayValue || item.InformationObjectAddress.HasValue) && IsAnalogMeasurementType(item.TypeId))
        {
            _proofAnalogObserved = true;
            _proofFirstAnalogUtc = now;
            EmitProtocolProofMarker(
                "ARIEC-PROOF-ANALOG-DATA",
                "Analog/process measurement observed",
                $"First analog measurement observed: TypeID={item.TypeId}, CA={item.CommonAddressNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}, IOA={item.InformationObjectAddress?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}, value={item.SignalDisplayValue ?? item.ObjectSummary ?? "-"}",
                "This proves measurement path is alive.");
        }

        if (!_proofCommandObserved && item.Direction == FrameDirection.MasterToSlave && IsIec10xCommandType(item.TypeId))
        {
            _proofCommandObserved = true;
            _proofFirstCommandUtc = now;
            EmitProtocolProofMarker(
                "ARIEC-PROOF-COMMAND-TX",
                "Command ASDU transmitted",
                $"Command TX observed: TypeID={item.TypeId}, CA={item.CommonAddressNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}, IOA={item.InformationObjectAddress?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}",
                "Command verdict requires ACTCON/ACTTERM and preferably mapped feedback IOA.");
        }
    }

    private void EmitProtocolProofMarker(string code, string message, string detail, string recommendation)
    {
        if (!_protocolProofMarkers.Add(code))
        {
            return;
        }

        AddUiDiagnostic("Info", "Protocol Proof", code, message, detail, recommendation);
        AppendSessionLog($"{code}: {message}");
    }


    private void EmitGiCoverageMatrixVerdict(string reason)
    {
        if (_ioaProfile.Points.Count == 0)
        {
            AddUiDiagnostic(
                "Info",
                "Protocol Proof",
                "ARIEC-PROOF-MAPPING-COVERAGE",
                "No IOA database loaded",
                $"{reason}. No Signal List / IOA database is available, so expected-vs-observed coverage cannot be calculated.",
                "Load the IOA database / Signal List to enable GI completeness matrix and command feedback mapping proof.");
            return;
        }

        var monitorPoints = _ioaProfile.Points
            .Where(IsMonitorPoint)
            .GroupBy(x => BuildIoaValueKey(x.Ioa), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();

        var commandPoints = _ioaProfile.Points
            .Where(IsCommandPoint)
            .ToArray();

        var receivedKeys = new HashSet<string>(_giReceivedValueKeys, StringComparer.OrdinalIgnoreCase);
        if (receivedKeys.Count == 0 && _valueRowsByKey.Count > 0)
        {
            foreach (var key in _valueRowsByKey.Keys)
            {
                receivedKeys.Add(key);
            }
        }

        var missing = monitorPoints
            .Where(point => !receivedKeys.Contains(BuildIoaValueKey(point.Ioa)))
            .ToArray();

        var digitalExpected = monitorPoints.Count(point => IsIec10xDigitalType(point.TypeId));
        var digitalReceived = monitorPoints.Count(point => IsIec10xDigitalType(point.TypeId) && receivedKeys.Contains(BuildIoaValueKey(point.Ioa)));
        var analogExpected = monitorPoints.Count(point => IsAnalogMeasurementType(point.TypeId));
        var analogReceived = monitorPoints.Count(point => IsAnalogMeasurementType(point.TypeId) && receivedKeys.Contains(BuildIoaValueKey(point.Ioa)));
        var otherExpected = Math.Max(0, monitorPoints.Length - digitalExpected - analogExpected);
        var otherReceived = monitorPoints.Count(point => !IsIec10xDigitalType(point.TypeId) && !IsAnalogMeasurementType(point.TypeId) && receivedKeys.Contains(BuildIoaValueKey(point.Ioa)));

        _lastMonitorExpectedCount = monitorPoints.Length;
        _lastMonitorReceivedCount = Math.Max(0, monitorPoints.Length - missing.Length);
        _lastDigitalExpectedCount = digitalExpected;
        _lastDigitalReceivedCount = digitalReceived;
        _lastAnalogExpectedCount = analogExpected;
        _lastAnalogReceivedCount = analogReceived;
        _lastOtherExpectedCount = otherExpected;
        _lastOtherReceivedCount = otherReceived;
        _lastCommandExpectedCount = commandPoints.Length;
        _lastFeedbackMappedCommandCount = commandPoints.Count(point => point.FeedbackIoa.HasValue);
        _lastMissingMonitorCount = missing.Length;
        _lastMissingMonitorPreview = missing.Length == 0
            ? "-"
            : string.Join("; ", missing.Take(12).Select(FormatIoaPointForProof));

        var percent = monitorPoints.Length > 0
            ? (_lastMonitorReceivedCount * 100.0 / monitorPoints.Length)
            : 0.0;

        AddUiDiagnostic(
            missing.Length == 0 ? "Info" : "Warning",
            "Protocol Proof",
            missing.Length == 0 ? "ARIEC-PROOF-GI-COMPLETENESS-PASS" : "ARIEC-PROOF-GI-COMPLETENESS-RISK",
            missing.Length == 0 ? "GI / scan coverage complete for mapped monitor points" : "GI / scan coverage has missing mapped monitor points",
            $"{reason}. Monitor coverage={_lastMonitorReceivedCount}/{_lastMonitorExpectedCount} ({percent:0.0}%). Missing={missing.Length}. Missing preview={_lastMissingMonitorPreview}.",
            missing.Length == 0
                ? "Mapped monitor points have been observed in the runtime value store."
                : "Check ASDU CA, GI support, group interrogation support, class assignment, IOA mapping correctness, and whether the RTU only sends some points on change.");

        AddUiDiagnostic(
            digitalReceived == digitalExpected ? "Info" : "Warning",
            "Protocol Proof",
            digitalReceived == digitalExpected ? "ARIEC-PROOF-DIGITAL-COVERAGE-PASS" : "ARIEC-PROOF-DIGITAL-COVERAGE-RISK",
            "Digital SP/DP coverage proof",
            $"Digital monitor coverage={digitalReceived}/{digitalExpected}.",
            digitalReceived == digitalExpected
                ? "All mapped digital monitor points have been observed."
                : "Digital points are expected but not all have been observed. Verify GI/group GI and digital class assignment.");

        AddUiDiagnostic(
            analogExpected == 0 || analogReceived == analogExpected ? "Info" : "Warning",
            "Protocol Proof",
            analogExpected == 0 || analogReceived == analogExpected ? "ARIEC-PROOF-ANALOG-COVERAGE-PASS" : "ARIEC-PROOF-ANALOG-COVERAGE-RISK",
            "Analog measurement coverage proof",
            $"Analog monitor coverage={analogReceived}/{analogExpected}.",
            analogExpected == 0
                ? "No mapped analog monitor points are expected in the current database."
                : analogReceived == analogExpected
                    ? "All mapped analog monitor points have been observed."
                    : "Analog points are expected but not all have been observed. Verify cyclic scan, class 2 polling, and IOA mapping.");

        AddUiDiagnostic(
            _lastFeedbackMappedCommandCount == _lastCommandExpectedCount ? "Info" : "Warning",
            "Protocol Proof",
            _lastFeedbackMappedCommandCount == _lastCommandExpectedCount ? "ARIEC-PROOF-COMMAND-MAPPING-PASS" : "ARIEC-PROOF-COMMAND-MAPPING-RISK",
            "Command feedback mapping coverage",
            $"Command points={_lastCommandExpectedCount}, feedback mapped={_lastFeedbackMappedCommandCount}.",
            _lastFeedbackMappedCommandCount == _lastCommandExpectedCount
                ? "All command points have feedback IOA mapping."
                : "Some command points have no feedback IOA. Command validator can check ACTCON/ACTTERM, but physical/process feedback proof will be limited.");
    }

    private static string FormatIoaPointForProof(Iec10xPointMappingEntry point)
    {
        var name = string.IsNullOrWhiteSpace(point.Name) ? $"IOA {point.Ioa}" : point.Name;
        var type = point.TypeId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";
        var ca = point.Ca?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*";
        return $"{name} (CA {ca}, IOA {point.Ioa}, T{type})";
    }

    private void EmitSessionProofVerdict(string reason)
    {
        var mode = GetSelectedProtocolMode();
        if (mode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104))
        {
            return;
        }

        var expected = _giExpectedValueKeys.Count;
        var received = _giReceivedValueKeys.Count;
        var completeness = expected > 0 ? (received * 100.0 / expected) : 0.0;
        var traceMode = GetTraceVerbosityMode();
        var criticalProofs = new List<string>();
        var risks = new List<string>();

        if (_proofObservedCa > 0) criticalProofs.Add($"CA observed={_proofObservedCa}");
        else risks.Add("No ASDU CA observed");

        if (_proofGiObserved) criticalProofs.Add("GI activity observed");
        else risks.Add("No GI activity observed");

        if (_proofGiCompleted) criticalProofs.Add("GI completion observed");
        if (_proofGiNegative) risks.Add("GI/control negative confirmation observed");

        if (_proofDigitalObserved) criticalProofs.Add("Digital SP/DP data observed");
        else risks.Add("No digital SP/DP data observed yet");

        if (_proofAnalogObserved) criticalProofs.Add("Analog measurement data observed");
        if (_proofCommandObserved) criticalProofs.Add("Command TX observed");
        if (_proofCommandFeedbackObserved) criticalProofs.Add("Command feedback observed");

        if (_backpressureDroppedEvents > 0 || _traceVerbositySuppressedRows > 0)
        {
            criticalProofs.Add($"Retention declared: traceMode={traceMode}, traceSkip={_traceVerbositySuppressedRows}, lowValueDropped={_backpressureDroppedEvents}");
        }

        if (_maxUiFlushMs >= UiFlushSlowWarningMs)
        {
            risks.Add($"UI slow flush observed max={_maxUiFlushMs}ms");
        }

        var severity = risks.Count == 0 || (_proofDigitalObserved && (_proofGiObserved || _proofAnalogObserved))
            ? "Info"
            : "Warning";

        var verdict = severity == "Info" ? "Protocol proof acceptable" : "Protocol proof has open risks";
        AddUiDiagnostic(
            severity,
            "Protocol Proof",
            "ARIEC-PROOF-SESSION-VERDICT",
            verdict,
            $"{reason}. Proofs: {(criticalProofs.Count == 0 ? "-" : string.Join("; ", criticalProofs))}. GI completeness={received}/{expected} ({completeness:0.0}%). Risks: {(risks.Count == 0 ? "-" : string.Join("; ", risks))}.",
            "Use this verdict as the top-level commissioning proof summary, then inspect Evidence Summary, Value Viewer, Event Log, Diagnostics, and export retention policy for detail.");
    }

    private void ObserveCommandBehaviour(Iec103MasterEvidenceEvent item)
    {
        if (item.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104))
        {
            return;
        }

        if (item.Direction == FrameDirection.MasterToSlave && IsIec10xCommandType(item.TypeId) && item.InformationObjectAddress.HasValue)
        {
            RegisterPendingCommand(item);
            return;
        }

        if (item.Direction != FrameDirection.SlaveToMaster)
        {
            return;
        }

        if (IsIec10xCommandType(item.TypeId) && item.InformationObjectAddress.HasValue)
        {
            ObserveCommandAsduResponse(item);
            return;
        }

        if (item.IsRelayValue || item.InformationObjectAddress.HasValue)
        {
            ObserveCommandFeedback(item);
        }
    }

    private void RegisterPendingCommand(Iec103MasterEvidenceEvent item)
    {
        var key = BuildCommandLedgerKey(item.CommonAddressNumber, item.InformationObjectAddress, item.TypeId);
        if (string.IsNullOrWhiteSpace(key) || !item.InformationObjectAddress.HasValue)
        {
            return;
        }

        var feedbackIoa = ResolveFeedbackIoaForCommand(item);
        _commandLedgerByKey[key] = new CommandLedgerEntry
        {
            Key = key,
            CommonAddress = item.CommonAddressNumber,
            CommandIoa = item.InformationObjectAddress.Value,
            CommandTypeId = item.TypeId,
            FeedbackIoa = feedbackIoa,
            Summary = string.IsNullOrWhiteSpace(item.Summary) ? $"Command IOA {item.InformationObjectAddress.Value}" : item.Summary,
            Stage = "TX command",
            StartedUtc = DateTime.UtcNow,
            LastUpdateUtc = DateTime.UtcNow
        };

        AddRateLimitedDiagnostic(
            "IEC10X-COMMAND-TX",
            "Info",
            item.ProtocolMode.ToString(),
            "Command issued and ledger started",
            $"{item.Summary}. Feedback IOA={(feedbackIoa.HasValue ? feedbackIoa.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "not mapped")}.",
            "The validator will look for ACTCON/ACTTERM and feedback process value within the command window.",
            TimeSpan.FromSeconds(1));
    }

    private void ObserveCommandAsduResponse(Iec103MasterEvidenceEvent item)
    {
        var key = BuildCommandLedgerKey(item.CommonAddressNumber, item.InformationObjectAddress, item.TypeId);
        if (string.IsNullOrWhiteSpace(key) || !_commandLedgerByKey.TryGetValue(key, out var ledger))
        {
            return;
        }

        ledger.LastUpdateUtc = DateTime.UtcNow;
        var text = string.Join(" ", item.CauseName, item.Summary, item.Detail, item.OperatorMessage, item.ProtocolMeaning);
        if (text.Contains("NEG", StringComparison.OrdinalIgnoreCase) || text.Contains("negative", StringComparison.OrdinalIgnoreCase))
        {
            ledger.NegativeSeen = true;
            _commandLedgerByKey.Remove(key);
            AddUiDiagnostic(
                "Warning",
                item.ProtocolMode.ToString(),
                "IEC10X-COMMAND-NEGATIVE-CONFIRMATION",
                "Command negatively confirmed",
                $"{ledger.Summary} was negatively confirmed by the outstation. COT={item.CauseName}.",
                "Check SBO state, command qualifier, command IOA/type, interlock condition, CA, and whether operate was sent after select timeout.");
            AppendSessionLog($"Command validator: negative confirmation for {ledger.Summary}.");
            return;
        }

        if (item.CauseOfTransmission == 7 || text.Contains("ACTCON", StringComparison.OrdinalIgnoreCase) || text.Contains("activation confirmation", StringComparison.OrdinalIgnoreCase))
        {
            ledger.ActConSeen = true;
            ledger.Stage = "ACTCON";
            AddRateLimitedDiagnostic(
                "IEC10X-COMMAND-ACTCON",
                "Info",
                item.ProtocolMode.ToString(),
                "Command activation confirmed",
                $"{ledger.Summary} received activation confirmation.",
                "Continue watching for ACTTERM and feedback IOA.",
                TimeSpan.FromSeconds(1));
        }

        if (item.CauseOfTransmission == 10 || text.Contains("ACTTERM", StringComparison.OrdinalIgnoreCase) || text.Contains("activation termination", StringComparison.OrdinalIgnoreCase))
        {
            ledger.ActTermSeen = true;
            ledger.Stage = "ACTTERM";
            AddRateLimitedDiagnostic(
                "IEC10X-COMMAND-ACTTERM",
                "Info",
                item.ProtocolMode.ToString(),
                "Command activation terminated",
                $"{ledger.Summary} received activation termination.",
                "Command execution path is complete; feedback IOA remains the final process proof when mapped.",
                TimeSpan.FromSeconds(1));

            if (!ledger.FeedbackIoa.HasValue)
            {
                _commandLedgerByKey.Remove(key);
            }
        }
    }

    private void ObserveCommandFeedback(Iec103MasterEvidenceEvent item)
    {
        if (!item.InformationObjectAddress.HasValue)
        {
            return;
        }

        var feedbackIoa = item.InformationObjectAddress.Value;
        foreach (var ledger in _commandLedgerByKey.Values.ToArray())
        {
            if (ledger.FeedbackIoa != feedbackIoa)
            {
                continue;
            }

            ledger.FeedbackSeen = true;
            ledger.LastUpdateUtc = DateTime.UtcNow;
            _proofCommandFeedbackObserved = true;
            _proofFirstCommandFeedbackUtc = DateTime.UtcNow;
            _commandLedgerByKey.Remove(ledger.Key);
            AddUiDiagnostic(
                "Info",
                item.ProtocolMode.ToString(),
                "IEC10X-COMMAND-FEEDBACK-PROVEN",
                "Command feedback proven by process value",
                $"{ledger.Summary} feedback IOA {feedbackIoa} updated to '{item.SignalDisplayValue}'. ACTCON={ledger.ActConSeen}, ACTTERM={ledger.ActTermSeen}.",
                "This is the strongest command evidence: command path plus real process feedback.");
            AppendSessionLog($"Command validator: feedback proven for {ledger.Summary} via IOA {feedbackIoa}.");
            break;
        }
    }

    private void EvaluateCommandLedgerTimeouts()
    {
        if (_commandLedgerByKey.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var ledger in _commandLedgerByKey.Values.ToArray())
        {
            if (ledger.TimeoutReported || (now - ledger.StartedUtc).TotalSeconds < 8)
            {
                continue;
            }

            ledger.TimeoutReported = true;
            _commandLedgerByKey.Remove(ledger.Key);
            AddUiDiagnostic(
                "Warning",
                "IEC-101/104",
                "IEC10X-COMMAND-VERDICT-TIMEOUT",
                "Command verdict timed out",
                $"{ledger.Summary} did not receive complete command proof within 8 seconds. ACTCON={ledger.ActConSeen}, ACTTERM={ledger.ActTermSeen}, feedback={ledger.FeedbackSeen}.",
                "Check command mapping, feedback IOA, select/operate sequence, interlock, CA, and RTU command timeout settings.");
            AppendSessionLog($"Command validator: timeout for {ledger.Summary}.");
        }
    }

    private static string BuildCommandLedgerKey(int? commonAddress, int? ioa, int? typeId)
    {
        if (!ioa.HasValue)
        {
            return string.Empty;
        }

        return $"CA{(commonAddress?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*")}|IOA{ioa.Value}|T{(typeId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*")}";
    }

    private int? ResolveFeedbackIoaForCommand(Iec103MasterEvidenceEvent item)
    {
        if (!item.InformationObjectAddress.HasValue)
        {
            return null;
        }

        var point = _ioaProfile.Points.FirstOrDefault(x =>
            x.Ioa == item.InformationObjectAddress.Value &&
            (!x.TypeId.HasValue || !item.TypeId.HasValue || x.TypeId.Value == item.TypeId.Value) &&
            (!x.Ca.HasValue || !item.CommonAddressNumber.HasValue || x.Ca.Value == item.CommonAddressNumber.Value));

        return point?.FeedbackIoa;
    }

    private static bool IsIec10xCommandType(int? typeId)
        => typeId is 45 or 46 or 47 or 48 or 49 or 50 or 51;

    private void ReportRuntimeCommonAddressMismatch(Iec103MasterEvidenceEvent item)
    {
        if (_runtimeCaMismatchReported || item.ProtocolMode != Iec60870ProtocolMode.Iec101 || !item.CommonAddressNumber.HasValue)
        {
            return;
        }

        var observedCa = item.CommonAddressNumber.Value;
        if (observedCa <= 0)
        {
            return;
        }

        _firstObservedRuntimeCa ??= observedCa;
        if (!int.TryParse(CommonAddressBox.Text, out var configuredCa) || configuredCa == observedCa)
        {
            return;
        }

        // Wait until an actual process value, not a command echo/noise, to avoid false warnings.
        if (!item.IsRelayValue && item.TypeId is not (1 or 2 or 3 or 4 or 9 or 10 or 11 or 12 or 13 or 14 or 30 or 31 or 34 or 35 or 36))
        {
            return;
        }

        _runtimeCaMismatchReported = true;
        AddUiDiagnostic(
            "Warning",
            "IEC-101",
            "IEC101-RUNTIME-CA-MISMATCH",
            "Runtime ASDU common address differs from setup/profile",
            $"Live process data is arriving with CA={observedCa}, but setup/profile uses CA={configuredCa}. Station GI sent to the wrong CA can be negatively confirmed and may prevent SPS/DPS snapshots from arriving.",
            "Use the observed CA for GI/test runs, or keep auto CA-learning retry enabled. The Value Viewer still maps values by IOA where possible.");
        AppendSessionLog($"Runtime CA mismatch: live ASDU CA={observedCa}, configured CA={configuredCa}. Auto CA-learning in IEC-101 session will retry GI using observed CA.");
    }

    private void SeedValueViewerFromIoaProfile(Iec60870ProtocolMode protocolMode)
    {
        _giExpectedValueKeys.Clear();
        _giReceivedValueKeys.Clear();
        _giClass2CollectionWindowActive = false;
        _giClass2CollectionUntilUtc = DateTime.MinValue;
        _firstObservedRuntimeCa = null;
        _runtimeCaMismatchReported = false;
        _giCompletenessReported = false;
        _giCompletenessWatchActive = protocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104
            && _ioaProfile.HasPoints;

        if (!_giCompletenessWatchActive)
        {
            return;
        }

        var ordered = _ioaProfile.Points
            .Where(point => IsMonitorPoint(point))
            .OrderBy(point => point.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.Ioa)
            .ThenBy(point => point.TypeId ?? 0)
            .ToList();

        foreach (var point in ordered)
        {
            var key = BuildIoaValueKey(point.Ioa);
            _giExpectedValueKeys.Add(key);
            UpsertValueRowStable(new ValueRow(new Iec103ValuePoint
            {
                Key = key,
                IsMapped = true,
                SignalName = point.Name,
                SignalGroup = string.IsNullOrWhiteSpace(point.Group) ? "Profile" : point.Group,
                SignalType = string.IsNullOrWhiteSpace(point.SignalType) ? $"Type {point.TypeId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}" : point.SignalType,
                DisplayValue = "waiting for GI / scan",
                RawValue = string.Empty,
                CauseOfTransmission = "profile expected",
                AsduType = string.IsNullOrWhiteSpace(point.SignalType) ? string.Empty : point.SignalType,
                RelayTimeText = "not received",
                ArrivalTimeUtc = DateTime.UtcNow,
                ProtocolMode = protocolMode,
                CommonAddress = point.Ca ?? _ioaProfile.CommonAddress,
                InformationObjectAddress = point.Ioa,
                TypeId = point.TypeId,
                QualityText = "not received"
            }));
        }

        if (ordered.Count > 0)
        {
            AppendSessionLog($"Value Viewer seeded with {ordered.Count} expected IOA points from {_ioaProfile.ProfileName}. Missing GI values stay visible as 'waiting for GI / scan'.");
        }
    }

    private static bool IsMonitorPoint(Iec10xPointMappingEntry point)
    {
        return !IsCommandPoint(point);
    }

    private static bool IsCommandPoint(Iec10xPointMappingEntry point)
    {
        if (point.TypeId is 45 or 46 or 47 or 48 or 49 or 50 or 51)
        {
            return true;
        }

        var policy = point.CommandPolicy ?? string.Empty;
        return policy.Contains("Command", StringComparison.OrdinalIgnoreCase)
               || policy.Contains("RemoteOnly", StringComparison.OrdinalIgnoreCase)
               || policy.Contains("Control", StringComparison.OrdinalIgnoreCase)
               || policy.Contains("Setpoint", StringComparison.OrdinalIgnoreCase)
               || policy.Contains("Regulating", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildIoaValueKey(int ioa)
        => "IOA:" + ioa.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void MarkGiValueReceived(string key)
    {
        if (_giCompletenessWatchActive && _giExpectedValueKeys.Contains(key))
        {
            _giReceivedValueKeys.Add(key);
        }
    }

    private void ReportGiCompletenessIfReady(Iec103MasterEvidenceEvent item)
    {
        if (!_giCompletenessWatchActive || _giCompletenessReported || _giExpectedValueKeys.Count == 0)
        {
            return;
        }

        if (TryFinishGiCompletenessIfComplete())
        {
            return;
        }

        var text = string.Join(" ", item.Category, item.Summary, item.Detail, item.CauseName, item.ProtocolMeaning, item.OperatorMessage);
        var isGiNegativeConfirmation =
            item.TypeId == 100 &&
            (item.CauseName.Contains("NEG", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("negative", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("NEG activation", StringComparison.OrdinalIgnoreCase));

        if (isGiNegativeConfirmation)
        {
            AddUiDiagnostic(
                "Warning",
                "IEC-101",
                "IEC101-GI-NEGATIVE-CONFIRMATION",
                "GI negative confirmation observed; value scan continues",
                "The outstation negatively confirmed C_IC_NA_1. This is recorded as protocol evidence, but it does not overwrite seeded IOA rows. Values are still collected from subsequent Class 1/Class 2/background frames.",
                "Check GI qualifier/CA/profile if GI is required by the test case. For live monitoring, treat actual received IOA frames as the source of truth.");
            AppendSessionLog("GI note: NEGATIVE CONFIRMATION observed. Keeping Value Viewer neutral; continuing scan.");
            StartGiClass2CollectionWindow("GI negative confirmation; continue scan-tolerant Class 1/Class 2 collection");
            return;
        }

        var isActTerm = item.TypeId == 100 &&
                        (item.CauseOfTransmission == 10 ||
                         text.Contains("ACTTERM", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("activation termination", StringComparison.OrdinalIgnoreCase));

        var class1NoData = item.ProtocolMode == Iec60870ProtocolMode.Iec101 &&
                           item.DataClass.Equals("Class 1", StringComparison.OrdinalIgnoreCase) &&
                           text.Contains("NO DATA", StringComparison.OrdinalIgnoreCase);

        if ((isActTerm || class1NoData) && !_giClass2CollectionWindowActive)
        {
            StartGiClass2CollectionWindow(isActTerm ? "ACTTERM observed" : "Class 1 returned NO DATA");
        }
    }

    private void EvaluateGiCollectionWindow()
    {
        if (!_giCompletenessWatchActive || _giCompletenessReported)
        {
            return;
        }

        if (TryFinishGiCompletenessIfComplete())
        {
            return;
        }

        if (!_giClass2CollectionWindowActive || DateTime.UtcNow < _giClass2CollectionUntilUtc)
        {
            return;
        }

        _giClass2CollectionWindowActive = false;
        _giCompletenessReported = true;
        var missing = _giExpectedValueKeys.Except(_giReceivedValueKeys, StringComparer.OrdinalIgnoreCase).ToArray();
        var sample = string.Join(", ", missing.Take(12).Select(x => x.Replace("IOA:", "IOA ")));
        AddUiDiagnostic(
            "Warning",
            "IEC-101",
            "IEC101-SCAN-PROFILE-PENDING",
            "Profile points still pending after GI/group/Class 2 observation window",
            $"Received {_giReceivedValueKeys.Count}/{_giExpectedValueKeys.Count} expected profile points during the GI/group/Class 2 observation window. Pending sample: {sample}",
            "This is a non-destructive scan note. Value Viewer rows stay in waiting state until actual Class 1/Class 2 frames arrive. Verify RTU profile only if the test case requires every IOA to be returned in this window.");
        AppendSessionLog($"Scan observation note: pending {missing.Length}/{_giExpectedValueKeys.Count} profile points after GI/group/Class 2 window. Sample: {sample}");
    }

    private bool TryFinishGiCompletenessIfComplete()
    {
        if (_giExpectedValueKeys.Count == 0)
        {
            return false;
        }

        var missing = _giExpectedValueKeys.Except(_giReceivedValueKeys, StringComparer.OrdinalIgnoreCase).Any();
        if (missing)
        {
            return false;
        }

        _giCompletenessReported = true;
        _giClass2CollectionWindowActive = false;
        AppendSessionLog($"GI/Class 2 completeness: PASS. Received {_giReceivedValueKeys.Count}/{_giExpectedValueKeys.Count} expected profile points.");
        return true;
    }

    private void StartGiClass2CollectionWindow(string reason)
    {
        var window = CalculateGiClass2CollectionWindow();
        _giClass2CollectionWindowActive = true;
        _giClass2CollectionUntilUtc = DateTime.UtcNow.Add(window);

        var isNegativeFallback = reason.Contains("negative", StringComparison.OrdinalIgnoreCase);

        AddUiDiagnostic(
            "Info",
            "IEC-101",
            "IEC101-GI-CLASS2-COLLECTION",
            isNegativeFallback ? "GI negative confirmation observed; continuing normal scan" : "GI moved to Class 2/background collection window",
            $"{reason}. Value Viewer placeholders are kept neutral; only actual Class 1/Class 2 frames are allowed to update IOA values. Waiting {Math.Ceiling(window.TotalSeconds):0}s before reporting a non-destructive completeness note.",
            "SCADA master behaviour: GI is a collection trigger, not a reason to mark profile IOAs as failed. Continue bounded Class 1 drain and Class 2/background polling; do not mass-read or overwrite placeholders.");
        AppendSessionLog($"GI/Class2 collection: {reason}; neutral background collection window ≈{Math.Ceiling(window.TotalSeconds):0}s.");
    }

    private TimeSpan CalculateGiClass2CollectionWindow()
    {
        var intervalMs = int.TryParse(Class2IntervalBox.Text, out var configured) ? Math.Max(configured, 500) : 1000;
        var estimatedSeconds = Math.Ceiling(Math.Max(20, _giExpectedValueKeys.Count * intervalMs / 1000.0 * 2.5));
        return TimeSpan.FromSeconds(Math.Clamp((int)estimatedSeconds, 20, 120));
    }

    private void MarkMissingProfileRows(string displayValue, string cot, string quality)
    {
        foreach (var key in _giExpectedValueKeys.Except(_giReceivedValueKeys, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            var ioa = ParseIoaFromValueKey(key);
            if (ioa < 0)
            {
                continue;
            }

            var point = _ioaProfile.Points.FirstOrDefault(x => x.Ioa == ioa);
            UpsertValueRowStable(new ValueRow(new Iec103ValuePoint
            {
                Key = key,
                IsMapped = true,
                SignalName = point?.Name ?? $"IOA {ioa}",
                SignalGroup = string.IsNullOrWhiteSpace(point?.Group) ? "Profile" : point!.Group,
                SignalType = string.IsNullOrWhiteSpace(point?.SignalType) ? $"Type {point?.TypeId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}" : point!.SignalType,
                DisplayValue = displayValue,
                RawValue = string.Empty,
                CauseOfTransmission = cot,
                AsduType = string.IsNullOrWhiteSpace(point?.SignalType) ? string.Empty : point!.SignalType,
                RelayTimeText = "not received",
                ArrivalTimeUtc = DateTime.UtcNow,
                ProtocolMode = GetSelectedProtocolMode(),
                CommonAddress = point?.Ca ?? _ioaProfile.CommonAddress,
                InformationObjectAddress = ioa,
                TypeId = point?.TypeId,
                QualityText = quality
            }));
        }
    }

    private static int ParseIoaFromValueKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return -1;
        }

        var normalized = key.StartsWith("IOA:", StringComparison.OrdinalIgnoreCase) ? key[4..] : key;
        return int.TryParse(normalized, out var ioa) ? ioa : -1;
    }

    private void ApplyIoaProfileDefaultsToUi(Iec10xPointMappingProfile profile, bool onlyWhenUiLooksDefault)
    {
        var defaults = profile.DefaultSettings;
        if (defaults is null)
        {
            return;
        }

        var uiLooksUntouched = string.IsNullOrWhiteSpace(CommonAddressBox.Text) || CommonAddressBox.Text.Trim() == "1";
        if (onlyWhenUiLooksDefault && !uiLooksUntouched)
        {
            return;
        }

        if (defaults.BaudRate.HasValue)
        {
            SetEditableComboText(BaudComboBox, defaults.BaudRate.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrWhiteSpace(defaults.SerialMode))
        {
            SelectComboContent(SerialModeComboBox, defaults.SerialMode);
        }
        if (defaults.LinkAddress.HasValue)
        {
            LinkAddressBox.Text = defaults.LinkAddress.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (defaults.CommonAddress.HasValue)
        {
            CommonAddressBox.Text = defaults.CommonAddress.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            CommandCaBox.Text = CommonAddressBox.Text;
        }
        if (defaults.LinkAddressSize.HasValue)
        {
            SelectComboContent(LinkAddressSizeComboBox, Math.Clamp(defaults.LinkAddressSize.Value, 0, 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (defaults.CauseOfTransmissionSize.HasValue)
        {
            SelectComboContent(CotSizeComboBox, Math.Clamp(defaults.CauseOfTransmissionSize.Value, 1, 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (defaults.CommonAddressSize.HasValue)
        {
            SelectComboContent(CaSizeComboBox, Math.Clamp(defaults.CommonAddressSize.Value, 1, 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (defaults.InformationObjectAddressSize.HasValue)
        {
            SelectComboContent(IoaSizeComboBox, Math.Clamp(defaults.InformationObjectAddressSize.Value, 1, 3).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrWhiteSpace(defaults.TransmissionMode))
        {
            SelectComboContent(TransmissionModeComboBox, defaults.TransmissionMode);
        }
        if (!string.IsNullOrWhiteSpace(defaults.TcpHost))
        {
            TcpHostBox.Text = defaults.TcpHost;
        }
        if (defaults.TcpPort.HasValue)
        {
            TcpPortBox.Text = defaults.TcpPort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        _defaultIoaSeedSettingsApplied = true;
        AppendSessionLog($"IOA profile defaults applied: CA={CommonAddressBox.Text}, COT size={CotSizeComboBox.Text}, CA size={CaSizeComboBox.Text}, IOA size={IoaSizeComboBox.Text}, serial={BaudComboBox.Text} {SerialModeComboBox.Text}.");
    }

    private void LoadSetupPreferences()
    {
        try
        {
            var path = SetupPreferencesPath;
            if (!File.Exists(path))
            {
                return;
            }

            var prefs = JsonSerializer.Deserialize<SetupPreferences>(File.ReadAllText(path, Encoding.UTF8));
            if (prefs is null)
            {
                return;
            }

            _savedSetupPreferencesLoaded = true;
            _isApplyingSavedSetup = true;
            SelectProtocolMode(prefs.ProtocolMode);
            SelectComboContent(TransportModeComboBox, prefs.UseSimulatedSlave ? "Built-in demo simulation" : "Real device / server");

            if (!string.IsNullOrWhiteSpace(prefs.PortName))
            {
                EnsureComboItem(PortComboBox, prefs.PortName);
                PortComboBox.SelectedItem = prefs.PortName;
            }

            SetEditableComboText(BaudComboBox, prefs.BaudRate.ToString());
            SelectComboContent(SerialModeComboBox, string.IsNullOrWhiteSpace(prefs.SerialMode) ? "8E1" : prefs.SerialMode);
            TcpHostBox.Text = string.IsNullOrWhiteSpace(prefs.TcpHost) ? "127.0.0.1" : prefs.TcpHost;
            TcpPortBox.Text = prefs.TcpPort <= 0 ? "2404" : prefs.TcpPort.ToString();
            LinkAddressBox.Text = prefs.LinkAddress.ToString();
            CommonAddressBox.Text = prefs.CommonAddress.ToString();
            CommandCaBox.Text = prefs.CommonAddress.ToString();
            SelectComboContent(LinkAddressSizeComboBox, Math.Clamp(prefs.LinkAddressSize, 0, 2).ToString());
            SelectComboContent(CotSizeComboBox, Math.Clamp(prefs.CauseOfTransmissionSize, 1, 2).ToString());
            SelectComboContent(CaSizeComboBox, Math.Clamp(prefs.CommonAddressSize, 1, 2).ToString());
            SelectComboContent(IoaSizeComboBox, Math.Clamp(prefs.InformationObjectAddressSize, 1, 3).ToString());
            SelectComboContent(TransmissionModeComboBox, prefs.TransmissionMode?.StartsWith("Balanced", StringComparison.OrdinalIgnoreCase) == true ? "Unbalanced" : "Unbalanced");

            Class2IntervalBox.Text = prefs.Class2PollIntervalMs > 0 ? prefs.Class2PollIntervalMs.ToString() : "500";
            MaxDrainBox.Text = prefs.MaxClass1DrainFrames > 0 ? prefs.MaxClass1DrainFrames.ToString() : "64";
            Iec104T0Box.Text = prefs.Iec104T0TimeoutMs > 0 ? prefs.Iec104T0TimeoutMs.ToString() : "30000";
            Iec104T1Box.Text = prefs.Iec104T1AckTimeoutMs > 0 ? prefs.Iec104T1AckTimeoutMs.ToString() : "15000";
            Iec104T2Box.Text = prefs.Iec104T2AckDelayMs > 0 ? prefs.Iec104T2AckDelayMs.ToString() : "10000";
            Iec104T3Box.Text = prefs.Iec104T3TestIntervalMs > 0 ? prefs.Iec104T3TestIntervalMs.ToString() : "20000";
            Iec104KBox.Text = prefs.Iec104KMaxUnacknowledged > 0 ? prefs.Iec104KMaxUnacknowledged.ToString() : "12";
            Iec104WBox.Text = prefs.Iec104WReceiveWindow > 0 ? prefs.Iec104WReceiveWindow.ToString() : "8";
            TimeoutBox.Text = prefs.ResponseTimeoutMs > 0 ? prefs.ResponseTimeoutMs.ToString() : "1500";
            DurationBox.Text = prefs.DurationSeconds >= 0 ? prefs.DurationSeconds.ToString() : "0";
            ResetRemoteLinkCheckBox.IsChecked = prefs.ResetRemoteLinkOnConnect;
            ResetFcbCheckBox.IsChecked = prefs.ResetFcbOnConnect;
            Class2StartupCheckBox.IsChecked = prefs.RequestClass2ImmediatelyAfterStartup;
            ClockSyncCheckBox.IsChecked = prefs.SendClockSyncOnConnect;
            GiCheckBox.IsChecked = prefs.SendGeneralInterrogationOnConnect;
            MappingProfilePathBox.Text = prefs.MappingProfilePath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(MappingProfilePathBox.Text) && File.Exists(MappingProfilePathBox.Text))
            {
                TryLoadMappingProfile(MappingProfilePathBox.Text, showMessage: false);
            }
            _commandDockExpanded = prefs.CommandDockExpanded;
            ApplyCommandDockLayout();
        }
        catch (Exception ex)
        {
            AddUiDiagnostic("Warning", "Setup", "IEC60870-SETUP-PREF-LOAD", "Saved setup could not be loaded", ex.Message, "The app will continue with default setup. Re-enter the settings once and they will be saved again.", ex);
        }
        finally
        {
            _isApplyingSavedSetup = false;
        }
    }

    private void SaveSetupPreferencesFromUi(bool silent)
    {
        if (_isApplyingSavedSetup)
        {
            return;
        }

        try
        {
            var settings = BuildSettingsFromUi();
            var duration = ReadInt(DurationBox, "Session timeout", 0, 86400);
            SaveSetupPreferences(settings, duration, silent);
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                MessageBox.Show(this, ex.Message, "Could not save setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void SaveSetupPreferences(Iec103MasterSettings settings, int durationSeconds, bool silent)
    {
        try
        {
            var prefs = new SetupPreferences
            {
                ProtocolMode = settings.ProtocolMode.ToString(),
                UseSimulatedSlave = settings.UseSimulatedSlave,
                PortName = settings.PortName,
                BaudRate = settings.BaudRate,
                SerialMode = (SerialModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? SerialModeComboBox.Text,
                TcpHost = settings.TcpHost,
                TcpPort = settings.TcpPort,
                LinkAddress = settings.LinkAddress,
                CommonAddress = settings.CommonAddress,
                LinkAddressSize = settings.LinkAddressSize,
                CauseOfTransmissionSize = settings.CauseOfTransmissionSize,
                CommonAddressSize = settings.CommonAddressSize,
                InformationObjectAddressSize = settings.InformationObjectAddressSize,
                TransmissionMode = settings.TransmissionMode,
                Iec104T0TimeoutMs = settings.Iec104T0TimeoutMs,
                Iec104T1AckTimeoutMs = settings.Iec104T1AckTimeoutMs,
                Iec104T2AckDelayMs = settings.Iec104T2AckDelayMs,
                Iec104T3TestIntervalMs = settings.Iec104T3TestIntervalMs,
                Iec104KMaxUnacknowledged = settings.Iec104KMaxUnacknowledged,
                Iec104WReceiveWindow = settings.Iec104WReceiveWindow,
                ResponseTimeoutMs = settings.ResponseTimeoutMs,
                Class2PollIntervalMs = settings.Class2PollIntervalMs,
                MaxClass1DrainFrames = settings.MaxClass1DrainFrames,
                ResetRemoteLinkOnConnect = settings.ResetRemoteLinkOnConnect,
                ResetFcbOnConnect = settings.ResetFcbOnConnect,
                RequestClass2ImmediatelyAfterStartup = settings.RequestClass2ImmediatelyAfterStartup,
                SendClockSyncOnConnect = settings.SendClockSyncOnConnect,
                SendGeneralInterrogationOnConnect = settings.SendGeneralInterrogationOnConnect,
                MappingProfilePath = settings.MappingProfilePath,
                CommandDockExpanded = _commandDockExpanded,
                DurationSeconds = durationSeconds,
                SavedUtc = DateTime.UtcNow
            };

            var path = SetupPreferencesPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            if (!silent)
            {
                AppendSessionLog("Setup preferences saved for next launch.");
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                MessageBox.Show(this, ex.Message, "Could not save setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void SelectProtocolMode(string? protocolMode)
    {
        var needle = protocolMode?.Contains("104", StringComparison.OrdinalIgnoreCase) == true ? "104"
            : protocolMode?.Contains("101", StringComparison.OrdinalIgnoreCase) == true ? "101"
            : "103";
        for (var i = 0; i < ProtocolModeComboBox.Items.Count; i++)
        {
            if ((ProtocolModeComboBox.Items[i] as ComboBoxItem)?.Content?.ToString()?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true)
            {
                ProtocolModeComboBox.SelectedIndex = i;
                return;
            }
        }
    }

    private static void EnsureComboItem(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items)
        {
            if (string.Equals((item as ComboBoxItem)?.Content?.ToString() ?? item?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        comboBox.Items.Add(value);
    }

    private static void SelectComboContent(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items)
        {
            var text = (item as ComboBoxItem)?.Content?.ToString() ?? item?.ToString();
            if (string.Equals(text, value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        if (comboBox.IsEditable)
        {
            comboBox.Text = value;
        }
    }

    private static void SetEditableComboText(ComboBox comboBox, string value)
    {
        SelectComboContent(comboBox, value);
        if (comboBox.IsEditable)
        {
            comboBox.Text = value;
        }
    }

    private sealed class SetupPreferences
    {
        public string ProtocolMode { get; set; } = nameof(Iec60870ProtocolMode.Iec103);
        public bool UseSimulatedSlave { get; set; }
        public string PortName { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public string SerialMode { get; set; } = "8E1";
        public string TcpHost { get; set; } = "127.0.0.1";
        public int TcpPort { get; set; } = 2404;
        public int LinkAddress { get; set; } = 1;
        public int CommonAddress { get; set; } = 1;
        public int LinkAddressSize { get; set; } = 1;
        public int CauseOfTransmissionSize { get; set; } = 2;
        public int CommonAddressSize { get; set; } = 2;
        public int InformationObjectAddressSize { get; set; } = 3;
        public string TransmissionMode { get; set; } = "Unbalanced";
        public int Iec104T0TimeoutMs { get; set; } = 30000;
        public int Iec104T1AckTimeoutMs { get; set; } = 15000;
        public int Iec104T2AckDelayMs { get; set; } = 10000;
        public int Iec104T3TestIntervalMs { get; set; } = 20000;
        public int Iec104KMaxUnacknowledged { get; set; } = 12;
        public int Iec104WReceiveWindow { get; set; } = 8;
        public int ResponseTimeoutMs { get; set; } = 1500;
        public int Class2PollIntervalMs { get; set; } = 500;
        public int MaxClass1DrainFrames { get; set; } = 64;
        public bool ResetRemoteLinkOnConnect { get; set; }
        public bool ResetFcbOnConnect { get; set; } = false;
        public bool RequestClass2ImmediatelyAfterStartup { get; set; } = true;
        public bool SendClockSyncOnConnect { get; set; }
        public bool SendGeneralInterrogationOnConnect { get; set; } = true;
        public string MappingProfilePath { get; set; } = string.Empty;
        public bool CommandDockExpanded { get; set; } = true;
        public int DurationSeconds { get; set; }
        public DateTime SavedUtc { get; set; }
    }



    private void ApplyCommandDockLayout()
    {
        if (CommandDockPanel is null || CommandDockColumn is null)
        {
            return;
        }

        CommandDockColumn.Width = _commandDockExpanded ? new GridLength(320) : new GridLength(42);
        CommandDockPanel.Visibility = _commandDockExpanded ? Visibility.Visible : Visibility.Collapsed;
        CommandDockMiniButton.Visibility = _commandDockExpanded ? Visibility.Collapsed : Visibility.Visible;

        if (CommandDockToggleIcon is not null)
        {
            CommandDockToggleIcon.Data = (Geometry)FindResource(_commandDockExpanded ? "LucideCircleChevronRight" : "LucideCircleChevronLeft");
        }
    }

    private void ToggleCommandDock_Click(object sender, RoutedEventArgs e)
    {
        _commandDockExpanded = !_commandDockExpanded;
        ApplyCommandDockLayout();
        SaveSetupPreferencesFromUi(silent: true);
    }

    private void CommandDock_Gi_Click(object sender, RoutedEventArgs e)
    {
        SeedValueViewerFromIoaProfile(GetSelectedProtocolMode());
        IssuePriorityRuntimeCommand(new Iec60870ControlCommandRequest { Kind = Iec60870ControlCommandKind.GeneralInterrogation, OperatorNote = "Command dock GI" });
    }

    private void CommandDock_ClockSync_Click(object sender, RoutedEventArgs e) => IssuePriorityRuntimeCommand(new Iec60870ControlCommandRequest { Kind = Iec60870ControlCommandKind.ClockSync, OperatorNote = "Command dock clock sync" });

    private void CommandDock_Read_Click(object sender, RoutedEventArgs e)
    {
        var request = new Iec60870ControlCommandRequest
        {
            Kind = Iec60870ControlCommandKind.Read,
            CommonAddress = ReadInt(CommandCaBox, "Command CA", 0, 0xFFFF),
            InformationObjectAddress = ReadInt(CommandIoaBox, "Command IOA", 0, 0xFFFFFF),
            OperatorNote = "Command dock read"
        };

        if (ValidateCommandTargetBeforeIssue(request, isCommandAction: false))
        {
            IssuePriorityRuntimeCommand(request);
        }
    }

    private void CommandTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCommandDockActionButtons();
        AutoFillCommandTargetFromProfile(preferCurrentSelection: true);
        UpdateCommandPreview(CommandSignalComboBox?.SelectedItem as CommandSignalOption);
    }

    private void UpdateCommandDockActionButtons()
    {
        if (CommandSelectOpenButton is null || CommandOperateOpenButton is null)
        {
            return;
        }

        var kind = ResolveCommandKindFromCombo();
        var isSetpoint = kind == Iec60870ControlCommandKind.SetpointNormalizedCommand;
        CommandSetpointLabel.Visibility = isSetpoint ? Visibility.Visible : Visibility.Hidden;
        CommandSetpointBox.Visibility = isSetpoint ? Visibility.Visible : Visibility.Hidden;
        CommandSelectCloseButton.Visibility = isSetpoint ? Visibility.Collapsed : Visibility.Visible;
        CommandOperateCloseButton.Visibility = isSetpoint ? Visibility.Collapsed : Visibility.Visible;

        if (kind == Iec60870ControlCommandKind.RegulatingStepCommand)
        {
            CommandSelectOpenButton.Content = "Select Lower";
            CommandOperateOpenButton.Content = "Operate Lower";
            CommandSelectCloseButton.Content = "Select Raise";
            CommandOperateCloseButton.Content = "Operate Raise";
            return;
        }

        if (isSetpoint)
        {
            CommandSelectOpenButton.Content = "Select Setpoint";
            CommandOperateOpenButton.Content = "Operate Setpoint";
            return;
        }

        CommandSelectOpenButton.Content = "Select Open";
        CommandOperateOpenButton.Content = "Operate Open";
        CommandSelectCloseButton.Content = "Select Close";
        CommandOperateCloseButton.Content = "Operate Close";
    }


    private void RefreshCommandSignalOptions()
    {
        if (CommandSignalOptions is null)
        {
            return;
        }

        var previousIoa = CommandSignalComboBox?.SelectedItem is CommandSignalOption selected
            ? selected.InformationObjectAddress
            : (int?)null;

        CommandSignalOptions.Clear();
        foreach (var point in _ioaProfile.Points
                     .Where(IsCommandPoint)
                     .OrderBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.Ioa))
        {
            var typeName = point.TypeId switch
            {
                45 => "Single C_SC_NA_1",
                46 => "Double C_DC_NA_1",
                47 => "Regulating C_RC_NA_1",
                48 => "Setpoint C_SE_NA_1",
                49 => "Setpoint scaled C_SE_NB_1",
                50 => "Setpoint float C_SE_NC_1",
                51 => "Bitstring C_BO_NA_1",
                _ => string.IsNullOrWhiteSpace(point.CommandPolicy) ? "Command" : point.CommandPolicy
            };

            var ca = point.Ca?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*";
            var feedbackPoint = point.FeedbackIoa.HasValue
                ? _ioaProfile.Points.FirstOrDefault(x => x.Ioa == point.FeedbackIoa.Value)
                : null;
            var fb = point.FeedbackIoa.HasValue ? $" · FB IOA {point.FeedbackIoa.Value}" : string.Empty;
            var range = point.EngineeringMin.HasValue || point.EngineeringMax.HasValue
                ? $" · range {point.EngineeringMin?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "-"}..{point.EngineeringMax?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "-"} {point.Unit}".TrimEnd()
                : string.Empty;

            CommandSignalOptions.Add(new CommandSignalOption
            {
                Name = string.IsNullOrWhiteSpace(point.Name) ? $"IOA {point.Ioa}" : point.Name,
                Detail = $"{typeName} · CA {ca} · IOA {point.Ioa}{fb}{range}",
                SearchText = $"{point.Name} {point.Group} IOA {point.Ioa} CA {ca} {typeName} {point.CommandPolicy} {point.Mnemonic}",
                CommonAddress = point.Ca,
                InformationObjectAddress = point.Ioa,
                TypeId = point.TypeId,
                FeedbackIoa = point.FeedbackIoa,
                FeedbackName = feedbackPoint?.Name ?? string.Empty,
                CommandPolicy = point.CommandPolicy,
                EngineeringMin = point.EngineeringMin,
                EngineeringMax = point.EngineeringMax,
                Unit = point.Unit
            });
        }

        if (CommandSignalComboBox is not null)
        {
            CommandSignalComboBox.SelectedItem = previousIoa.HasValue
                ? CommandSignalOptions.FirstOrDefault(x => x.InformationObjectAddress == previousIoa.Value)
                : null;
        }
    }

    private void CommandSignalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSavedSetup || CommandSignalComboBox?.SelectedItem is not CommandSignalOption option)
        {
            return;
        }

        ApplyCommandSignalOption(option);
    }

    private void ApplyCommandSignalOption(CommandSignalOption option)
    {
        if (CommandCaBox is null || CommandIoaBox is null)
        {
            return;
        }

        if (option.CommonAddress.HasValue)
        {
            CommandCaBox.Text = option.CommonAddress.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (!int.TryParse(CommandCaBox.Text, out _))
        {
            CommandCaBox.Text = (_ioaProfile.CommonAddress ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        CommandIoaBox.Text = option.InformationObjectAddress.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SelectCommandTypeByTypeId(option.TypeId);

        if (option.TypeId is 48 or 49 or 50 && option.EngineeringMin.HasValue && option.EngineeringMax.HasValue)
        {
            var mid = (option.EngineeringMin.Value + option.EngineeringMax.Value) / 2.0;
            CommandSetpointBox.Text = mid.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        UpdateCommandPreview(option);
        CommandDockStatusText.Text = $"Target selected: {option.Name}";
        AppendSessionLog($"Command target selected: {option.Name} ({option.Detail}).");
    }

    private void SelectCommandTypeByTypeId(int? typeId)
    {
        if (CommandTypeComboBox is null || !typeId.HasValue)
        {
            return;
        }

        var targetIndex = typeId.Value switch
        {
            45 => 0,
            46 => 1,
            47 => 2,
            48 or 49 or 50 => 3,
            _ => -1
        };

        if (targetIndex >= 0 && CommandTypeComboBox.SelectedIndex != targetIndex)
        {
            CommandTypeComboBox.SelectedIndex = targetIndex;
        }

        UpdateCommandDockActionButtons();
    }


    private void UpdateCommandPreview(CommandSignalOption? option = null)
    {
        if (CommandPreviewTitleText is null)
        {
            return;
        }

        if (option is null)
        {
            var caText = CommandCaBox?.Text ?? "-";
            var ioaText = CommandIoaBox?.Text ?? "-";
            var kind = ResolveCommandKindFromCombo();
            CommandPreviewTitleText.Text = "Manual command target";
            CommandPreviewAddressText.Text = $"{kind} · CA {caText} · IOA {ioaText}";
            CommandPreviewFeedbackText.Text = "Feedback IOA: not mapped from database";
            CommandPreviewSafetyText.Text = "Manual target is allowed, but the validator cannot prove feedback unless the Signal List maps command feedback.";
            return;
        }

        var kindText = option.TypeId switch
        {
            45 => "Single command",
            46 => "Double command",
            47 => "Regulating step",
            48 => "Setpoint normalized",
            49 => "Setpoint scaled",
            50 => "Setpoint float",
            51 => "Bitstring command",
            _ => "Command"
        };

        var ca = option.CommonAddress?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? (CommandCaBox?.Text ?? "*");
        CommandPreviewTitleText.Text = option.Name;
        CommandPreviewAddressText.Text = $"{kindText} · CA {ca} · IOA {option.InformationObjectAddress} · policy {option.CommandPolicy}";
        CommandPreviewFeedbackText.Text = option.FeedbackIoa.HasValue
            ? $"Feedback IOA {option.FeedbackIoa.Value}: {(string.IsNullOrWhiteSpace(option.FeedbackName) ? "mapped process point" : option.FeedbackName)}"
            : "Feedback IOA: not mapped";
        CommandPreviewSafetyText.Text = option.FeedbackIoa.HasValue
            ? "Validator will look for ACTCON, ACTTERM and mapped feedback value."
            : "Validator can check ACTCON/ACTTERM, but feedback proof needs FeedbackIoa in Signal List.";
    }

    private bool ValidateCommandTargetBeforeIssue(Iec60870ControlCommandRequest request, bool isCommandAction)
    {
        if (request.Kind is Iec60870ControlCommandKind.GeneralInterrogation or Iec60870ControlCommandKind.ClockSync or Iec60870ControlCommandKind.Read)
        {
            return true;
        }

        var selected = CommandSignalComboBox?.SelectedItem as CommandSignalOption;
        if (selected is null)
        {
            AddUiDiagnostic(
                "Info",
                "Command",
                "IEC10X-COMMAND-MANUAL-TARGET",
                "Manual command target is being used",
                $"Command will be sent to CA={request.CommonAddress}, IOA={request.InformationObjectAddress}. No command signal was selected from the database.",
                "Manual IOA is allowed, but selecting a command signal gives feedback mapping and stronger command verdicts.");
            return true;
        }

        if (selected.InformationObjectAddress != request.InformationObjectAddress)
        {
            AddUiDiagnostic(
                "Warning",
                "Command",
                "IEC10X-COMMAND-TARGET-MISMATCH",
                "Selected command signal and IOA field do not match",
                $"Selected '{selected.Name}' is IOA {selected.InformationObjectAddress}, but IOA box contains {request.InformationObjectAddress}.",
                "Either re-select the command signal or clear the dropdown if you intentionally want manual IOA.");
            CommandDockStatusText.Text = "Command blocked: selected signal and IOA field mismatch.";
            return false;
        }

        if (selected.CommonAddress.HasValue && request.CommonAddress.HasValue && selected.CommonAddress.Value != request.CommonAddress.Value)
        {
            AddUiDiagnostic(
                "Warning",
                "Command",
                "IEC10X-COMMAND-CA-MISMATCH",
                "Selected command signal and CA field do not match",
                $"Selected '{selected.Name}' uses CA {selected.CommonAddress.Value}, but CA box contains {request.CommonAddress.Value}.",
                "Use the database CA or intentionally clear the selection for manual target testing.");
            CommandDockStatusText.Text = "Command blocked: selected signal and CA field mismatch.";
            return false;
        }

        if (isCommandAction && !selected.FeedbackIoa.HasValue)
        {
            AddUiDiagnostic(
                "Info",
                "Command",
                "IEC10X-COMMAND-NO-FEEDBACK-MAP",
                "Selected command has no feedback IOA mapping",
                $"'{selected.Name}' can be commanded, but the Signal List does not define FeedbackIoa.",
                "Command validator will still check ACTCON/ACTTERM, but cannot prove physical feedback until FeedbackIoa is mapped.");
        }

        return true;
    }

    private void AutoFillCommandTargetFromProfile(bool preferCurrentSelection = false)
    {
        if (_isApplyingSavedSetup || _ioaProfile.Points.Count == 0 || CommandIoaBox is null)
        {
            return;
        }

        if (preferCurrentSelection && CommandSignalComboBox?.SelectedItem is CommandSignalOption selected)
        {
            ApplyCommandSignalOption(selected);
            return;
        }

        // Do not keep overwriting manual IOA entry. Auto-fill only when the box is empty
        // or still at the old starter value.
        var hasManualIoa = int.TryParse(CommandIoaBox.Text, out var currentIoa) && currentIoa > 0 && currentIoa != 101;
        if (hasManualIoa)
        {
            return;
        }

        var kind = ResolveCommandKindFromCombo();
        var typeId = kind switch
        {
            Iec60870ControlCommandKind.SingleCommand => 45,
            Iec60870ControlCommandKind.DoubleCommand => 46,
            Iec60870ControlCommandKind.RegulatingStepCommand => 47,
            Iec60870ControlCommandKind.SetpointNormalizedCommand => 48,
            _ => 0
        };

        if (typeId == 0)
        {
            return;
        }

        var option = CommandSignalOptions.FirstOrDefault(x => x.TypeId == typeId)
            ?? CommandSignalOptions.FirstOrDefault();

        if (option is null)
        {
            return;
        }

        if (CommandSignalComboBox is not null)
        {
            CommandSignalComboBox.SelectedItem = option;
        }

        ApplyCommandSignalOption(option);
    }

    private void CommandDock_Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var tag = button.Tag?.ToString() ?? string.Empty;
        var select = tag.StartsWith("select", StringComparison.OrdinalIgnoreCase);
        var leftAction = tag.EndsWith("left", StringComparison.OrdinalIgnoreCase);
        var kind = ResolveCommandKindFromCombo();
        var value = BuildCommandValue(kind, leftAction);

        var request = new Iec60870ControlCommandRequest
        {
            Kind = kind,
            CommonAddress = ReadInt(CommandCaBox, "Command CA", 0, 0xFFFF),
            InformationObjectAddress = ReadInt(CommandIoaBox, "Command IOA", 0, 0xFFFFFF),
            Value = value,
            NumericValue = ParseLeadingDouble(CommandSetpointBox.Text, 0),
            Qualifier = ReadInt(CommandQualifierBox, "Command qualifier", 0, 31),
            SelectBeforeOperate = select,
            OperatorNote = select ? "Command dock SELECT" : "Command dock OPERATE"
        };

        if (ValidateCommandTargetBeforeIssue(request, isCommandAction: true))
        {
            IssuePriorityRuntimeCommand(request);
        }
    }

    private Iec60870ControlCommandKind ResolveCommandKindFromCombo()
    {
        var typeText = (CommandTypeComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Double";
        if (typeText.Contains("Setpoint", StringComparison.OrdinalIgnoreCase)) return Iec60870ControlCommandKind.SetpointNormalizedCommand;
        if (typeText.Contains("Regulating", StringComparison.OrdinalIgnoreCase)) return Iec60870ControlCommandKind.RegulatingStepCommand;
        if (typeText.Contains("Double", StringComparison.OrdinalIgnoreCase)) return Iec60870ControlCommandKind.DoubleCommand;
        return Iec60870ControlCommandKind.SingleCommand;
    }

    private static int BuildCommandValue(Iec60870ControlCommandKind kind, bool leftAction)
    {
        return kind switch
        {
            Iec60870ControlCommandKind.SingleCommand => leftAction ? 0 : 1,       // OFF/Open, ON/Close
            Iec60870ControlCommandKind.DoubleCommand => leftAction ? 1 : 2,       // DCS=1 Open/Off, DCS=2 Close/On
            Iec60870ControlCommandKind.RegulatingStepCommand => leftAction ? 1 : 2, // RCS=1 Lower, RCS=2 Raise
            _ => 0
        };
    }

    private static double ParseLeadingDouble(string text, double fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var token = text.Trim().Split(' ', '/', '\t', '\r', '\n').FirstOrDefault() ?? string.Empty;
        return double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private void IssuePriorityRuntimeCommand(Iec60870ControlCommandRequest request)
    {
        if (_activeControlSession is null || !_activeControlSession.SupportsRuntimeControlCommands)
        {
            CommandDockStatusText.Text = "No active IEC-101/104 runtime session. Connect first before issuing a command.";
            AppendSessionLog("Command dock refused: no active runtime control session.");
            return;
        }

        if (GetSelectedProtocolMode() == Iec60870ProtocolMode.Iec103)
        {
            CommandDockStatusText.Text = "IEC-103 control command dock is not enabled. Use IEC-101/104 command ASDUs only in this build.";
            AppendSessionLog("Command dock refused: IEC-103 command workflow is not enabled in this build.");
            return;
        }

        _activeControlSession.QueueControlCommand(request);
        var selectedName = CommandSignalComboBox?.SelectedItem is CommandSignalOption option ? $" · {option.Name}" : string.Empty;
        CommandDockStatusText.Text = "Issued priority command: " + request.Summary + selectedName;
        AppendSessionLog("Command dock issued: " + request.Summary + selectedName);
    }

    private void ConnectToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionCancellation is null)
        {
            Start_Click(sender, e);
        }
        else
        {
            Stop_Click(sender, e);
        }
    }

    private void UpdateConnectToggleVisual(bool isRunning)
    {
        if (StartButton is null)
        {
            return;
        }

        if (ConnectToggleCaption is not null)
        {
            ConnectToggleCaption.Text = isRunning ? "Disconnect" : "Connect";
        }

        if (ConnectIconOn is not null)
        {
            ConnectIconOn.Visibility = isRunning ? Visibility.Collapsed : Visibility.Visible;
        }

        if (ConnectIconOff is not null)
        {
            ConnectIconOff.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        }

        StartButton.Background = (Brush)new BrushConverter().ConvertFromString("#F4F8FF")!;
        StartButton.BorderBrush = Brushes.Transparent;
        StartButton.Foreground = (Brush)new BrushConverter().ConvertFromString(isRunning ? "#B91C1C" : "#166534")!;
        StartButton.ToolTip = isRunning ? "Disconnect and close transport" : "Connect and monitor continuously";
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionCancellation != null)
        {
            return;
        }

        Iec103MasterSettings settings;
        int durationSeconds;
        try
        {
            settings = BuildSettingsFromUi();
            durationSeconds = ReadInt(DurationBox, "Session timeout", 0, 86400);
            SaveSetupPreferences(settings, durationSeconds, silent: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearSessionView(clearLog: false);
        SeedValueViewerFromIoaProfile(settings.ProtocolMode);
        _stopRequested = false;
        SetRunUiState(isRunning: true);
        _lastResult = null;
        _sessionCancellation = new CancellationTokenSource();
        SessionSubtitleText.Text = settings.SerialSummary;
        UpdateStableHeader("Monitoring", settings.UseSimulatedSlave
            ? "Demo mode active. Monitoring continuously until Stop."
            : (settings.ProtocolMode == Iec60870ProtocolMode.Iec104
                ? "TCP client session active. Monitoring continuously until Stop."
                : "Serial master session active. Monitoring continuously until Stop."));
        AppendSessionLog("Starting master session: " + settings.SerialSummary);
        if (settings.ProtocolMode != Iec60870ProtocolMode.Iec104)
        {
            var estimatedClass2CycleMs = EstimatePracticalSerialCycleMs(settings);
            AppendSessionLog($"Class 2 scan feasibility: configured={settings.Class2PollIntervalMs} ms, estimated physical minimum≈{estimatedClass2CycleMs} ms at {settings.BaudRate} bps.");
            if (settings.BaudRate <= 1200)
            {
                AppendSessionLog("Low-baud serial timing guard active: timeout/poll/backoff widened for 1200 bps field channels; 100 ms polling cannot be treated as a guaranteed measurement refresh at this speed.");
            }
        }

        AppendSessionLog("Target mode: " + (settings.UseSimulatedSlave ? settings.TargetProfile + " simulation" : settings.TargetProfile));
        AppendSessionLog(settings.ProtocolMode == Iec60870ProtocolMode.Iec104
            ? "IEC-104 profile: STARTDT, optional clock sync/GI, I/S/U frame evidence, and TESTFR health check."
            : "Polling profile: Class 2 normal cycle; Class 1 only when ACD=1 or bounded GI follow-up.");
        AppendSessionLog(settings.ProtocolMode == Iec60870ProtocolMode.Iec103
            ? (_mappingProfile.HasSignals ? $"Mapping profile loaded: {_mappingProfile.ProfileName} ({_mappingProfile.Signals.Count} signals)." : "No mapping profile loaded. Value/Event views will show raw FUN/INF names.")
            : (_ioaProfile.HasPoints ? $"IOA mapping profile loaded: {_ioaProfile.ProfileName} ({_ioaProfile.Points.Count} points)." : "IEC-101/104 uses raw IOA labels. Load or edit an IOA mapping profile for project names."));

        try
        {
            await using var transport = CreateTransport(settings);
            _activeTransport = transport;
            var session = CreateSession(settings, transport);
            _activeControlSession = session as IProtocolControlCommandSession;
            session.EvidenceReceived += OnEvidenceReceived;
            session.FindingRaised += OnFindingRaised;

            var result = durationSeconds <= 0
                ? await session.RunAsync(_sessionCancellation.Token).ConfigureAwait(false)
                : await session.RunForAsync(TimeSpan.FromSeconds(durationSeconds), _sessionCancellation.Token).ConfigureAwait(false);
            _lastResult = result;

            await Dispatcher.InvokeAsync(() =>
            {
                ApplyFinalResult(result);
                AppendSessionLog("Monitor session completed: " + result.CompletionReason);
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateStableHeader("Stopped", "Session stopped by user.");
                AppendSessionLog("Session stopped by user.");
            });
        }
        catch (Exception ex) when (_stopRequested || _sessionCancellation?.IsCancellationRequested == true)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateStableHeader("Stopped", "Session stopped and transport was closed safely.");
                AppendSessionLog("Session stopped while transport was closing: " + ex.Message);
                AddUiDiagnostic("Warning", "Desktop", "IEC103-DESKTOP-STOP-CLOSE", "Session stopped while transport was closing", ex.Message, "Usually safe during Stop/Force Close. If repeated, check USB/serial driver stability.", ex);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateStableHeader("Faulted", ex.Message);
                AppendSessionLog("Fault captured in Diagnostics: " + ex.Message);
                AddUiDiagnostic("Error", "Desktop", "IEC103-DESKTOP-SESSION-FAULT", "Master session fault", ex.Message, "Select this diagnostic row and copy detail if escalation/debugging is needed.", ex);
            });
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _activeControlSession = null;
                _activeTransport = null;
                _stopRequested = false;
                _sessionCancellation?.Dispose();
                _sessionCancellation = null;
                SetRunUiState(isRunning: false);
            });
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionCancellation is null)
        {
            SetRunUiState(isRunning: false);
            return;
        }

        _stopRequested = true;
        _sessionCancellation.Cancel();
        StopButton.IsEnabled = true;
        StopButton.ToolTip = "Force close transport";
        UpdateStableHeader("Stopping", "Closing active transport safely.");
        AppendSessionLog("Stop requested by user. Active transport close requested.");

        await TryCloseActiveTransportAsync("Stop request");
    }

    private void SignalList_Click(object sender, RoutedEventArgs e)
    {
        EditSignalList_Click(sender, e);
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => ClearSessionView(clearLog: true);


    private string AppendEvidenceRetentionPolicy(string markdown)
    {
        var builder = new StringBuilder(markdown ?? string.Empty);
        if (builder.Length > 0 && !builder.ToString().EndsWith(Environment.NewLine, StringComparison.Ordinal))
        {
            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("## Evidence Retention / UI Store Policy");
        builder.AppendLine();
        foreach (var line in BuildEvidenceRetentionPolicyLines())
        {
            builder.AppendLine("- " + line);
        }

        builder.AppendLine();
        builder.AppendLine("> This section is generated by the UI runtime. It describes evidence retention, trace suppression, low-value compression, and dispatcher pressure at export time.");
        return builder.ToString();
    }

    private string BuildTextEvidenceRetentionHeader(string exportName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# ARIEC60870 export integrity marker");
        builder.AppendLine("# Export: " + exportName);
        foreach (var line in BuildEvidenceRetentionPolicyLines())
        {
            builder.AppendLine("# " + line);
        }
        builder.AppendLine("#");
        return builder.ToString();
    }

    private IEnumerable<string> BuildEvidenceRetentionPolicyLines()
    {
        yield return $"Protocol Trace mode: {GetTraceVerbosityMode()}";
        yield return $"Trace ring visible/stored: {FrameTraceRows.Count}/{MaxVisibleFrameTraceRows}";
        yield return $"Evidence Summary ring visible/stored: {EvidenceRows.Count}/{MaxVisibleEvidenceRows}";
        yield return $"Value store visible/keyed limit: {ValueRows.Count}/{MaxVisibleValueRows}";
        yield return $"Event Log ring visible/stored: {RelayEventRows.Count}/{MaxVisibleRelayEventRows}";
        yield return $"Diagnostics ring visible/stored: {DiagnosticRows.Count}/{MaxVisibleDiagnosticRows}";
        yield return $"Trace verbosity suppressed rows: total={_traceVerbositySuppressedRows}, routine={_traceVerbositySuppressedRoutine}, supervisory={_traceVerbositySuppressedSupervisory}";
        yield return $"Backpressure low-value compression: total={_backpressureDroppedEvents}, ack/no-data={_backpressureDroppedAckNoData}, background-poll={_backpressureDroppedBackgroundPoll}, test/supervisory={_backpressureDroppedTestFrames}, other={_backpressureDroppedOtherLowValue}";
        yield return $"Dispatcher queue: current={_pendingEvidence.Count}, maxObserved={_maxPendingEvidenceDepth}, adaptiveBudget={_lastFlushBudget}";
        yield return $"Dispatcher flush: last={_lastUiFlushMs} ms, max={_maxUiFlushMs} ms, ticks={_uiFlushTicks}, lastProcessed={_lastEvidenceProcessed}+{_lastFindingProcessed}, lastVisibleBatchRows={_lastVisibleBatchRows}";
        yield return $"Protocol proof state: CA={(_proofObservedCa > 0 ? _proofObservedCa.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}, GI={_proofGiObserved}, GIComplete={_proofGiCompleted}, GINegative={_proofGiNegative}, Digital={_proofDigitalObserved}, Analog={_proofAnalogObserved}, Command={_proofCommandObserved}, CommandFeedback={_proofCommandFeedbackObserved}";
        yield return $"GI coverage matrix: monitor={_lastMonitorReceivedCount}/{_lastMonitorExpectedCount}, digital={_lastDigitalReceivedCount}/{_lastDigitalExpectedCount}, analog={_lastAnalogReceivedCount}/{_lastAnalogExpectedCount}, other={_lastOtherReceivedCount}/{_lastOtherExpectedCount}, missing={_lastMissingMonitorCount}, missingPreview={_lastMissingMonitorPreview}";
        yield return $"Command mapping coverage: commands={_lastCommandExpectedCount}, feedbackMapped={_lastFeedbackMappedCommandCount}";
        yield return "Protected evidence policy: diagnostics/warnings/errors, mapped values, process values, digital values, GI activity, command ASDUs, ACTCON and ACTTERM are protected from low-value trace compression.";
    }

    private void AddEvidenceRetentionExportMarker(string exportTarget)
    {
        AddUiDiagnostic(
            "Info",
            "Evidence",
            "ARIEC-EVIDENCE-RETENTION-POLICY",
            "Evidence export includes retention policy marker",
            $"{exportTarget} captured TraceMode={GetTraceVerbosityMode()}, traceSkip={_traceVerbositySuppressedRows}, lowValueDropped={_backpressureDroppedEvents}, qMax={_maxPendingEvidenceDepth}, maxFlush={_maxUiFlushMs} ms.",
            "Use this marker when reviewing FAT/SAT evidence so compressed routine trace rows are not mistaken for missing protocol evidence.");
    }

    private void ExportMarkdown_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult == null)
        {
            MessageBox.Show(this, "No completed session result is available yet.", "Export evidence", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export ARIEC60870 Evidence Report",
            Filter = "Markdown report (*.md)|*.md|All files (*.*)|*.*",
            FileName = "ARIEC60870-master-evidence.md",
            AddExtension = true,
            DefaultExt = ".md"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var markdown = new MasterMarkdownReportWriter().Write(_lastResult, maxEvents: 1000);
        markdown = AppendEvidenceRetentionPolicy(markdown);
        File.WriteAllText(dialog.FileName, markdown, Encoding.UTF8);
        AddEvidenceRetentionExportMarker("Markdown evidence report");
        AppendSessionLog("Evidence report exported with retention policy marker: " + dialog.FileName);
        MessageBox.Show(this, "Evidence report exported successfully.", "Export evidence", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private Iec103MasterSettings BuildSettingsFromUi()
    {
        var port = (PortComboBox.SelectedItem as string)?.Trim();

        var settings = Iec103MasterSettings.CreateDefault();
        settings.UseSimulatedSlave = IsDemoModeSelected();
        settings.ProtocolMode = GetSelectedProtocolMode();
        if (settings.ProtocolMode != Iec60870ProtocolMode.Iec104 && string.IsNullOrWhiteSpace(port))
        {
            throw new InvalidOperationException("COM port is required for IEC-101/103 serial mode.");
        }
        settings.TargetProfile = settings.ProtocolMode switch
        {
            Iec60870ProtocolMode.Iec101 => settings.UseSimulatedSlave ? "IEC-101 demo outstation" : "IEC-101 RTU/outstation",
            Iec60870ProtocolMode.Iec104 => settings.UseSimulatedSlave ? "IEC-104 demo server" : "IEC-104 server",
            _ => settings.UseSimulatedSlave ? "generic relay demo slave" : "IEC-103 protection relay"
        };
        settings.PortName = port ?? string.Empty;
        settings.BaudRate = ReadComboInt(BaudComboBox, "Baudrate");
        if (settings.BaudRate < 300 || settings.BaudRate > 921600)
        {
            throw new InvalidOperationException("Baudrate must be between 300 and 921600 bps.");
        }

        settings.TcpHost = TcpHostBox.Text.Trim();
        settings.TcpPort = ReadInt(TcpPortBox, "IEC-104 TCP Port", 1, 65535);

        if (settings.ProtocolMode == Iec60870ProtocolMode.Iec103)
        {
            settings.LinkAddressSize = 1;
            settings.CauseOfTransmissionSize = 1;
            settings.CommonAddressSize = 1;
            settings.InformationObjectAddressSize = 1;
            settings.LinkAddress = ReadInt(LinkAddressBox, "IEC-103 Link Address", 0, 255);
            settings.CommonAddress = ReadInt(CommonAddressBox, "IEC-103 Common Address", 0, 255);
        }
        else
        {
            settings.LinkAddressSize = settings.ProtocolMode == Iec60870ProtocolMode.Iec101 ? ReadComboInt(LinkAddressSizeComboBox, "Link address size") : 1;
            settings.CauseOfTransmissionSize = ReadComboInt(CotSizeComboBox, "Cause of transmission size");
            settings.CommonAddressSize = ReadComboInt(CaSizeComboBox, "Common address size");
            settings.InformationObjectAddressSize = ReadComboInt(IoaSizeComboBox, "Information object address size");
            settings.TransmissionMode = (TransmissionModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Unbalanced";

            if (settings.ProtocolMode == Iec60870ProtocolMode.Iec101 && settings.TransmissionMode.StartsWith("Balanced", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("IEC-101 Balanced mode is not active in this build. Use Unbalanced for master polling, or treat Balanced as a roadmap item until the balanced link-layer engine is implemented.");
            }

            if (settings.ProtocolMode == Iec60870ProtocolMode.Iec101 && settings.LinkAddressSize == 0)
            {
                throw new InvalidOperationException("IEC-101 link address size 0 is a valid profile case only for specific balanced/monitor links. This build implements unbalanced master polling, so use 1 or 2 octets for field validation.");
            }

            var linkMax = settings.LinkAddressSize == 0 ? 0 : settings.LinkAddressSize == 1 ? 255 : 65535;
            var caMax = settings.CommonAddressSize == 1 ? 255 : 65535;
            settings.LinkAddress = settings.ProtocolMode == Iec60870ProtocolMode.Iec101 ? ReadInt(LinkAddressBox, "IEC-101 Link Address", 0, linkMax) : 0;
            settings.CommonAddress = ReadInt(CommonAddressBox, "Common Address", 0, caMax);
        }

        settings.Iec104T0TimeoutMs = ReadInt(Iec104T0Box, "IEC-104 t0", 1000, 120000);
        settings.Iec104T1AckTimeoutMs = ReadInt(Iec104T1Box, "IEC-104 t1", 1000, 120000);
        settings.Iec104T2AckDelayMs = ReadInt(Iec104T2Box, "IEC-104 t2", 1000, 120000);
        settings.Iec104T3TestIntervalMs = ReadInt(Iec104T3Box, "IEC-104 t3", 1000, 300000);
        settings.Iec104KMaxUnacknowledged = ReadInt(Iec104KBox, "IEC-104 k", 1, 32767);
        settings.Iec104WReceiveWindow = ReadInt(Iec104WBox, "IEC-104 w", 1, 32767);
        settings.ResponseTimeoutMs = ReadInt(TimeoutBox, "Timeout", 100, 60000);
        settings.Class2PollIntervalMs = ReadInt(Class2IntervalBox, "Class 2 interval", 50, 60000);
        settings.MaxClass1DrainFrames = ReadInt(MaxDrainBox, "Max Class 1 drain", 1, 512);
        settings.ResetRemoteLinkOnConnect = ResetRemoteLinkCheckBox.IsChecked == true;
        settings.ResetFcbOnConnect = settings.ProtocolMode == Iec60870ProtocolMode.Iec101
            ? false
            : ResetFcbCheckBox.IsChecked == true;
        settings.SendClockSyncOnConnect = ClockSyncCheckBox.IsChecked == true;
        settings.SendGeneralInterrogationOnConnect = GiCheckBox.IsChecked == true;
        settings.RequestClass2ImmediatelyAfterStartup = Class2StartupCheckBox.IsChecked == true;
        settings.MappingProfilePath = MappingProfilePathBox.Text.Trim();
        ApplyLowBaudSerialTimingGuard(settings);

        var serialMode = (SerialModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "8E1";
        settings.DataBits = 8;
        settings.StopBits = StopBits.One;
        settings.Parity = serialMode switch
        {
            "8N1" => Parity.None,
            "8O1" => Parity.Odd,
            _ => Parity.Even
        };

        return settings;
    }

    private static int EstimatePracticalSerialCycleMs(Iec103MasterSettings settings)
    {
        var bitsPerByte = 1 + settings.DataBits + (settings.Parity == Parity.None ? 0 : 1) + (settings.StopBits == StopBits.Two ? 2 : 1);
        var requestBytes = 4 + Math.Max(0, settings.LinkAddressSize);
        var typicalResponseBytes = 16 + Math.Max(0, settings.LinkAddressSize) + settings.CommonAddressSize + settings.CauseOfTransmissionSize + settings.InformationObjectAddressSize + 12;
        var baud = Math.Max(300, settings.BaudRate);
        var wireMs = (int)Math.Ceiling((requestBytes + typicalResponseBytes) * bitsPerByte * 1000.0 / baud);
        var turnaroundMs = baud <= 1200 ? 220 : baud <= 2400 ? 140 : 70;
        return Math.Max(50, wireMs + turnaroundMs + settings.Class1DrainDelayMs);
    }

    private static void ApplyLowBaudSerialTimingGuard(Iec103MasterSettings settings)
    {
        if (settings.ProtocolMode == Iec60870ProtocolMode.Iec104 || settings.BaudRate > 1200)
        {
            return;
        }

        // Low-speed IEC-101/103 channels are common in legacy utility links. A large ASDU,
        // modem/RS-485 turnaround time, or Class 1 drain cycle can exceed aggressive bench
        // timing. Guard the session so 1200 bps does not fail simply because the analyzer
        // was tuned for 9600/19200 bps lab links.
        settings.ResponseTimeoutMs = Math.Max(settings.ResponseTimeoutMs, 5000);
        settings.Class2PollIntervalMs = Math.Max(settings.Class2PollIntervalMs, 1000);
        settings.BusyBackoffMs = Math.Max(settings.BusyBackoffMs, 500);
        settings.TimeoutRecoveryBackoffMs = Math.Max(settings.TimeoutRecoveryBackoffMs, 500);
    }

    private async Task TryCloseActiveTransportAsync(string reason)
    {
        var transport = _activeTransport;
        if (transport is null)
        {
            return;
        }

        try
        {
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() => AppendSessionLog($"Transport closed: {reason}."));
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                AppendSessionLog($"Transport close warning: {ex.Message}");
                AddUiDiagnostic("Warning", "Transport", "IEC103-TRANSPORT-CLOSE", "Transport close warning", ex.Message, "Stop/Force Close requested. If COM port remains locked, unplug/replug the USB converter or restart the app.", ex);
            });
        }
    }

    private IByteTransport CreateTransport(Iec103MasterSettings settings)
    {
        return settings.ProtocolMode switch
        {
            Iec60870ProtocolMode.Iec104 => settings.UseSimulatedSlave
                ? new SimulatedIec104ServerTransport(settings)
                : new TcpClientByteTransport(settings),
            Iec60870ProtocolMode.Iec101 => settings.UseSimulatedSlave
                ? new SimulatedIec101Transport(settings)
                : new SerialByteTransport(settings),
            _ => settings.UseSimulatedSlave
                ? new SimulatedRelayTransport(settings)
                : new SerialByteTransport(settings)
        };
    }

    private IProtocolMasterSession CreateSession(Iec103MasterSettings settings, IByteTransport transport)
    {
        return settings.ProtocolMode switch
        {
            Iec60870ProtocolMode.Iec104 => new Iec104ClientSession(settings, transport),
            Iec60870ProtocolMode.Iec101 => new Iec101MasterSession(settings, transport),
            _ => new Iec103MasterSession(settings, transport, _mappingProfile)
        };
    }


    private void ProtocolModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ApplyProtocolUxProfile(GetSelectedProtocolMode());
    }

    private void ApplyProtocolUxProfile(Iec60870ProtocolMode mode)
    {
        var is103 = mode == Iec60870ProtocolMode.Iec103;
        var is101 = mode == Iec60870ProtocolMode.Iec101;
        var is104 = mode == Iec60870ProtocolMode.Iec104;
        var serialVisibility = is104 ? Visibility.Collapsed : Visibility.Visible;
        var tcpVisibility = is104 ? Visibility.Visible : Visibility.Collapsed;
        var funInfVisibility = is103 ? Visibility.Visible : Visibility.Collapsed;
        var ioaVisibility = is103 ? Visibility.Collapsed : Visibility.Visible;
        var apciVisibility = is104 ? Visibility.Visible : Visibility.Collapsed;
        var classVisibility = is104 ? Visibility.Collapsed : Visibility.Visible;

        ProductTitleText.Text = "ARIEC60870 Protocol Lab";
        ApplyProtocolLogo(mode);
        ClassPollLabelText.Text = is104 ? "GI/I/S " : "GI/C1/C2 ";
        EventChipLabelText.Text = is104 ? "ASDU " : "EVENT ";
        CommandDockStatusText.Text = is103
            ? "IEC-103 selected. Command Dock is active for IEC-101/104 control ASDUs only in this build."
            : "Ready. Connect first, then queue GI, read, clock sync, or safe test commands.";

        SetupTitleText.Text = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "IEC-101 telecontrol serial setup",
            Iec60870ProtocolMode.Iec104 => "IEC-104 telecontrol TCP/IP setup",
            _ => "IEC-103 protection relay setup"
        };
        SetupSubtitleText.Text = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "Serial telecontrol interface: link address, CA, IOA, COT, General Interrogation and Class 1/Class 2 polling.",
            Iec60870ProtocolMode.Iec104 => "TCP/IP telecontrol interface: server endpoint, STARTDT, APCI I/S/U frames, CA, IOA and ASDU decode.",
            _ => "Serial protection interface: link address, Class 1/Class 2 policy, FUN/INF mapping."
        };
        ProtocolSetupBadgeText.Text = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "IEC-101 · FT1.2 serial telecontrol",
            Iec60870ProtocolMode.Iec104 => "IEC-104 · TCP/IP telecontrol",
            _ => "IEC-103 · FT1.2 serial protection"
        };
        ProtocolSetupDescriptionText.Text = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "Use this profile for serial RTU/outstation tests. Main addressing is CA + IOA; Type ID and COT explain what data is returned and why.",
            Iec60870ProtocolMode.Iec104 => "Use this profile for IEC-104 server tests over TCP. The frame trace exposes APCI format, sequence numbers, STARTDT/TESTFR control and ASDU payload.",
            _ => "Use this profile for protection IED IEC-103 tests. Main addressing is FUN/INF; Class 1 carries events, Class 2 carries background data."
        };
        SerialConnectionTitleText.Text = is101 ? "IEC-101 SERIAL CONNECTION" : "IEC-103 SERIAL CONNECTION";
        PollingPolicyTitleText.Text = is101 ? "IEC-101 CLASS POLLING" : "IEC-103 CLASS POLLING";
        Class2IntervalLabelText.Text = is101 ? "Class 2 scan interval (ms)" : "Class 2 interval (ms)";
        MaxDrainLabelText.Text = is101 ? "Max Class 1 event drain" : "Max Class 1 drain";
        LinkAddressLabelText.Text = is101 ? "Link Address" : "Link Address";
        CommonAddressLabelText.Text = is104 ? "Common Address (CA)" : is101 ? "Common Address (CA)" : "Common Address";
        if (string.IsNullOrWhiteSpace(CommandCaBox.Text)) CommandCaBox.Text = string.IsNullOrWhiteSpace(CommonAddressBox.Text) ? "1" : CommonAddressBox.Text;
        Iec10xProfileTitleText.Text = is104 ? "IEC-104 INTEROPERABILITY PROFILE" : "IEC-101 INTEROPERABILITY PROFILE";
        if (is103)
        {
            LinkAddressSizeComboBox.SelectedIndex = 1;
            CotSizeComboBox.SelectedIndex = 0;
            CaSizeComboBox.SelectedIndex = 0;
            IoaSizeComboBox.SelectedIndex = 0;
        }
        else
        {
            if (CotSizeComboBox.SelectedIndex < 0) CotSizeComboBox.SelectedIndex = 1;
            if (CaSizeComboBox.SelectedIndex < 0) CaSizeComboBox.SelectedIndex = 1;
            if (IoaSizeComboBox.SelectedIndex < 0) IoaSizeComboBox.SelectedIndex = 2;
        }
        MappingProfileTitleText.Text = is103 ? "IEC-103 FUN/INF MAPPING PROFILE" : "IEC-101/104 IOA POINT PROFILE";
        if (!is103)
        {
            if (_ioaProfile.HasPoints && string.IsNullOrWhiteSpace(MappingProfilePathBox.Text))
            {
                var candidate = File.Exists(BundledPlnPusertifSeedPath) ? BundledPlnPusertifSeedPath : Path.GetFullPath(SourceTreePlnPusertifSeedPath);
                if (File.Exists(candidate)) MappingProfilePathBox.Text = candidate;
            }
            if (_ioaProfile.HasPoints && !_defaultIoaSeedSettingsApplied)
            {
                ApplyIoaProfileDefaultsToUi(_ioaProfile, onlyWhenUiLooksDefault: !_savedSetupPreferencesLoaded);
            }
            var scenarioText = _ioaProfile.TestScenarios.Count > 0 ? $", {_ioaProfile.TestScenarios.Count} Pusertif-style test scenarios" : string.Empty;
            MappingProfileStatusText.Text = _ioaProfile.HasPoints
                ? $"Loaded: {_ioaProfile.ProfileName} ({_ioaProfile.Points.Count} IOA points{scenarioText}). User-editable JSON; start from PLN/Pusertif seed then adapt globally."
                : "No IOA profile loaded. Raw IOA, Type ID, COT and CA will be shown.";
        }
        else if (string.IsNullOrWhiteSpace(MappingProfilePathBox.Text))
        {
            MappingProfileStatusText.Text = "No mapping profile loaded. Raw FUN/INF will be shown.";
        }

        SerialConnectionPanel.Visibility = serialVisibility;
        TcpConnectionPanel.Visibility = tcpVisibility;
        SerialPollingPanel.Visibility = is104 ? Visibility.Collapsed : Visibility.Visible;
        Iec10xProfilePanel.Visibility = is103 ? Visibility.Collapsed : Visibility.Visible;
        LinkAddressSizePanel.Visibility = is101 ? Visibility.Visible : Visibility.Collapsed;
        TransmissionModeComboBox.IsEnabled = is101;
        Iec104RuntimePanel.Visibility = tcpVisibility;
        Iec103OptionsPanel.Visibility = is104 ? Visibility.Collapsed : Visibility.Visible;
        LinkAddressPanel.Visibility = is104 ? Visibility.Collapsed : Visibility.Visible;
        MappingProfilePanel.Visibility = Visibility.Visible;

        // Evidence Summary is a distilled human-readable proof view. Keep protocol-heavy columns in Protocol Trace and the selected-row inspector.
        SetColumnVisibility(EvidenceClassColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceApciColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceTypeColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceCotColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceCaColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceIoaColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceFunInfColumn, Visibility.Collapsed);
        SetColumnVisibility(EvidenceQualityColumn, Visibility.Collapsed);
        EvidenceSignalColumn.Header = is103 ? "Signal" : "Signal";

        // Protocol Trace is now a lightweight line monitor, not a protocol column grid.
        // Protocol-specific fields are rendered inside the line text and decoded in the interpreter.

        // Value/Event main grids also keep one compact Address column; raw CA/IOA/FUN/INF/TypeID columns stay in Protocol Trace.
        SetColumnVisibility(ValueCaColumn, Visibility.Collapsed);
        SetColumnVisibility(ValueIoaColumn, Visibility.Collapsed);
        SetColumnVisibility(ValueFunInfColumn, Visibility.Collapsed);
        SetColumnVisibility(ValueTypeIdColumn, Visibility.Collapsed);
        SetColumnVisibility(ValueQualityColumn, Visibility.Collapsed);

        SetColumnVisibility(EventCaColumn, Visibility.Collapsed);
        SetColumnVisibility(EventIoaColumn, Visibility.Collapsed);
        SetColumnVisibility(EventFunInfColumn, Visibility.Collapsed);
        SetColumnVisibility(EventTypeIdColumn, Visibility.Collapsed);
        SetColumnVisibility(EventQualityColumn, Visibility.Collapsed);

        RawFrameGroupingHintText.Text = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "grouped by IEC-101 FT1.2 + ASDU fields",
            Iec60870ProtocolMode.Iec104 => "grouped by IEC-104 APCI/APDU fields",
            _ => "grouped by IEC-103 FT1.2 + FUN/INF fields"
        };

        SessionSubtitleText.Text = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "IEC-101 selected: serial FT1.2, ACD/DFC, Class 1/Class 2, Type ID/COT/CA/IOA views.",
            Iec60870ProtocolMode.Iec104 => "IEC-104 selected: TCP/IP, APCI I/S/U trace, sequence numbers, Type ID/COT/CA/IOA views.",
            _ => "IEC-103 selected: serial protection relay, ACD/DFC, Class 1/Class 2, FUN/INF views."
        };
    }

    private static void SetColumnVisibility(DataGridColumn column, Visibility visibility)
    {
        column.Visibility = visibility;
    }

    private void ApplyProtocolLogo(Iec60870ProtocolMode mode)
    {
        var iconFile = mode switch
        {
            Iec60870ProtocolMode.Iec101 => "iec101-icon.png",
            Iec60870ProtocolMode.Iec104 => "iec104-icon.png",
            _ => "iec103-icon.png"
        };

        try
        {
            var source = new BitmapImage(new Uri($"pack://application:,,,/Assets/Icons/{iconFile}", UriKind.Absolute));
            ProtocolLogoImage.Source = source;
            Icon = source;
        }
        catch
        {
            // Keep the default app icon if a resource is unavailable in a developer build.
        }
    }

    private Iec60870ProtocolMode GetSelectedProtocolMode()
    {
        var protocol = (ProtocolModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
        if (protocol.Contains("104", StringComparison.OrdinalIgnoreCase)) return Iec60870ProtocolMode.Iec104;
        if (protocol.Contains("101", StringComparison.OrdinalIgnoreCase)) return Iec60870ProtocolMode.Iec101;
        return Iec60870ProtocolMode.Iec103;
    }

    private bool IsDemoModeSelected()
    {
        var mode = (TransportModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
        return mode.Contains("demo", StringComparison.OrdinalIgnoreCase) || mode.Contains("simulated", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadComboInt(ComboBox comboBox, string label)
    {
        var value = (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = comboBox.Text;
        }

        if (!int.TryParse(value?.Trim(), out var number))
        {
            throw new InvalidOperationException(label + " is invalid.");
        }

        return number;
    }

    private static int ReadInt(TextBox textBox, string label, int min, int max)
    {
        if (!int.TryParse(textBox.Text.Trim(), out var number))
        {
            throw new InvalidOperationException(label + " must be a number.");
        }

        if (number < min || number > max)
        {
            throw new InvalidOperationException($"{label} must be between {min} and {max}.");
        }

        return number;
    }


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
                "This is normally survivable. If it persists, reduce trace verbosity, keep Protocol Trace tab inactive during long tests, or increase polling interval for low-baud serial links.");
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
                "The protocol engine continues to protect important evidence. For smoother UI, avoid leaving high-volume Protocol Trace visible during long IEC-101/104 polling sessions.");
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
            EvidenceRows.AddRange(_pendingEvidenceSummaryUiRows);
            _pendingEvidenceSummaryUiRows.Clear();
            _visibleEvidenceDropped += EvidenceRows.TrimStart(MaxVisibleEvidenceRows);
        }

        if (_pendingProtocolTraceUiRows.Count > 0)
        {
            FrameTraceRows.AddRange(_pendingProtocolTraceUiRows);
            _pendingProtocolTraceUiRows.Clear();
            _visibleEvidenceDropped += FrameTraceRows.TrimStart(MaxVisibleFrameTraceRows);
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

        _lastVisibleBatchRows = batchRows;
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
    }


    private enum TraceVerbosityMode
    {
        Proof,
        Balanced,
        Full
    }

    private TraceVerbosityMode GetTraceVerbosityMode()
    {
        var text = (TraceVerbosityComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString()
                   ?? TraceVerbosityComboBox?.Text
                   ?? "Balanced";

        if (text.Contains("Full", StringComparison.OrdinalIgnoreCase))
        {
            return TraceVerbosityMode.Full;
        }

        if (text.Contains("Proof", StringComparison.OrdinalIgnoreCase))
        {
            return TraceVerbosityMode.Proof;
        }

        return TraceVerbosityMode.Balanced;
    }

    private void TraceVerbosityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BufferStatusText is not null)
        {
            UpdateBufferStatus();
        }

        if (IsLoaded && SessionLogBox is not null)
        {
            AppendSessionLog($"Protocol Trace mode: {GetTraceVerbosityMode()}. Critical evidence remains protected; routine trace retention follows selected mode.");
        }
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

    private void ApplyEvidenceToUi(Iec103MasterEvidenceEvent item)
    {
        var row = new EvidenceRow(item, ResolveIoaPoint(item));

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
        // detailed per-frame state belongs in Evidence Summary / Protocol Trace.

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

        // Do not pollute the summary with routine line traffic. Protocol Trace remains the source of truth for these.
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
            // Analog measurement scan is high-volume. Value Viewer must stay live, but Evidence Summary
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
        ExportMarkdownButton.IsEnabled = true;

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

    private void EvidenceGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedItem = sender switch
        {
            DataGrid grid => grid.SelectedItem,
            ListBox listBox => listBox.SelectedItem,
            _ => null
        };

        if (selectedItem is not EvidenceRow row)
        {
            _selectedFrameRow = null;
            SelectedDetailText.Text = "Select evidence row to inspect decoded meaning.";
            SelectedRawText.Text = "-";
            _selectedFrameExplanation = "Select a frame. This panel translates raw bytes into commissioning meaning.";
            SelectedLineSummaryText.Text = _selectedFrameExplanation;
            SelectedLineDirectionText.Text = "Select a frame";
            SelectedLineSummaryText.Text = "The selected IEC 60870 frame will be decoded into transport/link layer, ASDU/APCI fields, address, value/time, and integrity groups.";
            SelectedProtocolMapLines.Clear();
            SelectedHexSegments.Clear();
            UpdateFrameInterpreterTone(null);
            if (ActiveProtocolMapText is not null)
            {
                ActiveProtocolMapText.Text = "linked highlight";
            }
            return;
        }

        _selectedFrameRow = row;
        _pinnedProtocolMapKey = null;
        if (PinProtocolMapCheckBox != null)
        {
            PinProtocolMapCheckBox.IsChecked = false;
        }

        var explanation = BuildCompactFrameExplanation(row);
        SelectedDetailText.Text = explanation + Environment.NewLine + Environment.NewLine + "Raw: " + row.RawHex;
        SelectedRawText.Text = row.RawHex;
        _selectedFrameExplanation = explanation;
        SelectedLineSummaryText.Text = "Hover or click a protocol group. The panel stays stable; linked raw/meaning groups are highlighted without rewriting the inspector.";
        SelectedLineDirectionText.Text = BuildLineMonitorTitle(row);
        SelectedLineSummaryText.Text = BuildLineMonitorSummary(row);
        UpdateFrameInterpreterTone(row);
        RebuildProtocolMap(row);
        ActivateDefaultProtocolMapGroup(row);
    }


    private void ActivateDefaultProtocolMapGroup(EvidenceRow row)
    {
        if (PinProtocolMapCheckBox?.IsChecked == true && !string.IsNullOrWhiteSpace(_pinnedProtocolMapKey))
        {
            SetActiveProtocolMap(_pinnedProtocolMapKey);
            return;
        }

        var key = row.ProtocolMode switch
        {
            "104" when row.ApciFormat == "I" && row.IoAddress != "-" => "object",
            "104" => "apci",
            "101" when row.IoAddress != "-" => "object",
            "101" when row.TypeId != "-" => "asdu",
            "103" when row.FunInf != "-" => "asdu",
            _ => "raw"
        };

        SetActiveProtocolMap(key);
    }

    private static string DescribeProtocolMapKey(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "apci" => "APCI selected",
            "ft12" => "FT1.2 selected",
            "control" => "link control selected",
            "asdu" => "ASDU header selected",
            "object" => "object address selected",
            "payload" => "payload selected",
            "value" => "value selected",
            "check" => "integrity selected",
            "raw" => "raw frame selected",
            _ => key + " selected"
        };
    }


    private void UpdateFrameInterpreterTone(EvidenceRow? row)
    {
        if (FrameInterpreterPanel is null)
        {
            return;
        }

        var tone = row?.TrafficTone ?? string.Empty;
        var background = tone switch
        {
            "Tx" => Color.FromRgb(245, 250, 255),
            "Rx" => Color.FromRgb(244, 255, 249),
            "Error" => Color.FromRgb(255, 245, 245),
            _ => Color.FromRgb(248, 251, 255)
        };
        var border = tone switch
        {
            "Tx" => Color.FromRgb(191, 219, 254),
            "Rx" => Color.FromRgb(187, 247, 208),
            "Error" => Color.FromRgb(254, 202, 202),
            _ => Color.FromRgb(226, 232, 240)
        };

        FrameInterpreterPanel.Background = new SolidColorBrush(background);
        FrameInterpreterPanel.BorderBrush = new SolidColorBrush(border);
    }

    private static string BuildCompactFrameExplanation(EvidenceRow row)
    {
        var parts = new List<string>();
        parts.Add(row.ReadableMeaning);

        if (!string.IsNullOrWhiteSpace(row.SignalOrAddress) && row.SignalOrAddress != "-")
        {
            parts.Add($"Address: {row.SignalOrAddress}.");
        }

        if (!string.IsNullOrWhiteSpace(row.SemanticState))
        {
            parts.Add($"Value: {row.SemanticState}.");
        }

        parts.Add(row.ProtocolMode switch
        {
            "104" => $"Protocol: IEC-104 {row.Direction}, APCI={row.ApciFormat}, NS={row.SendSequence}, NR={row.ReceiveSequence}, Type ID={row.TypeIdName}, COT={row.CotDisplay}, CA={row.CommonAddress}, IOA={row.IoAddress}.",
            "101" => $"Protocol: IEC-101 {row.Direction} {row.DataClass}, Link={row.LinkAddress}, Type ID={row.TypeIdName}, COT={row.CotDisplay}, CA={row.CommonAddress}, IOA={row.IoAddress}, ACD={row.Acd}, DFC={row.Dfc}.",
            _ => $"Protocol: IEC-103 {row.Direction} {row.DataClass}, ASDU={row.AsduType}, COT={row.Cot}, FUN/INF={row.FunInf}, ACD={row.Acd}, DFC={row.Dfc}."
        });

        if (!string.IsNullOrWhiteSpace(row.PollingReason) && row.PollingReason != "-")
        {
            parts.Add($"Why it happened: {row.PollingReason}.");
        }

        if (!string.IsNullOrWhiteSpace(row.OperatorAction))
        {
            parts.Add($"Recommended action: {row.OperatorAction}.");
        }

        if (!string.IsNullOrWhiteSpace(row.RelayTime) && row.RelayTime != "-")
        {
            parts.Add($"Relay time: {row.RelayTime}.");
        }

        return string.Join(Environment.NewLine, parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string BuildLineMonitorTitle(EvidenceRow row)
    {
        var arrow = row.Direction.Equals("TX", StringComparison.OrdinalIgnoreCase)
            ? "TX → Master to relay"
            : row.Direction.Equals("RX", StringComparison.OrdinalIgnoreCase)
                ? "RX ← Relay to master"
                : row.Direction;
        var cls = row.DataClass == "-" ? "Link" : row.DataClass;
        var service = row.ProtocolMode == "104"
            ? row.ProtocolService
            : row.AsduType == "-" ? row.Summary : row.AsduType;
        return $"{arrow} · {cls} · {service}";
    }

    private static string BuildLineMonitorSummary(EvidenceRow row)
    {
        var parts = new List<string> { row.ReadableMeaning };
        if (!string.IsNullOrWhiteSpace(row.ProtocolAddress) && row.ProtocolAddress != "-") parts.Add(row.ProtocolAddress);
        if (!string.IsNullOrWhiteSpace(row.RelayTime) && row.RelayTime != "-") parts.Add("relay time " + row.RelayTime);
        return string.Join(" · ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private void RebuildProtocolMap(EvidenceRow row)
    {
        SelectedProtocolMapLines.Clear();
        SelectedHexSegments.Clear();

        foreach (var line in BuildProtocolMapLines(row))
        {
            SelectedProtocolMapLines.Add(line);
        }

        foreach (var segment in BuildHexSegments(row))
        {
            SelectedHexSegments.Add(segment);
        }
    }

    private static IEnumerable<ProtocolMapLine> BuildProtocolMapLines(EvidenceRow row)
    {
        var bytes = SplitHexBytes(row.RawHex);
        var directionMeaning = row.Direction.Equals("TX", StringComparison.OrdinalIgnoreCase)
            ? "Master-to-relay frame. This is a tester action, not a relay event."
            : row.Direction.Equals("RX", StringComparison.OrdinalIgnoreCase)
                ? "Relay-to-master frame. This is relay evidence returned to the tester."
                : "Session note or diagnostic entry.";

        yield return new ProtocolMapLine("direction", "Direction", directionMeaning, row.Direction);

        if (bytes.Length == 0)
        {
            yield return new ProtocolMapLine("summary", "No raw frame", $"This row is a state/diagnostic note, not a physical {row.ProtocolName} frame.", "-");
            yield break;
        }

        if (row.ProtocolMode == "104")
        {
            yield return new ProtocolMapLine("envelope", "APDU envelope", "IEC-104 APDU starts with 0x68 and a length byte. It is TCP stream framing, not FT1.2 serial framing.", string.Join(" ", bytes.Take(Math.Min(2, bytes.Length))));
            if (bytes.Length >= 6)
            {
                yield return new ProtocolMapLine("control", "APCI control", $"Format={row.ApciFormat}, N(S)={row.SendSequence}, N(R)={row.ReceiveSequence}, U={row.UFormatName}. I/S/U format tells whether this is payload transfer, acknowledgement, or connection control.", string.Join(" ", bytes.Skip(2).Take(4)));
            }
            if (row.ApciFormat == "I" && bytes.Length > 6)
            {
                yield return new ProtocolMapLine("asdu", "ASDU header", BuildAsduHeaderMeaning(row), string.Join(" ", bytes.Skip(6).Take(Math.Min(6, Math.Max(0, bytes.Length - 6)))));
                yield return new ProtocolMapLine("object", "CA / IOA", BuildSignalAddressMeaning(row), row.ProtocolAddress);
                if (!string.IsNullOrWhiteSpace(row.SemanticState))
                {
                    yield return new ProtocolMapLine("payload", "Information element", BuildPayloadMeaning(row), row.SemanticState);
                }
            }
            yield break;
        }

        if (row.ProtocolMode == "101" && bytes[0].Equals("68", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 9)
        {
            yield return new ProtocolMapLine("envelope", "FT1.2 variable frame", "IEC-101 serial variable-length frame. The repeated length block and checksum protect the serial telegram.", string.Join(" ", bytes.Take(4)));
            yield return new ProtocolMapLine("control", "Link control", BuildControlMeaning(row), string.Join(" ", bytes.Skip(4).Take(2)));
            yield return new ProtocolMapLine("asdu", "ASDU header", BuildAsduHeaderMeaning(row), string.Join(" ", bytes.Skip(6).Take(Math.Min(6, Math.Max(0, bytes.Length - 8)))));
            yield return new ProtocolMapLine("object", "Information object address", BuildSignalAddressMeaning(row), row.ProtocolAddress);
            if (!string.IsNullOrWhiteSpace(row.SemanticState))
            {
                yield return new ProtocolMapLine("payload", "Information element", BuildPayloadMeaning(row), row.SemanticState);
            }
            yield return new ProtocolMapLine("check", "Integrity", "Checksum and end byte close the FT1.2 frame. Keep this as audit evidence when discussing serial quality.", string.Join(" ", bytes.Skip(Math.Max(0, bytes.Length - 2)).Take(2)));
            yield break;
        }

        if (bytes[0].Equals("E5", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ProtocolMapLine("envelope", "Single char ACK", "IEC FT1.2 single-character acknowledgement. The relay accepted the previous link/action frame.", "E5");
            yield break;
        }


        if (bytes[0].Equals("10", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 5)
        {
            yield return new ProtocolMapLine("envelope", "FT1.2 fixed frame", $"Short {row.ProtocolName} link frame. Used for reset, Class 1/Class 2 request, ACK, or NO DATA response.", string.Join(" ", bytes.Take(1)));
            yield return new ProtocolMapLine("control", "Control field", BuildControlMeaning(row), bytes.ElementAtOrDefault(1) ?? "-");
            yield return new ProtocolMapLine("address", "Link address", "Slave/link address on the serial IEC-60870 link.", bytes.ElementAtOrDefault(2) ?? "-");
            yield return new ProtocolMapLine("check", "Integrity", "Checksum and stop byte. This proves what was actually transmitted on the wire.", string.Join(" ", bytes.Skip(3).Take(2)));
            yield break;
        }

        if (bytes[0].Equals("68", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 9)
        {
            yield return new ProtocolMapLine("envelope", "FT1.2 variable frame", $"Variable {row.ProtocolName} frame carrying an ASDU. The length bytes define the payload size and must match.", string.Join(" ", bytes.Take(4)));
            yield return new ProtocolMapLine("control", "Link control", BuildControlMeaning(row), string.Join(" ", bytes.Skip(4).Take(2)));
            yield return new ProtocolMapLine("asdu", "ASDU header", BuildAsduHeaderMeaning(row), string.Join(" ", bytes.Skip(6).Take(Math.Min(4, Math.Max(0, bytes.Length - 8)))));

            if (bytes.Length > 11)
            {
                yield return new ProtocolMapLine("object", "FUN / INF", BuildSignalAddressMeaning(row), string.Join(" ", bytes.Skip(10).Take(2)));
            }

            var payloadEnd = Math.Max(12, bytes.Length - 2);
            if (payloadEnd > 12)
            {
                yield return new ProtocolMapLine("payload", "Information element", BuildPayloadMeaning(row), string.Join(" ", bytes.Skip(12).Take(payloadEnd - 12)));
            }

            yield return new ProtocolMapLine("check", "Integrity", "Checksum and end byte close the frame. Keep this as audit evidence when discussing interoperability.", string.Join(" ", bytes.Skip(Math.Max(0, bytes.Length - 2)).Take(2)));
            yield break;
        }

        yield return new ProtocolMapLine("raw", $"Raw {row.ProtocolName} bytes", "The analyzer preserved this frame as raw evidence, but it could not classify it into the expected protocol structure.", string.Join(" ", bytes));
    }

    private static string BuildControlMeaning(EvidenceRow row)
    {
        if (row.Direction == "TX")
        {
            return row.DataClass.Contains("Class 1", StringComparison.OrdinalIgnoreCase)
                ? "Master asks for pending Class 1 event data. This should be done only during ACD=1 event drain or bounded GI follow-up."
                : row.DataClass.Contains("Class 2", StringComparison.OrdinalIgnoreCase)
                    ? "Master performs normal Class 2 background polling."
                    : string.IsNullOrWhiteSpace(row.ReadableMeaning) ? "Master link/control action." : row.ReadableMeaning;
        }

        if (row.Direction == "RX")
        {
            if (row.Acd == "1")
            {
                return "Relay response indicates ACD=1, meaning Class 1 data is pending and the master may drain event data.";
            }

            if (row.ProtocolMeaning.Contains("FC=9", StringComparison.OrdinalIgnoreCase) || row.ReadableMeaning.Contains("ACK", StringComparison.OrdinalIgnoreCase))
            {
                return "Relay acknowledges the link/application command.";
            }

            return string.IsNullOrWhiteSpace(row.ProtocolMeaning) ? "Relay link response." : row.ProtocolMeaning;
        }

        return string.IsNullOrWhiteSpace(row.ReadableMeaning) ? "Link-layer control information." : row.ReadableMeaning;
    }

    private static string BuildAsduHeaderMeaning(EvidenceRow row)
    {
        if (row.ProtocolMode is "101" or "104")
        {
            if (row.TypeIdName == "-" && row.CotDisplay == "-")
            {
                return "No IEC-10x ASDU payload is present in this frame.";
            }

            return $"Type ID={row.TypeIdName}, VSQ={row.Vsq}, COT={row.CotDisplay}, CA={row.CommonAddress}. These fields define the telecontrol data class, cause, station/common address, and object addressing context.";
        }

        if (row.AsduType == "-" && row.Cot == "-")
        {
            return "No ASDU payload is present in this link frame.";
        }

        return $"ASDU={row.AsduType}, COT={row.Cot}. This tells the tester what kind of protection information is being transferred and why it was sent.";
    }

    private static string BuildSignalAddressMeaning(EvidenceRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.SemanticLabel))
        {
            return $"Mapped signal: {row.SemanticLabel}. Raw address remains {row.SignalOrAddress}.";
        }

        if (row.ProtocolMode is "101" or "104")
        {
            return row.IoAddress == "-"
                ? "This ASDU has no decoded IOA."
                : $"Information object address {row.IoAddress} inside common address {row.CommonAddress}. Add an IOA naming profile later to show a readable signal name.";
        }

        return row.FunInf == "-"
            ? "This ASDU has no decoded FUN/INF signal address."
            : $"Unmapped IEC-103 signal address {row.SignalOrAddress}. Add it to the user mapping profile to show a readable signal name.";
    }

    private static string BuildPayloadMeaning(EvidenceRow row)
    {
        var state = string.IsNullOrWhiteSpace(row.SemanticState) ? "state/value" : row.SemanticState;
        var time = string.IsNullOrWhiteSpace(row.RelayTime) || row.RelayTime == "-" ? "No field timestamp decoded." : $"Field timestamp: {row.RelayTime}.";
        var typeText = row.ProtocolMode is "101" or "104" ? row.TypeIdName : row.AsduType;

        if (typeText.Contains("Measur", StringComparison.OrdinalIgnoreCase))
        {
            return $"Measurement payload. Decoded value/state: {state}. Quality: {row.Quality}. {time}";
        }

        if (typeText.Contains("DPI", StringComparison.OrdinalIgnoreCase) ||
            typeText.Contains("SPI", StringComparison.OrdinalIgnoreCase) ||
            typeText.Contains("single", StringComparison.OrdinalIgnoreCase) ||
            typeText.Contains("double", StringComparison.OrdinalIgnoreCase) ||
            typeText.Contains("time-tagged", StringComparison.OrdinalIgnoreCase))
        {
            return $"Status/event payload. Decoded state: {state}. Quality: {row.Quality}. {time}";
        }

        return $"Information element payload. Decoded state/value: {state}. Quality: {row.Quality}. {time}";
    }

    private static string[] SplitHexBytes(string rawHex)
    {
        return rawHex
            .Split(new[] { ' ', '|', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x != "-")
            .ToArray();
    }

    private static IEnumerable<HexSegment> BuildHexSegments(EvidenceRow row)
    {
        var bytes = SplitHexBytes(row.RawHex);

        if (bytes.Length == 0)
        {
            yield break;
        }

        if (row.ProtocolMode == "104")
        {
            yield return new HexSegment("envelope", string.Join(" ", bytes.Take(Math.Min(2, bytes.Length))), "IEC-104 APDU", "0x68 start byte and APDU length for TCP stream framing.");
            if (bytes.Length >= 6)
            {
                yield return new HexSegment("control", string.Join(" ", bytes.Skip(2).Take(4)), "APCI control", $"Format={row.ApciFormat}, N(S)={row.SendSequence}, N(R)={row.ReceiveSequence}, U={row.UFormatName}.");
            }
            if (bytes.Length > 6)
            {
                yield return new HexSegment("asdu", string.Join(" ", bytes.Skip(6).Take(Math.Min(6, Math.Max(0, bytes.Length - 6)))), "ASDU header", BuildAsduHeaderMeaning(row));
                yield return new HexSegment("object", row.ProtocolAddress, "CA / IOA", BuildSignalAddressMeaning(row));
                if (!string.IsNullOrWhiteSpace(row.SemanticState))
                {
                    yield return new HexSegment("payload", row.SemanticState, "Value / quality", BuildPayloadMeaning(row));
                }
            }
            yield break;
        }

        if (row.ProtocolMode == "101" && bytes[0].Equals("68", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 9)
        {
            yield return new HexSegment("envelope", string.Join(" ", bytes.Take(4)), "FT1.2 variable frame", "IEC-101 variable-length serial frame envelope.");
            yield return new HexSegment("control", string.Join(" ", bytes.Skip(4).Take(2)), "Control + link", BuildControlMeaning(row));
            yield return new HexSegment("asdu", string.Join(" ", bytes.Skip(6).Take(Math.Min(6, Math.Max(0, bytes.Length - 8)))), "ASDU header", BuildAsduHeaderMeaning(row));
            yield return new HexSegment("object", row.ProtocolAddress, "Information object address", BuildSignalAddressMeaning(row));
            if (!string.IsNullOrWhiteSpace(row.SemanticState))
            {
                yield return new HexSegment("payload", row.SemanticState, "Value / quality", BuildPayloadMeaning(row));
            }
            yield return new HexSegment("check", string.Join(" ", bytes.Skip(Math.Max(0, bytes.Length - 2)).Take(2)), "Integrity", "Checksum and end byte.");
            yield break;
        }

        if (bytes[0].Equals("10", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 5)
        {
            yield return new HexSegment("envelope", bytes[0], "FT1.2 fixed frame", "Fixed-length link frame envelope.");
            yield return new HexSegment("control", bytes[1], "Control", BuildControlMeaning(row));
            yield return new HexSegment("address", bytes[2], "Link address", "Relay/slave link address.");
            yield return new HexSegment("check", string.Join(" ", bytes.Skip(3).Take(2)), "Integrity", "Checksum and end byte.");
            yield break;
        }

        if (bytes[0].Equals("68", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 9)
        {
            yield return new HexSegment("envelope", string.Join(" ", bytes.Take(4)), "FT1.2 variable frame", "Variable frame start and length block.");
            yield return new HexSegment("control", string.Join(" ", bytes.Skip(4).Take(2)), "Control + link", BuildControlMeaning(row));
            yield return new HexSegment("asdu", string.Join(" ", bytes.Skip(6).Take(Math.Min(4, Math.Max(0, bytes.Length - 8)))), "ASDU header", BuildAsduHeaderMeaning(row));

            if (bytes.Length > 11)
            {
                yield return new HexSegment("object", string.Join(" ", bytes.Skip(10).Take(2)), "Signal address", BuildSignalAddressMeaning(row));
            }

            var payloadEnd = Math.Max(12, bytes.Length - 2);
            if (payloadEnd > 12)
            {
                yield return new HexSegment("payload", string.Join(" ", bytes.Skip(12).Take(payloadEnd - 12)), "State / value / relay time", BuildPayloadMeaning(row));
            }

            yield return new HexSegment("check", string.Join(" ", bytes.Skip(Math.Max(0, bytes.Length - 2)).Take(2)), "Integrity", "Checksum and end byte.");
            yield break;
        }

        yield return new HexSegment("raw", string.Join(" ", bytes), "Raw frame", "Frame bytes are preserved as evidence. This frame is not recognized by the high-level mapper.");
    }

    private void HexSegment_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_pinnedProtocolMapKey != null && PinProtocolMapCheckBox?.IsChecked == true)
        {
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is HexSegment segment)
        {
            SetActiveProtocolMap(segment.Key);
        }
    }

    private void HexSegment_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_pinnedProtocolMapKey != null && PinProtocolMapCheckBox?.IsChecked == true)
        {
            return;
        }

        ClearActiveProtocolMap();
    }

    private void HexSegment_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HexSegment segment)
        {
            _pinnedProtocolMapKey = segment.Key;
            if (PinProtocolMapCheckBox != null)
            {
                PinProtocolMapCheckBox.IsChecked = true;
            }
            SetActiveProtocolMap(segment.Key);
        }
    }

    private void ProtocolMapLine_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_pinnedProtocolMapKey != null && PinProtocolMapCheckBox?.IsChecked == true)
        {
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is ProtocolMapLine line)
        {
            SetActiveProtocolMap(line.Key);
        }
    }

    private void ProtocolMapLine_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_pinnedProtocolMapKey != null && PinProtocolMapCheckBox?.IsChecked == true)
        {
            return;
        }

        ClearActiveProtocolMap();
    }

    private void ProtocolMapLine_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProtocolMapLine line)
        {
            _pinnedProtocolMapKey = line.Key;
            if (PinProtocolMapCheckBox != null)
            {
                PinProtocolMapCheckBox.IsChecked = true;
            }
            SetActiveProtocolMap(line.Key);
        }
    }

    private void ClearProtocolMapHighlight_Click(object sender, RoutedEventArgs e)
    {
        _pinnedProtocolMapKey = null;
        if (PinProtocolMapCheckBox != null)
        {
            PinProtocolMapCheckBox.IsChecked = false;
        }
        ClearActiveProtocolMap();
    }

    private void CopySelectedRawFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFrameRow is null || string.IsNullOrWhiteSpace(_selectedFrameRow.RawHex) || _selectedFrameRow.RawHex == "-")
        {
            return;
        }

        Clipboard.SetText(_selectedFrameRow.RawHex);
        AppendSessionLog($"Copied raw frame #{_selectedFrameRow.Sequence} to clipboard.");
    }

    private void CopySelectedFrameDecode_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFrameRow is null)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Frame #{_selectedFrameRow.Sequence} {BuildLineMonitorTitle(_selectedFrameRow)}");
        builder.AppendLine(BuildCompactFrameExplanation(_selectedFrameRow));
        builder.AppendLine();
        builder.AppendLine("Raw: " + _selectedFrameRow.RawHex);
        Clipboard.SetText(builder.ToString());
        AppendSessionLog($"Copied decoded frame #{_selectedFrameRow.Sequence} to clipboard.");
    }

    private void SetActiveProtocolMap(string key)
    {
        var matched = false;

        foreach (var line in SelectedProtocolMapLines)
        {
            line.IsActive = string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase);
            matched |= line.IsActive;
        }

        foreach (var segment in SelectedHexSegments)
        {
            segment.IsActive = string.Equals(segment.Key, key, StringComparison.OrdinalIgnoreCase);
            matched |= segment.IsActive;
        }

        if (ActiveProtocolMapText is not null)
        {
            ActiveProtocolMapText.Text = matched ? DescribeProtocolMapKey(key) : "linked highlight";
        }
    }

    private void ClearActiveProtocolMap()
    {
        foreach (var line in SelectedProtocolMapLines)
        {
            line.IsActive = false;
        }

        foreach (var segment in SelectedHexSegments)
        {
            segment.IsActive = false;
        }

        if (ActiveProtocolMapText is not null)
        {
            ActiveProtocolMapText.Text = "linked highlight";
        }
    }


    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabControl))
        {
            return;
        }

        RefreshActiveTraceSnapshot();
        ExportDataButton.IsEnabled = GetCurrentTabDataGrid() is not null;
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
    {
        return new[]
        {
            NavOperatorButton,
            NavFrameButton,
            NavValueButton,
            NavEventButton,
            NavAssessmentButton,
            NavFindingsButton,
            NavDiagnosticsButton,
            NavNotesButton
        };
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

        var buttons = GetSegmentedNavButtons();
        var index = Math.Clamp(MainTabControl.SelectedIndex, 0, buttons.Length - 1);
        var inactiveBrush = (Brush)FindResource("Ink600Brush");
        var activeForegroundBrush = (Brush)FindResource("Ink900Brush");
        var activeBackgroundBrush = (Brush)FindResource("AccentSoftBrush");
        var activeBorderBrush = (Brush)FindResource("AccentBrush");
        var transparentBrush = Brushes.Transparent;

        for (var i = 0; i < buttons.Length; i++)
        {
            var isActive = i == index;
            buttons[i].Background = isActive ? activeBackgroundBrush : transparentBrush;
            buttons[i].BorderBrush = isActive ? activeBorderBrush : transparentBrush;
            buttons[i].Foreground = isActive ? activeForegroundBrush : inactiveBrush;
            buttons[i].FontWeight = isActive ? FontWeights.Medium : FontWeights.Normal;
        }
    }



    private void SaveSelectedCapture_Click(object sender, RoutedEventArgs e)
    {
        var rows = GetSelectedProtocolTraceRowsForCapture();
        if (rows.Count == 0)
        {
            MessageBox.Show(this,
                "Select one or more Protocol Trace rows first. Use Ctrl/Shift selection to save a block as an ARIEC capture.",
                "Save capture",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save selected Protocol Trace rows as ARIEC capture",
            Filter = "ARIEC capture (*.ariec)|*.ariec|Zip container (*.zip)|*.zip|All files (*.*)|*.*",
            FileName = $"ARIEC60870-selected-capture-{DateTime.Now:yyyyMMdd-HHmmss}.ariec",
            AddExtension = true,
            DefaultExt = ".ariec"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            WriteSelectedProtocolTraceCapture(dialog.FileName, rows);
            AddUiDiagnostic(
                "Info",
                "Capture",
                "ARIEC-CAPTURE-SELECTION-SAVED",
                "Selected Protocol Trace block saved as capture",
                $"Saved {rows.Count} selected Protocol Trace rows to {dialog.FileName}.",
                "This selected-block capture is portable evidence and will be supported by offline re-open/review mode in the next capture phase.");
            AppendSessionLog($"Selected Protocol Trace capture saved: {rows.Count} rows -> {dialog.FileName}");
            MessageBox.Show(this,
                $"Selected capture saved successfully.\n\nRows: {rows.Count}\nFile: {dialog.FileName}",
                "Save capture",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AddUiDiagnostic(
                "Error",
                "Capture",
                "ARIEC-CAPTURE-SELECTION-FAILED",
                "Failed to save selected Protocol Trace capture",
                ex.Message,
                "Check destination write permission and available disk space.",
                ex);
            MessageBox.Show(this,
                "Failed to save capture: " + ex.Message,
                "Save capture",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private IReadOnlyList<EvidenceRow> GetSelectedProtocolTraceRowsForCapture()
    {
        var selected = FrameTraceGrid?.SelectedItems
            ?.OfType<EvidenceRow>()
            .OrderBy(row => row.Sequence)
            .ToArray();

        if (selected is { Length: > 0 })
        {
            return selected;
        }

        if (FrameTraceGrid?.SelectedItem is EvidenceRow single)
        {
            return new[] { single };
        }

        return Array.Empty<EvidenceRow>();
    }

    private void WriteSelectedProtocolTraceCapture(string fileName, IReadOnlyList<EvidenceRow> rows)
    {
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("No Protocol Trace rows selected.");
        }

        var createdUtc = DateTime.UtcNow;
        var captureId = "ARIEC-" + createdUtc.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var framesJsonl = BuildCaptureFramesJsonl(rows);
        var framesSha256 = ComputeSha256(framesJsonl);
        var manifest = BuildSelectedCaptureManifest(captureId, createdUtc, rows, framesSha256);
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        var retentionJson = JsonSerializer.Serialize(BuildCaptureRetentionSnapshot(), new JsonSerializerOptions { WriteIndented = true });
        var reportMarkdown = BuildSelectedCaptureMarkdownReport(manifest, rows, framesSha256);

        var target = Path.GetFullPath(fileName);
        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (File.Exists(target))
        {
            File.Delete(target);
        }

        using var archive = ZipFile.Open(target, ZipArchiveMode.Create);
        WriteZipTextEntry(archive, "manifest.json", manifestJson);
        WriteZipTextEntry(archive, "frames.jsonl", framesJsonl);
        WriteZipTextEntry(archive, "retention.json", retentionJson);
        WriteZipTextEntry(archive, "report.md", reportMarkdown);
        WriteZipTextEntry(archive, "hash.txt", $"frames.jsonl sha256 {framesSha256}{Environment.NewLine}");
    }

    private CaptureManifest BuildSelectedCaptureManifest(string captureId, DateTime createdUtc, IReadOnlyList<EvidenceRow> rows, string framesSha256)
    {
        var first = rows.First();
        var last = rows.Last();
        return new CaptureManifest
        {
            Format = "ARIEC_CAPTURE_V1",
            CaptureId = captureId,
            CaptureKind = "SelectedProtocolTraceBlock",
            CreatedUtc = createdUtc,
            Application = "ARIEC60870 Protocol Lab",
            ProtocolMode = GetSelectedProtocolMode().ToString(),
            TraceVerbosityMode = GetTraceVerbosityMode().ToString(),
            RowCount = rows.Count,
            FirstSequence = first.Sequence,
            LastSequence = last.Sequence,
            FirstTimestampText = first.Time,
            LastTimestampText = last.Time,
            FramesSha256 = framesSha256,
            SourceSession = new CaptureSessionSnapshot
            {
                TxCount = _txCount,
                RxCount = _rxCount,
                GiCount = _giCount,
                Class1Count = _class1Count,
                Class2Count = _class2Count,
                NoDataCount = _noDataCount,
                DpiCount = _dpiCount,
                ValueRows = ValueRows.Count,
                EventRows = RelayEventRows.Count,
                DiagnosticRows = DiagnosticRows.Count,
                TraceRowsVisible = FrameTraceRows.Count,
                TraceRowsLimit = MaxVisibleFrameTraceRows,
                TraceSuppressedRows = _traceVerbositySuppressedRows,
                BackpressureDroppedRows = _backpressureDroppedEvents,
                QueueMaxObserved = _maxPendingEvidenceDepth,
                MaxUiFlushMs = _maxUiFlushMs
            }
        };
    }

    private object BuildCaptureRetentionSnapshot()
    {
        return new
        {
            policy = "Selected capture is generated from visible Protocol Trace rows. Full lossless background ledger is a separate capture phase.",
            retention = BuildEvidenceRetentionPolicyLines().ToArray(),
            trace = new
            {
                mode = GetTraceVerbosityMode().ToString(),
                visible = FrameTraceRows.Count,
                limit = MaxVisibleFrameTraceRows,
                suppressed = _traceVerbositySuppressedRows,
                routineSuppressed = _traceVerbositySuppressedRoutine,
                supervisorySuppressed = _traceVerbositySuppressedSupervisory
            },
            backpressure = new
            {
                dropped = _backpressureDroppedEvents,
                ackNoData = _backpressureDroppedAckNoData,
                backgroundPoll = _backpressureDroppedBackgroundPoll,
                testSupervisory = _backpressureDroppedTestFrames,
                other = _backpressureDroppedOtherLowValue
            },
            proof = new
            {
                caObserved = _proofObservedCa,
                giObserved = _proofGiObserved,
                giCompleted = _proofGiCompleted,
                giNegative = _proofGiNegative,
                digitalObserved = _proofDigitalObserved,
                analogObserved = _proofAnalogObserved,
                commandObserved = _proofCommandObserved,
                commandFeedbackObserved = _proofCommandFeedbackObserved,
                monitorCoverage = $"{_lastMonitorReceivedCount}/{_lastMonitorExpectedCount}",
                digitalCoverage = $"{_lastDigitalReceivedCount}/{_lastDigitalExpectedCount}",
                analogCoverage = $"{_lastAnalogReceivedCount}/{_lastAnalogExpectedCount}"
            }
        };
    }

    private static string BuildCaptureFramesJsonl(IReadOnlyList<EvidenceRow> rows)
    {
        var builder = new StringBuilder();
        var options = new JsonSerializerOptions { WriteIndented = false };

        foreach (var row in rows)
        {
            var record = CaptureFrameRecord.FromEvidenceRow(row);
            builder.AppendLine(JsonSerializer.Serialize(record, options));
        }

        return builder.ToString();
    }

    private string BuildSelectedCaptureMarkdownReport(CaptureManifest manifest, IReadOnlyList<EvidenceRow> rows, string framesSha256)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# ARIEC60870 Selected Protocol Trace Capture");
        builder.AppendLine();
        builder.AppendLine($"- Capture ID: `{manifest.CaptureId}`");
        builder.AppendLine($"- Format: `{manifest.Format}`");
        builder.AppendLine($"- Kind: `{manifest.CaptureKind}`");
        builder.AppendLine($"- Created UTC: `{manifest.CreatedUtc:O}`");
        builder.AppendLine($"- Protocol mode: `{manifest.ProtocolMode}`");
        builder.AppendLine($"- Trace mode: `{manifest.TraceVerbosityMode}`");
        builder.AppendLine($"- Rows: `{manifest.RowCount}`");
        builder.AppendLine($"- Sequence range: `{manifest.FirstSequence}` → `{manifest.LastSequence}`");
        builder.AppendLine($"- frames.jsonl SHA256: `{framesSha256}`");
        builder.AppendLine();
        builder.AppendLine("## Evidence Retention / Capture Integrity");
        builder.AppendLine();
        foreach (var line in BuildEvidenceRetentionPolicyLines())
        {
            builder.AppendLine("- " + line);
        }
        builder.AppendLine();
        builder.AppendLine("## Selected Line Monitor Rows");
        builder.AppendLine();
        builder.AppendLine("| # | Time | Dir | Service | Address | Meaning | Raw |");
        builder.AppendLine("|---:|---|---|---|---|---|---|");

        foreach (var row in rows)
        {
            builder.Append("| ")
                .Append(row.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" | ")
                .Append(EscapeMarkdownTable(row.Time)).Append(" | ")
                .Append(EscapeMarkdownTable(row.Direction)).Append(" | ")
                .Append(EscapeMarkdownTable(row.ProtocolService)).Append(" | ")
                .Append(EscapeMarkdownTable(row.ProtocolAddress)).Append(" | ")
                .Append(EscapeMarkdownTable(row.ProtocolTraceMeaning)).Append(" | ")
                .Append(EscapeMarkdownTable(row.RawHex)).AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("> Selected-block capture is portable evidence. Offline re-open/review mode will consume `manifest.json` and `frames.jsonl` in the next capture phase.");
        return builder.ToString();
    }

    private static string EscapeMarkdownTable(string value)
        => (value ?? string.Empty)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

    private static void WriteZipTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content ?? string.Empty);
    }

    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private void ExportData_Click(object sender, RoutedEventArgs e)
    {
        var grid = GetCurrentTabDataGrid();
        if (grid is null)
        {
            MessageBox.Show(this, "The selected tab does not contain exportable grid data.", "Export Data", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var tabName = (MainTabControl.SelectedItem as TabItem)?.Header?.ToString() ?? "data";
        var safeName = string.Concat(tabName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');
        var dialog = new SaveFileDialog
        {
            Title = "Export selected tab data",
            Filter = "Tab-separated text (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"ARIEC60870-{safeName}.txt",
            AddExtension = true,
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var exportText = BuildTextEvidenceRetentionHeader(tabName) + BuildTabSeparatedText(grid);
        File.WriteAllText(dialog.FileName, exportText, Encoding.UTF8);
        AddEvidenceRetentionExportMarker($"Tab export: {tabName}");
        AppendSessionLog($"Data exported from {tabName} with retention policy marker: {dialog.FileName}");
    }

    private DataGrid? GetCurrentTabDataGrid()
    {
        var header = (MainTabControl.SelectedItem as TabItem)?.Header?.ToString() ?? string.Empty;
        return header switch
        {
            "Evidence Summary" => EvidenceGrid,
            "Value Viewer" => ValueGrid,
            "Event Log" => RelayEventGrid,
            "AutoTest Assessment" => AssessmentGrid,
            "Findings" => FindingsGrid,
            "Diagnostics" => DiagnosticsGrid,
            _ => null
        };
    }

    private static string BuildTabSeparatedText(DataGrid grid)
    {
        var visibleColumns = grid.Columns
            .Where(c => c.Visibility == Visibility.Visible)
            .OrderBy(c => c.DisplayIndex)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine(string.Join("\t", visibleColumns.Select(c => EscapeTabValue(c.Header?.ToString() ?? string.Empty))));

        foreach (var item in grid.ItemsSource?.Cast<object>() ?? Enumerable.Empty<object>())
        {
            var values = visibleColumns.Select(column => EscapeTabValue(ReadGridColumnValue(column, item)));
            builder.AppendLine(string.Join("\t", values));
        }

        return builder.ToString();
    }

    private static string ReadGridColumnValue(DataGridColumn column, object item)
    {
        if (column is DataGridTextColumn textColumn && textColumn.Binding is Binding binding && binding.Path is not null)
        {
            var path = binding.Path.Path;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var value = item.GetType().GetProperty(path)?.GetValue(item);
                return value?.ToString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string EscapeTabValue(string value)
    {
        return (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
    }

    private void DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            return;
        }

        if (!row.IsSelected)
        {
            grid.SelectedItems.Clear();
            row.IsSelected = true;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T target)
            {
                return target;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private void BrowseMapping_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = GetSelectedProtocolMode() == Iec60870ProtocolMode.Iec103 ? "Open IEC-103 FUN/INF Mapping Profile" : "Open IEC-101/104 IOA Point Profile",
            Filter = "ARIEC60870 mapping profile (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        TryLoadMappingProfile(dialog.FileName, showMessage: true);
        SaveSetupPreferencesFromUi(silent: true);
    }

    private void TryLoadMappingProfile(string fileName, bool showMessage)
    {
        try
        {
            if (GetSelectedProtocolMode() == Iec60870ProtocolMode.Iec103)
            {
                _mappingProfile = Iec103SignalMappingProfile.LoadFromFile(fileName);
                MappingProfilePathBox.Text = fileName;
                MappingProfileStatusText.Text = $"Loaded: {_mappingProfile.ProfileName} ({_mappingProfile.Signals.Count} signals)";
                AppendSessionLog("IEC-103 mapping profile loaded: " + _mappingProfile.ProfileName);
            }
            else
            {
                _ioaProfile = Iec10xPointMappingProfile.LoadFromFile(fileName);
                MappingProfilePathBox.Text = fileName;
                ApplyIoaProfileDefaultsToUi(_ioaProfile, onlyWhenUiLooksDefault: false);
                var scenarioText = _ioaProfile.TestScenarios.Count > 0 ? $", {_ioaProfile.TestScenarios.Count} test scenarios" : string.Empty;
                MappingProfileStatusText.Text = $"Loaded: {_ioaProfile.ProfileName} ({_ioaProfile.Points.Count} IOA points{scenarioText})";
                RefreshIoaProfileRows();
                AppendSessionLog("IEC-101/104 IOA profile loaded: " + _ioaProfile.ProfileName);
            }
        }
        catch (Exception ex)
        {
            AddUiDiagnostic("Warning", "Mapping", "IEC60870-MAPPING-LOAD", "Mapping profile could not be loaded", ex.Message, "Check JSON syntax and schema. IEC-103 uses FUN/INF schema; IEC-101/104 uses ariec10x-ioa-profile-v1.", ex);
            if (showMessage)
            {
                MessageBox.Show(this, ex.Message, "Mapping profile error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void ClearMapping_Click(object sender, RoutedEventArgs e)
    {
        _mappingProfile = Iec103SignalMappingProfile.Empty;
        _ioaProfile = Iec10xPointMappingProfile.Empty;
        MappingProfilePathBox.Text = string.Empty;
        RefreshIoaProfileRows();
        MappingProfileStatusText.Text = GetSelectedProtocolMode() == Iec60870ProtocolMode.Iec103
            ? "No mapping profile loaded. Raw FUN/INF will be shown."
            : "No IOA profile loaded. Raw IOA labels will be shown.";
        AppendSessionLog("Mapping profile cleared.");
        SaveSetupPreferencesFromUi(silent: true);
    }

    private void EditSignalList_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedProtocolMode() == Iec60870ProtocolMode.Iec103)
        {
            MessageBox.Show(this,
                "Signal List Editor is for IEC-101/104 IOA mapping profiles. IEC-103 uses FUN/INF mapping and will get a dedicated editor later.",
                "Signal List Editor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var editor = new SignalListEditorWindow(_ioaProfile.HasPoints ? _ioaProfile : Iec10xPointMappingProfile.Empty, MappingProfilePathBox.Text)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            _ioaProfile = editor.Profile;
            MappingProfilePathBox.Text = editor.SavedProfilePath;
            ApplyIoaProfileDefaultsToUi(_ioaProfile, onlyWhenUiLooksDefault: false);
            var scenarioText = _ioaProfile.TestScenarios.Count > 0 ? $", {_ioaProfile.TestScenarios.Count} test scenarios" : string.Empty;
            MappingProfileStatusText.Text = $"Loaded: {_ioaProfile.ProfileName} ({_ioaProfile.Points.Count} IOA points{scenarioText})";
            RefreshIoaProfileRows();
            AppendSessionLog("IEC-101/104 IOA profile edited and applied: " + _ioaProfile.ProfileName);
            SaveSetupPreferencesFromUi(silent: true);
        }
    }


    private Iec10xPointMappingEntry? ResolveIoaPoint(Iec103MasterEvidenceEvent item)
    {
        return item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104
            ? _ioaProfile.Resolve(item.CommonAddressNumber, item.InformationObjectAddress, item.TypeId)
            : null;
    }

    private static string ExtractSimpleStateToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var equals = trimmed.IndexOf('=');
        if (equals >= 0 && equals < trimmed.Length - 1)
        {
            var right = trimmed[(equals + 1)..].Trim();
            var comma = right.IndexOf(',');
            return comma > 0 ? right[..comma].Trim() : right;
        }

        var comma2 = trimmed.IndexOf(',');
        return comma2 > 0 ? trimmed[..comma2].Trim() : trimmed;
    }

    private void UpdateValueAndEventViews(Iec103MasterEvidenceEvent item)
    {
        var shouldShowValue = item.IsRelayValue || IsIec10xProcessValue(item);
        var shouldShowEdgeEvent = item.IsRelayEdgeEvent || IsIec10xDigitalEdgeEvent(item);
        var ioaPoint = ResolveIoaPoint(item);
        var key = BuildValueKey(item);
        _lastDisplayedValueByKey.TryGetValue(key, out var previousValueBeforeUpdate);
        if (shouldShowValue)
        {
            MarkGiValueReceived(key);
        }
        ReportGiCompletenessIfReady(item);

        var fallbackSignal = BuildFallbackSignalName(item);
        var displayValue = !string.IsNullOrWhiteSpace(item.SignalDisplayValue)
            ? item.SignalDisplayValue
            : !string.IsNullOrWhiteSpace(item.ObjectSummary)
                ? item.ObjectSummary
                : item.SignalRawValue;
        if (ioaPoint is not null)
        {
            displayValue = ioaPoint.ResolveDisplayValue(ExtractSimpleStateToken(displayValue));
        }

        var previousValue = previousValueBeforeUpdate;
        if (string.IsNullOrWhiteSpace(previousValue) && !string.IsNullOrWhiteSpace(item.PreviousSignalValue))
        {
            previousValue = item.PreviousSignalValue;
        }
        if (ioaPoint is not null && !string.IsNullOrWhiteSpace(previousValue))
        {
            previousValue = ioaPoint.ResolveDisplayValue(ExtractSimpleStateToken(previousValue));
        }

        var hasMeaningfulChange = HasKnownStateTransition(previousValue, displayValue);
        var keepValueHighlight = _valueHighlightExpiryByKey.TryGetValue(key, out var highlightUntil) && highlightUntil > DateTime.UtcNow;

        if (shouldShowValue)
        {
            var valueRow = new ValueRow(new Iec103ValuePoint
            {
                Key = key,
                IsMapped = item.IsMappedSignal || ioaPoint is not null,
                SignalName = ioaPoint?.Name ?? (string.IsNullOrWhiteSpace(item.SignalName) ? fallbackSignal : item.SignalName),
                SignalGroup = ioaPoint?.Group ?? (string.IsNullOrWhiteSpace(item.SignalGroup) ? BuildFallbackSignalGroup(item) : item.SignalGroup),
                SignalType = !string.IsNullOrWhiteSpace(ioaPoint?.SignalType) ? ioaPoint!.SignalType : (!string.IsNullOrWhiteSpace(item.SignalType) ? item.SignalType : (item.AsduType ?? string.Empty)),
                FunctionType = item.FunctionType,
                InformationNumber = item.InformationNumber,
                RawValue = string.IsNullOrWhiteSpace(item.SignalRawValue) ? item.ObjectSummary : item.SignalRawValue,
                DisplayValue = displayValue,
                Source = item.Cot ?? string.Empty,
                CauseOfTransmission = item.Cot ?? string.Empty,
                AsduType = item.AsduType ?? string.Empty,
                RelayTimeText = string.IsNullOrWhiteSpace(item.RelayTimestampText) ? "no timestamp" : item.RelayTimestampText,
                RelayTimeInvalid = item.RelayTimestampInvalid,
                ArrivalTimeUtc = item.TimestampUtc,
                RawHex = item.RawHex,
                ProtocolMode = item.ProtocolMode,
                CommonAddress = item.CommonAddressNumber,
                InformationObjectAddress = item.InformationObjectAddress,
                TypeId = item.TypeId,
                QualityText = ExtractQualityTextFromEvidence(item)
            })
            {
                IsRecentlyChanged = hasMeaningfulChange || keepValueHighlight
            };

            UpsertValueRowStable(valueRow);
            if (hasMeaningfulChange)
            {
                MarkValueRowRecentlyChanged(key);
            }

            if (!string.IsNullOrWhiteSpace(displayValue))
            {
                _lastDisplayedValueByKey[key] = displayValue;
            }
        }

        if (shouldShowEdgeEvent)
        {
            var isIec10xDigital = item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104
                                  && IsIec10xDigitalType(item.TypeId);
            if (isIec10xDigital && !hasMeaningfulChange)
            {
                // Event Log is a change journal, not a value mirror. Ignore OFF->OFF,
                // ON->ON, and first-observed states with no trustworthy before value.
                return;
            }

            var relayEventRow = new RelayEventRow(new Iec103RelayEventLogEntry
            {
                EvidenceSequenceNumber = item.SequenceNumber,
                RelayTimeText = string.IsNullOrWhiteSpace(item.RelayTimestampText) ? "no timestamp" : item.RelayTimestampText,
                RelayTimeInvalid = item.RelayTimestampInvalid,
                ArrivalTimeUtc = item.TimestampUtc,
                IsMapped = item.IsMappedSignal || ioaPoint is not null,
                SignalName = ioaPoint?.Name ?? (string.IsNullOrWhiteSpace(item.SignalName) ? fallbackSignal : item.SignalName),
                SignalGroup = ioaPoint?.Group ?? (string.IsNullOrWhiteSpace(item.SignalGroup) ? BuildFallbackSignalGroup(item) : item.SignalGroup),
                SignalType = !string.IsNullOrWhiteSpace(ioaPoint?.SignalType) ? ioaPoint!.SignalType : (!string.IsNullOrWhiteSpace(item.SignalType) ? item.SignalType : (item.AsduType ?? string.Empty)),
                FunctionType = item.FunctionType,
                InformationNumber = item.InformationNumber,
                PreviousValue = string.IsNullOrWhiteSpace(previousValue) ? string.Empty : previousValue,
                NewValue = displayValue,
                EdgeReason = string.IsNullOrWhiteSpace(item.EdgeReason) ? (item.Cot ?? string.Empty) : item.EdgeReason,
                CauseOfTransmission = item.Cot ?? string.Empty,
                AsduType = item.AsduType ?? string.Empty,
                RawHex = item.RawHex,
                ProtocolMode = item.ProtocolMode,
                CommonAddress = item.CommonAddressNumber,
                InformationObjectAddress = item.InformationObjectAddress,
                TypeId = item.TypeId,
                QualityText = ExtractQualityTextFromEvidence(item)
            });

            _relayEventStore.Add(relayEventRow);
            _relayEventRowsDirty = true;
        }
    }

    private static bool IsIec10xDigitalType(int? typeId)
        => typeId is 1 or 2 or 3 or 4 or 30 or 31;

    private static bool HasKnownStateTransition(string? previousValue, string? newValue)
    {
        var before = NormalizeStateForComparison(previousValue);
        var after = NormalizeStateForComparison(newValue);
        if (string.IsNullOrWhiteSpace(before) || string.IsNullOrWhiteSpace(after))
        {
            return false;
        }

        return !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeStateForComparison(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var token = ExtractSimpleStateToken(value).Trim();
        if (token.Length == 0 || token == "-" || token.Equals("no timestamp", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var upper = token.ToUpperInvariant();
        if (upper.StartsWith("SP=", StringComparison.Ordinal) || upper.StartsWith("DP=", StringComparison.Ordinal))
        {
            upper = upper[3..].Trim();
        }

        if (upper is "0" or "FALSE") return "0";
        if (upper is "1" or "TRUE") return "1";
        if (upper is "2") return "2";
        if (upper is "3") return "3";
        if (upper.Contains("INVALID OPEN", StringComparison.Ordinal)) return "INVALID_OPEN";
        if (upper.Contains("INVALID CLOSE", StringComparison.Ordinal)) return "INVALID_CLOSE";
        if (upper.Contains("OPEN", StringComparison.Ordinal)) return "OPEN";
        if (upper.Contains("CLOSE", StringComparison.Ordinal) || upper.Contains("CLOSED", StringComparison.Ordinal)) return "CLOSED";
        if (upper.Contains("OFF", StringComparison.Ordinal) || upper.Contains("NORMAL", StringComparison.Ordinal)) return "OFF";
        if (upper.Contains("ON", StringComparison.Ordinal) || upper.Contains("ACTIVE", StringComparison.Ordinal)) return "ON";
        return upper;
    }

    private void MarkValueRowRecentlyChanged(string key)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        _valueHighlightExpiryByKey[key] = until;
        if (_valueRowsByKey.TryGetValue(key, out var storedRow))
        {
            storedRow.IsRecentlyChanged = true;
            _valueRowsDirty = true;
        }

        foreach (var row in ValueRows)
        {
            if (string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                row.IsRecentlyChanged = true;
                break;
            }
        }
    }

    private void ResetExpiredValueHighlights()
    {
        if (_valueHighlightExpiryByKey.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var expired = _valueHighlightExpiryByKey
            .Where(x => x.Value <= now)
            .Select(x => x.Key)
            .ToArray();
        if (expired.Length == 0)
        {
            return;
        }

        foreach (var key in expired)
        {
            _valueHighlightExpiryByKey.Remove(key);
            if (_valueRowsByKey.TryGetValue(key, out var storedRow))
            {
                storedRow.IsRecentlyChanged = false;
                _valueRowsDirty = true;
            }

            foreach (var row in ValueRows)
            {
                if (string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    row.IsRecentlyChanged = false;
                    break;
                }
            }
        }
    }

    private static bool IsIec10xProcessValue(Iec103MasterEvidenceEvent item)
    {
        if (item.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104))
        {
            return false;
        }

        if (!item.TypeId.HasValue || !item.InformationObjectAddress.HasValue)
        {
            return false;
        }

        return item.TypeId.Value is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37;
    }

    private static bool IsIec10xDigitalEdgeEvent(Iec103MasterEvidenceEvent item)
    {
        if (item.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104))
        {
            return false;
        }

        if (!item.TypeId.HasValue || !item.CauseOfTransmission.HasValue || !item.InformationObjectAddress.HasValue)
        {
            return false;
        }

        var isEventCause = item.CauseOfTransmission.Value is 3 or 11 or 12;
        return IsIec10xDigitalType(item.TypeId) && isEventCause;
    }

    private static string BuildValueKey(Iec103MasterEvidenceEvent item)
    {
        if (!string.IsNullOrWhiteSpace(item.SignalKey))
        {
            return item.SignalKey;
        }

        if (item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104)
        {
            return item.InformationObjectAddress.HasValue
                ? BuildIoaValueKey(item.InformationObjectAddress.Value)
                : $"{item.ProtocolMode}:IOA-";
        }

        return $"FUN{(item.FunctionType ?? 0):000}:INF{(item.InformationNumber ?? 0):000}";
    }

    private static string BuildFallbackSignalName(Iec103MasterEvidenceEvent item)
    {
        if (item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104)
        {
            return item.InformationObjectAddress.HasValue
                ? $"IOA {item.InformationObjectAddress.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                : "Unaddressed IEC-10x object";
        }

        return $"FUN {item.FunctionType} / INF {item.InformationNumber}";
    }

    private static string BuildFallbackSignalGroup(Iec103MasterEvidenceEvent item)
    {
        return item.ProtocolMode switch
        {
            Iec60870ProtocolMode.Iec101 => "IEC-101",
            Iec60870ProtocolMode.Iec104 => "IEC-104",
            _ => "Unmapped"
        };
    }


    private void EventLogFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyRelayEventFilter();
    }

    private void ApplyRelayEventFilter()
    {
        if (RelayEventRows is null)
        {
            return;
        }

        var filter = (EventLogFilterComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
        var rows = _relayEventStore
            .Snapshot()
            .Reverse()
            .Where(row => ShouldIncludeRelayEvent(row, filter))
            .Take(MaxVisibleRelayEventRows)
            .ToArray();

        RelayEventRows.ReplaceRange(rows);
    }

    private static bool ShouldIncludeRelayEvent(RelayEventRow row, string filter)
    {
        if (filter.Equals("Digital status", StringComparison.OrdinalIgnoreCase))
        {
            return IsDigitalEvent(row);
        }

        if (filter.Equals("Analog", StringComparison.OrdinalIgnoreCase))
        {
            return IsAnalogEvent(row);
        }

        return true;
    }

    private static bool IsDigitalEvent(RelayEventRow row)
    {
        var text = string.Join(" ", row.Type, row.Cot, row.Signal, row.NewValue, row.Reason);
        return text.Contains("DPI", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("SPI", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("status", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("trip", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("pickup", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ON", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("OFF", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAnalogEvent(RelayEventRow row)
    {
        var text = string.Join(" ", row.Type, row.Cot, row.Signal, row.NewValue, row.Reason);
        return text.Contains("Measur", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Analog", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("current", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("voltage", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Measurands", StringComparison.OrdinalIgnoreCase);
    }


    private void UpsertValueRowStable(ValueRow row)
    {
        _valueRowsByKey[row.Key] = row;
        _valueRowsDirty = true;

        if (_valueRowsByKey.Count > MaxVisibleValueRows + 200)
        {
            foreach (var stale in _valueRowsByKey.Values
                         .OrderBy(GetValueRowSortRank)
                         .ThenBy(x => x.IoaSortKey)
                         .ThenBy(x => x.TypeSortKey)
                         .ThenBy(x => x.Signal, StringComparer.OrdinalIgnoreCase)
                         .Skip(MaxVisibleValueRows)
                         .Select(x => x.Key)
                         .ToArray())
            {
                _valueRowsByKey.Remove(stale);
                _valueHighlightExpiryByKey.Remove(stale);
                _lastDisplayedValueByKey.Remove(stale);
            }
        }
    }

    private IReadOnlyList<ValueRow> GetSortedValueRowsSnapshot()
        => _valueRowsByKey.Values
            .OrderBy(GetValueRowSortRank)
            .ThenBy(x => x.IoaSortKey)
            .ThenBy(x => x.TypeSortKey)
            .ThenBy(x => x.Signal, StringComparer.OrdinalIgnoreCase)
            .Take(MaxVisibleValueRows)
            .ToArray();

    private static int CompareValueRowsForOperatorGrouping(ValueRow left, ValueRow right)
    {
        var rank = GetValueRowSortRank(left).CompareTo(GetValueRowSortRank(right));
        if (rank != 0) return rank;

        var ioa = left.IoaSortKey.CompareTo(right.IoaSortKey);
        if (ioa != 0) return ioa;

        var type = left.TypeSortKey.CompareTo(right.TypeSortKey);
        if (type != 0) return type;

        return string.Compare(left.Signal, right.Signal, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetValueRowSortRank(ValueRow row)
    {
        var text = string.Join(" ", row.Type, row.Cot, row.Signal, row.Group, row.TypeId);
        if (row.TypeId is "1" or "2" or "3" or "4" or "30" or "31" ||
            text.Contains("DPI", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("SPI", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("single-point", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("double-point", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("status", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("trip", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("fault", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("local remote", StringComparison.OrdinalIgnoreCase))
        {
            return 0; // digital/protection status first
        }

        if (row.TypeId is "9" or "10" or "11" or "12" or "13" or "14" or "21" or "34" or "35" or "36" ||
            text.Contains("measur", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("analog", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("current", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("voltage", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("power", StringComparison.OrdinalIgnoreCase))
        {
            return 1; // analog/measurand after digital
        }

        return 2;
    }


    private static string ExtractQualityTextFromEvidence(Iec103MasterEvidenceEvent item)
    {
        if (!string.IsNullOrWhiteSpace(item.QualityText))
        {
            return item.QualityText;
        }

        var text = string.Join(" ", item.SignalDisplayValue, item.SignalRawValue, item.ObjectSummary);
        var marker = "QDS=0x";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || text.Length < index + marker.Length + 2)
        {
            return string.Empty;
        }

        return text.Substring(index, marker.Length + 2);
    }

    private static bool IsDiagnosticEvidence(Iec103MasterEvidenceEvent item)
    {
        return !string.IsNullOrWhiteSpace(item.ExceptionType)
               || item.Category.Contains("Error", StringComparison.OrdinalIgnoreCase)
               || item.Category.Contains("Warning", StringComparison.OrdinalIgnoreCase)
               || item.Category.Contains("Fault", StringComparison.OrdinalIgnoreCase)
               || item.Category.Contains("Diagnostic", StringComparison.OrdinalIgnoreCase)
               || item.Summary.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || item.Detail.Contains("exception", StringComparison.OrdinalIgnoreCase);
    }

    private void AddUiDiagnostic(string severity, string source, string code, string message, string detail, string recommendation, Exception? exception = null)
    {
        AddDiagnosticRow(new DiagnosticRow(severity, source, code, message, detail, recommendation, exception));
        UpdateBufferStatus();
    }

    private void AddDiagnosticRow(DiagnosticRow row)
    {
        _diagnosticStore.Add(row);
        _pendingDiagnosticUiRows.Add(row);
    }

    private void DiagnosticsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is not DiagnosticRow row)
        {
            DiagnosticDetailBox.Text = "Select a diagnostic row to view complete detail.";
            return;
        }

        DiagnosticDetailBox.Text = row.ToClipboardText();
    }

    private void CopySelectedDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        if (DiagnosticsGrid.SelectedItem is DiagnosticRow row)
        {
            Clipboard.SetText(row.ToClipboardText());
            AppendSessionLog("Diagnostic row copied to clipboard.");
        }
    }

    private void CopyDiagnosticDetail_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(DiagnosticDetailBox.Text))
        {
            Clipboard.SetText(DiagnosticDetailBox.Text);
            AppendSessionLog("Diagnostic detail copied to clipboard.");
        }
    }

    private void UpdateStableHeader(string state, string detail)
    {
        StateText.Text = state;
        CompletionText.Text = "History below";
        StatusHistorySummaryText.Text = CompactSessionDetail(detail);
        StatusHistorySummaryText.ToolTip = string.IsNullOrWhiteSpace(detail) ? "-" : detail;
    }

    private static string CompactSessionDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return "-";
        }

        var text = detail.Replace("Assessment:", "Assess:", StringComparison.OrdinalIgnoreCase)
            .Replace("Stopped by cancellation or requested duration.", "Stopped/duration reached.", StringComparison.OrdinalIgnoreCase)
            .Replace("Stopped by cancellation.", "Stopped by user.", StringComparison.OrdinalIgnoreCase);

        const int max = 74;
        return text.Length <= max ? text : text[..max] + "…";
    }

    private void SetRunUiState(bool isRunning)
    {
        StartButton.IsEnabled = true;
        if (StopButton is not null)
        {
            StopButton.IsEnabled = false;
            StopButton.Visibility = Visibility.Collapsed;
        }
        UpdateConnectToggleVisual(isRunning);
        SetupButton.IsEnabled = !isRunning;
        SetupOverlay.Visibility = isRunning ? Visibility.Collapsed : SetupOverlay.Visibility;
        ExportMarkdownButton.IsEnabled = !isRunning && _lastResult != null;
        ProtocolModeComboBox.IsEnabled = !isRunning;
        TransportModeComboBox.IsEnabled = !isRunning;
        TcpHostBox.IsEnabled = !isRunning;
        TcpPortBox.IsEnabled = !isRunning;
        PortComboBox.IsEnabled = !isRunning;
        BaudComboBox.IsEnabled = !isRunning;
        SerialModeComboBox.IsEnabled = !isRunning;
        LinkAddressBox.IsEnabled = !isRunning;
        CommonAddressBox.IsEnabled = !isRunning;
        LinkAddressSizeComboBox.IsEnabled = !isRunning;
        TransmissionModeComboBox.IsEnabled = !isRunning;
        CotSizeComboBox.IsEnabled = !isRunning;
        CaSizeComboBox.IsEnabled = !isRunning;
        IoaSizeComboBox.IsEnabled = !isRunning;
        Iec104T0Box.IsEnabled = !isRunning;
        Iec104T1Box.IsEnabled = !isRunning;
        Iec104T2Box.IsEnabled = !isRunning;
        Iec104T3Box.IsEnabled = !isRunning;
        Iec104KBox.IsEnabled = !isRunning;
        Iec104WBox.IsEnabled = !isRunning;
        DurationBox.IsEnabled = !isRunning;
        TimeoutBox.IsEnabled = !isRunning;
        Class2IntervalBox.IsEnabled = !isRunning;
        MaxDrainBox.IsEnabled = !isRunning;
        ResetRemoteLinkCheckBox.IsEnabled = !isRunning;
        ResetFcbCheckBox.IsEnabled = !isRunning;
        ClockSyncCheckBox.IsEnabled = !isRunning;
        GiCheckBox.IsEnabled = !isRunning;
        Class2StartupCheckBox.IsEnabled = !isRunning;
        MappingProfilePathBox.IsEnabled = !isRunning;
        BrowseMappingButton.IsEnabled = !isRunning;
        ClearMappingButton.IsEnabled = !isRunning;
    }

    private void ClearSessionView(bool clearLog)
    {
        EvidenceRows.Clear();
        FrameTraceRows.Clear();
        _evidenceSummaryStore.Clear();
        _protocolTraceStore.Clear();
        _pendingEvidenceSummaryUiRows.Clear();
        _pendingProtocolTraceUiRows.Clear();
        _pendingFindingUiRows.Clear();
        _pendingDiagnosticUiRows.Clear();
        _findingStore.Clear();
        _diagnosticStore.Clear();
        _relayEventStore.Clear();
        _valueRowsByKey.Clear();
        _valueRowsDirty = false;
        _relayEventRowsDirty = false;
        _backpressureDroppedEvents = 0;
        _backpressureDroppedAckNoData = 0;
        _backpressureDroppedBackgroundPoll = 0;
        _backpressureDroppedTestFrames = 0;
        _backpressureDroppedOtherLowValue = 0;
        _backpressureNoticePending = 0;
        _lastDropSummaryMarkerTotal = 0;
        _traceVerbositySuppressedRows = 0;
        _traceVerbositySuppressedRoutine = 0;
        _traceVerbositySuppressedSupervisory = 0;
        _maxPendingEvidenceDepth = 0;
        _uiFlushTicks = 0;
        _lastUiFlushMs = 0;
        _maxUiFlushMs = 0;
        _lastEvidenceProcessed = 0;
        _lastFindingProcessed = 0;
        _lastVisibleBatchRows = 0;
        _lastFlushBudget = MaxUiFlushPerTick;
        _lastBackpressureLogUtc = DateTime.MinValue;
        _lastDispatcherPressureDiagnosticUtc = DateTime.MinValue;
        _lastDispatcherSlowDiagnosticUtc = DateTime.MinValue;
        FindingRows.Clear();
        ValueRows.Clear();
        RelayEventRows.Clear();
        _lastDisplayedValueByKey.Clear();
        _valueHighlightExpiryByKey.Clear();
        _evidenceSummarySignatureByKey.Clear();
        _evidenceSummaryLastUtcByKey.Clear();
        _evidenceSummaryLastAnalogValueByKey.Clear();
        _evidenceSummaryLastAnalogUtcByKey.Clear();
        _giExpectedValueKeys.Clear();
        _giReceivedValueKeys.Clear();
        _giCompletenessWatchActive = false;
        _giCompletenessReported = false;
        _firstObservedRuntimeCa = null;
        _runtimeCaMismatchReported = false;
        ResetRuntimeHealthStores();
        AssessmentRows.Clear();
        DiagnosticRows.Clear();
        while (_pendingEvidence.TryDequeue(out _)) { }
        while (_pendingFindings.TryDequeue(out _)) { }
        _visibleEvidenceDropped = 0;
        _visibleRelayEventsDropped = 0;
        _visibleLogLinesDropped = 0;
        _visibleDiagnosticsDropped = 0;
        _txCount = 0;
        _rxCount = 0;
        _giCount = 0;
        _class1Count = 0;
        _class2Count = 0;
        _noDataCount = 0;
        _dpiCount = 0;
        TxLed.Opacity = 0.28;
        RxLed.Opacity = 0.28;
        GiLed.Opacity = 0.28;
        Class1Led.Opacity = 0.28;
        Class2Led.Opacity = 0.28;
        EventLed.Opacity = 0.28;
        DiagLed.Opacity = 0.28;
        TxRxText.Text = "0 / 0";
        ClassPollText.Text = "0 / 0 / 0";
        NoDataText.Text = "0";
        DpiText.Text = "0";
        FindingCountText.Text = "0";
        SelectedDetailText.Text = "Select evidence row to inspect decoded meaning.";
        SelectedRawText.Text = "-";
        _selectedFrameExplanation = "Select a frame. This panel translates raw bytes into commissioning meaning.";
        SelectedLineSummaryText.Text = _selectedFrameExplanation;
        SelectedProtocolMapLines.Clear();
        SelectedHexSegments.Clear();
        StatusHistorySummaryText.Text = "Visible session rows cleared.";
        UpdateBufferStatus();
        if (clearLog)
        {
            _sessionLogLines.Clear();
            SessionLogBox?.Clear();
            StatusHistoryRows.Clear();
            AppendSessionLog("Session view cleared.");
        }
    }

    private void AppendSessionLog(string message)
    {
        _sessionLogLines.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}");
        while (_sessionLogLines.Count > MaxSessionLogLines)
        {
            _sessionLogLines.Dequeue();
            _visibleLogLinesDropped++;
        }

        if (SessionLogBox is not null)
        {
            SessionLogBox.Text = string.Join(Environment.NewLine, _sessionLogLines);
            if (SessionLogBox.Text.Length > 0)
            {
                SessionLogBox.AppendText(Environment.NewLine);
            }
            SessionLogBox.ScrollToEnd();
        }

        if (StatusHistoryRows is not null && StatusHistorySummaryText is not null)
        {
            AddStatusHistoryRow(message);
        }

        if (BufferStatusText is not null)
        {
            UpdateBufferStatus();
        }
    }

    private void AddStatusHistoryRow(string message)
    {
        if (StatusHistoryRows is null)
        {
            return;
        }

        StatusHistoryRows.Insert(0, new StatusHistoryRow(DateTime.Now.ToString("HH:mm:ss"), ClassifyStatusMessage(message), message));
        while (StatusHistoryRows.Count > 160)
        {
            StatusHistoryRows.RemoveAt(StatusHistoryRows.Count - 1);
        }

        if (StatusHistorySummaryText is not null)
        {
            StatusHistorySummaryText.Text = CompactSessionDetail(message);
            StatusHistorySummaryText.ToolTip = message;
        }
    }

    private static string ClassifyStatusMessage(string message)
    {
        if (message.Contains("fault", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("warning", StringComparison.OrdinalIgnoreCase))
        {
            return "Attention";
        }

        if (message.Contains("stopped", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("disconnect", StringComparison.OrdinalIgnoreCase))
        {
            return "Stopped";
        }

        if (message.Contains("starting", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("monitor", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("transport", StringComparison.OrdinalIgnoreCase))
        {
            return "Runtime";
        }

        return "Info";
    }

    private void ToggleStatusHistory_Click(object sender, RoutedEventArgs e)
    {
        _statusHistoryExpanded = !_statusHistoryExpanded;
        StatusHistoryPanel.Height = _statusHistoryExpanded ? double.NaN : 52;
        StatusHistoryGapRow.Height = _statusHistoryExpanded ? new GridLength(8) : new GridLength(0);
        StatusHistoryContentRow.Height = _statusHistoryExpanded ? new GridLength(118) : new GridLength(0);
        StatusHistoryGrid.Visibility = _statusHistoryExpanded ? Visibility.Visible : Visibility.Collapsed;
        StatusHistoryToggleText.Text = _statusHistoryExpanded ? "Hide" : "Show";
        StatusHistoryToggleIcon.Data = (Geometry)FindResource(_statusHistoryExpanded ? "LucideCircleChevronDown" : "LucideCircleChevronUp");
    }

    private void UpdateBufferStatus()
    {
        if (BufferStatusText == null)
        {
            return;
        }

        BufferStatusText.Text =
            $"Buffer: trace {GetTraceVerbosityMode()}, operator {EvidenceRows.Count}/{MaxVisibleEvidenceRows}, frames {FrameTraceRows.Count}/{MaxVisibleFrameTraceRows}, values {ValueRows.Count}/{MaxVisibleValueRows}, events {RelayEventRows.Count}/{MaxVisibleRelayEventRows}, diagnostics {DiagnosticRows.Count}/{MaxVisibleDiagnosticRows}, queued {_pendingEvidence.Count}, qMax {_maxPendingEvidenceDepth}, budget {_lastFlushBudget}, dropped {_backpressureDroppedEvents} [ack {_backpressureDroppedAckNoData}, poll {_backpressureDroppedBackgroundPoll}, test {_backpressureDroppedTestFrames}, other {_backpressureDroppedOtherLowValue}], traceSkip {_traceVerbositySuppressedRows} [routine {_traceVerbositySuppressedRoutine}, sup {_traceVerbositySuppressedSupervisory}], flush {_lastUiFlushMs}/{_maxUiFlushMs} ms, ticks {_uiFlushTicks}, rows {_lastEvidenceProcessed}+{_lastFindingProcessed}/{_lastVisibleBatchRows}, relayDrop {_visibleRelayEventsDropped}, diagDrop {_visibleDiagnosticsDropped}";
    }

    private sealed class CaptureManifest
    {
        public string Format { get; set; } = string.Empty;
        public string CaptureId { get; set; } = string.Empty;
        public string CaptureKind { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public string Application { get; set; } = string.Empty;
        public string ProtocolMode { get; set; } = string.Empty;
        public string TraceVerbosityMode { get; set; } = string.Empty;
        public int RowCount { get; set; }
        public long FirstSequence { get; set; }
        public long LastSequence { get; set; }
        public string FirstTimestampText { get; set; } = string.Empty;
        public string LastTimestampText { get; set; } = string.Empty;
        public string FramesSha256 { get; set; } = string.Empty;
        public CaptureSessionSnapshot SourceSession { get; set; } = new();
    }

    private sealed class CaptureSessionSnapshot
    {
        public int TxCount { get; set; }
        public int RxCount { get; set; }
        public int GiCount { get; set; }
        public int Class1Count { get; set; }
        public int Class2Count { get; set; }
        public int NoDataCount { get; set; }
        public int DpiCount { get; set; }
        public int ValueRows { get; set; }
        public int EventRows { get; set; }
        public int DiagnosticRows { get; set; }
        public int TraceRowsVisible { get; set; }
        public int TraceRowsLimit { get; set; }
        public long TraceSuppressedRows { get; set; }
        public long BackpressureDroppedRows { get; set; }
        public long QueueMaxObserved { get; set; }
        public long MaxUiFlushMs { get; set; }
    }

    private sealed class CaptureFrameRecord
    {
        public long Sequence { get; set; }
        public string Time { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public string ProtocolName { get; set; } = string.Empty;
        public string ProtocolMode { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string DataClass { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string SignalOrAddress { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Quality { get; set; } = string.Empty;
        public string AsduType { get; set; } = string.Empty;
        public string TypeId { get; set; } = string.Empty;
        public string Cot { get; set; } = string.Empty;
        public string CotCode { get; set; } = string.Empty;
        public string LinkAddress { get; set; } = string.Empty;
        public string CommonAddress { get; set; } = string.Empty;
        public string Ioa { get; set; } = string.Empty;
        public string Acd { get; set; } = string.Empty;
        public string Dfc { get; set; } = string.Empty;
        public string RelayTime { get; set; } = string.Empty;
        public string ResponseTime { get; set; } = string.Empty;
        public string Meaning { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string RawHex { get; set; } = string.Empty;
        public string ProtocolTraceTitle { get; set; } = string.Empty;
        public string ProtocolTraceMeaning { get; set; } = string.Empty;
        public string ProtocolTraceRaw { get; set; } = string.Empty;
        public string ProtocolTraceMeta { get; set; } = string.Empty;

        public static CaptureFrameRecord FromEvidenceRow(EvidenceRow row)
            => new()
            {
                Sequence = row.Sequence,
                Time = row.Time,
                Direction = row.Direction,
                ProtocolName = row.ProtocolName,
                ProtocolMode = row.ProtocolMode,
                State = row.State,
                Category = row.Category,
                DataClass = row.DataClass,
                Service = row.ProtocolService,
                Address = row.ProtocolAddress,
                SignalOrAddress = row.SignalOrAddress,
                Value = row.SemanticState,
                Quality = row.Quality,
                AsduType = row.AsduType,
                TypeId = row.TypeId,
                Cot = row.Cot,
                CotCode = row.CotCode,
                LinkAddress = row.LinkAddress,
                CommonAddress = row.CommonAddress,
                Ioa = row.IoAddress,
                Acd = row.Acd,
                Dfc = row.Dfc,
                RelayTime = row.RelayTime,
                ResponseTime = row.ResponseTime,
                Meaning = row.ReadableMeaning,
                Detail = row.Detail,
                RawHex = row.RawHex,
                ProtocolTraceTitle = row.ProtocolTraceTitle,
                ProtocolTraceMeaning = row.ProtocolTraceMeaning,
                ProtocolTraceRaw = row.ProtocolTraceRaw,
                ProtocolTraceMeta = row.ProtocolTraceMeta
            };
    }


}

public sealed record StatusHistoryRow(string Time, string Status, string Detail);
