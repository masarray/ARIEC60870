// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Mapping;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Analysis;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Transport;

namespace ARIEC60870.Master;

/// <summary>
/// Single-connection IEC-103 active master session.
///
/// Product rule:
/// - Normal loop uses Class 2/background polling.
/// - Class 1 is drained only when slave response indicates ACD=1 or during a bounded command/GI follow-up window.
/// - The master intentionally avoids the bad pattern: Class 1 -> NO DATA -> Class 1 -> NO DATA storm.
/// </summary>
public sealed class Iec103MasterSession : IProtocolMasterSession
{
    private readonly Iec103MasterSettings _settings;
    private readonly IByteTransport _transport;
    private readonly Iec103SignalMappingProfile _mappingProfile;
    private readonly Ft12Parser _parser = new();
    private readonly List<Iec103MasterEvidenceEvent> _events = new();
    private readonly List<Iec103MasterFinding> _findings = new();
    private readonly Iec103MasterCounters _counters = new();
    private readonly Dictionary<string, Iec103ValuePoint> _valuePoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Iec103RelayEventLogEntry> _eventLog = new();
    private bool _fcb;
    private bool _accessDemand;
    private bool _dataFlowControl;
    private bool _giInProgress;
    private long _sequence;
    private Iec103MasterState _state = Iec103MasterState.Created;
    private DateTime _lastClass2PollUtc = DateTime.MinValue;

    public Iec103MasterSession(Iec103MasterSettings settings, IByteTransport transport)
        : this(settings, transport, Iec103SignalMappingProfile.Empty)
    {
    }

    public Iec103MasterSession(Iec103MasterSettings settings, IByteTransport transport, Iec103SignalMappingProfile mappingProfile)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _mappingProfile = mappingProfile ?? Iec103SignalMappingProfile.Empty;
    }

    public event EventHandler<Iec103MasterEvidenceEvent>? EvidenceReceived;
    public event EventHandler<Iec103MasterFinding>? FindingRaised;

    public async Task<Iec103MasterRunResult> RunForAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(duration);
        return await RunAsync(timeout.Token).ConfigureAwait(false);
    }

    public async Task<Iec103MasterRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var completion = "Stopped by cancellation or requested duration.";

        try
        {
            SetState(Iec103MasterState.OpeningTransport, "Opening serial transport", _settings.SerialSummary);
            await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);
            SetState(Iec103MasterState.Connected, "Connected", _settings.SerialSummary);

            if (_settings.StartupDelayMs > 0)
            {
                SetState(Iec103MasterState.StartupDelay, "Startup delay", $"Waiting {_settings.StartupDelayMs} ms before first master command.");
                await Task.Delay(_settings.StartupDelayMs, cancellationToken).ConfigureAwait(false);
            }

            if (_settings.ResetRemoteLinkOnConnect)
            {
                await ResetRemoteLinkAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_settings.ResetFcbOnConnect)
            {
                await ResetFcbAsync("Startup synchronization", cancellationToken).ConfigureAwait(false);
            }

            if (_settings.SendClockSyncOnConnect)
            {
                await SendClockSyncAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_settings.SendGeneralInterrogationOnConnect)
            {
                await SendGeneralInterrogationAsync(cancellationToken).ConfigureAwait(false);
                await DrainClass1Async("GI follow-up bounded event-drain", stopWhenGiEnds: true, cancellationToken).ConfigureAwait(false);
            }
            else if (_settings.RequestClass2ImmediatelyAfterStartup)
            {
                await RequestClass2Async("Initial background scan", cancellationToken).ConfigureAwait(false);
                _lastClass2PollUtc = DateTime.UtcNow;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                if (_dataFlowControl)
                {
                    SetState(Iec103MasterState.BusyBackoff, "DFC backoff", $"Slave indicated DFC=1. Backing off {_settings.BusyBackoffMs} ms.");
                    await Task.Delay(_settings.BusyBackoffMs, cancellationToken).ConfigureAwait(false);
                    _dataFlowControl = false;
                    continue;
                }

                if (_accessDemand)
                {
                    await DrainClass1Async("ACD=1 event-drain", stopWhenGiEnds: false, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var elapsed = DateTime.UtcNow - _lastClass2PollUtc;
                if (elapsed.TotalMilliseconds >= _settings.Class2PollIntervalMs)
                {
                    await RequestClass2Async("Normal Class 2 background poll", cancellationToken).ConfigureAwait(false);
                    _lastClass2PollUtc = DateTime.UtcNow;
                    continue;
                }

                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            completion = "Stopped by cancellation or requested duration.";
        }
        catch (Exception ex)
        {
            completion = "Fault: " + ex.Message;
            SetState(Iec103MasterState.Faulted, "Fault", ex.Message, category: "Error");
            AddExceptionDiagnosticEvent(
                "IEC103-MASTER-FAULT",
                "Master session faulted",
                ex,
                "Check serial settings, wiring, relay address, relay IEC-103 mode, and diagnostic rows before retrying.",
                category: "Error");
            RaiseFinding(
                FindingSeverity.Error,
                "IEC103-MASTER-FAULT",
                "Master session faulted",
                ex.Message,
                "The active test session could not continue.",
                "Check serial settings, wiring, relay address, and relay communication mode.");
        }
        finally
        {
            SetState(Iec103MasterState.Stopping, "Closing transport", "Closing serial connection.");
            try
            {
                await _transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddExceptionDiagnosticEvent(
                    "IEC103-TRANSPORT-CLOSE",
                    "Serial transport close exception captured",
                    ex,
                    "Stop request was contained. If the COM port remains locked, unplug/replug the USB converter or restart the app before retrying.",
                    category: "Warning");
            }

            DrainTransportDiagnostics("Close transport");
            SetState(Iec103MasterState.Stopped, "Stopped", completion);
        }

        BuildPostRunFindings();

        var events = _events.ToArray();
        var findings = _findings.ToArray();
        var valuePoints = _valuePoints.Values
            .OrderBy(x => x.SignalGroup)
            .ThenBy(x => x.SignalName)
            .ThenBy(x => x.Key)
            .ToArray();
        var eventLog = _eventLog.ToArray();
        var completedNormally = !completion.StartsWith("Fault:", StringComparison.OrdinalIgnoreCase);
        var reportSettings = _settings.CreateReportSnapshot();
        var assessment = Iec103MasterAssessmentBuilder.Build(
            reportSettings,
            _counters,
            events,
            findings,
            valuePoints,
            eventLog,
            completedNormally);

        return new Iec103MasterRunResult
        {
            Settings = reportSettings,
            Counters = _counters,
            Events = events,
            Findings = findings,
            ValuePoints = valuePoints,
            EventLog = eventLog,
            Assessment = assessment,
            StartedUtc = started,
            FinishedUtc = DateTime.UtcNow,
            CompletedNormally = completedNormally,
            CompletionReason = completion
        };
    }

    public async Task ResetRemoteLinkAsync(CancellationToken cancellationToken)
    {
        _counters.ResetRemoteLinkCommands++;
        SetState(Iec103MasterState.ResetRemoteLink, "Reset remote link", "Sending primary function 0 before normal polling.");
        await SendFixedAndReceiveAsync("Reset remote link", "Link", functionCode: 0, fcv: false, pollingReason: "Startup link reset", consumeResponse: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task ResetFcbAsync(string reason, CancellationToken cancellationToken)
    {
        _counters.ResetFcbCommands++;
        SetState(Iec103MasterState.ResetFcb, "Reset FCB", reason);
        await SendFixedAndReceiveAsync("Reset FCB", "Link", functionCode: 7, fcv: false, pollingReason: reason, consumeResponse: true, cancellationToken).ConfigureAwait(false);
        _fcb = false;
    }

    public async Task RequestClass2Async(string pollingReason, CancellationToken cancellationToken)
    {
        _counters.Class2Requests++;
        SetState(Iec103MasterState.NormalClass2Polling, "Request Class 2", pollingReason, dataClass: "Class 2");
        await SendFixedAndReceiveAsync("Request Class 2", "Class 2", functionCode: 11, fcv: true, pollingReason: pollingReason, consumeResponse: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task RequestClass1Async(string pollingReason, CancellationToken cancellationToken)
    {
        _counters.Class1Requests++;
        SetState(Iec103MasterState.Class1EventDrain, "Request Class 1", pollingReason, dataClass: "Class 1");
        await SendFixedAndReceiveAsync("Request Class 1", "Class 1", functionCode: 10, fcv: true, pollingReason: pollingReason, consumeResponse: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendGeneralInterrogationAsync(CancellationToken cancellationToken)
    {
        var asdu = Iec103AsduBuilder.GeneralInterrogation(_settings.CommonAddress);
        _counters.GiCommands++;
        _giInProgress = true;
        SetState(Iec103MasterState.GeneralInterrogation, "General Interrogation", "Starting bounded GI sequence. Class 1 follow-up is allowed only until GI END, NO DATA, or drain limit.", dataClass: "Class 2");
        await SendVariableAndReceiveAsync("General Interrogation", "Class 2", asdu, "Startup GI command", cancellationToken).ConfigureAwait(false);
    }

    public async Task SendClockSyncAsync(CancellationToken cancellationToken)
    {
        var asdu = Iec103AsduBuilder.ClockSynchronization(_settings.CommonAddress, DateTime.Now);
        _counters.ClockSyncCommands++;
        SetState(Iec103MasterState.ClockSynchronization, "Clock Sync", "Sending IEC-103 CP32Time2a clock synchronization command.", dataClass: "Class 2");
        await SendVariableAndReceiveAsync("Clock Sync", "Class 2", asdu, "Startup clock synchronization", cancellationToken).ConfigureAwait(false);
    }

    private async Task DrainClass1Async(string reason, bool stopWhenGiEnds, CancellationToken cancellationToken)
    {
        _counters.Class1DrainBursts++;
        SetState(
            stopWhenGiEnds ? Iec103MasterState.GiFollowUpDrain : Iec103MasterState.Class1EventDrain,
            "Class 1 drain started",
            reason,
            dataClass: "Class 1");

        var drained = 0;
        var stoppedByNoData = false;
        var stoppedByGiEnd = false;
        var stoppedByAcdClear = false;

        while (!cancellationToken.IsCancellationRequested && drained < _settings.MaxClass1DrainFrames)
        {
            var beforeNoData = _counters.NoDataResponses;
            var beforeGiEnd = _counters.GiEndResponses;
            var beforeUserData = _counters.UserDataResponses;

            await RequestClass1Async(reason, cancellationToken).ConfigureAwait(false);
            drained++;
            _counters.Class1DrainFrames++;

            if (_counters.NoDataResponses > beforeNoData)
            {
                stoppedByNoData = true;
                _counters.Class1DrainStoppedByNoData++;
                break;
            }

            if (stopWhenGiEnds && _counters.GiEndResponses > beforeGiEnd)
            {
                stoppedByGiEnd = true;
                _giInProgress = false;
                break;
            }

            if (_dataFlowControl)
            {
                break;
            }

            if (!_accessDemand && _counters.UserDataResponses > beforeUserData)
            {
                stoppedByAcdClear = true;
                _counters.Class1DrainStoppedByAcdClear++;
                break;
            }

            if (!stopWhenGiEnds && !_accessDemand)
            {
                stoppedByAcdClear = true;
                _counters.Class1DrainStoppedByAcdClear++;
                break;
            }

            if (_settings.Class1DrainDelayMs > 0)
            {
                await Task.Delay(_settings.Class1DrainDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        if (drained >= _settings.MaxClass1DrainFrames && (_accessDemand || _giInProgress))
        {
            _counters.Class1DrainLimitReached++;
            SetState(
                Iec103MasterState.Class1EventDrain,
                "Class 1 drain limit reached",
                $"Class 1 drain stopped after {drained} frames. ACD/GI still indicates possible pending data.",
                category: "Warning",
                dataClass: "Class 1");
            RaiseFinding(
                FindingSeverity.Warning,
                "IEC103-MASTER-CLASS1-DRAIN-LIMIT",
                "Class 1 drain limit reached",
                $"Drain reason: {reason}; frames drained={drained}; ACD={(_accessDemand ? 1 : 0)}; GI in progress={_giInProgress}.",
                "The relay may have a large event queue, stuck ACD, or the drain limit is too low for this test scenario.",
                "Review event queue size, increase MaxClass1DrainFrames for controlled tests, and verify relay ACD behavior.");
        }
        else
        {
            var stopReason = stoppedByNoData
                ? "NO DATA response"
                : stoppedByGiEnd
                    ? "GI END"
                    : stoppedByAcdClear
                        ? "ACD cleared"
                        : _dataFlowControl
                            ? "DFC busy"
                            : "controlled exit";

            SetState(
                Iec103MasterState.Class1EventDrain,
                "Class 1 drain completed",
                $"Frames drained={drained}. Stop reason={stopReason}. Returning to Class 2 normal cycle.",
                dataClass: "Class 1");
        }
    }

    private async Task SendFixedAndReceiveAsync(
        string summary,
        string dataClass,
        int functionCode,
        bool fcv,
        string pollingReason,
        bool consumeResponse,
        CancellationToken cancellationToken)
    {
        var fcbBeforeSend = _fcb;
        var control = Ft12FrameBuilder.BuildPrimaryControl(functionCode, fcv: fcv, fcb: fcv && fcbBeforeSend);
        var frame = Ft12FrameBuilder.Fixed(control, _settings.LinkAddress);
        await SendRawAsync(frame, summary, dataClass, pollingReason, cancellationToken).ConfigureAwait(false);

        if (!consumeResponse)
        {
            AdvanceFcbAfterUnconfirmedSend(fcv, fcbBeforeSend);
            return;
        }

        var response = await ReceiveOneAsync(dataClass, pollingReason, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            await HandleTimeoutRecoveryAsync(summary, cancellationToken).ConfigureAwait(false);
            return;
        }

        AdvanceFcbAfterResponse(fcv, fcbBeforeSend, response, summary, dataClass, pollingReason);
    }

    private async Task SendVariableAndReceiveAsync(string summary, string dataClass, IReadOnlyList<byte> asdu, string pollingReason, CancellationToken cancellationToken)
    {
        var fcbBeforeSend = _fcb;
        var control = Ft12FrameBuilder.BuildPrimaryControl(3, fcv: true, fcb: fcbBeforeSend);
        var frame = Ft12FrameBuilder.Variable(control, _settings.LinkAddress, asdu);
        await SendRawAsync(frame, summary, dataClass, pollingReason, cancellationToken).ConfigureAwait(false);

        var response = await ReceiveOneAsync(dataClass, pollingReason, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            await HandleTimeoutRecoveryAsync(summary, cancellationToken).ConfigureAwait(false);
            return;
        }

        AdvanceFcbAfterResponse(fcv: true, fcbBeforeSend, response, summary, dataClass, pollingReason);
    }

    private void AdvanceFcbAfterUnconfirmedSend(bool fcv, bool fcbBeforeSend)
    {
        if (!fcv)
        {
            return;
        }

        _fcb = !fcbBeforeSend;
    }

    private void AdvanceFcbAfterResponse(bool fcv, bool fcbBeforeSend, Ft12FrameDecode response, string summary, string dataClass, string pollingReason)
    {
        if (!fcv)
        {
            return;
        }

        if (IsValidSecondaryTransactionResponse(response))
        {
            _fcb = !fcbBeforeSend;
            return;
        }

        _fcb = fcbBeforeSend;
        AddFcbDiagnosticEvent(
            "IEC103-FCB-HOLD",
            "FCB held after invalid or ambiguous relay response",
            $"{summary}: response was not accepted for FCB advancement. Current FCB remains {(_fcb ? 1 : 0)}. Response={response.ShortMeaning}",
            dataClass,
            pollingReason,
            category: "Warning");
    }

    private static bool IsValidSecondaryTransactionResponse(Ft12FrameDecode response)
    {
        if (response.Format == Ft12FrameFormat.Malformed || !response.IsChecksumValid)
        {
            return false;
        }

        if (response.Format == Ft12FrameFormat.SingleCharacter)
        {
            return true;
        }

        return response.LinkControl is not null && !response.LinkControl.Prm;
    }

    private void AddFcbDiagnosticEvent(string eventId, string summary, string detail, string dataClass, string pollingReason, string category)
    {
        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.Unknown,
            State = _state,
            Category = category,
            DataClass = dataClass,
            PollingReason = pollingReason,
            Summary = summary,
            Detail = detail,
            OperatorMessage = summary,
            ProtocolMeaning = eventId,
            OperatorAction = pollingReason,
            RawHex = string.Empty
        });
    }

    private async Task SendRawAsync(byte[] frame, string summary, string dataClass, string pollingReason, CancellationToken cancellationToken)
    {
        try
        {
            await _transport.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _counters.TransportExceptions++;
            AddExceptionDiagnosticEvent(
                "IEC103-TRANSPORT-WRITE",
                "Serial write exception",
                ex,
                "Verify COM port availability, USB/RS485 converter state, relay wiring, and retry after closing other tools that may hold the port.",
                category: "Error",
                dataClass: dataClass);
            RaiseFinding(
                FindingSeverity.Error,
                "IEC103-TRANSPORT-WRITE",
                "Serial write exception",
                ex.Message,
                "The master could not send the next IEC-103 frame.",
                "Check COM port ownership, converter driver, wiring, and serial settings.");
            throw;
        }

        _counters.TxFrames++;

        var decoded = _parser.Decode(frame);
        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.MasterToSlave,
            State = _state,
            Category = "TX",
            DataClass = dataClass,
            PollingReason = pollingReason,
            Summary = summary,
            Detail = decoded.ShortMeaning,
            OperatorMessage = BuildTransmitOperatorMessage(summary, dataClass, pollingReason),
            ProtocolMeaning = decoded.ShortMeaning,
            OperatorAction = pollingReason,
            RawHex = ToHex(frame),
            Frame = decoded
        });
    }

    private async Task<Ft12FrameDecode?> ReceiveOneAsync(string dataClass, string pollingReason, CancellationToken cancellationToken)
    {
        var reader = new Ft12StreamReader(_transport);
        var stopwatch = Stopwatch.StartNew();
        byte[]? raw;
        try
        {
            raw = await reader.ReadFrameAsync(_settings.ResponseTimeoutMs, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _counters.TransportExceptions++;
            _counters.Timeouts++;
            _counters.ConsecutiveTimeouts++;
            _counters.MaxConsecutiveTimeouts = Math.Max(_counters.MaxConsecutiveTimeouts, _counters.ConsecutiveTimeouts);
            AddExceptionDiagnosticEvent(
                "IEC103-TRANSPORT-READ",
                "Serial read exception captured as diagnostic evidence",
                ex,
                "ARIEC60870 will treat this as a recoverable communication fault. Check COM setting, USB/RS485 driver, relay address, and line condition.",
                category: "Warning",
                dataClass: dataClass);
            RaiseFinding(
                FindingSeverity.Warning,
                "IEC103-TRANSPORT-READ",
                "Serial read exception captured",
                ex.Message,
                "The master could not read a complete IEC-103 response frame.",
                "Inspect Diagnostics tab, verify serial settings, and confirm the relay is configured as IEC-103 slave on the selected port.");
            return null;
        }
        stopwatch.Stop();

        if (raw is null || raw.Length == 0)
        {
            _counters.Timeouts++;
            _counters.ConsecutiveTimeouts++;
            _counters.MaxConsecutiveTimeouts = Math.Max(_counters.MaxConsecutiveTimeouts, _counters.ConsecutiveTimeouts);
            SetState(Iec103MasterState.TimeoutRecovery, "Response timeout", $"No slave response within {_settings.ResponseTimeoutMs} ms after {pollingReason}.", category: "Warning", dataClass: dataClass);
            return null;
        }

        _counters.RxFrames++;
        _counters.ConsecutiveTimeouts = 0;
        RecordResponseTime((int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds));

        var decoded = _parser.Decode(raw);
        if (!decoded.IsChecksumValid)
        {
            _counters.ChecksumErrors++;
        }

        if (decoded.Format == Ft12FrameFormat.Malformed)
        {
            _counters.MalformedFrames++;
        }

        var validFrame = decoded.Format != Ft12FrameFormat.Malformed && decoded.IsChecksumValid;
        if (decoded.IsSingleCharacterAck)
        {
            _counters.AckResponses++;
        }
        else if (decoded.IsSingleCharacterNack)
        {
            _counters.NackResponses++;
        }

        if (validFrame)
        {
            ApplySecondaryState(decoded);
        }

        var mappingUpdate = validFrame ? BuildMappingUpdate(decoded, ToHex(raw), DateTime.UtcNow) : MappingUpdate.Empty;
        var receiveDetail = BuildReceiveDetail(decoded, mappingUpdate);
        var operatorMessage = BuildReceiveOperatorMessage(decoded, mappingUpdate, dataClass, pollingReason);

        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.SlaveToMaster,
            State = _state,
            Category = decoded.IsChecksumValid ? "RX" : "RX Warning",
            DataClass = dataClass,
            PollingReason = pollingReason,
            Summary = decoded.ShortMeaning,
            Detail = receiveDetail,
            OperatorMessage = operatorMessage.Message,
            ProtocolMeaning = operatorMessage.ProtocolMeaning,
            OperatorAction = operatorMessage.Action,
            RawHex = ToHex(raw),
            ResponseTimeMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds),
            Frame = decoded,
            IsRelayValue = mappingUpdate.IsRelayValue,
            IsRelayEdgeEvent = mappingUpdate.IsRelayEdgeEvent,
            IsMappedSignal = mappingUpdate.IsMappedSignal,
            SignalKey = mappingUpdate.SignalKey,
            SignalName = mappingUpdate.SignalName,
            SignalGroup = mappingUpdate.SignalGroup,
            SignalType = mappingUpdate.SignalType,
            SignalDisplayValue = mappingUpdate.SignalDisplayValue,
            SignalRawValue = mappingUpdate.SignalRawValue,
            PreviousSignalValue = mappingUpdate.PreviousSignalValue,
            EdgeReason = mappingUpdate.EdgeReason,
            MappingProfileName = mappingUpdate.MappingProfileName,
            RelayTimestampText = mappingUpdate.RelayTimestampText,
            RelayTimestampInvalid = mappingUpdate.RelayTimestampInvalid
        });

        return decoded;
    }

    private async Task HandleTimeoutRecoveryAsync(string lastCommandSummary, CancellationToken cancellationToken)
    {
        if (_counters.ConsecutiveTimeouts < _settings.MaxConsecutiveTimeoutsBeforeResetFcb)
        {
            if (_settings.TimeoutRecoveryBackoffMs > 0)
            {
                SetState(Iec103MasterState.TimeoutRecovery, "Timeout backoff", $"Consecutive timeouts={_counters.ConsecutiveTimeouts}. Backing off {_settings.TimeoutRecoveryBackoffMs} ms before next action.", category: "Warning");
                await Task.Delay(_settings.TimeoutRecoveryBackoffMs, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        _counters.TimeoutRecoveries++;
        RaiseFinding(
            FindingSeverity.Warning,
            "IEC103-MASTER-TIMEOUT-BURST",
            "Consecutive response timeout burst",
            $"{_counters.ConsecutiveTimeouts} consecutive timeout(s) after command '{lastCommandSummary}'.",
            "The relay may be offline, wrong serial setting/address may be used, or RS485 turnaround/wiring may be incorrect.",
            "Verify COM port, baudrate/parity, link address, converter direction control, and relay IEC-103 settings. Use reset FCB recovery only after controlled timeout threshold.");

        if (_settings.ResetFcbAfterTimeoutBurst)
        {
            _counters.ConsecutiveTimeouts = 0;
            await ResetFcbAsync("Controlled timeout recovery", cancellationToken).ConfigureAwait(false);
        }
    }

    private void ApplySecondaryState(Ft12FrameDecode decoded)
    {
        var link = decoded.LinkControl;
        if (link is null || link.Prm)
        {
            return;
        }

        _accessDemand = link.Acd ?? false;
        _dataFlowControl = link.Dfc ?? false;

        if (_dataFlowControl)
        {
            _counters.BusyResponses++;
        }

        if (link.FunctionCode == 0)
        {
            _counters.AckResponses++;
        }
        else if (link.FunctionCode == 1)
        {
            _counters.NackResponses++;
        }
        else if (link.FunctionCode == 9)
        {
            _counters.NoDataResponses++;
            // A secondary NO DATA can still carry ACD=1, especially when a Class 2/background poll
            // tells the master that Class 1/event data is pending. Preserve that information so the
            // master can switch to a bounded Class 1 drain instead of ignoring access demand.
            _accessDemand = link.Acd == true;
        }
        else if (link.FunctionCode == 8 || decoded.Asdu is not null)
        {
            _counters.UserDataResponses++;
        }

        if (decoded.Asdu is not null)
        {
            ApplyAsduState(decoded.Asdu);
        }
    }

    private void ApplyAsduState(AsduDecode asdu)
    {
        switch (asdu.TypeId)
        {
            case 1:
            case 2:
                _counters.DpiEvents++;
                break;
            case 5:
                _counters.IdentificationResponses++;
                break;
            case 8:
                _counters.GiEndResponses++;
                _giInProgress = false;
                break;
            default:
                if (asdu.Status == DecodeStatus.Unknown)
                {
                    _counters.UnknownAsduResponses++;
                }
                break;
        }
    }

    private void RecordResponseTime(int responseTimeMs)
    {
        _counters.TimedResponses++;
        _counters.TotalResponseTimeMs += responseTimeMs;
        _counters.MaxResponseTimeMs = Math.Max(_counters.MaxResponseTimeMs, responseTimeMs);
    }

    private static string BuildTransmitOperatorMessage(string summary, string dataClass, string pollingReason)
    {
        if (summary.Contains("Class 2", StringComparison.OrdinalIgnoreCase))
        {
            return "TX: Class 2 background poll.";
        }

        if (summary.Contains("Class 1", StringComparison.OrdinalIgnoreCase))
        {
            return "TX: Class 1 event-drain request.";
        }

        if (summary.Contains("General Interrogation", StringComparison.OrdinalIgnoreCase))
        {
            return "Master starts General Interrogation to collect the relay snapshot.";
        }

        if (summary.Contains("Clock", StringComparison.OrdinalIgnoreCase))
        {
            return "Master sends time synchronization to the relay.";
        }

        if (summary.Contains("Reset", StringComparison.OrdinalIgnoreCase))
        {
            return "Master synchronizes the IEC-103 link before normal polling.";
        }

        return string.IsNullOrWhiteSpace(pollingReason) ? summary : $"{summary}: {pollingReason}";
    }

    private static OperatorMessage BuildReceiveOperatorMessage(Ft12FrameDecode decoded, MappingUpdate mappingUpdate, string dataClass, string pollingReason)
    {
        if (!decoded.IsChecksumValid || decoded.Format == Ft12FrameFormat.Malformed)
        {
            return new OperatorMessage(
                "Received frame has a quality problem.",
                decoded.ShortMeaning,
                "Check serial settings, line quality, converter direction control, grounding, and termination.");
        }

        if (decoded.Format == Ft12FrameFormat.SingleCharacter)
        {
            if (decoded.IsSingleCharacterNack)
            {
                return new OperatorMessage(
                    "Relay returned a single-character negative acknowledgement.",
                    decoded.ShortMeaning,
                    "Inspect link address, FCB/FCV state, requested class, and relay busy/no-data behaviour.");
            }

            return new OperatorMessage(
                "Relay acknowledged the previous master command.",
                decoded.ShortMeaning,
                "Continue the configured startup or polling sequence.");
        }

        var link = decoded.LinkControl;
        if (link is not null && !link.Prm && link.FunctionCode == 9)
        {
            var action = link.Acd == true
                ? "Relay has no data for the requested class, but ACD=1 indicates pending Class 1 event data. ARIEC60870 will enter bounded Class 1 drain."
                : "Relay has no data for the requested class. ARIEC60870 will avoid blind Class 1 bombardment and continue normal Class 2 cycle.";
            return new OperatorMessage(
                "Relay responded: no requested data available.",
                $"Secondary response FC=9, ACD={(link.Acd == true ? 1 : 0)}, DFC={(link.Dfc == true ? 1 : 0)}.",
                action);
        }

        if (decoded.Asdu is not null)
        {
            if (mappingUpdate.IsRelayValue)
            {
                var name = mappingUpdate.IsMappedSignal ? mappingUpdate.SignalName : $"Unmapped FUN/INF {decoded.Asdu.FunctionType}/{decoded.Asdu.InformationNumber}";
                var edge = mappingUpdate.IsRelayEdgeEvent ? $" Edge event: {mappingUpdate.EdgeReason}." : " Snapshot/value update only.";
                return new OperatorMessage(
                    $"Relay value received: {name} = {mappingUpdate.SignalDisplayValue}.",
                    $"ASDU={decoded.Asdu.TypeName}, COT={decoded.Asdu.CauseName}, FUN={decoded.Asdu.FunctionType}, INF={decoded.Asdu.InformationNumber}, RelayTime={mappingUpdate.RelayTimestampText}.{edge}",
                    mappingUpdate.IsMappedSignal ? "Update Value Viewer and Event Log when this is an edge." : "Add a user mapping profile entry if this point needs a readable signal name.");
            }

            if (decoded.Asdu.TypeId == 8)
            {
                return new OperatorMessage(
                    "General Interrogation completed.",
                    decoded.ShortMeaning,
                    "Return to normal Class 2 background polling unless ACD indicates pending Class 1 data.");
            }

            return new OperatorMessage(
                "Relay protocol data received.",
                decoded.ShortMeaning,
                "Inspect ASDU details if this response is relevant to the test case.");
        }

        if (link is not null)
        {
            return new OperatorMessage(
                decoded.ShortMeaning,
                $"Secondary control field FC={link.FunctionCode}, ACD={(link.Acd == true ? 1 : 0)}, DFC={(link.Dfc == true ? 1 : 0)}.",
                link.Dfc == true ? "Relay is busy. ARIEC60870 will back off." : "Continue normal polling policy.");
        }

        return new OperatorMessage(decoded.ShortMeaning, decoded.ShortMeaning, string.Empty);
    }

    private readonly record struct OperatorMessage(string Message, string ProtocolMeaning, string Action);

    private static string BuildReceiveDetail(Ft12FrameDecode decoded, MappingUpdate mappingUpdate)
    {
        var parts = new List<string>();
        if (decoded.LinkControl is not null)
        {
            var link = decoded.LinkControl;
            parts.Add($"FC={link.FunctionCode}");
            parts.Add($"ACD={(link.Acd == true ? 1 : 0)}");
            parts.Add($"DFC={(link.Dfc == true ? 1 : 0)}");
        }

        if (decoded.Asdu is not null)
        {
            parts.Add($"ASDU={decoded.Asdu.TypeName}");
            parts.Add($"COT={decoded.Asdu.CauseName}");
            parts.Add($"FUN={decoded.Asdu.FunctionType}");
            parts.Add($"INF={decoded.Asdu.InformationNumber}");
            if (mappingUpdate.IsRelayValue) parts.Add($"Signal={(mappingUpdate.IsMappedSignal ? mappingUpdate.SignalName : "Unmapped")}");
            if (mappingUpdate.IsRelayValue) parts.Add($"Value={mappingUpdate.SignalDisplayValue}");
            if (mappingUpdate.IsRelayEdgeEvent) parts.Add($"Edge={mappingUpdate.EdgeReason}");
            if (!string.IsNullOrWhiteSpace(mappingUpdate.RelayTimestampText)) parts.Add($"RelayTime={mappingUpdate.RelayTimestampText}");
            if (decoded.Asdu.Dpi.HasValue) parts.Add($"DPI={decoded.Asdu.Dpi} ({decoded.Asdu.DpiText})");
            if (decoded.Asdu.Time is not null) parts.Add($"Time={decoded.Asdu.Time}");
        }

        if (decoded.Issues.Count > 0)
        {
            parts.Add("Issues=" + string.Join("; ", decoded.Issues));
        }

        return string.Join(", ", parts);
    }


    private MappingUpdate BuildMappingUpdate(Ft12FrameDecode decoded, string rawHex, DateTime arrivalUtc)
    {
        var asdu = decoded.Asdu;
        if (asdu is null || asdu.FunctionType is null || asdu.InformationNumber is null)
        {
            return MappingUpdate.Empty;
        }

        var isRelayValue = asdu.TypeId is 1 or 2 or 3 or 4 or 9;
        if (!isRelayValue)
        {
            return MappingUpdate.Empty;
        }

        var resolved = _mappingProfile.Resolve(asdu);
        var key = string.IsNullOrWhiteSpace(resolved.SignalKey)
            ? $"FUN{asdu.FunctionType.Value:000}:INF{asdu.InformationNumber.Value:000}:{Iec103SignalMappingProfile.InferSignalType(asdu)}"
            : resolved.SignalKey;

        var fallbackName = $"FUN {asdu.FunctionType.Value} / INF {asdu.InformationNumber.Value}";
        var resolvedRawValue = resolved.RawValue ?? string.Empty;
        var displayName = resolved.IsMapped ? (resolved.SignalName ?? string.Empty) : fallbackName;
        var displayValue = string.IsNullOrWhiteSpace(resolved.DisplayValue)
            ? (asdu.DpiText ?? resolvedRawValue)
            : resolved.DisplayValue;

        var relayTimeText = asdu.Time?.DisplayTime ?? "No relay timestamp";
        var relayTimeInvalid = asdu.Time?.Invalid == true;

        _valuePoints.TryGetValue(key, out var previous);
        var previousValue = previous?.DisplayValue ?? string.Empty;
        var valueChanged = previous is not null && !string.Equals(previous.DisplayValue, displayValue, StringComparison.OrdinalIgnoreCase);
        var spontaneousEdge = asdu.CauseOfTransmission == 1;
        var shouldLogEdge = valueChanged || spontaneousEdge;
        var edgeReason = valueChanged
            ? "state change"
            : spontaneousEdge
                ? "relay spontaneous edge event"
                : string.Empty;

        var valuePoint = new Iec103ValuePoint
        {
            Key = key,
            IsMapped = resolved.IsMapped,
            SignalName = displayName,
            SignalGroup = resolved.IsMapped ? resolved.SignalGroup : "Unmapped",
            SignalType = resolved.SignalType,
            FunctionType = asdu.FunctionType,
            InformationNumber = asdu.InformationNumber,
            Dpi = asdu.Dpi,
            RawValue = resolvedRawValue,
            DisplayValue = displayValue,
            Source = asdu.CauseName,
            CauseOfTransmission = asdu.CauseName,
            AsduType = asdu.TypeName,
            RelayTimeText = relayTimeText,
            RelayTimeInvalid = relayTimeInvalid,
            ArrivalTimeUtc = arrivalUtc,
            RawHex = rawHex
        };

        _valuePoints[key] = valuePoint;

        if (shouldLogEdge)
        {
            _eventLog.Add(new Iec103RelayEventLogEntry
            {
                EvidenceSequenceNumber = _sequence + 1,
                RelayTimeText = relayTimeText,
                RelayTimeInvalid = relayTimeInvalid,
                ArrivalTimeUtc = arrivalUtc,
                IsMapped = resolved.IsMapped,
                SignalName = displayName,
                SignalGroup = valuePoint.SignalGroup,
                SignalType = valuePoint.SignalType,
                FunctionType = asdu.FunctionType,
                InformationNumber = asdu.InformationNumber,
                PreviousValue = previousValue,
                NewValue = displayValue,
                EdgeReason = edgeReason,
                CauseOfTransmission = asdu.CauseName,
                AsduType = asdu.TypeName,
                RawHex = rawHex
            });
            _counters.RelayEventsDroppedFromMemory += TrimHead(_eventLog, _settings.MaxRetainedRelayEvents);
        }

        return new MappingUpdate
        {
            IsRelayValue = true,
            IsRelayEdgeEvent = shouldLogEdge,
            IsMappedSignal = resolved.IsMapped,
            SignalKey = key,
            SignalName = displayName,
            SignalGroup = valuePoint.SignalGroup,
            SignalType = valuePoint.SignalType,
            SignalDisplayValue = displayValue,
            SignalRawValue = resolvedRawValue,
            PreviousSignalValue = previousValue,
            EdgeReason = edgeReason,
            MappingProfileName = resolved.IsMapped ? (_mappingProfile.ProfileName ?? string.Empty) : string.Empty,
            RelayTimestampText = relayTimeText,
            RelayTimestampInvalid = relayTimeInvalid
        };
    }

    private sealed class MappingUpdate
    {
        public static MappingUpdate Empty { get; } = new();
        public bool IsRelayValue { get; init; }
        public bool IsRelayEdgeEvent { get; init; }
        public bool IsMappedSignal { get; init; }
        public string SignalKey { get; init; } = string.Empty;
        public string SignalName { get; init; } = string.Empty;
        public string SignalGroup { get; init; } = string.Empty;
        public string SignalType { get; init; } = string.Empty;
        public string SignalDisplayValue { get; init; } = string.Empty;
        public string SignalRawValue { get; init; } = string.Empty;
        public string PreviousSignalValue { get; init; } = string.Empty;
        public string EdgeReason { get; init; } = string.Empty;
        public string MappingProfileName { get; init; } = string.Empty;
        public string RelayTimestampText { get; init; } = string.Empty;
        public bool RelayTimestampInvalid { get; init; }
    }


    private void DrainTransportDiagnostics(string phase)
    {
        if (_transport is not ITransportDiagnosticSource diagnosticSource)
        {
            return;
        }

        foreach (var diagnostic in diagnosticSource.DrainDiagnostics())
        {
            AddEvent(new Iec103MasterEvidenceEvent
            {
                Direction = FrameDirection.Unknown,
                State = _state,
                Category = diagnostic.Severity,
                DataClass = "-",
                PollingReason = string.IsNullOrWhiteSpace(diagnostic.Code) ? phase : diagnostic.Code,
                Summary = diagnostic.Message,
                Detail = diagnostic.Detail,
                OperatorMessage = diagnostic.Message,
                ProtocolMeaning = diagnostic.Detail,
                OperatorAction = diagnostic.Recommendation,
                RawHex = string.Empty,
                ExceptionType = diagnostic.ExceptionType,
                ExceptionMessage = diagnostic.ExceptionMessage,
                ExceptionStackTrace = diagnostic.ExceptionStackTrace
            });
        }
    }

    private void AddExceptionDiagnosticEvent(
        string code,
        string summary,
        Exception exception,
        string recommendation,
        string category = "Error",
        string dataClass = "-")
    {
        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.Unknown,
            State = _state,
            Category = category,
            DataClass = dataClass,
            PollingReason = code,
            Summary = summary,
            Detail = exception.Message,
            OperatorMessage = summary,
            ProtocolMeaning = exception.Message,
            OperatorAction = recommendation,
            RawHex = string.Empty,
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            ExceptionMessage = exception.Message,
            ExceptionStackTrace = exception.ToString()
        });
    }

    private void BuildPostRunFindings()
    {
        if (_settings.SendGeneralInterrogationOnConnect && _counters.GiCommands > 0 && _counters.GiEndResponses == 0)
        {
            RaiseFinding(
                FindingSeverity.Warning,
                "IEC103-MASTER-GI-NO-END",
                "General Interrogation did not reach GI END",
                $"GI commands={_counters.GiCommands}, GI END responses={_counters.GiEndResponses}.",
                "The relay may not have completed GI during the test window, or the master/relay addressing and GI handling may not match.",
                "Increase duration, verify CA/link address, and inspect Class 1 follow-up evidence after GI.");
        }

        if (_counters.Class1Requests > 0 && _counters.NoDataResponses > _counters.UserDataResponses * 5 && _counters.UserDataResponses == 0)
        {
            RaiseFinding(
                FindingSeverity.Info,
                "IEC103-MASTER-CLASS1-NO-DATA",
                "Class 1 follow-up produced no useful data",
                $"Class 1 requests={_counters.Class1Requests}, NO DATA={_counters.NoDataResponses}, user data={_counters.UserDataResponses}.",
                "This can be normal if the relay has no pending events, but it should not become a continuous Class 1 polling storm.",
                "ARIEC60870 stops Class 1 drain on NO DATA and returns to Class 2 normal polling. If NO DATA carries ACD=1 after a Class 2 poll, ARIEC60870 performs a bounded Class 1 event-drain.");
        }

        if (_counters.ChecksumErrors > 0 || _counters.MalformedFrames > 0)
        {
            RaiseFinding(
                FindingSeverity.Error,
                "IEC103-MASTER-FRAME-QUALITY",
                "Frame quality problem detected",
                $"Checksum errors={_counters.ChecksumErrors}, malformed frames={_counters.MalformedFrames}.",
                "Serial quality, baud/parity mismatch, or converter timing may be affecting IEC-103 communication.",
                "Check serial setting, grounding, RS485 polarity, converter, termination, and line noise.");
        }

        if (_counters.UnknownAsduResponses > 0)
        {
            RaiseFinding(
                FindingSeverity.Info,
                "IEC103-MASTER-UNKNOWN-ASDU",
                "Unknown or vendor-specific ASDU detected",
                $"Unknown ASDU responses={_counters.UnknownAsduResponses}.",
                "Relay-specific semantics may require a IEC-103 relay/FUN-INF profile mapping.",
                "Keep raw frame evidence and add a device profile mapping before presenting final signal labels.");
        }
    }

    private void SetState(Iec103MasterState state, string summary, string detail, string category = "Info", string dataClass = "-")
    {
        _state = state;
        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.Unknown,
            State = state,
            Category = category,
            DataClass = dataClass,
            Summary = summary,
            Detail = detail,
            OperatorMessage = summary,
            ProtocolMeaning = detail,
            OperatorAction = detail,
            RawHex = string.Empty
        });
    }

    private void RaiseFinding(FindingSeverity severity, string id, string title, string evidence, string impact, string recommendation)
    {
        if (_findings.Any(x => x.Id == id && x.Evidence == evidence))
        {
            return;
        }

        var finding = new Iec103MasterFinding
        {
            Severity = severity,
            Id = id,
            Title = title,
            Evidence = evidence,
            Impact = impact,
            Recommendation = recommendation
        };

        _findings.Add(finding);
        _counters.FindingsDroppedFromMemory += TrimHead(_findings, _settings.MaxRetainedFindings);
        FindingRaised?.Invoke(this, finding);
    }

    private void AddEvent(Iec103MasterEvidenceEvent item)
    {
        var enriched = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = ++_sequence,
            TimestampUtc = item.TimestampUtc,
            State = item.State,
            Direction = item.Direction,
            Category = item.Category,
            DataClass = item.DataClass,
            PollingReason = item.PollingReason,
            Summary = item.Summary,
            Detail = item.Detail,
            OperatorMessage = item.OperatorMessage,
            ProtocolMeaning = item.ProtocolMeaning,
            OperatorAction = item.OperatorAction,
            RawHex = item.RawHex,
            ExceptionType = item.ExceptionType,
            ExceptionMessage = item.ExceptionMessage,
            ExceptionStackTrace = item.ExceptionStackTrace,
            ResponseTimeMs = item.ResponseTimeMs,
            Frame = item.Frame,
            ProtocolMode = Iec60870ProtocolMode.Iec103,
            LinkAddress = item.LinkAddress ?? _settings.LinkAddress,
            TypeId = item.Frame?.Asdu?.TypeId,
            TypeName = item.Frame?.Asdu?.TypeName ?? string.Empty,
            VariableStructureQualifier = item.Frame?.Asdu?.Vsq,
            IsSequenceAsdu = item.Frame?.Asdu?.Sequence,
            ObjectCount = item.Frame?.Asdu?.ObjectCount,
            CauseOfTransmission = item.Frame?.Asdu?.CauseOfTransmission,
            CauseName = item.Frame?.Asdu?.CauseName ?? string.Empty,
            CommonAddressNumber = item.Frame?.Asdu?.CommonAddress,
            IsRelayValue = item.IsRelayValue,
            IsRelayEdgeEvent = item.IsRelayEdgeEvent,
            IsMappedSignal = item.IsMappedSignal,
            SignalKey = item.SignalKey,
            SignalName = item.SignalName,
            SignalGroup = item.SignalGroup,
            SignalType = item.SignalType,
            SignalDisplayValue = item.SignalDisplayValue,
            SignalRawValue = item.SignalRawValue,
            PreviousSignalValue = item.PreviousSignalValue,
            EdgeReason = item.EdgeReason,
            MappingProfileName = item.MappingProfileName,
            RelayTimestampText = item.RelayTimestampText,
            RelayTimestampInvalid = item.RelayTimestampInvalid
        };

        _events.Add(enriched);
        _counters.EvidenceEventsDroppedFromMemory += TrimHead(_events, _settings.MaxRetainedEvidenceEvents);
        EvidenceReceived?.Invoke(this, enriched);
    }

    private static long TrimHead<T>(List<T> list, int maxRetained)
    {
        if (maxRetained <= 0)
        {
            var removed = list.Count;
            list.Clear();
            return removed;
        }

        var excess = list.Count - maxRetained;
        if (excess <= 0)
        {
            return 0;
        }

        list.RemoveRange(0, excess);
        return excess;
    }

    private static string ToHex(IReadOnlyList<byte> bytes) => string.Join(" ", bytes.Select(x => x.ToString("X2")));
}
