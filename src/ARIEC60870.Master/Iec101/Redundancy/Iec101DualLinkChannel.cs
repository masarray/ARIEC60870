// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Master.Transport;

namespace ARIEC60870.Master.Iec101.Redundancy;

internal sealed class Iec101DualLinkChannel : IAsyncDisposable, IDisposable
{
    private readonly Iec103MasterSettings _settings;
    private readonly IByteTransport _transport;
    private readonly Ft12Parser _ft12;
    private readonly Ft12StreamReader _reader;
    private readonly Iec10xAsduDecoder _asduDecoder;
    private readonly Action<Iec103MasterEvidenceEvent> _publish;
    private readonly Action<Iec10xAsduDecode?, DateTime> _observeAsdu;

    public Iec101DualLinkChannel(
        Iec101DualLinkEndpoint endpoint,
        Iec103MasterSettings settings,
        IByteTransport transport,
        Action<Iec103MasterEvidenceEvent> publish,
        Action<Iec10xAsduDecode?, DateTime> observeAsdu)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _observeAsdu = observeAsdu ?? throw new ArgumentNullException(nameof(observeAsdu));
        _ft12 = new Ft12Parser(settings.LinkAddressSize);
        _reader = new Ft12StreamReader(transport, settings.LinkAddressSize);
    
        _asduDecoder = new Iec10xAsduDecoder(settings.CauseOfTransmissionSize, settings.CommonAddressSize, settings.InformationObjectAddressSize);
    }

    public Iec101DualLinkEndpoint Endpoint { get; }
    public string Name => Endpoint.Name;
    public Iec101RedundancyChannelRole Role { get; private set; }
    public Iec101RedundancyChannelState State { get; private set; } = Iec101RedundancyChannelState.Closed;
    public Iec101LinkLayerState LinkState { get; } = new();
    public bool IsOpen => _transport.IsOpen;
    public bool IsHealthy => IsOpen && LinkState.ConsecutiveFailures == 0;
    public bool IsPromotable(int failureThreshold) => IsOpen && LinkState.ConsecutiveFailures < Math.Max(1, failureThreshold);
    public bool HasRecentGoodResponse(TimeSpan window)
        => IsOpen
           && LinkState.ConsecutiveFailures == 0
           && LinkState.LastGoodResponseUtc is DateTime lastGood
           && DateTime.UtcNow - lastGood <= window;

    public bool CanRescueFailedActive(int failureThreshold, TimeSpan recentGoodWindow)
        => IsPromotable(failureThreshold)
           || HasRecentGoodResponse(recentGoodWindow)
           || (IsOpen
               && State == Iec101RedundancyChannelState.StandbySupervising
               && LinkState.ConsecutiveFailures == 0
               && LinkState.RxFrames > 0);

    public async Task OpenAsync(Iec101RedundancyChannelRole role, CancellationToken cancellationToken)
    {
        Role = role;
        State = Iec101RedundancyChannelState.Opening;
        PublishState(Iec101RedundancyEventKind.StateChanged, $"{Name} opening", $"Role={role}, endpoint={Endpoint}.");
        await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);
        State = role == Iec101RedundancyChannelRole.Active ? Iec101RedundancyChannelState.ActivePolling : Iec101RedundancyChannelState.StandbySupervising;
        PublishState(Iec101RedundancyEventKind.ChannelOpened, $"{Name} opened as {role}", Endpoint.ToString());
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        State = Iec101RedundancyChannelState.Closed;
        await _transport.CloseAsync(cancellationToken).ConfigureAwait(false);
        PublishState(Iec101RedundancyEventKind.ChannelClosed, $"{Name} closed", Endpoint.ToString());
    }

    public void PromoteToActive()
    {
        Role = Iec101RedundancyChannelRole.Active;
        State = Iec101RedundancyChannelState.Promoting;
        PublishState(Iec101RedundancyEventKind.StateChanged, $"{Name} promoted to active", "This link now owns Class 1/Class 2 polling, GI and command dispatch.");
        State = Iec101RedundancyChannelState.ActivePolling;
    }

    public void DemoteToStandby()
    {
        Role = Iec101RedundancyChannelRole.Standby;
        State = Iec101RedundancyChannelState.Demoting;
        PublishState(Iec101RedundancyEventKind.StateChanged, $"{Name} demoted to standby", "This link is limited to standby supervision and must not drain Class 1/Class 2 traffic.");
        State = Iec101RedundancyChannelState.StandbySupervising;
    }


    public void LatchAsFailed(string reason)
    {
        if (State == Iec101RedundancyChannelState.FailedLatched || IsRecoveryWindowOpen())
        {
            State = Iec101RedundancyChannelState.FailedLatched;
            return;
        }

        State = Iec101RedundancyChannelState.FailedLatched;
        LinkState.MarkRecoveryStarted(DateTime.UtcNow);
        PublishState(Iec101RedundancyEventKind.RecoveryStarted, $"{Name} recovery monitoring started", reason);
    }

    private bool IsRecoveryWindowOpen()
        => LinkState.LastRecoveryStartedUtc is not null
           && (LinkState.LastRecoveryCompletedUtc is null || LinkState.LastRecoveryCompletedUtc < LinkState.LastRecoveryStartedUtc);

    public void MarkRecoveryProbeSucceeded(int requiredGoodResponses)
    {
        State = Iec101RedundancyChannelState.Recovering;
        PublishState(
            Iec101RedundancyEventKind.RecoveryProbeSucceeded,
            $"{Name} recovery probe succeeded",
            $"Good probes={LinkState.ConsecutiveGoodResponses}/{Math.Max(1, requiredGoodResponses)}. The link remains standby until the recovery threshold is met.");
    }

    public void MarkRecoveredAsStandby(string reason)
    {
        LinkState.MarkRecoveryCompleted(DateTime.UtcNow);
        State = Iec101RedundancyChannelState.StandbySupervising;
        PublishState(Iec101RedundancyEventKind.RecoveryCompleted, $"{Name} recovered as standby", reason);
    }

    public async Task<Iec101ChannelExchangeResult> ResetRemoteLinkAsync(CancellationToken cancellationToken)
    {
        State = Iec101RedundancyChannelState.LinkResetting;
        return await SendFixedAndReceiveAsync("Reset remote link", "Link", 0, fcv: false, "Dual-link startup link reset", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Iec101ChannelExchangeResult> ResetFcbAsync(CancellationToken cancellationToken)
    {
        State = Iec101RedundancyChannelState.LinkResetting;
        var result = await SendFixedAndReceiveAsync("Reset FCB", "Link", 7, fcv: false, "Dual-link FCB synchronization", cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            LinkState.FrameCountBit = false;
        }
        return result;
    }

    public async Task<Iec101ChannelExchangeResult> RequestLinkStatusAsync(string reason, CancellationToken cancellationToken)
    {
        State = Role == Iec101RedundancyChannelRole.Standby
            ? Iec101RedundancyChannelState.StandbySupervising
            : Iec101RedundancyChannelState.LinkStatusChecking;
        LinkState.StandbySupervisionRequests++;
        PublishState(Iec101RedundancyEventKind.LinkStatusRequested, $"{Name} link status requested", reason);
        var result = await SendFixedAndReceiveAsync("Request link status", "Link", 9, fcv: false, reason, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded && Role == Iec101RedundancyChannelRole.Standby)
        {
            PublishState(Iec101RedundancyEventKind.StandbySupervisionConfirmed, $"{Name} standby supervision confirmed", $"ACD={(result.Acd ? 1 : 0)}, DFC={(result.Dfc ? 1 : 0)}, response={result.ResponseTimeMs} ms.");
        }
        return result;
    }

    public async Task<Iec101ChannelExchangeResult> SuperviseStandbyAsync(CancellationToken cancellationToken)
    {
        if (Role != Iec101RedundancyChannelRole.Standby)
        {
            throw new InvalidOperationException($"{Name} is not standby; standby supervision is not allowed for role {Role}.");
        }

        PublishState(Iec101RedundancyEventKind.StandbySupervisionSent, $"{Name} standby supervision sent", "Standby link health probe only; no Class 1/Class 2 event drain is performed.");
        return await RequestLinkStatusAsync("Standby dual-link supervision", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Iec101ChannelExchangeResult> SendGeneralInterrogationAsync(string reason, CancellationToken cancellationToken, byte qualifier = 20, int? commonAddress = null)
    {
        EnsureActiveOwner("general interrogation");
        var commandSettings = SettingsForCommonAddress(commonAddress);
        return await SendVariableAndReceiveAsync(
            qualifier == 20 ? "IEC-101 general interrogation" : $"IEC-101 group interrogation QOI={qualifier}",
            "Class 2",
            Iec10xAsduBuilder.GeneralInterrogation(commandSettings, qualifier),
            reason,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Iec101ChannelExchangeResult> SendClockSyncAsync(string reason, CancellationToken cancellationToken)
    {
        EnsureActiveOwner("clock synchronization");
        return await SendVariableAndReceiveAsync("IEC-101 clock sync", "Class 2", Iec10xAsduBuilder.ClockSynchronization(_settings, DateTime.Now), reason, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Iec101ChannelExchangeResult> RequestClass1Async(string reason, CancellationToken cancellationToken)
    {
        EnsureActiveOwner("Class 1 polling");
        LinkState.Class1Requests++;
        return await SendFixedAndReceiveAsync("Request Class 1", "Class 1", 10, fcv: true, reason, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Iec101ChannelExchangeResult> RequestClass2Async(string reason, CancellationToken cancellationToken)
    {
        EnsureActiveOwner("Class 2 polling");
        LinkState.Class2Requests++;
        return await SendFixedAndReceiveAsync("Request Class 2", "Class 2", 11, fcv: true, reason, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Iec101ChannelExchangeResult> SendControlCommandAsync(Iec60870ControlCommandRequest request, CancellationToken cancellationToken)
    {
        EnsureActiveOwner("control command");
        var asdu = request.Kind switch
        {
            Iec60870ControlCommandKind.GeneralInterrogation => Iec10xAsduBuilder.GeneralInterrogation(SettingsForCommand(request)),
            Iec60870ControlCommandKind.ClockSync => Iec10xAsduBuilder.ClockSynchronization(SettingsForCommand(request), DateTime.Now),
            Iec60870ControlCommandKind.Read => Iec10xAsduBuilder.ReadCommand(SettingsForCommand(request), request.InformationObjectAddress),
            Iec60870ControlCommandKind.SingleCommand => Iec10xAsduBuilder.SingleCommand(SettingsForCommand(request), request.InformationObjectAddress, request.Value != 0, request.SelectBeforeOperate, request.Qualifier),
            Iec60870ControlCommandKind.DoubleCommand => Iec10xAsduBuilder.DoubleCommand(SettingsForCommand(request), request.InformationObjectAddress, request.Value, request.SelectBeforeOperate, request.Qualifier),
            Iec60870ControlCommandKind.RegulatingStepCommand => Iec10xAsduBuilder.RegulatingStepCommand(SettingsForCommand(request), request.InformationObjectAddress, request.Value, request.SelectBeforeOperate, request.Qualifier),
            Iec60870ControlCommandKind.SetpointNormalizedCommand => Iec10xAsduBuilder.SetpointNormalizedCommand(SettingsForCommand(request), request.InformationObjectAddress, request.NumericValue, request.SelectBeforeOperate, request.Qualifier),
            _ => throw new InvalidOperationException("Unsupported IEC-101 control command kind: " + request.Kind)
        };

        return await SendVariableAndReceiveAsync(request.Summary, "Command", asdu, "Dual-link active command route", cancellationToken).ConfigureAwait(false);
    }

    public Iec101RedundancyChannelSnapshot CreateSnapshot(int standbyFailureThreshold)
    {
        return new Iec101RedundancyChannelSnapshot
        {
            Name = Name,
            PortName = Endpoint.PortName,
            LinkAddress = Endpoint.LinkAddress,
            Role = Role,
            State = State,
            IsOpen = IsOpen,
            IsHealthy = IsPromotable(standbyFailureThreshold),
            Acd = LinkState.Acd,
            Dfc = LinkState.Dfc,
            FrameCountBit = LinkState.FrameCountBit,
            LastGoodResponseUtc = LinkState.LastGoodResponseUtc,
            LastTimeoutUtc = LinkState.LastTimeoutUtc,
            LastRecoveryStartedUtc = LinkState.LastRecoveryStartedUtc,
            LastRecoveryCompletedUtc = LinkState.LastRecoveryCompletedUtc,
            ConsecutiveTimeouts = LinkState.ConsecutiveTimeouts,
            ConsecutiveFailures = LinkState.ConsecutiveFailures,
            ConsecutiveGoodResponses = LinkState.ConsecutiveGoodResponses,
            TxFrames = LinkState.TxFrames,
            RxFrames = LinkState.RxFrames,
            Class1Requests = LinkState.Class1Requests,
            Class2Requests = LinkState.Class2Requests,
            StandbySupervisionRequests = LinkState.StandbySupervisionRequests,
            NoDataResponses = LinkState.NoDataResponses,
            UserDataResponses = LinkState.UserDataResponses,
            ChecksumErrors = LinkState.ChecksumErrors,
            MalformedFrames = LinkState.MalformedFrames
        };
    }

    public void Dispose() => _transport.Dispose();

    public async ValueTask DisposeAsync() => await _transport.DisposeAsync().ConfigureAwait(false);

    private Iec103MasterSettings SettingsForCommand(Iec60870ControlCommandRequest request)
    {
        if (!request.CommonAddress.HasValue || request.CommonAddress.Value == _settings.CommonAddress)
        {
            return _settings;
        }

        return SettingsForCommonAddress(request.CommonAddress);
    }

    private Iec103MasterSettings SettingsForCommonAddress(int? commonAddress)
    {
        if (!commonAddress.HasValue || commonAddress.Value == _settings.CommonAddress)
        {
            return _settings;
        }

        var copy = _settings.CreateReportSnapshot();
        copy.CommonAddress = commonAddress.Value;
        return copy;
    }

    private void EnsureActiveOwner(string operation)
    {
        if (Role != Iec101RedundancyChannelRole.Active)
        {
            PublishState(Iec101RedundancyEventKind.CommandBlockedOnStandby, $"{Name} blocked {operation} on standby", "IEC-101 dual-link standby must not drain event/background queues or issue operator commands.");
            throw new InvalidOperationException($"IEC-101 dual-link {operation} is allowed only on the active link. {Name} is {Role}.");
        }
    }

    private async Task<Iec101ChannelExchangeResult> SendFixedAndReceiveAsync(string summary, string dataClass, int functionCode, bool fcv, string reason, CancellationToken cancellationToken)
    {
        var fcbBefore = LinkState.FrameCountBit;
        var control = Ft12FrameBuilder.BuildPrimaryControl(functionCode, fcv, fcv && fcbBefore);
        var frame = Ft12FrameBuilder.Fixed(control, _settings.LinkAddress, _settings.LinkAddressSize);
        await SendRawAsync(frame, summary, dataClass, reason, cancellationToken).ConfigureAwait(false);
        var response = await ReceiveOneAsync(dataClass, reason, cancellationToken).ConfigureAwait(false);
        if (response.Succeeded && fcv)
        {
            LinkState.FrameCountBit = !fcbBefore;
        }
        return response;
    }

    private async Task<Iec101ChannelExchangeResult> SendVariableAndReceiveAsync(string summary, string dataClass, byte[] asdu, string reason, CancellationToken cancellationToken)
    {
        var fcbBefore = LinkState.FrameCountBit;
        var control = Ft12FrameBuilder.BuildPrimaryControl(3, fcv: true, fcb: fcbBefore);
        var frame = Ft12FrameBuilder.Variable(control, _settings.LinkAddress, asdu, _settings.LinkAddressSize);
        await SendRawAsync(frame, summary, dataClass, reason, cancellationToken).ConfigureAwait(false);
        var response = await ReceiveOneAsync(dataClass, reason, cancellationToken).ConfigureAwait(false);
        if (response.Succeeded)
        {
            LinkState.FrameCountBit = !fcbBefore;
        }
        return response;
    }

    private async Task SendRawAsync(byte[] frame, string summary, string dataClass, string reason, CancellationToken cancellationToken)
    {
        await _transport.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        LinkState.TxFrames++;
        var decoded = _ft12.Decode(frame);
        var asdu = decoded.AsduBytes.Count > 0 ? _asduDecoder.Decode(decoded.AsduBytes) : null;
        _publish(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.MasterToSlave,
            State = Iec103MasterState.NormalClass2Polling,
            Category = "TX",
            DataClass = DecorateDataClass(dataClass),
            PollingReason = reason,
            Summary = $"{Name} TX · {summary}",
            Detail = decoded.ShortMeaning,
            OperatorMessage = summary,
            ProtocolMeaning = asdu?.ShortMeaning ?? decoded.ShortMeaning,
            OperatorAction = reason,
            RawHex = ToHex(frame),
            Frame = decoded,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
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
            SignalGroup = "IEC-101 Dual Link"
        });
    }

    private async Task<Iec101ChannelExchangeResult> ReceiveOneAsync(string dataClass, string reason, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var raw = await _reader.ReadFrameAsync(_settings.ResponseTimeoutMs, cancellationToken).ConfigureAwait(false);
        sw.Stop();
        var now = DateTime.UtcNow;
        if (raw is null || raw.Length == 0)
        {
            LinkState.MarkTimeout(now);
            State = Iec101RedundancyChannelState.TimeoutSuspect;
            _publish(new Iec103MasterEvidenceEvent
            {
                Direction = FrameDirection.Unknown,
                State = Iec103MasterState.TimeoutRecovery,
                Category = "Warning",
                DataClass = DecorateDataClass(dataClass),
                PollingReason = reason,
                Summary = $"{Name} response timeout",
                Detail = $"No outstation response within {_settings.ResponseTimeoutMs} ms after {reason}. Consecutive failures={LinkState.ConsecutiveFailures}.",
                OperatorMessage = $"{Name} timeout",
                ProtocolMeaning = "IEC-101 dual-link channel did not receive a valid FT1.2 response in the configured timeout.",
                OperatorAction = Role == Iec101RedundancyChannelRole.Active ? "Controller may fail over when the active failure threshold is reached." : "Keep active link unchanged; monitor standby recovery.",
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                LinkAddress = _settings.LinkAddress,
                SignalGroup = "IEC-101 Dual Link"
            });
            return new Iec101ChannelExchangeResult { TimedOut = true };
        }

        LinkState.RxFrames++;
        LinkState.MarkGoodResponse(now);
        var decoded = _ft12.Decode(raw);
        if (!decoded.IsChecksumValid) LinkState.ChecksumErrors++;
        if (decoded.Format == Ft12FrameFormat.Malformed) LinkState.MalformedFrames++;

        var asdu = decoded.AsduBytes.Count > 0 ? _asduDecoder.Decode(decoded.AsduBytes) : null;
        _observeAsdu(asdu, now);

        var isNoData = false;
        var isUserData = asdu is not null;
        if (decoded.LinkControl is not null && !decoded.LinkControl.Prm)
        {
            LinkState.Acd = decoded.LinkControl.Acd == true;
            LinkState.Dfc = decoded.LinkControl.Dfc == true;
            isNoData = decoded.LinkControl.FunctionCode == 9;
            isUserData = decoded.LinkControl.FunctionCode == 8 || asdu is not null;
            if (isNoData) LinkState.NoDataResponses++;
            if (isUserData) LinkState.UserDataResponses++;
        }

        _publish(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.SlaveToMaster,
            State = Iec103MasterState.NormalClass2Polling,
            Category = decoded.IsChecksumValid ? "RX" : "RX Warning",
            DataClass = DecorateDataClass(dataClass),
            PollingReason = reason,
            Summary = $"{Name} RX · {(asdu?.ShortMeaning ?? decoded.ShortMeaning)}",
            Detail = BuildReceiveDetail(decoded, asdu),
            OperatorMessage = asdu?.ShortMeaning ?? decoded.ShortMeaning,
            ProtocolMeaning = asdu?.ShortMeaning ?? decoded.ShortMeaning,
            OperatorAction = decoded.LinkControl?.Acd == true ? "Active link should drain Class 1. Standby must not drain the event queue." : "Continue configured dual-link policy.",
            RawHex = ToHex(raw),
            ResponseTimeMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds),
            Frame = decoded,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
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
            QualityText = asdu?.FirstObject?.QualityText ?? string.Empty,
            IsRelayValue = asdu is not null && (asdu.TypeId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37),
            IsRelayEdgeEvent = asdu is not null && (asdu.CauseOfTransmission is 3 or 11 or 12) && (asdu.TypeId is 1 or 2 or 3 or 4 or 30 or 31),
            SignalKey = asdu?.FirstObject is null ? string.Empty : $"{Name}:IOA:{asdu.FirstObject.InformationObjectAddress}",
            SignalName = asdu?.FirstObject is null ? string.Empty : $"{Name} IOA {asdu.FirstObject.InformationObjectAddress}",
            SignalGroup = "IEC-101 Dual Link",
            SignalType = asdu?.TypeName ?? string.Empty,
            SignalDisplayValue = asdu?.FirstObject?.ShortValue ?? asdu?.ValueText ?? string.Empty,
            SignalRawValue = asdu?.FirstObject?.ElementSummary ?? asdu?.ObjectSummary ?? string.Empty,
            RelayTimestampText = asdu?.FirstObject?.TimestampText ?? string.Empty,
            EdgeReason = asdu?.CauseName ?? string.Empty
        });
        PublishAdditionalObjectEvents(decoded, asdu, raw, dataClass, reason, sw.ElapsedMilliseconds);

        State = Role == Iec101RedundancyChannelRole.Active
            ? Iec101RedundancyChannelState.ActivePolling
            : Iec101RedundancyChannelState.StandbySupervising;

        return new Iec101ChannelExchangeResult
        {
            Succeeded = decoded.Format != Ft12FrameFormat.Malformed && decoded.IsChecksumValid,
            IsNoData = isNoData,
            IsUserData = isUserData,
            Acd = LinkState.Acd,
            Dfc = LinkState.Dfc,
            Frame = decoded,
            Asdu = asdu,
            ResponseTimeMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds)
        };
    }

    private void PublishAdditionalObjectEvents(Ft12FrameDecode decoded, Iec10xAsduDecode? asdu, IReadOnlyList<byte> raw, string dataClass, string reason, long responseTimeMs)
    {
        if (asdu is null || asdu.Objects.Count <= 1)
        {
            return;
        }

        foreach (var obj in asdu.Objects.Skip(1))
        {
            _publish(new Iec103MasterEvidenceEvent
            {
                Direction = FrameDirection.SlaveToMaster,
                State = Iec103MasterState.NormalClass2Polling,
                Category = decoded.IsChecksumValid ? "RX Object" : "RX Warning",
                DataClass = DecorateDataClass(dataClass),
                PollingReason = reason,
                Summary = $"{Name} RX object - {asdu.TypeName}, IOA={obj.InformationObjectAddress}, {obj.ShortValue}",
                Detail = obj.ReadableSummary,
                OperatorMessage = $"IEC-101 dual-link information object received on {Name}: IOA {obj.InformationObjectAddress} = {obj.ShortValue}.",
                ProtocolMeaning = $"{asdu.TypeName}, COT={asdu.CotDisplay}, CA={asdu.CommonAddress}, IOA={obj.InformationObjectAddress}, {obj.ShortValue}",
                OperatorAction = decoded.LinkControl?.Acd == true ? "Active link should drain Class 1. Standby must not drain the event queue." : "Continue configured dual-link policy.",
                RawHex = ToHex(raw),
                ResponseTimeMs = (int)Math.Min(int.MaxValue, responseTimeMs),
                Frame = decoded,
                ProtocolMode = Iec60870ProtocolMode.Iec101,
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
                SignalKey = $"{Name}:IOA:{obj.InformationObjectAddress}",
                SignalName = $"{Name} IOA {obj.InformationObjectAddress}",
                SignalGroup = "IEC-101 Dual Link",
                SignalType = asdu.TypeName,
                SignalDisplayValue = obj.ShortValue,
                SignalRawValue = obj.ElementSummary,
                RelayTimestampText = obj.TimestampText,
                EdgeReason = asdu.CauseName
            });
        }
    }

    private void PublishState(Iec101RedundancyEventKind kind, string summary, string detail)
    {
        _publish(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.Unknown,
            State = Iec103MasterState.Connected,
            Category = kind.ToString(),
            DataClass = DecorateDataClass("Redundancy"),
            PollingReason = kind.ToString(),
            Summary = summary,
            Detail = detail,
            OperatorMessage = summary,
            ProtocolMeaning = detail,
            OperatorAction = detail,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            LinkAddress = _settings.LinkAddress,
            SignalGroup = "IEC-101 Dual Link"
        });
    }

    private string DecorateDataClass(string dataClass) => $"{dataClass} · {Name}/{Role}";

    private static string BuildReceiveDetail(Ft12FrameDecode decoded, Iec10xAsduDecode? asdu)
    {
        var parts = new List<string> { decoded.ShortMeaning };
        if (decoded.LinkControl is not null)
        {
            parts.Add($"FC={decoded.LinkControl.FunctionCode}");
            parts.Add($"ACD={(decoded.LinkControl.Acd == true ? 1 : 0)}");
            parts.Add($"DFC={(decoded.LinkControl.Dfc == true ? 1 : 0)}");
        }
        if (asdu is not null)
        {
            parts.Add(asdu.ShortMeaning);
            parts.Add($"CA={asdu.CommonAddress}");
            if (asdu.FirstObject is not null) parts.Add($"IOA={asdu.FirstObject.InformationObjectAddress}");
        }
        return string.Join("; ", parts);
    }

    private static string ToHex(IReadOnlyList<byte> bytes) => string.Join(" ", bytes.Select(x => x.ToString("X2")));
}
