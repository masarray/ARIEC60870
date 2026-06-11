// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;
using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Master.Transport;

namespace ARIEC60870.Master;

public sealed class Iec104ClientSession : IProtocolMasterSession, IProtocolControlCommandSession
{
    private readonly Iec103MasterSettings _settings;
    private readonly IByteTransport _transport;
    private readonly Iec104ApduParser _parser;
    private readonly List<Iec103MasterEvidenceEvent> _events = new();
    private readonly List<Iec103MasterFinding> _findings = new();
    private readonly Iec103MasterCounters _counters = new();
    private int _sendSequence;
    private int _receiveSequence;
    private long _sequence;
    private Iec103MasterState _state = Iec103MasterState.Created;
    private DateTime _lastTestFrameUtc = DateTime.MinValue;
    private DateTime _lastBackgroundReadUtc = DateTime.MinValue;
    private readonly ConcurrentQueue<Iec60870ControlCommandRequest> _controlCommands = new();

    public Iec104ClientSession(Iec103MasterSettings settings, IByteTransport transport)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _parser = new Iec104ApduParser(settings.CauseOfTransmissionSize, settings.CommonAddressSize, settings.InformationObjectAddressSize);
    }

    public event EventHandler<Iec103MasterEvidenceEvent>? EvidenceReceived;
    public event EventHandler<Iec103MasterFinding>? FindingRaised;

    public bool SupportsRuntimeControlCommands => true;

    public void QueueControlCommand(Iec60870ControlCommandRequest request)
    {
        if (request is not null)
        {
            _controlCommands.Enqueue(request);
        }
    }

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
            SetState(Iec103MasterState.OpeningTransport, "Opening IEC-104 TCP connection", _settings.SerialSummary);
            await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);
            SetState(Iec103MasterState.Connected, "IEC-104 TCP connected", _settings.SerialSummary);

            await SendUAndReceiveAsync("STARTDT activation", Iec104FrameBuilder.StartDtActivation(), "Start data transfer before I-format ASDUs", cancellationToken).ConfigureAwait(false);

            if (_settings.SendClockSyncOnConnect)
            {
                _counters.ClockSyncCommands++;
                await SendIAsync("IEC-104 clock sync", Iec10xAsduBuilder.ClockSynchronization(_settings, DateTime.Now), "Startup CP56Time2a clock synchronization", cancellationToken).ConfigureAwait(false);
                await DrainBurstAsync("Clock sync confirmation", maxFrames: 2, cancellationToken).ConfigureAwait(false);
            }

            if (_settings.SendGeneralInterrogationOnConnect)
            {
                _counters.GiCommands++;
                SetState(Iec103MasterState.GeneralInterrogation, "IEC-104 general interrogation", "Sending C_IC_NA_1 activation over I-format APDU.", dataClass: "I-format");
                await SendIAsync("IEC-104 general interrogation", Iec10xAsduBuilder.GeneralInterrogation(_settings), "Startup station interrogation C_IC_NA_1", cancellationToken).ConfigureAwait(false);
                await DrainBurstAsync("GI follow-up receive window", Math.Max(6, Math.Min(_settings.MaxClass1DrainFrames, 64)), cancellationToken).ConfigureAwait(false);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                if (await ProcessPendingControlCommandsAsync(cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                if ((DateTime.UtcNow - _lastBackgroundReadUtc).TotalMilliseconds >= _settings.Class2PollIntervalMs)
                {
                    _lastBackgroundReadUtc = DateTime.UtcNow;
                    await DrainBurstAsync("IEC-104 passive receive / background data window", maxFrames: 2, cancellationToken).ConfigureAwait(false);
                }

                if ((DateTime.UtcNow - _lastTestFrameUtc).TotalMilliseconds >= _settings.Iec104T3TestIntervalMs)
                {
                    _lastTestFrameUtc = DateTime.UtcNow;
                    await SendUAndReceiveAsync("TESTFR activation", Iec104FrameBuilder.TestFrActivation(), "Connection health check", cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            completion = "Stopped by cancellation or requested duration.";
        }
        catch (Exception ex)
        {
            completion = "Fault: " + ex.Message;
            SetState(Iec103MasterState.Faulted, "IEC-104 session faulted", ex.Message, category: "Error");
            RaiseFinding(FindingSeverity.Error, "IEC104-SESSION-FAULT", "IEC-104 session faulted", ex.Message, "The IEC-104 client session could not continue.", "Check IP address, TCP port 2404, firewall, server active connection limit, CA/COT/IOA profile, and STARTDT handling.");
        }
        finally
        {
            SetState(Iec103MasterState.Stopping, "Closing IEC-104 TCP connection", "Closing TCP transport.");
            try { await _transport.CloseAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            DrainTransportDiagnostics("Close transport");
            SetState(Iec103MasterState.Stopped, "IEC-104 stopped", completion);
        }

        BuildPostRunFindings();
        return new Iec103MasterRunResult
        {
            ProductMode = "IEC 60870-5-104 TCP Client Tester",
            Settings = _settings.CreateReportSnapshot(),
            Counters = _counters,
            Events = _events.ToArray(),
            Findings = _findings.ToArray(),
            StartedUtc = started,
            FinishedUtc = DateTime.UtcNow,
            CompletedNormally = !completion.StartsWith("Fault:", StringComparison.OrdinalIgnoreCase),
            CompletionReason = completion
        };
    }


    private Iec103MasterSettings SettingsForCommand(Iec60870ControlCommandRequest request)
    {
        if (!request.CommonAddress.HasValue || request.CommonAddress.Value == _settings.CommonAddress)
        {
            return _settings;
        }

        var copy = _settings.CreateReportSnapshot();
        copy.CommonAddress = request.CommonAddress.Value;
        return copy;
    }

    private async Task<bool> ProcessPendingControlCommandsAsync(CancellationToken cancellationToken)
    {
        if (!_controlCommands.TryDequeue(out var request))
        {
            return false;
        }

        SetState(Iec103MasterState.GeneralInterrogation, "IEC-104 operator command", request.Summary, category: "Command", dataClass: "I-format");
        var commandSettings = SettingsForCommand(request);

        switch (request.Kind)
        {
            case Iec60870ControlCommandKind.GeneralInterrogation:
                _counters.GiCommands++;
                await SendIAsync("IEC-104 manual general interrogation", Iec10xAsduBuilder.GeneralInterrogation(commandSettings), "Manual command dock GI", cancellationToken).ConfigureAwait(false);
                await DrainBurstAsync("Manual GI follow-up receive window", Math.Max(6, Math.Min(_settings.MaxClass1DrainFrames, 64)), cancellationToken).ConfigureAwait(false);
                break;
            case Iec60870ControlCommandKind.ClockSync:
                _counters.ClockSyncCommands++;
                await SendIAsync("IEC-104 manual clock sync", Iec10xAsduBuilder.ClockSynchronization(commandSettings, DateTime.Now), "Manual command dock clock synchronization", cancellationToken).ConfigureAwait(false);
                await DrainBurstAsync("Clock sync confirmation", maxFrames: 2, cancellationToken).ConfigureAwait(false);
                break;
            case Iec60870ControlCommandKind.Read:
                await SendIAsync(request.Summary, Iec10xAsduBuilder.ReadCommand(commandSettings, request.InformationObjectAddress), "Manual command dock read C_RD_NA_1", cancellationToken).ConfigureAwait(false);
                await DrainBurstAsync("Read command response window", maxFrames: 4, cancellationToken).ConfigureAwait(false);
                break;
            case Iec60870ControlCommandKind.SingleCommand:
                await SendIAsync(request.Summary, Iec10xAsduBuilder.SingleCommand(commandSettings, request.InformationObjectAddress, request.Value != 0, request.SelectBeforeOperate, request.Qualifier), "Manual command dock C_SC_NA_1", cancellationToken).ConfigureAwait(false);
                await DrainBurstAsync("Single command feedback window", maxFrames: 4, cancellationToken).ConfigureAwait(false);
                break;
            case Iec60870ControlCommandKind.DoubleCommand:
                await SendIAsync(request.Summary, Iec10xAsduBuilder.DoubleCommand(commandSettings, request.InformationObjectAddress, request.Value, request.SelectBeforeOperate, request.Qualifier), "Manual command dock C_DC_NA_1", cancellationToken).ConfigureAwait(false);
                await DrainBurstAsync("Double command feedback window", maxFrames: 4, cancellationToken).ConfigureAwait(false);
                break;
            case Iec60870ControlCommandKind.RegulatingStepCommand:
                await SendIAsync(request.Summary, Iec10xAsduBuilder.RegulatingStepCommand(commandSettings, request.InformationObjectAddress, request.Value, request.SelectBeforeOperate, request.Qualifier), "Manual command dock C_RC_NA_1", cancellationToken).ConfigureAwait(false);
                await DrainBurstAsync("Regulating step feedback window", maxFrames: 4, cancellationToken).ConfigureAwait(false);
                break;
            case Iec60870ControlCommandKind.SetpointNormalizedCommand:
                await SendIAsync(request.Summary, Iec10xAsduBuilder.SetpointNormalizedCommand(commandSettings, request.InformationObjectAddress, request.NumericValue, request.SelectBeforeOperate, request.Qualifier), "Manual command dock C_SE_NA_1", cancellationToken).ConfigureAwait(false);
                await DrainBurstAsync("Setpoint feedback window", maxFrames: 4, cancellationToken).ConfigureAwait(false);
                break;
        }

        return true;
    }

    private async Task SendUAndReceiveAsync(string summary, byte[] frame, string reason, CancellationToken cancellationToken)
    {
        await SendRawAsync(frame, summary, "U-format", reason, cancellationToken).ConfigureAwait(false);
        var received = await ReceiveOneAsync("U-format", reason, cancellationToken).ConfigureAwait(false);
        if (summary.Contains("STARTDT", StringComparison.OrdinalIgnoreCase) && received?.UFormatName.Contains("STARTDT con", StringComparison.OrdinalIgnoreCase) != true)
        {
            RaiseFinding(FindingSeverity.Error, "IEC104-STARTDT-CON", "IEC-104 STARTDT confirmation was not received", received?.ShortMeaning ?? "No APDU received", "The server is not confirmed ready for I-format ASDU data transfer.", "Verify server connection limits, STARTDT policy, firewall/NAT interference, and server-side IEC-104 state.");
        }
        if (summary.Contains("TESTFR", StringComparison.OrdinalIgnoreCase) && received?.UFormatName.Contains("TESTFR con", StringComparison.OrdinalIgnoreCase) != true)
        {
            RaiseFinding(FindingSeverity.Warning, "IEC104-TESTFR-CON", "IEC-104 TESTFR confirmation was not received", received?.ShortMeaning ?? "No APDU received", "Idle connection supervision is questionable.", "Check t3/t1 values, server test-frame support, TCP idle timeout, and duplicate sessions.");
        }
    }

    private async Task SendIAsync(string summary, byte[] asdu, string reason, CancellationToken cancellationToken)
    {
        var frame = Iec104FrameBuilder.I(_sendSequence++, _receiveSequence, asdu);
        await SendRawAsync(frame, summary, "I-format", reason, cancellationToken).ConfigureAwait(false);
    }

    private async Task DrainBurstAsync(string reason, int maxFrames, CancellationToken cancellationToken)
    {
        var received = 0;
        while (!cancellationToken.IsCancellationRequested && received < maxFrames)
        {
            var before = _counters.RxFrames;
            var apdu = await ReceiveOneAsync("I/S/U-format", reason, cancellationToken).ConfigureAwait(false);
            if (apdu is null || _counters.RxFrames == before) break;
            received++;
            if (apdu.Asdu?.CauseOfTransmission == 10) break;
        }
    }

    private async Task SendRawAsync(byte[] frame, string summary, string dataClass, string reason, CancellationToken cancellationToken)
    {
        await _transport.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        _counters.TxFrames++;
        var decoded = _parser.Decode(frame);
        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.MasterToSlave,
            State = _state,
            Category = "TX",
            DataClass = dataClass,
            PollingReason = reason,
            Summary = summary,
            Detail = decoded.ShortMeaning,
            OperatorMessage = summary,
            ProtocolMeaning = decoded.ShortMeaning,
            OperatorAction = reason,
            RawHex = ToHex(frame),
            ApciFormat = decoded.Format,
            SendSequence = decoded.SendSequence,
            ReceiveSequence = decoded.ReceiveSequence,
            UFormatName = decoded.UFormatName,
            TypeId = decoded.Asdu?.TypeId,
            TypeName = decoded.Asdu?.TypeName ?? string.Empty,
            VariableStructureQualifier = decoded.Asdu?.VariableStructureQualifier,
            IsSequenceAsdu = decoded.Asdu?.IsSequence,
            ObjectCount = decoded.Asdu?.ObjectCount,
            CauseOfTransmission = decoded.Asdu?.CauseOfTransmission,
            CauseName = decoded.Asdu?.CotNameWithFlags ?? string.Empty,
            OriginatorAddress = decoded.Asdu?.OriginatorAddress,
            CommonAddressNumber = decoded.Asdu?.CommonAddress,
            InformationObjectAddress = decoded.Asdu?.FirstObject?.InformationObjectAddress,
            ObjectSummary = decoded.Asdu?.ObjectSummary ?? string.Empty
        });
    }

    private async Task<Iec104ApduDecode?> ReceiveOneAsync(string dataClass, string reason, CancellationToken cancellationToken)
    {
        var reader = new Iec104StreamReader(_transport);
        var sw = Stopwatch.StartNew();
        var raw = await reader.ReadFrameAsync(_settings.ResponseTimeoutMs, cancellationToken).ConfigureAwait(false);
        sw.Stop();
        if (raw is null || raw.Length == 0)
        {
            _counters.Timeouts++;
            SetState(Iec103MasterState.TimeoutRecovery, "IEC-104 receive timeout", $"No APDU within {_settings.ResponseTimeoutMs} ms during {reason}.", category: "Info", dataClass: dataClass);
            return null;
        }

        _counters.RxFrames++;
        _counters.TimedResponses++;
        _counters.TotalResponseTimeMs += sw.ElapsedMilliseconds;
        _counters.MaxResponseTimeMs = Math.Max(_counters.MaxResponseTimeMs, (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds));
        var decoded = _parser.Decode(raw);
        AuditAsduForensicFindings("IEC104", decoded.Asdu);
        if (!decoded.IsValid) _counters.MalformedFrames++;
        if (decoded.Format == "I")
        {
            _counters.UserDataResponses++;
            if (decoded.SendSequence.HasValue && decoded.SendSequence.Value != _receiveSequence)
            {
                RaiseFinding(FindingSeverity.Warning, "IEC104-NS-SEQUENCE", "IEC-104 N(S) sequence discontinuity", $"Expected N(S)={_receiveSequence}, received N(S)={decoded.SendSequence.Value}.", "The server sequence stream may have a gap, duplicate, or out-of-order I-frame.", "Check reconnect handling, duplicate clients, server buffer reset, and TCP stream integrity.");
            }
            if (decoded.ReceiveSequence.HasValue && decoded.ReceiveSequence.Value > _sendSequence)
            {
                RaiseFinding(FindingSeverity.Warning, "IEC104-NR-ACK-FUTURE", "IEC-104 N(R) acknowledges unsent I-frame", $"Local sent={_sendSequence}, peer N(R)={decoded.ReceiveSequence.Value}.", "The peer acknowledgement window is inconsistent with this client session.", "Check duplicate sessions, stale server state, or sequence reset after reconnect.");
            }
            _receiveSequence = Math.Max(_receiveSequence, (decoded.SendSequence ?? _receiveSequence) + 1);
            if (decoded.Asdu?.CauseOfTransmission == 10) _counters.GiEndResponses++;
            if (decoded.Asdu?.TypeId is 1 or 2 or 3 or 4 or 30 or 31) _counters.DpiEvents++;
            await SendRawAsync(Iec104FrameBuilder.S(_receiveSequence), "IEC-104 S-frame acknowledgement", "S-format", "Acknowledge received I-format APDU", cancellationToken).ConfigureAwait(false);
        }
        else if (decoded.Format == "S")
        {
            _counters.AckResponses++;
        }
        else if (decoded.Format == "U" && decoded.UFormatName.Contains("con", StringComparison.OrdinalIgnoreCase))
        {
            _counters.AckResponses++;
        }

        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.SlaveToMaster,
            State = _state,
            Category = decoded.IsValid ? "RX" : "RX Warning",
            DataClass = decoded.Format,
            PollingReason = reason,
            Summary = decoded.ShortMeaning,
            Detail = BuildReceiveDetail(decoded),
            OperatorMessage = BuildReceiveOperatorMessage(decoded),
            ProtocolMeaning = decoded.ShortMeaning,
            OperatorAction = BuildReceiveAction(decoded),
            RawHex = ToHex(raw),
            ResponseTimeMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds),
            ApciFormat = decoded.Format,
            SendSequence = decoded.SendSequence,
            ReceiveSequence = decoded.ReceiveSequence,
            UFormatName = decoded.UFormatName,
            TypeId = decoded.Asdu?.TypeId,
            TypeName = decoded.Asdu?.TypeName ?? string.Empty,
            VariableStructureQualifier = decoded.Asdu?.VariableStructureQualifier,
            IsSequenceAsdu = decoded.Asdu?.IsSequence,
            ObjectCount = decoded.Asdu?.ObjectCount,
            CauseOfTransmission = decoded.Asdu?.CauseOfTransmission,
            CauseName = decoded.Asdu?.CotNameWithFlags ?? string.Empty,
            OriginatorAddress = decoded.Asdu?.OriginatorAddress,
            CommonAddressNumber = decoded.Asdu?.CommonAddress,
            InformationObjectAddress = decoded.Asdu?.FirstObject?.InformationObjectAddress,
            ObjectSummary = decoded.Asdu?.ObjectSummary ?? string.Empty,
            IsRelayValue = decoded.Asdu is not null && (decoded.Asdu.TypeId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37),
            IsRelayEdgeEvent = decoded.Asdu is not null && (decoded.Asdu.CauseOfTransmission is 3 or 11 or 12) && (decoded.Asdu.TypeId is 1 or 2 or 3 or 4 or 30 or 31),
            SignalKey = decoded.Asdu?.FirstObject is null ? string.Empty : $"IOA:{decoded.Asdu.FirstObject.InformationObjectAddress}",
            SignalName = decoded.Asdu?.FirstObject is null ? string.Empty : $"IOA {decoded.Asdu.FirstObject.InformationObjectAddress}",
            SignalGroup = "IEC-104",
            SignalType = decoded.Asdu?.TypeName ?? string.Empty,
            SignalDisplayValue = decoded.Asdu?.FirstObject?.ShortValue ?? decoded.Asdu?.ValueText ?? string.Empty,
            SignalRawValue = decoded.Asdu?.FirstObject?.ElementSummary ?? decoded.Asdu?.ObjectSummary ?? string.Empty,
            QualityText = decoded.Asdu?.FirstObject?.QualityText ?? string.Empty,
            RelayTimestampText = decoded.Asdu?.FirstObject?.TimestampText ?? string.Empty,
            EdgeReason = decoded.Asdu?.CauseName ?? string.Empty
        });
        AddAdditionalObjectEvents(decoded, raw, dataClass, reason, sw.ElapsedMilliseconds);
        return decoded;
    }

    private void AddAdditionalObjectEvents(Iec104ApduDecode decoded, IReadOnlyList<byte> raw, string dataClass, string reason, long responseTimeMs)
    {
        var asdu = decoded.Asdu;
        if (asdu is null || asdu.Objects.Count <= 1)
        {
            return;
        }

        foreach (var obj in asdu.Objects.Skip(1))
        {
            AddEvent(new Iec103MasterEvidenceEvent
            {
                Direction = FrameDirection.SlaveToMaster,
                State = _state,
                Category = decoded.IsValid ? "RX Object" : "RX Warning",
                DataClass = dataClass,
                PollingReason = reason,
                Summary = $"{asdu.TypeName}, IOA={obj.InformationObjectAddress}, {obj.ShortValue}",
                Detail = obj.ReadableSummary,
                OperatorMessage = $"IEC-104 information object received: IOA {obj.InformationObjectAddress} = {obj.ShortValue}.",
                ProtocolMeaning = $"{asdu.TypeName}, COT={asdu.CotDisplay}, CA={asdu.CommonAddress}, IOA={obj.InformationObjectAddress}, {obj.ShortValue}",
                OperatorAction = BuildReceiveAction(decoded),
                RawHex = ToHex(raw),
                ResponseTimeMs = (int)Math.Min(int.MaxValue, responseTimeMs),
                ApciFormat = decoded.Format,
                SendSequence = decoded.SendSequence,
                ReceiveSequence = decoded.ReceiveSequence,
                UFormatName = decoded.UFormatName,
                TypeId = asdu.TypeId,
                TypeName = asdu.TypeName,
                VariableStructureQualifier = asdu.VariableStructureQualifier,
                IsSequenceAsdu = asdu.IsSequence,
                ObjectCount = asdu.ObjectCount,
                CauseOfTransmission = asdu.CauseOfTransmission,
                CauseName = asdu.CotNameWithFlags,
                OriginatorAddress = asdu.OriginatorAddress,
                CommonAddressNumber = asdu.CommonAddress,
                InformationObjectAddress = obj.InformationObjectAddress,
                ObjectSummary = obj.ElementSummary,
                QualityText = obj.QualityText,
                IsRelayValue = asdu.TypeId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37,
                IsRelayEdgeEvent = (asdu.CauseOfTransmission is 3 or 11 or 12) && (asdu.TypeId is 1 or 2 or 3 or 4 or 30 or 31),
                SignalKey = $"IOA:{obj.InformationObjectAddress}",
                SignalName = $"IOA {obj.InformationObjectAddress}",
                SignalGroup = "IEC-104",
                SignalType = asdu.TypeName,
                SignalDisplayValue = obj.ShortValue,
                SignalRawValue = obj.ElementSummary,
                RelayTimestampText = obj.TimestampText,
                EdgeReason = asdu.CauseName
            });
        }
    }

    private static string BuildReceiveDetail(Iec104ApduDecode decoded)
    {
        var parts = new List<string> { $"Format={decoded.Format}" };
        if (decoded.SendSequence.HasValue) parts.Add($"NS={decoded.SendSequence.Value}");
        if (decoded.ReceiveSequence.HasValue) parts.Add($"NR={decoded.ReceiveSequence.Value}");
        if (!string.IsNullOrWhiteSpace(decoded.UFormatName)) parts.Add(decoded.UFormatName);
        if (decoded.Asdu is not null)
        {
            parts.Add(decoded.Asdu.ShortMeaning);
            if (!string.IsNullOrWhiteSpace(decoded.Asdu.ObjectSummary)) parts.Add(decoded.Asdu.ObjectSummary);
        }
        if (decoded.Issues.Count > 0) parts.Add("Issues=" + string.Join("; ", decoded.Issues));
        return string.Join(", ", parts);
    }

    private static string BuildReceiveOperatorMessage(Iec104ApduDecode decoded)
    {
        if (!decoded.IsValid) return "IEC-104 APDU quality problem detected.";
        if (decoded.Format == "U") return "IEC-104 connection control received: " + decoded.UFormatName;
        if (decoded.Format == "S") return "IEC-104 acknowledgement received.";
        if (decoded.Asdu is not null) return "IEC-104 process/control data received: " + decoded.Asdu.ShortMeaning;
        return decoded.ShortMeaning;
    }

    private static string BuildReceiveAction(Iec104ApduDecode decoded)
    {
        if (!decoded.IsValid) return "Check TCP stream integrity, duplicate client sessions, and APCI framing.";
        if (decoded.Format == "I") return "Acknowledge with S-frame and continue receiving ASDUs.";
        if (decoded.Format == "U") return "Maintain STARTDT/TESTFR control-state handshake.";
        return "Continue IEC-104 session.";
    }

    private void AuditAsduForensicFindings(string prefix, Iec10xAsduDecode? asdu)
    {
        if (asdu is null)
        {
            return;
        }

        if (asdu.IsNegativeConfirm)
        {
            RaiseFinding(FindingSeverity.Warning, $"{prefix}-NEG-COT-{asdu.TypeId}-{asdu.CauseOfTransmission}", "Negative IEC-104 confirmation received", asdu.ShortMeaning, "The server rejected or negatively confirmed a requested operation.", "Check COT, CA, IOA, command qualifier, select/execute mode, and the interoperability profile.");
        }

        foreach (var issue in asdu.Issues)
        {
            RaiseFinding(FindingSeverity.Warning, $"{prefix}-ASDU-DECODE", "IEC-104 ASDU decode issue", issue, "The ASDU cannot be fully trusted as decoded evidence.", "Verify COT size, CA size, IOA size, VSQ/SQ and Type ID profile.");
        }

        foreach (var obj in asdu.Objects.Where(x => !string.IsNullOrWhiteSpace(x.QualityText) && !x.QualityText.Equals("Good", StringComparison.OrdinalIgnoreCase)))
        {
            RaiseFinding(FindingSeverity.Warning, $"{prefix}-QUALITY-{asdu.TypeId}-{obj.InformationObjectAddress}", "IEC-104 information object quality is not good", $"IOA={obj.InformationObjectAddress}, Quality={obj.QualityText}, Value={obj.ShortValue}", "The value is present but should not be treated as a healthy engineering value.", "Check server quality source, blocked/substituted status, time topicality, and invalid flags.");
        }
    }

    private void BuildPostRunFindings()
    {
        if (_settings.SendGeneralInterrogationOnConnect && _counters.GiCommands > 0 && _counters.GiEndResponses == 0)
        {
            RaiseFinding(FindingSeverity.Warning, "IEC104-GI-NO-ACTTERM", "IEC-104 GI did not reach activation termination", $"GI commands={_counters.GiCommands}, termination={_counters.GiEndResponses}.", "The server may not support GI as configured, or CA/COT/IOA sizes are mismatched.", "Verify interoperability table, common address, cause size, IOA size, and server GI policy.");
        }
        if (_counters.MalformedFrames > 0)
        {
            RaiseFinding(FindingSeverity.Error, "IEC104-APDU-QUALITY", "IEC-104 APDU quality problem detected", $"Malformed APDUs={_counters.MalformedFrames}.", "TCP framing or APCI decoding found invalid frames.", "Inspect raw hex and verify that the endpoint is IEC-104, not another TCP service.");
        }
    }

    private void SetState(Iec103MasterState state, string summary, string detail, string category = "Info", string dataClass = "-")
    {
        _state = state;
        AddEvent(new Iec103MasterEvidenceEvent { Direction = FrameDirection.Unknown, State = state, Category = category, DataClass = dataClass, Summary = summary, Detail = detail, OperatorMessage = summary, ProtocolMeaning = detail, OperatorAction = detail });
    }

    private void RaiseFinding(FindingSeverity severity, string id, string title, string evidence, string impact, string recommendation)
    {
        if (_findings.Any(x => x.Id == id && x.Evidence == evidence)) return;
        var finding = new Iec103MasterFinding { Severity = severity, Id = id, Title = title, Evidence = evidence, Impact = impact, Recommendation = recommendation };
        _findings.Add(finding);
        FindingRaised?.Invoke(this, finding);
    }

    private void DrainTransportDiagnostics(string phase)
    {
        if (_transport is not ITransportDiagnosticSource source) return;
        foreach (var d in source.DrainDiagnostics())
        {
            AddEvent(new Iec103MasterEvidenceEvent { Direction = FrameDirection.Unknown, State = _state, Category = d.Severity, PollingReason = d.Code, Summary = d.Message, Detail = d.Detail, OperatorMessage = d.Message, ProtocolMeaning = d.Detail, OperatorAction = d.Recommendation });
        }
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
            ResponseTimeMs = item.ResponseTimeMs,
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            LinkAddress = item.LinkAddress,
            ApciFormat = item.ApciFormat,
            SendSequence = item.SendSequence,
            ReceiveSequence = item.ReceiveSequence,
            UFormatName = item.UFormatName,
            TypeId = item.TypeId,
            TypeName = item.TypeName,
            VariableStructureQualifier = item.VariableStructureQualifier,
            IsSequenceAsdu = item.IsSequenceAsdu,
            ObjectCount = item.ObjectCount,
            CauseOfTransmission = item.CauseOfTransmission,
            CauseName = item.CauseName,
            OriginatorAddress = item.OriginatorAddress,
            CommonAddressNumber = item.CommonAddressNumber,
            InformationObjectAddress = item.InformationObjectAddress,
            ObjectSummary = item.ObjectSummary,
            QualityText = item.QualityText,
            IsRelayValue = item.IsRelayValue,
            SignalKey = item.SignalKey,
            IsRelayEdgeEvent = item.IsRelayEdgeEvent,
            IsMappedSignal = item.IsMappedSignal,
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
        EvidenceReceived?.Invoke(this, enriched);
    }

    private static string ToHex(IReadOnlyList<byte> bytes) => string.Join(" ", bytes.Select(x => x.ToString("X2")));
}
