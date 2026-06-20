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
            "Use this verdict as the top-level commissioning proof summary, then inspect Values, Events, Trace, Report, and export retention policy for detail.");
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
        ApplyObservedCommonAddressToSetupPreferences(observedCa, configuredCa);
        AddUiDiagnostic(
            "Warning",
            "IEC-101",
            "IEC101-RUNTIME-CA-MISMATCH",
            "Runtime ASDU common address differs from setup/profile",
            $"Live process data is arriving with CA={observedCa}, but setup/profile uses CA={configuredCa}. Station GI sent to the wrong CA can be negatively confirmed and may prevent SPS/DPS snapshots from arriving.",
            "Use the observed CA for GI/test runs, or keep auto CA-learning retry enabled. The Values still maps values by IOA where possible.");
        AppendSessionLog($"Runtime CA mismatch: live ASDU CA={observedCa}, configured CA={configuredCa}. Auto CA-learning in IEC-101 session will retry GI using observed CA; setup preferences were updated for the next run.");
    }

    private void ApplyObservedCommonAddressToSetupPreferences(int observedCa, int configuredCa)
    {
        if (observedCa <= 0 || observedCa > 65535)
        {
            return;
        }

        var observedText = observedCa.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var configuredText = configuredCa.ToString(System.Globalization.CultureInfo.InvariantCulture);
        CommonAddressBox.Text = observedText;
        if (CommandCaBox is not null
            && (string.IsNullOrWhiteSpace(CommandCaBox.Text)
                || string.Equals(CommandCaBox.Text.Trim(), configuredText, StringComparison.OrdinalIgnoreCase)))
        {
            CommandCaBox.Text = observedText;
        }

        SaveSetupPreferencesFromUi(silent: true);
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
                DisplayValue = "awaiting live value",
                RawValue = string.Empty,
                CauseOfTransmission = "profile pending",
                AsduType = string.IsNullOrWhiteSpace(point.SignalType) ? string.Empty : point.SignalType,
                RelayTimeText = "not received",
                ArrivalTimeUtc = DateTime.UtcNow,
                ProtocolMode = protocolMode,
                CommonAddress = point.Ca ?? _ioaProfile.CommonAddress,
                InformationObjectAddress = point.Ioa,
                TypeId = point.TypeId,
                QualityText = "pending"
            }));
        }

        if (ordered.Count > 0)
        {
            AppendSessionLog($"Values seeded with {ordered.Count} expected IOA points from {_ioaProfile.ProfileName}. Missing profile values stay visible as 'awaiting live value' until the RTU sends the IOA.");
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
            AppendSessionLog("GI note: NEGATIVE CONFIRMATION observed. Keeping Values neutral; continuing scan.");
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
            "This is a non-destructive scan note. Values rows stay in waiting state until actual Class 1/Class 2 frames arrive. Verify RTU profile only if the test case requires every IOA to be returned in this window.");
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
            $"{reason}. Values placeholders are kept neutral; only actual Class 1/Class 2 frames are allowed to update IOA values. Waiting {Math.Ceiling(window.TotalSeconds):0}s before reporting a non-destructive completeness note.",
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
}
