// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;
using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Master.Transport;

namespace ARIEC60870.Master.Iec101.Redundancy;

public sealed class Iec101DualLinkRedundancySession : IProtocolMasterSession, IProtocolControlCommandSession
{
    private readonly Iec101DualLinkRedundancyOptions _options;
    private readonly Iec101DualLinkChannel _linkA;
    private readonly Iec101DualLinkChannel _linkB;
    private readonly Iec101ApplicationImageTracker _imageTracker = new();
    private readonly ConcurrentQueue<Iec60870ControlCommandRequest> _controlCommands = new();
    private readonly ConcurrentQueue<string> _manualFailoverRequests = new();
    private readonly List<Iec103MasterEvidenceEvent> _events = new();
    private readonly List<Iec103MasterFinding> _findings = new();
    private readonly List<Iec101FailoverJournalEntry> _failoverJournal = new();
    private readonly Iec103MasterCounters _counters = new();
    private Iec101RedundancyControllerState _controllerState = Iec101RedundancyControllerState.Created;
    private Iec101DualLinkChannel? _active;
    private Iec101DualLinkChannel? _standby;
    private DateTime _lastClass2PollUtc = DateTime.MinValue;
    private DateTime _lastStandbySupervisionUtc = DateTime.MinValue;
    private DateTime _lastFailoverUtc = DateTime.MinValue;
    private int _lastFailoverLatencyMs;
    private long _sequence;
    private bool _switchInProgress;

    public Iec101DualLinkRedundancySession(
        Iec101DualLinkRedundancyOptions options,
        IByteTransport linkATransport,
        IByteTransport linkBTransport)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _linkA = new Iec101DualLinkChannel(_options.LinkA, _options.LinkA.ApplyTo(_options.BaseSettings), linkATransport ?? throw new ArgumentNullException(nameof(linkATransport)), AddEvent, ObserveApplicationAsdu);
        _linkB = new Iec101DualLinkChannel(_options.LinkB, _options.LinkB.ApplyTo(_options.BaseSettings), linkBTransport ?? throw new ArgumentNullException(nameof(linkBTransport)), AddEvent, ObserveApplicationAsdu);
    }

    public event EventHandler<Iec103MasterEvidenceEvent>? EvidenceReceived;
    public event EventHandler<Iec103MasterFinding>? FindingRaised;
    public event EventHandler<Iec101RedundancySessionSnapshot>? SnapshotChanged;

    public bool SupportsRuntimeControlCommands => true;

    public IReadOnlyList<Iec101FailoverJournalEntry> FailoverJournal => _failoverJournal;

    public void QueueControlCommand(Iec60870ControlCommandRequest request)
    {
        if (request is not null)
        {
            _controlCommands.Enqueue(request);
        }
    }

    /// <summary>
    /// Queues an operator-requested switchover from the current active link to the current standby link.
    /// The controller still verifies that the standby link is promotable before ownership changes.
    /// </summary>
    public void QueueManualFailover(string reason)
    {
        _manualFailoverRequests.Enqueue(string.IsNullOrWhiteSpace(reason)
            ? "Operator requested manual dual-link switchover"
            : reason.Trim());
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
            await OpenBothLinksAsync(cancellationToken).ConfigureAwait(false);
            await BootstrapActiveLinkAsync(cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                await RunOneSchedulerCycleAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            completion = "Stopped by cancellation or requested duration.";
        }
        catch (Exception ex)
        {
            completion = "Fault: " + ex.Message;
            SetControllerState(Iec101RedundancyControllerState.Faulted, "IEC-101 dual-link session faulted", ex.Message, "Error");
            RaiseFinding(FindingSeverity.Error, "IEC101-DUAL-LINK-FAULT", "IEC-101 dual-link redundancy session faulted", ex.Message, "The redundancy controller could not continue safely.", "Check both serial links, link address, common address, port drivers, and outstation dual-link behavior.");
        }
        finally
        {
            SetControllerState(Iec101RedundancyControllerState.Stopping, "Stopping IEC-101 dual-link redundancy", "Closing both link transports.");
            await CloseBothLinksAsync().ConfigureAwait(false);
            SetControllerState(Iec101RedundancyControllerState.Stopped, "IEC-101 dual-link redundancy stopped", completion);
        }

        SyncAggregateCounters();
        BuildPostRunFindings();
        return new Iec103MasterRunResult
        {
            ProductMode = "IEC 60870-5-101 Dual Link Redundancy Master",
            Settings = _options.BaseSettings.CreateReportSnapshot(),
            Counters = _counters,
            Events = _events.ToArray(),
            Findings = _findings.ToArray(),
            StartedUtc = started,
            FinishedUtc = DateTime.UtcNow,
            CompletedNormally = !completion.StartsWith("Fault:", StringComparison.OrdinalIgnoreCase),
            CompletionReason = completion
        };
    }

    public Iec101RedundancySessionSnapshot CreateSnapshot()
    {
        return new Iec101RedundancySessionSnapshot
        {
            ControllerState = _controllerState,
            ActiveLinkName = _active?.Name ?? string.Empty,
            StandbyLinkName = _standby?.Name ?? string.Empty,
            ApplicationImageState = _imageTracker.State,
            ApplicationImageObjectCount = _imageTracker.ObjectCount,
            LastGiStartedUtc = _imageTracker.LastGiStartedUtc,
            LastGiCompletedUtc = _imageTracker.LastGiCompletedUtc,
            LastFailoverUtc = _lastFailoverUtc == DateTime.MinValue ? null : _lastFailoverUtc,
            LastFailoverLatencyMs = _lastFailoverLatencyMs,
            FailoverCount = _failoverJournal.Count(x => x.Completed),
            LastFailoverFromLink = _failoverJournal.LastOrDefault()?.FromLink ?? string.Empty,
            LastFailoverToLink = _failoverJournal.LastOrDefault()?.ToLink ?? string.Empty,
            LastFailoverReason = _failoverJournal.LastOrDefault()?.Reason ?? string.Empty,
            LastFailoverCompleted = _failoverJournal.LastOrDefault()?.Completed ?? false,
            LastStandbySupervisionUtc = _lastStandbySupervisionUtc == DateTime.MinValue ? null : _lastStandbySupervisionUtc,
            RecoverySummary = BuildRecoverySummary(),
            FailbackPolicy = _options.FailbackPolicy,
            LinkA = _linkA.CreateSnapshot(_options.StandbyFailureThreshold),
            LinkB = _linkB.CreateSnapshot(_options.StandbyFailureThreshold)
        };
    }

    private async Task OpenBothLinksAsync(CancellationToken cancellationToken)
    {
        SetControllerState(Iec101RedundancyControllerState.OpeningLinks, "Opening IEC-101 dual-link redundancy", $"{_options.LinkA}; {_options.LinkB}.");
        var preferB = _options.PreferredActiveLink.Equals("B", StringComparison.OrdinalIgnoreCase)
                      || _options.PreferredActiveLink.Equals(_options.LinkB.Name, StringComparison.OrdinalIgnoreCase);
        _active = preferB ? _linkB : _linkA;
        _standby = preferB ? _linkA : _linkB;

        await _active.OpenAsync(Iec101RedundancyChannelRole.Active, cancellationToken).ConfigureAwait(false);
        await _standby.OpenAsync(Iec101RedundancyChannelRole.Standby, cancellationToken).ConfigureAwait(false);
        PublishSnapshot();
    }

    private async Task BootstrapActiveLinkAsync(CancellationToken cancellationToken)
    {
        if (_active is null || _standby is null)
        {
            throw new InvalidOperationException("IEC-101 dual-link active/standby election failed.");
        }

        SetControllerState(Iec101RedundancyControllerState.ElectingActive, "IEC-101 active link elected", $"Active={_active.Name}; Standby={_standby.Name}.");

        if (_options.BaseSettings.ResetRemoteLinkOnConnect)
        {
            await _active.ResetRemoteLinkAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_options.BaseSettings.ResetFcbOnConnect)
        {
            await _active.ResetFcbAsync(cancellationToken).ConfigureAwait(false);
        }

        await _standby.RequestLinkStatusAsync("Initial standby health probe", cancellationToken).ConfigureAwait(false);
        _lastStandbySupervisionUtc = DateTime.UtcNow;

        if (_options.BaseSettings.SendClockSyncOnConnect)
        {
            await _active.SendClockSyncAsync("Dual-link startup clock synchronization on active link", cancellationToken).ConfigureAwait(false);
            _counters.ClockSyncCommands++;
        }

        if (_options.BaseSettings.SendGeneralInterrogationOnConnect)
        {
            SetControllerState(Iec101RedundancyControllerState.BootstrappingApplicationImage, "Bootstrapping IEC-101 application image", "General Interrogation is sent only on the active link.");
            await RunGeneralInterrogationLifecycleOnActiveAsync(
                "Dual-link startup station interrogation",
                "Dual-link startup GI",
                "Dual-link startup post-GI Class 2 verification sweep",
                commonAddress: null,
                cancellationToken).ConfigureAwait(false);
        }

        SetControllerState(Iec101RedundancyControllerState.Healthy, "IEC-101 dual-link redundancy running", $"Active={_active.Name}; standby={_standby.Name}; standby is supervised without Class 1/Class 2 polling.");
    }

    private async Task RunOneSchedulerCycleAsync(CancellationToken cancellationToken)
    {
        if (_active is null || _standby is null)
        {
            SetControllerState(Iec101RedundancyControllerState.NoAvailableLink, "No IEC-101 active link available", "Controller has no elected active link.", "Error");
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (await ProcessManualFailoverRequestAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (await ProcessPendingControlCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (_active.LinkState.Acd)
        {
            await DrainClass1OnActiveAsync("ACD=1 event data pending on active link", stopWhenGiEnds: false, cancellationToken).ConfigureAwait(false);
            await EvaluateActiveHealthAsync("Class 1 drain", cancellationToken).ConfigureAwait(false);
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastClass2PollUtc >= TimeSpan.FromMilliseconds(_options.BaseSettings.Class2PollIntervalMs))
        {
            _counters.Class2Requests++;
            await _active.RequestClass2Async("Dual-link active background scan", cancellationToken).ConfigureAwait(false);
            _lastClass2PollUtc = DateTime.UtcNow;
            await EvaluateActiveHealthAsync("Class 2 background scan", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (now - _lastStandbySupervisionUtc >= _options.StandbySupervisionInterval)
        {
            await SuperviseStandbyAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ProcessManualFailoverRequestAsync(CancellationToken cancellationToken)
    {
        if (!_manualFailoverRequests.TryDequeue(out var reason))
        {
            return false;
        }

        if (_active is null || _standby is null)
        {
            AddEvent(new Iec103MasterEvidenceEvent
            {
                Direction = FrameDirection.Unknown,
                State = Iec103MasterState.TimeoutRecovery,
                Category = Iec101RedundancyEventKind.ManualFailoverBlocked.ToString(),
                DataClass = "Redundancy",
                Summary = "IEC-101 manual failover blocked",
                Detail = "No active/standby pair is currently elected.",
                OperatorMessage = "Manual switchover is unavailable until both links are open and one active owner exists.",
                ProtocolMeaning = "The redundancy controller refuses manual ownership changes when it cannot prove a safe target link.",
                OperatorAction = "Connect both IEC-101 links and confirm the standby is supervised before retrying.",
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                SignalGroup = "IEC-101 Dual Link"
            });
            return true;
        }

        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.Unknown,
            State = Iec103MasterState.TimeoutRecovery,
            Category = Iec101RedundancyEventKind.ManualFailoverRequested.ToString(),
            DataClass = "Redundancy",
            Summary = "IEC-101 manual failover requested",
            Detail = $"Active={_active.Name}; standby={_standby.Name}; reason={reason}.",
            OperatorMessage = "Operator requested active/standby switchover proof.",
            ProtocolMeaning = "The controller will promote the supervised standby link only if it is still promotable.",
            OperatorAction = "Use the following failover and post-switch GI events as FAT/SAT evidence.",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            SignalGroup = "IEC-101 Dual Link"
        });

        await TryFailoverAsync(reason, cancellationToken, bypassStabilizationGuard: true).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ProcessPendingControlCommandAsync(CancellationToken cancellationToken)
    {
        if (!_controlCommands.TryDequeue(out var request))
        {
            return false;
        }

        if (_active is null)
        {
            RaiseFinding(FindingSeverity.Error, "IEC101-DUAL-COMMAND-NO-ACTIVE", "IEC-101 command blocked because no active link exists", request.Summary, "Operator command cannot be delivered safely.", "Restore at least one healthy active link before issuing controls.");
            return true;
        }

        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.Unknown,
            State = Iec103MasterState.GeneralInterrogation,
            Category = Iec101RedundancyEventKind.CommandDispatchedOnActive.ToString(),
            DataClass = $"Command · {_active.Name}/Active",
            Summary = $"IEC-101 command routed to active link {_active.Name}",
            Detail = request.Summary,
            OperatorMessage = "Command is dispatched on active link only.",
            ProtocolMeaning = "Dual-link standby is protected from command dispatch and event/background queue draining.",
            OperatorAction = "Verify ACTCON/ACTTERM and field feedback on the active link evidence timeline.",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            SignalGroup = "IEC-101 Dual Link"
        });

        if (request.Kind == Iec60870ControlCommandKind.GeneralInterrogation)
        {
            await RunGeneralInterrogationLifecycleOnActiveAsync(
                "Manual dual-link station interrogation",
                "Manual dual-link GI",
                "Manual dual-link GI post-GI Class 2 verification sweep",
                request.CommonAddress,
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        await _active.SendControlCommandAsync(request, cancellationToken).ConfigureAwait(false);
        if (_active.LinkState.Acd)
        {
            await DrainClass1OnActiveAsync("Command feedback drain on active link", stopWhenGiEnds: false, cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    private async Task DrainClass1OnActiveAsync(string reason, bool stopWhenGiEnds, CancellationToken cancellationToken)
    {
        if (_active is null)
        {
            return;
        }

        _counters.Class1DrainBursts++;
        var drained = 0;
        var stoppedByGiEnd = false;
        while (!cancellationToken.IsCancellationRequested && drained < _options.BaseSettings.MaxClass1DrainFrames)
        {
            var beforeGood = _active.LinkState.LastGoodResponseUtc;
            _counters.Class1Requests++;
            var response = await _active.RequestClass1Async(reason, cancellationToken).ConfigureAwait(false);
            drained++;
            _counters.Class1DrainFrames++;

            if (response.TimedOut || beforeGood == _active.LinkState.LastGoodResponseUtc)
            {
                break;
            }

            if (response.Asdu?.CauseOfTransmission == 10)
            {
                _counters.GiEndResponses++;
                stoppedByGiEnd = true;
                break;
            }

            if (response.IsNoData)
            {
                _counters.NoDataResponses++;
                break;
            }

            if (!stopWhenGiEnds && !_active.LinkState.Acd && response.IsUserData)
            {
                break;
            }

            if (_options.BaseSettings.Class1DrainDelayMs > 0)
            {
                await Task.Delay(_options.BaseSettings.Class1DrainDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        if (drained >= _options.BaseSettings.MaxClass1DrainFrames && (_active.LinkState.Acd || stopWhenGiEnds))
        {
            _counters.Class1DrainLimitReached++;
            RaiseFinding(
                FindingSeverity.Warning,
                stopWhenGiEnds ? "IEC101-DUAL-GI-DRAIN-LIMIT" : "IEC101-DUAL-CLASS1-DRAIN-LIMIT",
                stopWhenGiEnds ? "IEC-101 dual-link GI drain limit reached" : "IEC-101 dual-link Class 1 drain limit reached",
                $"Active={_active.Name}; frames drained={drained}; ACD={(_active.LinkState.Acd ? 1 : 0)}; GI ACTTERM observed={(stoppedByGiEnd ? "yes" : "no")}",
                stopWhenGiEnds ? "The post-switch or startup application image may be incomplete." : "The outstation may have a large event queue or stuck ACD bit.",
                "Increase the bounded drain limit for this outstation profile and inspect ACD/NO DATA/ACTTERM behavior.");
        }
    }

    private async Task SuperviseStandbyAsync(CancellationToken cancellationToken)
    {
        if (_standby is null)
        {
            return;
        }

        var standbyWasLatched = _standby.State is Iec101RedundancyChannelState.FailedLatched or Iec101RedundancyChannelState.Recovering;
        if (standbyWasLatched
            && _standby.LinkState.LastTimeoutUtc is DateTime lastTimeoutUtc
            && DateTime.UtcNow - lastTimeoutUtc < _options.RecoveryBackoff)
        {
            _lastStandbySupervisionUtc = DateTime.UtcNow;
            return;
        }

        var response = await _standby.SuperviseStandbyAsync(cancellationToken).ConfigureAwait(false);
        _lastStandbySupervisionUtc = DateTime.UtcNow;

        if (response.TimedOut && _standby.LinkState.ConsecutiveFailures >= _options.StandbyFailureThreshold)
        {
            _standby.LatchAsFailed($"{_standby.Name} failed standby supervision threshold. Active link remains {_active?.Name ?? "-"}.");
            SetControllerState(Iec101RedundancyControllerState.Degraded, "IEC-101 standby link degraded", $"{_standby.Name} failed standby supervision threshold. Active link remains {_active?.Name ?? "-"}.", Iec101RedundancyEventKind.StandbyTimeout.ToString());
            return;
        }

        if (response.Succeeded && standbyWasLatched)
        {
            if (_standby.LinkState.ConsecutiveGoodResponses < _options.StandbyRecoveryGoodResponseThreshold)
            {
                _standby.MarkRecoveryProbeSucceeded(_options.StandbyRecoveryGoodResponseThreshold);
                SetControllerState(Iec101RedundancyControllerState.Recovering, "IEC-101 standby recovery probe succeeded", $"{_standby.Name} good probes={_standby.LinkState.ConsecutiveGoodResponses}/{_options.StandbyRecoveryGoodResponseThreshold}.");
                return;
            }

            _standby.MarkRecoveredAsStandby($"{_standby.Name} has met the standby recovery threshold with {_standby.LinkState.ConsecutiveGoodResponses} consecutive good supervision responses.");
            SetControllerState(Iec101RedundancyControllerState.Healthy, "IEC-101 standby link recovered", $"Active={_active?.Name ?? "-"}; standby={_standby.Name}; failback policy={_options.FailbackPolicy}.");
            await ConsiderAutoFailbackAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_controllerState == Iec101RedundancyControllerState.Degraded && _active?.IsHealthy == true && _standby.IsPromotable(_options.StandbyFailureThreshold))
        {
            SetControllerState(Iec101RedundancyControllerState.Healthy, "IEC-101 dual-link redundancy recovered", $"Active={_active.Name}; standby={_standby.Name} is healthy again.");
        }
    }

    private async Task EvaluateActiveHealthAsync(string reason, CancellationToken cancellationToken)
    {
        if (_active is null)
        {
            return;
        }

        if (_active.LinkState.ConsecutiveFailures >= _options.ActiveFailureThreshold)
        {
            await TryFailoverAsync(reason, cancellationToken, bypassStabilizationGuard: false).ConfigureAwait(false);
        }
    }

    private async Task TryFailoverAsync(string reason, CancellationToken cancellationToken, bool bypassStabilizationGuard)
    {
        if (_switchInProgress || _active is null || _standby is null)
        {
            return;
        }

        if (!bypassStabilizationGuard && DateTime.UtcNow - _lastFailoverUtc < _options.AntiPingPongWindow)
        {
            AddEvent(new Iec103MasterEvidenceEvent
            {
                Direction = FrameDirection.Unknown,
                State = Iec103MasterState.TimeoutRecovery,
                Category = Iec101RedundancyEventKind.FailoverRejected.ToString(),
                DataClass = "Redundancy",
                Summary = "IEC-101 failover rejected by anti-ping-pong guard",
                Detail = $"Active={_active.Name}; standby={_standby.Name}; reason={reason}; last failover={_lastFailoverUtc:O}.",
                OperatorMessage = "Failover rejected because the controller is inside the stabilization window.",
                ProtocolMeaning = "This prevents unstable serial links from oscillating active ownership.",
                OperatorAction = "Inspect both link health counters and field wiring before lowering the anti-ping-pong window.",
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                SignalGroup = "IEC-101 Dual Link"
            });
            return;
        }

        _switchInProgress = true;
        var sw = Stopwatch.StartNew();
        var from = _active;
        var to = _standby;
        var fromWasFailed = from.LinkState.ConsecutiveFailures >= _options.ActiveFailureThreshold
            || from.State == Iec101RedundancyChannelState.TimeoutSuspect;
        try
        {
            SetControllerState(Iec101RedundancyControllerState.Switching, "IEC-101 failover started", $"{from.Name} → {to.Name}. Reason: {reason}.", Iec101RedundancyEventKind.FailoverStarted.ToString());
            _imageTracker.MarkStale();

            if (!to.IsPromotable(_options.StandbyFailureThreshold))
            {
                _failoverJournal.Add(new Iec101FailoverJournalEntry
                {
                    FromLink = from.Name,
                    ToLink = to.Name,
                    Reason = reason,
                    LatencyMs = 0,
                    Completed = false,
                    Detail = "Standby link is not promotable."
                });
                SetControllerState(Iec101RedundancyControllerState.NoAvailableLink, "IEC-101 failover failed", $"Standby {to.Name} is not healthy enough for promotion.", Iec101RedundancyEventKind.FailoverRejected.ToString());
                return;
            }

            to.PromoteToActive();
            from.DemoteToStandby();
            if (fromWasFailed)
            {
                from.LatchAsFailed($"{from.Name} was demoted after active-link failure. It must pass standby recovery supervision before it is considered recovered.");
            }
            _active = to;
            _standby = from;
            _lastClass2PollUtc = DateTime.MinValue;
            _lastStandbySupervisionUtc = DateTime.MinValue;

            await RunPostSwitchGiPolicyAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();
            _lastFailoverUtc = DateTime.UtcNow;
            _lastFailoverLatencyMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds);
            _failoverJournal.Add(new Iec101FailoverJournalEntry
            {
                FromLink = from.Name,
                ToLink = to.Name,
                Reason = reason,
                LatencyMs = _lastFailoverLatencyMs,
                Completed = true,
                Detail = $"Active link is now {to.Name}."
            });
            SetControllerState(
                fromWasFailed ? Iec101RedundancyControllerState.Degraded : Iec101RedundancyControllerState.Healthy,
                "IEC-101 failover completed",
                fromWasFailed
                    ? $"Active={to.Name}; old active {from.Name} is now standby under recovery supervision; latency={_lastFailoverLatencyMs} ms."
                    : $"Active={to.Name}; standby={from.Name}; latency={_lastFailoverLatencyMs} ms.",
                Iec101RedundancyEventKind.FailoverCompleted.ToString());
        }
        finally
        {
            _switchInProgress = false;
            PublishSnapshot();
        }
    }

    private async Task RunPostSwitchGiPolicyAsync(CancellationToken cancellationToken)
    {
        if (_active is null)
        {
            return;
        }

        var shouldRun = _options.PostSwitchGiPolicy switch
        {
            Iec101PostSwitchGiPolicy.Required => true,
            Iec101PostSwitchGiPolicy.OptionalIfApplicationImageFresh => !_imageTracker.IsFresh(TimeSpan.FromMinutes(5)),
            Iec101PostSwitchGiPolicy.ManualOnly => false,
            Iec101PostSwitchGiPolicy.Disabled => false,
            _ => true
        };

        if (!shouldRun)
        {
            AddApplicationImageEvent(Iec101RedundancyEventKind.ApplicationImageStale, "IEC-101 post-switch GI skipped by policy", $"Policy={_options.PostSwitchGiPolicy}; image={_imageTracker.State}.");
            return;
        }

        AddApplicationImageEvent(Iec101RedundancyEventKind.PostSwitchGiStarted, "IEC-101 post-switch GI started", $"Active={_active.Name}; policy={_options.PostSwitchGiPolicy}.");
        if (_options.DrainClass1BeforePostSwitchGi && _active.LinkState.Acd)
        {
            await DrainClass1OnActiveAsync("Pre-GI active event queue drain after failover", stopWhenGiEnds: false, cancellationToken).ConfigureAwait(false);
        }

        await RunGeneralInterrogationLifecycleOnActiveAsync(
            "Post-switch station interrogation on promoted active link",
            "Post-switch GI",
            "Post-switch GI Class 2 verification sweep",
            commonAddress: null,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RunGeneralInterrogationLifecycleOnActiveAsync(string commandReason, string lifecycleLabel, string sweepReason, int? commonAddress, CancellationToken cancellationToken)
    {
        if (_active is null)
        {
            return;
        }

        _imageTracker.MarkGiStarted(DateTime.UtcNow);
        _counters.GiCommands++;
        var response = await _active.SendGeneralInterrogationAsync(commandReason, cancellationToken, qualifier: 20, commonAddress: commonAddress).ConfigureAwait(false);
        if (IsNegativeConfirmation(response, expectedTypeId: 100))
        {
            var caText = commonAddress.HasValue ? commonAddress.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : _options.BaseSettings.CommonAddress.ToString(System.Globalization.CultureInfo.InvariantCulture);
            AddApplicationImageEvent(
                Iec101RedundancyEventKind.ApplicationImagePartial,
                "IEC-101 station GI negatively confirmed",
                $"{lifecycleLabel}: outstation negatively confirmed QOI=20 station interrogation for CA={caText}. Trying bounded group interrogation QOI=21..36 on active link.");
            RaiseFinding(
                FindingSeverity.Warning,
                "IEC101-DUAL-GI-NEGATIVE-CONFIRMATION",
                "IEC-101 dual-link station GI negatively confirmed",
                $"{lifecycleLabel}; CA={caText}; active={_active.Name}; COT={response.Asdu?.CotDisplay ?? response.Frame?.ShortMeaning ?? "-"}",
                "The outstation rejected station interrogation, so waiting for a full QOI=20 image can leave mapped points pending.",
                "Use group interrogation fallback, verify the interoperability list/QOI support, and continue Class 2/background scan for values.");
            await DrainClass1OnActiveAsync($"{lifecycleLabel} follow-up drain after negative station GI", stopWhenGiEnds: true, cancellationToken).ConfigureAwait(false);
            await RunGroupInterrogationFallbackOnActiveAsync($"{lifecycleLabel} negative station GI", commonAddress, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await DrainClass1OnActiveAsync($"{lifecycleLabel} follow-up drain", stopWhenGiEnds: true, cancellationToken).ConfigureAwait(false);
        }

        if (_options.BaseSettings.RequestClass2ImmediatelyAfterStartup)
        {
            await RunPostGiClass2VerificationSweepAsync(sweepReason, cancellationToken).ConfigureAwait(false);
        }

        PublishApplicationImageMilestone();
    }

    private async Task RunGroupInterrogationFallbackOnActiveAsync(string reason, int? commonAddress, CancellationToken cancellationToken)
    {
        if (_active is null)
        {
            return;
        }

        AddApplicationImageEvent(
            Iec101RedundancyEventKind.ApplicationImagePartial,
            "IEC-101 group interrogation fallback started",
            reason + ". Station interrogation QOI=20 was negatively confirmed; trying bounded group interrogation QOI=21..36 on the active link.");

        var acceptedGroups = 0;
        var negativeGroups = 0;
        var noResponseGroups = 0;
        const int firstGroup = 21;
        const int lastGroup = 36;

        for (var qoi = firstGroup; qoi <= lastGroup && !cancellationToken.IsCancellationRequested; qoi++)
        {
            var beforeRx = _active.LinkState.RxFrames;
            var response = await _active.SendGeneralInterrogationAsync(
                $"Group interrogation fallback QOI={qoi}",
                cancellationToken,
                qualifier: (byte)qoi,
                commonAddress: commonAddress).ConfigureAwait(false);

            if (response.TimedOut || _active.LinkState.RxFrames == beforeRx)
            {
                noResponseGroups++;
            }
            else if (IsNegativeConfirmation(response, expectedTypeId: 100))
            {
                negativeGroups++;
            }
            else
            {
                acceptedGroups++;
                await DrainClass1OnActiveAsync($"Group GI QOI={qoi} follow-up drain on active link", stopWhenGiEnds: true, cancellationToken).ConfigureAwait(false);
            }

            if (acceptedGroups > 0 && (negativeGroups + noResponseGroups) >= Math.Max(4, acceptedGroups + 2))
            {
                break;
            }

            if (_options.BaseSettings.Class1DrainDelayMs > 0)
            {
                await Task.Delay(_options.BaseSettings.Class1DrainDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        AddApplicationImageEvent(
            Iec101RedundancyEventKind.ApplicationImagePartial,
            "IEC-101 group interrogation fallback completed",
            $"Groups accepted={acceptedGroups}; negative/no-response={negativeGroups + noResponseGroups}. Continuing Class 2/background polling.");
    }

    private static bool IsNegativeConfirmation(Iec101ChannelExchangeResult? response, int expectedTypeId)
    {
        if (response is null)
        {
            return false;
        }

        if (response.Frame?.IsSingleCharacterNack == true)
        {
            return true;
        }

        return response.Asdu?.IsNegativeConfirm == true
            && (expectedTypeId <= 0 || response.Asdu.TypeId == expectedTypeId);
    }

    private async Task RunPostGiClass2VerificationSweepAsync(string reason, CancellationToken cancellationToken)
    {
        if (_active is null)
        {
            return;
        }

        AddApplicationImageEvent(
            Iec101RedundancyEventKind.ApplicationImagePartial,
            "IEC-101 dual-link post-GI Class 2 verification sweep started",
            $"{reason}. Active={_active.Name}; background values may arrive after station/group interrogation.");

        var noDataStreak = 0;
        var userDataBefore = _active.LinkState.UserDataResponses;
        var maxSweeps = Math.Clamp(_options.BaseSettings.MaxClass1DrainFrames / 2, 8, 32);
        for (var i = 0; i < maxSweeps && !cancellationToken.IsCancellationRequested; i++)
        {
            var beforeNoData = _active.LinkState.NoDataResponses;
            var beforeUserData = _active.LinkState.UserDataResponses;

            _counters.Class2Requests++;
            await _active.RequestClass2Async(reason, cancellationToken).ConfigureAwait(false);
            _lastClass2PollUtc = DateTime.UtcNow;

            if (_active.LinkState.UserDataResponses > beforeUserData)
            {
                noDataStreak = 0;
            }
            else if (_active.LinkState.NoDataResponses > beforeNoData)
            {
                noDataStreak++;
            }

            if (noDataStreak >= 2 && _active.LinkState.UserDataResponses > userDataBefore)
            {
                break;
            }

            if (_options.BaseSettings.Class1DrainDelayMs > 0)
            {
                await Task.Delay(_options.BaseSettings.Class1DrainDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void ObserveApplicationAsdu(Iec10xAsduDecode? asdu, DateTime utcNow)
    {
        _imageTracker.Observe(asdu, utcNow);
        if (asdu?.CauseOfTransmission == 10 && asdu.TypeId == 100)
        {
            AddApplicationImageEvent(Iec101RedundancyEventKind.PostSwitchGiCompleted, "IEC-101 GI activation termination observed", $"Application image={_imageTracker.State}; objects={_imageTracker.ObjectCount}.");
        }
    }

    private void PublishApplicationImageMilestone()
    {
        var kind = _imageTracker.State == Iec101ApplicationImageState.Ready
            ? Iec101RedundancyEventKind.ApplicationImageReady
            : Iec101RedundancyEventKind.ApplicationImagePartial;
        AddApplicationImageEvent(kind, $"IEC-101 application image {_imageTracker.State}", $"Objects={_imageTracker.ObjectCount}; last GI complete={_imageTracker.LastGiCompletedUtc:O}.");
    }


    private async Task ConsiderAutoFailbackAsync(CancellationToken cancellationToken)
    {
        if (_active is null || _standby is null)
        {
            return;
        }

        if (_options.FailbackPolicy != Iec101DualLinkFailbackPolicy.PreferredLinkAfterStableRecovery)
        {
            return;
        }

        if (!IsPreferredActive(_standby) || IsPreferredActive(_active))
        {
            return;
        }

        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.Unknown,
            State = Iec103MasterState.TimeoutRecovery,
            Category = Iec101RedundancyEventKind.AutoFailbackRequested.ToString(),
            DataClass = "Redundancy",
            Summary = "IEC-101 preferred-link failback requested",
            Detail = $"Preferred standby {_standby.Name} recovered while {_active.Name} is active. Failback policy={_options.FailbackPolicy}.",
            OperatorMessage = "Preferred active link recovered and automatic failback is enabled.",
            ProtocolMeaning = "The controller will attempt ownership return only after recovery threshold and anti-ping-pong rules are satisfied.",
            OperatorAction = "Use this event with the following failover evidence to prove controlled failback behavior.",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            SignalGroup = "IEC-101 Dual Link"
        });

        var before = _failoverJournal.Count;
        await TryFailoverAsync("Preferred active link recovered after stable standby supervision", cancellationToken, bypassStabilizationGuard: false).ConfigureAwait(false);
        if (_failoverJournal.Count == before)
        {
            AddEvent(new Iec103MasterEvidenceEvent
            {
                Direction = FrameDirection.Unknown,
                State = Iec103MasterState.TimeoutRecovery,
                Category = Iec101RedundancyEventKind.AutoFailbackBlocked.ToString(),
                DataClass = "Redundancy",
                Summary = "IEC-101 preferred-link failback blocked",
                Detail = "No failback journal entry was created. The anti-ping-pong guard or standby health policy prevented active ownership return.",
                OperatorMessage = "Preferred-link failback was blocked by controller safety policy.",
                ProtocolMeaning = "Automatic failback must never oscillate ownership during unstable link recovery.",
                OperatorAction = "Wait for the stabilization window or use manual switch during FAT/SAT only after validating link health.",
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                SignalGroup = "IEC-101 Dual Link"
            });
        }
    }

    private bool IsPreferredActive(Iec101DualLinkChannel channel)
        => _options.PreferredActiveLink.Equals(channel.Name, StringComparison.OrdinalIgnoreCase)
           || _options.PreferredActiveLink.Equals(channel.Endpoint.Name, StringComparison.OrdinalIgnoreCase)
           || (_options.PreferredActiveLink.Equals("A", StringComparison.OrdinalIgnoreCase) && ReferenceEquals(channel, _linkA))
           || (_options.PreferredActiveLink.Equals("B", StringComparison.OrdinalIgnoreCase) && ReferenceEquals(channel, _linkB));

    private string BuildRecoverySummary()
    {
        var standby = _standby;
        if (standby is null)
        {
            return "No standby link elected.";
        }

        if (standby.State == Iec101RedundancyChannelState.FailedLatched)
        {
            return $"{standby.Name} failed and is waiting for recovery probes.";
        }

        if (standby.State == Iec101RedundancyChannelState.Recovering)
        {
            return $"{standby.Name} recovering: {standby.LinkState.ConsecutiveGoodResponses}/{_options.StandbyRecoveryGoodResponseThreshold} good probes.";
        }

        if (standby.LinkState.LastRecoveryCompletedUtc is not null)
        {
            return $"{standby.Name} recovered at {standby.LinkState.LastRecoveryCompletedUtc:O}.";
        }

        return $"{standby.Name} supervised as standby.";
    }

    private void AddApplicationImageEvent(Iec101RedundancyEventKind kind, string summary, string detail)    {
        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.Unknown,
            State = Iec103MasterState.GeneralInterrogation,
            Category = kind.ToString(),
            DataClass = "Application Image",
            Summary = summary,
            Detail = detail,
            OperatorMessage = summary,
            ProtocolMeaning = detail,
            OperatorAction = "Use this milestone in the dual-link FAT/SAT evidence report.",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            SignalGroup = "IEC-101 Dual Link"
        });
    }

    private void SetControllerState(Iec101RedundancyControllerState state, string summary, string detail, string category = "Info")
    {
        _controllerState = state;
        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.Unknown,
            State = MapControllerState(state),
            Category = category == "Info" ? Iec101RedundancyEventKind.StateChanged.ToString() : category,
            DataClass = "Redundancy Controller",
            Summary = summary,
            Detail = detail,
            OperatorMessage = summary,
            ProtocolMeaning = detail,
            OperatorAction = detail,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            SignalGroup = "IEC-101 Dual Link"
        });
        PublishSnapshot();
    }

    private static Iec103MasterState MapControllerState(Iec101RedundancyControllerState state)
        => state switch
        {
            Iec101RedundancyControllerState.OpeningLinks => Iec103MasterState.OpeningTransport,
            Iec101RedundancyControllerState.ElectingActive => Iec103MasterState.Connected,
            Iec101RedundancyControllerState.BootstrappingApplicationImage => Iec103MasterState.GeneralInterrogation,
            Iec101RedundancyControllerState.Switching => Iec103MasterState.TimeoutRecovery,
            Iec101RedundancyControllerState.NoAvailableLink => Iec103MasterState.Faulted,
            Iec101RedundancyControllerState.Stopping => Iec103MasterState.Stopping,
            Iec101RedundancyControllerState.Stopped => Iec103MasterState.Stopped,
            Iec101RedundancyControllerState.Faulted => Iec103MasterState.Faulted,
            _ => Iec103MasterState.NormalClass2Polling
        };

    private async Task CloseBothLinksAsync()
    {
        try { await _linkA.CloseAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        try { await _linkB.CloseAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
    }

    private void SyncAggregateCounters()
    {
        _counters.TxFrames = _linkA.LinkState.TxFrames + _linkB.LinkState.TxFrames;
        _counters.RxFrames = _linkA.LinkState.RxFrames + _linkB.LinkState.RxFrames;
        _counters.Timeouts = _linkA.LinkState.ConsecutiveTimeouts + _linkB.LinkState.ConsecutiveTimeouts;
        _counters.ChecksumErrors = _linkA.LinkState.ChecksumErrors + _linkB.LinkState.ChecksumErrors;
        _counters.MalformedFrames = _linkA.LinkState.MalformedFrames + _linkB.LinkState.MalformedFrames;
        _counters.NoDataResponses = Math.Max(_counters.NoDataResponses, _linkA.LinkState.NoDataResponses + _linkB.LinkState.NoDataResponses);
        _counters.UserDataResponses = Math.Max(_counters.UserDataResponses, _linkA.LinkState.UserDataResponses + _linkB.LinkState.UserDataResponses);
    }

    private void BuildPostRunFindings()
    {
        if (_failoverJournal.Count == 0)
        {
            RaiseFinding(FindingSeverity.Info, "IEC101-DUAL-NO-FAILOVER", "IEC-101 dual-link session completed without failover", "No failover journal entries were recorded.", "This is expected when both links remain healthy during the run.", "For FAT/SAT redundancy proof, inject an active-link failure and verify failover evidence appears in the report.");
        }

        if (_options.FailbackPolicy == Iec101DualLinkFailbackPolicy.PreferredLinkAfterStableRecovery)
        {
            RaiseFinding(FindingSeverity.Info, "IEC101-DUAL-AUTO-FAILBACK", "Preferred-link auto failback is enabled", $"PreferredActiveLink={_options.PreferredActiveLink}; antiPingPong={_options.AntiPingPongWindow}.", "Automatic failback can be useful for utility procedures but must be proven against link oscillation.", "Keep ManualOnly for conservative operation unless the project FAT/SAT procedure explicitly requires automatic return to preferred link.");
        }

        if (_options.AllowStandbyClass1Polling || _options.AllowStandbyClass2Polling)
        {
            RaiseFinding(FindingSeverity.Warning, "IEC101-DUAL-STANDBY-POLLING-ENABLED", "Standby Class polling is enabled", $"Class1={_options.AllowStandbyClass1Polling}; Class2={_options.AllowStandbyClass2Polling}.", "Standby polling can drain event/background queues from the non-active path on many outstations.", "Keep standby polling disabled unless the outstation interoperability list explicitly permits it.");
        }
    }

    private void RaiseFinding(FindingSeverity severity, string id, string title, string evidence, string impact, string recommendation)
    {
        if (_findings.Any(x => x.Id == id && x.Evidence == evidence)) return;
        var finding = new Iec103MasterFinding { Severity = severity, Id = id, Title = title, Evidence = evidence, Impact = impact, Recommendation = recommendation };
        _findings.Add(finding);
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
            SignalGroup = string.IsNullOrWhiteSpace(item.SignalGroup) ? "IEC-101 Dual Link" : item.SignalGroup,
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
        var retainedLimit = Math.Max(100, _options.BaseSettings.MaxRetainedEvidenceEvents);
        if (_events.Count > retainedLimit)
        {
            _events.RemoveAt(0);
            _counters.EvidenceEventsDroppedFromMemory++;
        }
        EvidenceReceived?.Invoke(this, enriched);
    }

    private void PublishSnapshot() => SnapshotChanged?.Invoke(this, CreateSnapshot());
}
