// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Master.Transport;

namespace ARIEC60870.Master;

public sealed class Iec101MasterSession : IProtocolMasterSession, IProtocolControlCommandSession
{
    private readonly Iec103MasterSettings _settings;
    private readonly IByteTransport _transport;
    private readonly Ft12Parser _ft12;
    private readonly Iec10xAsduDecoder _asduDecoder;
    private readonly List<Iec103MasterEvidenceEvent> _events = new();
    private readonly List<Iec103MasterFinding> _findings = new();
    private readonly Iec103MasterCounters _counters = new();
    private bool _fcb;
    private bool _acd;
    private long _sequence;
    private Iec103MasterState _state = Iec103MasterState.Created;
    private DateTime _lastClass2PollUtc = DateTime.MinValue;
    private DateTime _lastClass2ResponseUtc = DateTime.MinValue;
    private int _lastMeasuredClass2CycleMs;
    private readonly Dictionary<int, int> _observedCommonAddressHits = new();
    private int? _dominantObservedCommonAddress;
    private bool _retriedGiWithObservedCommonAddress;
    private readonly ConcurrentQueue<Iec60870ControlCommandRequest> _controlCommands = new();

    public Iec101MasterSession(Iec103MasterSettings settings, IByteTransport transport)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _ft12 = new Ft12Parser(settings.LinkAddressSize);
        _asduDecoder = new Iec10xAsduDecoder(settings.CauseOfTransmissionSize, settings.CommonAddressSize, settings.InformationObjectAddressSize);
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
            SetState(Iec103MasterState.OpeningTransport, "Opening IEC-101 serial transport", _settings.SerialSummary);
            await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);
            SetState(Iec103MasterState.Connected, "IEC-101 connected", _settings.SerialSummary);

            if (_settings.ResetRemoteLinkOnConnect)
            {
                await SendFixedAndReceiveAsync("Reset remote link", "Link", 0, false, "Startup link reset", cancellationToken).ConfigureAwait(false);
            }

            if (_settings.ResetFcbOnConnect)
            {
                await SendFixedAndReceiveAsync("Reset FCB", "Link", 7, false, "Startup FCB synchronization", cancellationToken).ConfigureAwait(false);
                _fcb = false;
            }

            if (_settings.SendClockSyncOnConnect)
            {
                await SendVariableAndReceiveAsync("IEC-101 clock sync", "Class 2", Iec10xAsduBuilder.ClockSynchronization(_settings, DateTime.Now), "Startup CP56Time2a clock synchronization", cancellationToken).ConfigureAwait(false);
            }

            if (_settings.SendGeneralInterrogationOnConnect)
            {
                _counters.GiCommands++;
                var giResponse = await SendVariableAndReceiveAsync("IEC-101 general interrogation", "Class 2", Iec10xAsduBuilder.GeneralInterrogation(_settings), "Startup station interrogation C_IC_NA_1", cancellationToken).ConfigureAwait(false);
                if (IsNegativeConfirmation(giResponse, 100))
                {
                    SetState(Iec103MasterState.NormalClass2Polling, "IEC-101 station GI negatively confirmed", "Outstation negatively confirmed QOI=20 station interrogation for configured CA. Continuing scan and enabling observed-CA retry if live traffic proves a different CA.", category: "Warning", dataClass: "Class 2");
                    await DrainClass1Async("GI follow-up / event queue drain after negative confirmation", stopWhenGiEnds: true, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await DrainClass1Async("GI follow-up / event queue drain", stopWhenGiEnds: true, cancellationToken).ConfigureAwait(false);
                }

                if (_settings.RequestClass2ImmediatelyAfterStartup)
                {
                    await RunPostGiClass2VerificationSweepAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                if (await ProcessPendingControlCommandsAsync(cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                if (await TryRetryGiUsingObservedCommonAddressAsync(cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                if (_acd)
                {
                    await DrainClass1Async("ACD=1 event data pending", stopWhenGiEnds: false, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if ((DateTime.UtcNow - _lastClass2PollUtc).TotalMilliseconds >= _settings.Class2PollIntervalMs)
                {
                    _counters.Class2Requests++;
                    await SendFixedAndReceiveAsync("Request Class 2", "Class 2", 11, true, "Normal IEC-101 background scan", cancellationToken).ConfigureAwait(false);
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
            SetState(Iec103MasterState.Faulted, "IEC-101 session faulted", ex.Message, category: "Error");
            RaiseFinding(FindingSeverity.Error, "IEC101-SESSION-FAULT", "IEC-101 session faulted", ex.Message, "The IEC-101 test session could not continue.", "Check serial mode, link address, common address, and balanced/unbalanced settings of the RTU/IED.");
        }
        finally
        {
            SetState(Iec103MasterState.Stopping, "Closing IEC-101 transport", "Closing serial connection.");
            try { await _transport.CloseAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            DrainTransportDiagnostics("Close transport");
            SetState(Iec103MasterState.Stopped, "IEC-101 stopped", completion);
        }

        BuildPostRunFindings();
        return new Iec103MasterRunResult
        {
            ProductMode = "IEC 60870-5-101 Serial Master Tester",
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



    private void ObserveAsduCommonAddress(Iec10xAsduDecode? asdu)
    {
        if (asdu is null)
        {
            return;
        }

        var ca = asdu.CommonAddress;
        if (ca <= 0)
        {
            return;
        }

        _observedCommonAddressHits.TryGetValue(ca, out var count);
        _observedCommonAddressHits[ca] = count + 1;

        var dominant = _observedCommonAddressHits
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .First();

        if (dominant.Value >= 2)
        {
            _dominantObservedCommonAddress = dominant.Key;
        }
    }

    private async Task<bool> TryRetryGiUsingObservedCommonAddressAsync(CancellationToken cancellationToken)
    {
        if (_retriedGiWithObservedCommonAddress || !_dominantObservedCommonAddress.HasValue)
        {
            return false;
        }

        var observedCa = _dominantObservedCommonAddress.Value;
        if (observedCa == _settings.CommonAddress)
        {
            return false;
        }

        _retriedGiWithObservedCommonAddress = true;
        var learnedSettings = SettingsForCommonAddress(observedCa);

        SetState(
            Iec103MasterState.GeneralInterrogation,
            "IEC-101 observed CA learned",
            $"Live ASDU traffic is using CA={observedCa}, while configured GI CA={_settings.CommonAddress}. Retrying station/group interrogation with observed CA.",
            category: "Warning",
            dataClass: "Class 2");

        _counters.GiCommands++;
        var retry = await SendVariableAndReceiveAsync(
            $"IEC-101 general interrogation using observed CA {observedCa}",
            "Class 2",
            Iec10xAsduBuilder.GeneralInterrogation(learnedSettings),
            $"Auto CA learning retry GI CA={observedCa}",
            cancellationToken).ConfigureAwait(false);

        if (IsNegativeConfirmation(retry, 100))
        {
            SetState(
                Iec103MasterState.NormalClass2Polling,
                "IEC-101 observed-CA GI negatively confirmed",
                $"Observed CA={observedCa} also negatively confirmed station GI. Trying bounded group interrogation QOI=21..36 for observed CA, then continuing Class 2/background scan.",
                category: "Warning",
                dataClass: "Class 2");
            await RunGroupInterrogationFallbackAsync($"Observed CA {observedCa} station GI negative confirmation", learnedSettings, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await DrainClass1Async($"Observed CA {observedCa} GI follow-up / event queue drain", stopWhenGiEnds: true, cancellationToken).ConfigureAwait(false);
        }

        await RunPostGiClass2VerificationSweepAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private Iec103MasterSettings SettingsForCommonAddress(int commonAddress)
    {
        if (commonAddress == _settings.CommonAddress)
        {
            return _settings;
        }

        var copy = _settings.CreateReportSnapshot();
        copy.CommonAddress = commonAddress;
        return copy;
    }

    private Iec103MasterSettings SettingsForCommand(Iec60870ControlCommandRequest request)
    {
        if (!request.CommonAddress.HasValue || request.CommonAddress.Value == _settings.CommonAddress)
        {
            return _settings;
        }

        return SettingsForCommonAddress(request.CommonAddress.Value);
    }

    private async Task<bool> ProcessPendingControlCommandsAsync(CancellationToken cancellationToken)
    {
        if (!_controlCommands.TryDequeue(out var request))
        {
            return false;
        }

        SetState(Iec103MasterState.GeneralInterrogation, "IEC-101 operator command", request.Summary, category: "Command", dataClass: "Class 2");
        var commandSettings = SettingsForCommand(request);

        switch (request.Kind)
        {
            case Iec60870ControlCommandKind.GeneralInterrogation:
                _counters.GiCommands++;
                var manualGiResponse = await SendVariableAndReceiveAsync("IEC-101 manual general interrogation", "Class 2", Iec10xAsduBuilder.GeneralInterrogation(commandSettings), "Manual command dock GI", cancellationToken).ConfigureAwait(false);
                if (IsNegativeConfirmation(manualGiResponse, 100))
                {
                    SetState(Iec103MasterState.NormalClass2Polling, "IEC-101 manual station GI negatively confirmed", "Outstation negatively confirmed QOI=20 station interrogation for requested CA. Continuing scan and enabling observed-CA retry if live traffic proves a different CA.", category: "Warning", dataClass: "Class 2");
                    await DrainClass1Async("Manual GI follow-up / event queue drain after negative confirmation", stopWhenGiEnds: true, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await DrainClass1Async("Manual GI follow-up / event queue drain", stopWhenGiEnds: true, cancellationToken).ConfigureAwait(false);
                }

                await RunPostGiClass2VerificationSweepAsync(cancellationToken).ConfigureAwait(false);
                break;
            case Iec60870ControlCommandKind.ClockSync:
                _counters.ClockSyncCommands++;
                await SendVariableAndReceiveAsync("IEC-101 manual clock sync", "Class 2", Iec10xAsduBuilder.ClockSynchronization(commandSettings, DateTime.Now), "Manual command dock clock synchronization", cancellationToken).ConfigureAwait(false);
                break;
            case Iec60870ControlCommandKind.Read:
                await SendVariableAndReceiveAsync(request.Summary, "Class 2", Iec10xAsduBuilder.ReadCommand(commandSettings, request.InformationObjectAddress), "Manual command dock read C_RD_NA_1", cancellationToken).ConfigureAwait(false);
                if (_acd) await DrainClass1Async("Read command follow-up", stopWhenGiEnds: false, cancellationToken).ConfigureAwait(false);
                break;
            case Iec60870ControlCommandKind.SingleCommand:
                await SendVariableAndReceiveAsync(request.Summary, "Command", Iec10xAsduBuilder.SingleCommand(commandSettings, request.InformationObjectAddress, request.Value != 0, request.SelectBeforeOperate, request.Qualifier), "Manual command dock C_SC_NA_1", cancellationToken).ConfigureAwait(false);
                if (_acd) await DrainClass1Async("Single command feedback drain", stopWhenGiEnds: false, cancellationToken).ConfigureAwait(false);
                break;
            case Iec60870ControlCommandKind.DoubleCommand:
                await SendVariableAndReceiveAsync(request.Summary, "Command", Iec10xAsduBuilder.DoubleCommand(commandSettings, request.InformationObjectAddress, request.Value, request.SelectBeforeOperate, request.Qualifier), "Manual command dock C_DC_NA_1", cancellationToken).ConfigureAwait(false);
                if (_acd) await DrainClass1Async("Double command feedback drain", stopWhenGiEnds: false, cancellationToken).ConfigureAwait(false);
                break;
            case Iec60870ControlCommandKind.RegulatingStepCommand:
                await SendVariableAndReceiveAsync(request.Summary, "Command", Iec10xAsduBuilder.RegulatingStepCommand(commandSettings, request.InformationObjectAddress, request.Value, request.SelectBeforeOperate, request.Qualifier), "Manual command dock C_RC_NA_1", cancellationToken).ConfigureAwait(false);
                if (_acd) await DrainClass1Async("Regulating step feedback drain", stopWhenGiEnds: false, cancellationToken).ConfigureAwait(false);
                break;
            case Iec60870ControlCommandKind.SetpointNormalizedCommand:
                await SendVariableAndReceiveAsync(request.Summary, "Command", Iec10xAsduBuilder.SetpointNormalizedCommand(commandSettings, request.InformationObjectAddress, request.NumericValue, request.SelectBeforeOperate, request.Qualifier), "Manual command dock C_SE_NA_1", cancellationToken).ConfigureAwait(false);
                if (_acd) await DrainClass1Async("Setpoint feedback drain", stopWhenGiEnds: false, cancellationToken).ConfigureAwait(false);
                break;
        }

        return true;
    }


    private bool IsNegativeConfirmation(Ft12FrameDecode? decoded, int expectedTypeId)
    {
        if (decoded is null)
        {
            return false;
        }

        if (decoded.IsSingleCharacterNack)
        {
            return true;
        }

        if (decoded.AsduBytes.Count == 0)
        {
            return false;
        }

        var asdu = _asduDecoder.Decode(decoded.AsduBytes);
        return asdu.IsNegativeConfirm && (expectedTypeId <= 0 || asdu.TypeId == expectedTypeId);
    }


    private async Task RunGroupInterrogationFallbackAsync(string reason, Iec103MasterSettings settings, CancellationToken cancellationToken)
    {
        SetState(
            Iec103MasterState.GeneralInterrogation,
            "IEC-101 group interrogation fallback started",
            reason + ". Station interrogation QOI=20 was negatively confirmed; trying bounded group interrogation QOI=21..36.",
            category: "Warning",
            dataClass: "Class 2");

        var acceptedGroups = 0;
        var negativeGroups = 0;
        var noResponseGroups = 0;
        var firstGroup = 21;
        var lastGroup = 36;

        for (var qoi = firstGroup; qoi <= lastGroup && !cancellationToken.IsCancellationRequested; qoi++)
        {
            var beforeRx = _counters.RxFrames;
            var beforeNoData = _counters.NoDataResponses;
            var response = await SendVariableAndReceiveAsync(
                $"IEC-101 group interrogation QOI={qoi}",
                "Class 2",
                Iec10xAsduBuilder.GeneralInterrogation(settings, (byte)qoi),
                $"Group interrogation fallback QOI={qoi}",
                cancellationToken).ConfigureAwait(false);

            if (response is null || _counters.RxFrames == beforeRx)
            {
                noResponseGroups++;
            }
            else if (IsNegativeConfirmation(response, 100))
            {
                negativeGroups++;
            }
            else
            {
                acceptedGroups++;
                await DrainClass1Async($"Group GI QOI={qoi} follow-up drain", stopWhenGiEnds: true, cancellationToken).ConfigureAwait(false);
            }

            // When the outstation says NO DATA after a group request and no group has been accepted
            // yet, still continue through the remaining groups. Some RTUs implement only a subset.
            // Once at least one group has been accepted, two consecutive NO DATA/negative groups are
            // enough to prevent this fallback from monopolizing a slow 1200 bps link.
            if (acceptedGroups > 0 && (negativeGroups + noResponseGroups) >= Math.Max(4, acceptedGroups + 2))
            {
                break;
            }

            if (_settings.Class1DrainDelayMs > 0)
            {
                await Task.Delay(_settings.Class1DrainDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        SetState(
            Iec103MasterState.NormalClass2Polling,
            "IEC-101 group interrogation fallback completed",
            $"Groups accepted={acceptedGroups}; negative/no-response={negativeGroups + noResponseGroups}. Continuing Class 2/background polling.",
            dataClass: "Class 2");
    }

    private async Task RunPostGiClass2VerificationSweepAsync(CancellationToken cancellationToken)
    {
        SetState(
            Iec103MasterState.NormalClass2Polling,
            "IEC-101 post-GI Class 2 verification sweep",
            "Running an adaptive Class 2/background sweep after station/group interrogation path. Class 1 empty is not a failure; monitor values may arrive through GI groups or Class 2/background polling.",
            dataClass: "Class 2");

        var noDataStreak = 0;
        var userDataBefore = _counters.UserDataResponses;
        var maxSweeps = Math.Clamp(_settings.MaxClass1DrainFrames / 2, 8, 32);
        for (var i = 0; i < maxSweeps && !cancellationToken.IsCancellationRequested; i++)
        {
            var beforeNoData = _counters.NoDataResponses;
            var beforeUserData = _counters.UserDataResponses;

            _counters.Class2Requests++;
            await SendFixedAndReceiveAsync("Request Class 2", "Class 2", 11, true, "Post-GI Class 2 verification sweep", cancellationToken).ConfigureAwait(false);
            _lastClass2PollUtc = DateTime.UtcNow;

            if (_counters.UserDataResponses > beforeUserData)
            {
                noDataStreak = 0;
            }
            else if (_counters.NoDataResponses > beforeNoData)
            {
                noDataStreak++;
            }

            if (noDataStreak >= 2 && _counters.UserDataResponses > userDataBefore)
            {
                break;
            }

            if (_settings.Class1DrainDelayMs > 0)
            {
                await Task.Delay(_settings.Class1DrainDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DrainClass1Async(string reason, bool stopWhenGiEnds, CancellationToken cancellationToken)
    {
        _counters.Class1DrainBursts++;
        SetState(
            stopWhenGiEnds ? Iec103MasterState.GiFollowUpDrain : Iec103MasterState.Class1EventDrain,
            stopWhenGiEnds ? "IEC-101 GI follow-up drain started" : "IEC-101 Class 1 drain started",
            stopWhenGiEnds
                ? reason + "; bounded drain for GI/user-data compatibility until ACTTERM, NO DATA, cancellation, or drain limit. Class 1 empty is not a failure; Class 2 sweep follows."
                : reason,
            dataClass: "Class 1");

        var drained = 0;
        var stoppedByGiEnd = false;
        var stoppedByNoData = false;
        var stoppedByAcdClearAfterEvent = false;
        var stoppedByClass2Fairness = false;

        while (!cancellationToken.IsCancellationRequested && drained < _settings.MaxClass1DrainFrames)
        {
            // Normal ACD/event drain must not starve background telemetering. On slow IEC-101
            // serial links, continuous Class 1 requests can prevent Class 2 measurements from
            // being refreshed even when the configured Class 2 interval is already due.
            if (!stopWhenGiEnds && drained > 0 && _settings.MaxConsecutiveClass1BeforeClass2 > 0)
            {
                var class2Due = (DateTime.UtcNow - _lastClass2PollUtc).TotalMilliseconds >= _settings.Class2PollIntervalMs;
                if (class2Due && drained % _settings.MaxConsecutiveClass1BeforeClass2 == 0)
                {
                    stoppedByClass2Fairness = true;
                    break;
                }
            }
            var beforeNoData = _counters.NoDataResponses;
            var beforeUserData = _counters.UserDataResponses;
            var beforeGiEnd = _counters.GiEndResponses;

            _counters.Class1Requests++;
            await SendFixedAndReceiveAsync("Request Class 1", "Class 1", 10, true, reason, cancellationToken).ConfigureAwait(false);
            drained++;
            _counters.Class1DrainFrames++;

            if (_counters.GiEndResponses > beforeGiEnd)
            {
                stoppedByGiEnd = true;
                break;
            }

            if (_counters.NoDataResponses > beforeNoData)
            {
                stoppedByNoData = true;
                break;
            }

            // Normal spontaneous/event drain may stop after a user-data response when ACD clears.
            // GI is different: many RTUs return station interrogation data in several consecutive
            // class-1 responses and may clear ACD before the final ACTTERM. Stopping GI on ACD=0
            // loses SP/DP/measurement objects and makes the value/event views look empty.
            if (!stopWhenGiEnds && !_acd && _counters.UserDataResponses > beforeUserData)
            {
                stoppedByAcdClearAfterEvent = true;
                break;
            }

            if (_settings.Class1DrainDelayMs > 0)
            {
                await Task.Delay(_settings.Class1DrainDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        if (drained >= _settings.MaxClass1DrainFrames && (_acd || stopWhenGiEnds))
        {
            _counters.Class1DrainLimitReached++;
            RaiseFinding(
                FindingSeverity.Warning,
                stopWhenGiEnds ? "IEC101-GI-DRAIN-LIMIT" : "IEC101-CLASS1-DRAIN-LIMIT",
                stopWhenGiEnds ? "IEC-101 GI drain limit reached" : "IEC-101 Class 1 drain limit reached",
                $"Frames drained={drained}; ACD={(_acd ? 1 : 0)}; GI ACTTERM observed={(_counters.GiEndResponses > 0 ? "yes" : "no")}.",
                stopWhenGiEnds ? "The GI response may be incomplete in the captured evidence." : "The outstation may have a large event queue or stuck ACD bit.",
                "Increase Max Class 1 event drain for this outstation profile and inspect ACTCON/ACTTERM, ACD and NO DATA behaviour.");
        }
        else
        {
            var stopReason = stoppedByGiEnd
                ? "ACTTERM observed"
                : stoppedByNoData
                    ? "NO DATA received"
                    : stoppedByAcdClearAfterEvent
                        ? "ACD cleared after user-data response"
                        : stoppedByClass2Fairness
                            ? "Class 2 fairness yield"
                            : "cancellation or configured duration";

            SetState(
                stopWhenGiEnds ? Iec103MasterState.GiFollowUpDrain : Iec103MasterState.Class1EventDrain,
                stopWhenGiEnds ? "IEC-101 GI follow-up drain completed" : "IEC-101 Class 1 drain completed",
                $"Frames drained={drained}; stop={stopReason}; returning to Class 2 background scan.",
                dataClass: "Class 1");
        }
    }

    private async Task SendFixedAndReceiveAsync(string summary, string dataClass, int functionCode, bool fcv, string reason, CancellationToken cancellationToken)
    {
        var fcbBefore = _fcb;
        var control = Ft12FrameBuilder.BuildPrimaryControl(functionCode, fcv, fcv && fcbBefore);
        await SendRawAsync(Ft12FrameBuilder.Fixed(control, _settings.LinkAddress, _settings.LinkAddressSize), summary, dataClass, reason, cancellationToken).ConfigureAwait(false);
        var response = await ReceiveOneAsync(dataClass, reason, cancellationToken).ConfigureAwait(false);
        if (response is not null && fcv && response.Format != Ft12FrameFormat.Malformed && response.IsChecksumValid) _fcb = !fcbBefore;
    }

    private async Task<Ft12FrameDecode?> SendVariableAndReceiveAsync(string summary, string dataClass, byte[] asdu, string reason, CancellationToken cancellationToken)
    {
        var fcbBefore = _fcb;
        var control = Ft12FrameBuilder.BuildPrimaryControl(3, true, fcbBefore);
        await SendRawAsync(Ft12FrameBuilder.Variable(control, _settings.LinkAddress, asdu, _settings.LinkAddressSize), summary, dataClass, reason, cancellationToken).ConfigureAwait(false);
        var response = await ReceiveOneAsync(dataClass, reason, cancellationToken).ConfigureAwait(false);
        if (response is not null && response.Format != Ft12FrameFormat.Malformed && response.IsChecksumValid) _fcb = !fcbBefore;
        return response;
    }

    private async Task SendRawAsync(byte[] frame, string summary, string dataClass, string reason, CancellationToken cancellationToken)
    {
        await _transport.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        _counters.TxFrames++;
        var decoded = _ft12.Decode(frame);
        var asdu = decoded.AsduBytes.Count > 0 ? _asduDecoder.Decode(decoded.AsduBytes) : null;
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
            ProtocolMeaning = asdu?.ShortMeaning ?? decoded.ShortMeaning,
            OperatorAction = reason,
            RawHex = ToHex(frame),
            Frame = decoded,
            LinkAddress = _settings.LinkAddress,
            TypeId = asdu?.TypeId,
            TypeName = asdu?.TypeName ?? string.Empty,
            VariableStructureQualifier = asdu?.VariableStructureQualifier,
            IsSequenceAsdu = asdu?.IsSequence,
            ObjectCount = asdu?.ObjectCount,
            CauseOfTransmission = asdu?.CauseOfTransmission,
            CauseName = asdu?.CotNameWithFlags ?? string.Empty,
            OriginatorAddress = asdu?.OriginatorAddress,
            CommonAddressNumber = asdu?.CommonAddress,
            InformationObjectAddress = asdu?.FirstObject?.InformationObjectAddress,
            ObjectSummary = asdu?.ObjectSummary ?? string.Empty
        });
    }

    private async Task<Ft12FrameDecode?> ReceiveOneAsync(string dataClass, string reason, CancellationToken cancellationToken)
    {
        var reader = new Ft12StreamReader(_transport, _settings.LinkAddressSize);
        var sw = Stopwatch.StartNew();
        var raw = await reader.ReadFrameAsync(_settings.ResponseTimeoutMs, cancellationToken).ConfigureAwait(false);
        sw.Stop();
        if (raw is null || raw.Length == 0)
        {
            _counters.Timeouts++;
            _counters.ConsecutiveTimeouts++;
            _counters.MaxConsecutiveTimeouts = Math.Max(_counters.MaxConsecutiveTimeouts, _counters.ConsecutiveTimeouts);
            SetState(Iec103MasterState.TimeoutRecovery, "IEC-101 response timeout", $"No outstation response within {_settings.ResponseTimeoutMs} ms after {reason}.", category: "Warning", dataClass: dataClass);
            return null;
        }

        _counters.RxFrames++;
        if (string.Equals(dataClass, "Class 2", StringComparison.OrdinalIgnoreCase))
        {
            var nowCycle = DateTime.UtcNow;
            if (_lastClass2ResponseUtc != DateTime.MinValue)
            {
                _lastMeasuredClass2CycleMs = (int)Math.Min(int.MaxValue, Math.Max(0, (nowCycle - _lastClass2ResponseUtc).TotalMilliseconds));
            }
            _lastClass2ResponseUtc = nowCycle;
        }
        _counters.ConsecutiveTimeouts = 0;
        _counters.TimedResponses++;
        _counters.TotalResponseTimeMs += sw.ElapsedMilliseconds;
        _counters.MaxResponseTimeMs = Math.Max(_counters.MaxResponseTimeMs, (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds));

        var decoded = _ft12.Decode(raw);
        if (!decoded.IsChecksumValid) _counters.ChecksumErrors++;
        if (decoded.Format == Ft12FrameFormat.Malformed) _counters.MalformedFrames++;
        if (decoded.IsSingleCharacterAck) _counters.AckResponses++;
        else if (decoded.IsSingleCharacterNack) _counters.NackResponses++;

        var asdu = decoded.AsduBytes.Count > 0 ? _asduDecoder.Decode(decoded.AsduBytes) : null;
        ObserveAsduCommonAddress(asdu);
        AuditAsduForensicFindings("IEC101", asdu);
        if (decoded.LinkControl is not null && !decoded.LinkControl.Prm)
        {
            _acd = decoded.LinkControl.Acd == true;
            if (decoded.LinkControl.Dfc == true) _counters.BusyResponses++;
            if (decoded.LinkControl.FunctionCode == 9) _counters.NoDataResponses++;
            else if (decoded.LinkControl.FunctionCode == 0) _counters.AckResponses++;
            else if (decoded.LinkControl.FunctionCode == 1) _counters.NackResponses++;
            else if (decoded.LinkControl.FunctionCode == 8 || asdu is not null) _counters.UserDataResponses++;
        }

        if (asdu?.CauseOfTransmission == 10) _counters.GiEndResponses++;
        if (asdu?.TypeId is 1 or 2 or 3 or 4 or 30 or 31) _counters.DpiEvents++;
        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.SlaveToMaster,
            State = _state,
            Category = decoded.IsChecksumValid ? "RX" : "RX Warning",
            DataClass = dataClass,
            PollingReason = reason,
            Summary = asdu?.ShortMeaning ?? decoded.ShortMeaning,
            Detail = BuildReceiveDetail(decoded, asdu),
            OperatorMessage = BuildReceiveOperatorMessage(decoded, asdu),
            ProtocolMeaning = asdu?.ShortMeaning ?? decoded.ShortMeaning,
            OperatorAction = BuildReceiveAction(decoded, asdu),
            RawHex = ToHex(raw),
            ResponseTimeMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds),
            Frame = decoded,
            LinkAddress = _settings.LinkAddress,
            TypeId = asdu?.TypeId,
            TypeName = asdu?.TypeName ?? string.Empty,
            VariableStructureQualifier = asdu?.VariableStructureQualifier,
            IsSequenceAsdu = asdu?.IsSequence,
            ObjectCount = asdu?.ObjectCount,
            CauseOfTransmission = asdu?.CauseOfTransmission,
            CauseName = asdu?.CotNameWithFlags ?? string.Empty,
            OriginatorAddress = asdu?.OriginatorAddress,
            CommonAddressNumber = asdu?.CommonAddress,
            InformationObjectAddress = asdu?.FirstObject?.InformationObjectAddress,
            ObjectSummary = asdu?.ObjectSummary ?? string.Empty,
            IsRelayValue = asdu is not null && (asdu.TypeId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37),
            IsRelayEdgeEvent = asdu is not null && (asdu.CauseOfTransmission is 3 or 11 or 12) && (asdu.TypeId is 1 or 2 or 3 or 4 or 30 or 31),
            SignalKey = asdu?.FirstObject is null ? string.Empty : $"IOA:{asdu.FirstObject.InformationObjectAddress}",
            SignalName = asdu?.FirstObject is null ? string.Empty : $"IOA {asdu.FirstObject.InformationObjectAddress}",
            SignalGroup = "IEC-101",
            SignalType = asdu?.TypeName ?? string.Empty,
            SignalDisplayValue = asdu?.FirstObject?.ShortValue ?? asdu?.ValueText ?? string.Empty,
            SignalRawValue = asdu?.FirstObject?.ElementSummary ?? asdu?.ObjectSummary ?? string.Empty,
            QualityText = asdu?.FirstObject?.QualityText ?? string.Empty,
            RelayTimestampText = asdu?.FirstObject?.TimestampText ?? string.Empty,
            EdgeReason = asdu?.CauseName ?? string.Empty
        });
        AddAdditionalObjectEvents(decoded, asdu, raw, dataClass, reason, sw.ElapsedMilliseconds);
        return decoded;
    }

    private void AddAdditionalObjectEvents(Ft12FrameDecode decoded, Iec10xAsduDecode? asdu, IReadOnlyList<byte> raw, string dataClass, string reason, long responseTimeMs)
    {
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
                Category = decoded.IsChecksumValid ? "RX Object" : "RX Warning",
                DataClass = dataClass,
                PollingReason = reason,
                Summary = $"{asdu.TypeName}, IOA={obj.InformationObjectAddress}, {obj.ShortValue}",
                Detail = obj.ReadableSummary,
                OperatorMessage = $"IEC-101 information object received: IOA {obj.InformationObjectAddress} = {obj.ShortValue}.",
                ProtocolMeaning = $"{asdu.TypeName}, COT={asdu.CotDisplay}, CA={asdu.CommonAddress}, IOA={obj.InformationObjectAddress}, {obj.ShortValue}",
                OperatorAction = BuildReceiveAction(decoded, asdu),
                RawHex = ToHex(raw),
                ResponseTimeMs = (int)Math.Min(int.MaxValue, responseTimeMs),
                Frame = decoded,
                LinkAddress = _settings.LinkAddress,
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
                SignalGroup = "IEC-101",
                SignalType = asdu.TypeName,
                SignalDisplayValue = obj.ShortValue,
                SignalRawValue = obj.ElementSummary,
                RelayTimestampText = obj.TimestampText,
                EdgeReason = asdu.CauseName
            });
        }
    }

    private string DecodeTxAsduMeaning(Ft12FrameDecode decoded)
    {
        return decoded.AsduBytes.Count == 0 ? decoded.ShortMeaning : _asduDecoder.Decode(decoded.AsduBytes).ShortMeaning;
    }

    private static string BuildReceiveDetail(Ft12FrameDecode decoded, Iec10xAsduDecode? asdu)
    {
        var parts = new List<string>();
        if (decoded.LinkControl is not null)
        {
            parts.Add($"FC={decoded.LinkControl.FunctionCode}");
            parts.Add($"ACD={(decoded.LinkControl.Acd == true ? 1 : 0)}");
            parts.Add($"DFC={(decoded.LinkControl.Dfc == true ? 1 : 0)}");
        }
        if (asdu is not null)
        {
            parts.Add(asdu.ShortMeaning);
            if (!string.IsNullOrWhiteSpace(asdu.ObjectSummary)) parts.Add(asdu.ObjectSummary);
        }
        if (decoded.Issues.Count > 0) parts.Add("Issues=" + string.Join("; ", decoded.Issues));
        if (asdu?.Issues.Count > 0) parts.Add("ASDU issues=" + string.Join("; ", asdu.Issues));
        return string.Join(", ", parts);
    }

    private static string BuildReceiveOperatorMessage(Ft12FrameDecode decoded, Iec10xAsduDecode? asdu)
    {
        if (decoded.Format == Ft12FrameFormat.Malformed || !decoded.IsChecksumValid) return "IEC-101 frame quality problem detected.";
        if (decoded.IsSingleCharacterAck) return "IEC-101 outstation sent single-character ACK (E5).";
        if (decoded.IsSingleCharacterNack) return "IEC-101 outstation sent single-character NACK (A2).";
        if (asdu is not null) return "IEC-101 outstation data received: " + asdu.ShortMeaning;
        if (decoded.LinkControl?.FunctionCode == 9) return decoded.LinkControl.Acd == true ? "No requested data, but ACD=1 indicates pending Class 1 data." : "No requested data available.";
        return decoded.ShortMeaning;
    }

    private static string BuildReceiveAction(Ft12FrameDecode decoded, Iec10xAsduDecode? asdu)
    {
        if (decoded.Format == Ft12FrameFormat.Malformed || !decoded.IsChecksumValid) return "Check serial settings, converter direction control, line quality, and address configuration.";
        if (asdu is not null && asdu.TypeId == 100 && asdu.CauseOfTransmission == 10) return "General interrogation completed; continue normal background scan.";
        if (decoded.IsSingleCharacterNack) return "Treat as a negative link-layer response; inspect polling class, link address, FCB/FCV and outstation busy/no-data policy.";
        if (decoded.LinkControl?.Acd == true) return "Drain Class 1 in a bounded loop; do not storm the link blindly.";
        return "Continue configured IEC-101 polling policy.";
    }

    private void AuditAsduForensicFindings(string prefix, Iec10xAsduDecode? asdu)
    {
        if (asdu is null)
        {
            return;
        }

        if (asdu.IsNegativeConfirm)
        {
            RaiseFinding(FindingSeverity.Warning, $"{prefix}-NEG-COT-{asdu.TypeId}-{asdu.CauseOfTransmission}", "Negative IEC-101 confirmation received", asdu.ShortMeaning, "The outstation rejected or negatively confirmed a requested operation.", "Check COT, CA, IOA, command qualifier, select/execute mode, and the interoperability profile.");
        }

        foreach (var issue in asdu.Issues)
        {
            RaiseFinding(FindingSeverity.Warning, $"{prefix}-ASDU-DECODE", "IEC-101 ASDU decode issue", issue, "The ASDU cannot be fully trusted as decoded evidence.", "Verify COT size, CA size, IOA size, VSQ/SQ and Type ID profile.");
        }

        foreach (var obj in asdu.Objects.Where(x => !string.IsNullOrWhiteSpace(x.QualityText) && !x.QualityText.Equals("Good", StringComparison.OrdinalIgnoreCase)))
        {
            RaiseFinding(FindingSeverity.Warning, $"{prefix}-QUALITY-{asdu.TypeId}-{obj.InformationObjectAddress}", "IEC-101 information object quality is not good", $"IOA={obj.InformationObjectAddress}, Quality={obj.QualityText}, Value={obj.ShortValue}", "The value is present but should not be treated as a healthy engineering value.", "Check RTU quality source, blocked/substituted status, time topicality, and invalid flags.");
        }
    }

    private void BuildPostRunFindings()
    {
        if (_settings.SendGeneralInterrogationOnConnect && _counters.GiCommands > 0 && _counters.GiEndResponses == 0)
        {
            RaiseFinding(FindingSeverity.Warning, "IEC101-GI-NO-ACTTERM", "IEC-101 GI did not reach activation termination", $"GI commands={_counters.GiCommands}, termination={_counters.GiEndResponses}.", "Some RTUs return GI data without a clean activation termination, or the address/profile is mismatched.", "Verify COT/CA/IOA size, common address, link address, and outstation interoperability table.");
        }
        if (_counters.ChecksumErrors > 0 || _counters.MalformedFrames > 0)
        {
            RaiseFinding(FindingSeverity.Error, "IEC101-FRAME-QUALITY", "IEC-101 frame quality problem detected", $"Checksum={_counters.ChecksumErrors}, malformed={_counters.MalformedFrames}.", "Serial quality or configuration mismatch may corrupt FT1.2 frames.", "Check baud/parity, RS485 polarity, termination, grounding, and link address.");
        }

        var estimatedMinimumCycle = EstimatePracticalClass2CycleMs();
        if (_settings.ProtocolMode == Iec60870ProtocolMode.Iec101 && _settings.Class2PollIntervalMs < estimatedMinimumCycle)
        {
            RaiseFinding(
                FindingSeverity.Info,
                "IEC101-CLASS2-INTERVAL-UNREALISTIC",
                "Configured Class 2 interval is below practical serial throughput",
                $"Configured={_settings.Class2PollIntervalMs} ms; estimated practical minimum≈{estimatedMinimumCycle} ms at {_settings.BaudRate} bps, link={_settings.LinkAddressSize} octet, CA={_settings.CommonAddressSize}, COT={_settings.CauseOfTransmissionSize}, IOA={_settings.InformationObjectAddressSize}.",
                "Measurement values may update slower than the configured interval because the physical IEC-101 serial link cannot carry request/response traffic that fast.",
                "Use the measured effective cycle as the acceptance reference, or increase baudrate / reduce background scan payload / use IEC-104 for fast polling tests.");
        }

        if (_lastMeasuredClass2CycleMs > 0 && _lastMeasuredClass2CycleMs > Math.Max(_settings.Class2PollIntervalMs * 2, estimatedMinimumCycle * 2))
        {
            RaiseFinding(
                FindingSeverity.Warning,
                "IEC101-CLASS2-EFFECTIVE-CYCLE-SLOW",
                "Measured Class 2 refresh cycle is slower than expected",
                $"Configured={_settings.Class2PollIntervalMs} ms; last measured Class 2 response cycle={_lastMeasuredClass2CycleMs} ms; estimated minimum≈{estimatedMinimumCycle} ms.",
                "Telemetering/background scan may look stale even while the link is responsive. This often indicates low baudrate, large ASDU payload, Class 1/event drain pressure, or outstation scan throttling.",
                "Inspect ACD/Class 1 pressure, response sizes, baudrate, and the outstation interoperability profile. The v1.7.2 scheduler yields Class 1 drain to Class 2 when due to reduce starvation.");
        }
    }

    private int EstimatePracticalClass2CycleMs()
    {
        var bitsPerByte = 1 + _settings.DataBits + (_settings.Parity == System.IO.Ports.Parity.None ? 0 : 1) + (_settings.StopBits == System.IO.Ports.StopBits.Two ? 2 : 1);
        var requestBytes = 4 + Math.Max(0, _settings.LinkAddressSize);
        var typicalResponseBytes = 16 + Math.Max(0, _settings.LinkAddressSize) + _settings.CommonAddressSize + _settings.CauseOfTransmissionSize + _settings.InformationObjectAddressSize + 12;
        var totalBytes = requestBytes + typicalResponseBytes;
        var baud = Math.Max(300, _settings.BaudRate);
        var wireMs = (int)Math.Ceiling(totalBytes * bitsPerByte * 1000.0 / baud);
        var turnaroundMs = baud <= 1200 ? 220 : baud <= 2400 ? 140 : 70;
        return Math.Max(50, wireMs + turnaroundMs + _settings.Class1DrainDelayMs);
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
            Frame = item.Frame,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
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
