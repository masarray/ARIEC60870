// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Protocol;
using System.Diagnostics;
using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Master.Transport;

namespace ARIEC60870.Master.Iec101.Redundancy;

public sealed class Iec101DualLinkRedundancySession : IProtocolMasterSession, IProtocolCommandAdmissionSession
{
    private readonly Iec101DualLinkRedundancyOptions _options;
    private readonly Iec101DualLinkChannel _linkA;
    private readonly Iec101DualLinkChannel _linkB;
    private readonly Iec101ApplicationImageTracker _imageTracker = new();
    private readonly Iec101BoundedFifo<QueuedControl> _controlCommands = new(64);
    private readonly record struct QueuedControl(Iec60870ControlCommandRequest Request,
        Iec101OwnerStamp OwnerAtQueue, bool VerifiedWhenQueued, DateTime QueuedUtc);
    private readonly Iec101BoundedFifo<string> _manualFailoverRequests = new(8);
    private static bool IsMutatingCommand(Iec60870ControlCommandRequest request)
        => request.Kind is not (Iec60870ControlCommandKind.Read or Iec60870ControlCommandKind.GeneralInterrogation);
    private readonly Iec101ObservedCaPolicy _observedCa = new();
    private readonly Iec101BoundedEvidenceHistory _events;
    private readonly List<Iec103MasterFinding> _findings = new();
    private readonly object _findingGate = new();
    private readonly List<Iec101FailoverJournalEntry> _failoverJournal = new();
    private readonly Iec103MasterCounters _counters = new();
    private Iec101RedundancyControllerState _controllerState = Iec101RedundancyControllerState.Created;
    private Iec101DualLinkChannel? _active;
    private Iec101DualLinkChannel? _standby;
    private int? _learnedCommonAddress;
    private DateTime _lastClass2PollUtc = DateTime.MinValue;
    private DateTime _lastStandbySupervisionUtc = DateTime.MinValue;
    private DateTime _lastFailoverUtc = DateTime.MinValue;
    private int _lastFailoverLatencyMs;
    // Physical COM recovery is independent of RTU DFC/timeout recovery.
    private readonly Dictionary<Iec101DualLinkChannel, int> _physicalPortFailureCount = new();
    private readonly Dictionary<Iec101DualLinkChannel, DateTime> _physicalPortNextAttemptUtc = new();
    private long _sequence;
    private bool _switchInProgress;
    private bool _startupGiPending;
    private bool _retriedGiWithObservedCommonAddress;
    private int _activeGeneration;
    // Per-active-owner authorization, never inferred from passive RX or old owner GI.
    private bool _giRequestedOnActiveOwner;

    public Iec101DualLinkRedundancySession(
        Iec101DualLinkRedundancyOptions options,
        IByteTransport linkATransport,
        IByteTransport linkBTransport)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _events = new Iec101BoundedEvidenceHistory(_options.BaseSettings.MaxRetainedEvidenceEvents);
        _linkA = new Iec101DualLinkChannel(_options.LinkA, _options.LinkA.ApplyTo(_options.BaseSettings), linkATransport ?? throw new ArgumentNullException(nameof(linkATransport)), AddEvent, ObserveApplicationAsdu);
        _linkB = new Iec101DualLinkChannel(_options.LinkB, _options.LinkB.ApplyTo(_options.BaseSettings), linkBTransport ?? throw new ArgumentNullException(nameof(linkBTransport)), AddEvent, ObserveApplicationAsdu);
    }

    public event EventHandler<Iec103MasterEvidenceEvent>? EvidenceReceived;
    public event EventHandler<Iec103MasterFinding>? FindingRaised;
    public event EventHandler<Iec101RedundancySessionSnapshot>? SnapshotChanged;

    public bool SupportsRuntimeControlCommands => true;

    public IReadOnlyList<Iec101FailoverJournalEntry> FailoverJournal => _failoverJournal;

    public void QueueControlCommand(Iec60870ControlCommandRequest request)
        => TryQueueControlCommand(request);

    public bool TryQueueControlCommand(Iec60870ControlCommandRequest request)
    {
        if (request is null) return false;
        var active = Volatile.Read(ref _active);
        var beforeElection = Volatile.Read(ref _activeGeneration);
        var beforeEpoch = active?.CommandAuthorityEpoch ?? 0;
        var validLink = active is { IsHealthy: true, HasVerifiedCommandAuthority: true };
        var afterEpoch = active?.CommandAuthorityEpoch ?? 0;
        var afterElection = Volatile.Read(ref _activeGeneration);
        var readyAtAdmission = validLink && !_switchInProgress &&
            ReferenceEquals(active, Volatile.Read(ref _active)) &&
            beforeElection == afterElection && beforeEpoch == afterEpoch;
        var stamp = active is null ? default :
            new Iec101OwnerStamp(active.Name, afterEpoch, afterElection);
        if (IsMutatingCommand(request) && !readyAtAdmission)
        {
            RaiseFinding(FindingSeverity.Warning, "IEC101-DUAL-COMMAND-ACTIVE-UNVERIFIED",
                "IEC-101 A/B physical control rejected before queue admission",
                request.Summary,
                "The active secondary link is not freshly verified, or an A/B switch is in progress.",
                "Verify the active physical link, then issue a deliberate new control.");
            return false;
        }
        if (!_controlCommands.TryEnqueue(new QueuedControl(request, stamp,
                readyAtAdmission, DateTime.UtcNow)))
        {
            RaiseFinding(FindingSeverity.Warning, "IEC101-DUAL-COMMAND-QUEUE-FULL",
                "IEC-101 A/B operator command rejected: bounded queue full",
                "Capacity=64; new intent was NOT admitted: " + request.Summary,
                "Old commands are retained in arrival order without replacement.",
                "Reissue the control after the active-link queue drains.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Queues an operator-requested switchover from the current active link to the current standby link.
    /// The controller still verifies that the standby link is promotable before ownership changes.
    /// </summary>
    public void QueueManualFailover(string reason)
    {
        if (!_manualFailoverRequests.TryEnqueue(string.IsNullOrWhiteSpace(reason)
                ? "Operator requested manual dual-link switchover"
                : reason.Trim()))
        {
            RaiseFinding(FindingSeverity.Warning, "IEC101-DUAL-FAILOVER-QUEUE-FULL",
                "IEC-101 manual failover request rejected: bounded queue full",
                "Capacity=8; additional switch was NOT admitted.",
                "A pending switchover cannot create an unbounded replay backlog.",
                "Retry after the current failover operation completes.");
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
            Interlocked.Increment(ref _activeGeneration);
            _observedCa.Reset();
            _retriedGiWithObservedCommonAddress = false;
            _learnedCommonAddress = null;
            _giRequestedOnActiveOwner = false;
            await OpenBothLinksAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await BootstrapActiveLinkAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Iec101PhysicalLinkFault fault) when (!cancellationToken.IsCancellationRequested)
            {
                await HandlePhysicalPortFaultAsync(fault).ConfigureAwait(false);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RunOneSchedulerCycleAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Iec101PhysicalLinkFault fault) when (!cancellationToken.IsCancellationRequested)
                {
                    await HandlePhysicalPortFaultAsync(fault).ConfigureAwait(false);
                }
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
            Interlocked.Increment(ref _activeGeneration);
            var droppedControls = _controlCommands.Clear();
            var droppedSwitches = _manualFailoverRequests.Clear();
            if (droppedControls + droppedSwitches > 0)
                RaiseFinding(FindingSeverity.Warning, "IEC101-DUAL-QUEUES-DISCARDED-ON-STOP",
                    "IEC-101 A/B pending operator intents discarded on stop",
                    $"Unsent controls={droppedControls}; pending manual failovers={droppedSwitches}.",
                    "A stopped session cannot replay physical commands or failover requests.",
                    "Reissue operator intents after validating the active physical link.");
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
            Events = _events.Snapshot(),
            Findings = SnapshotFindings(),
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

        try
        {
            await _active.OpenAsync(Iec101RedundancyChannelRole.Active, cancellationToken).ConfigureAwait(false);
        }
        catch (Iec101PhysicalLinkFault fault) when (!cancellationToken.IsCancellationRequested)
        {
            await HandlePhysicalPortFaultAsync(fault).ConfigureAwait(false);
        }
        try
        {
            await _standby.OpenAsync(Iec101RedundancyChannelRole.Standby, cancellationToken).ConfigureAwait(false);
        }
        catch (Iec101PhysicalLinkFault fault) when (!cancellationToken.IsCancellationRequested)
        {
            await HandlePhysicalPortFaultAsync(fault).ConfigureAwait(false);
        }
        PublishSnapshot();
    }

    // An I/O fault does not prove non-delivery of a preceding FC3 command.
    // Quarantine only the affected physical link; never move old controls to B.
    private async Task HandlePhysicalPortFaultAsync(Iec101PhysicalLinkFault fault)
    {
        var link = string.Equals(fault.PhysicalSource, _linkA.Name, StringComparison.Ordinal)
            ? _linkA : string.Equals(fault.PhysicalSource, _linkB.Name, StringComparison.Ordinal)
                ? _linkB : throw new InvalidOperationException("Unknown physical A/B fault source.");
        var wasActive = ReferenceEquals(link, _active);
        var discardedControls = 0;
        if (wasActive)
        {
            Interlocked.Increment(ref _activeGeneration);
            discardedControls = _controlCommands.Clear();
            _observedCa.Reset();
            _learnedCommonAddress = null;
            _retriedGiWithObservedCommonAddress = false;
            _giRequestedOnActiveOwner = false;
            // A GI already attempted was removed from the pending queue before
            // TX. If no GI write was attempted yet, keep first-Connect intent
            // through physical recovery without replaying any ambiguous FC3.
            _lastClass2PollUtc = DateTime.MinValue;
        }

        try
        {
            await link.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Reopen must obtain its own fresh OS ownership and RTU proof.
        }
        link.LatchAsFailed("Physical COM I/O fault; old command and response delivery unknown.");
        var attempt = _physicalPortFailureCount.TryGetValue(link, out var old) ? old + 1 : 1;
        _physicalPortFailureCount[link] = attempt;
        var retryMs = Math.Max(
            Iec101PhysicalPortRecovery.BackoffMs(_options.BaseSettings.TimeoutRecoveryBackoffMs, attempt),
            (int)Math.Clamp(_options.RecoveryBackoff.TotalMilliseconds, 100, 5000));
        _physicalPortNextAttemptUtc[link] = DateTime.UtcNow.AddMilliseconds(retryMs);

        if (Iec101PhysicalPortRecovery.ShouldReport(attempt) || discardedControls > 0)
            RaiseFinding(FindingSeverity.Warning, "IEC101-DUAL-COM-PHYSICAL-RECOVERY",
                $"IEC-101 {link.Name} physical COM fault — bounded independent reopen",
                $"COM={link.Endpoint.PortName}; role={link.Role}; attempt={attempt}; " +
                $"retry={retryMs}ms; old-active commands discarded={discardedControls}; " +
                $"driver error={fault.InnerException?.Message ?? fault.Message}.",
                "Neither COM-open nor an unverified prior TX establishes RTU link health or control delivery.",
                "Check the affected adapter. No automatic RTS/DTR change, FC0/FC7, GI replay or cross-link control transfer.");
        SetControllerState(Iec101RedundancyControllerState.Degraded,
            $"IEC-101 {link.Name} COM disconnected — other physical channel retained",
            $"Faulted={link.Name}; active={_active?.Name ?? "-"}; standby={_standby?.Name ?? "-"}; " +
            $"next COM reopen in {retryMs}ms; prior TX delivery UNKNOWN.",
            "Warning");
    }

    private async Task ReopenPhysicalPortsIfDueAsync(CancellationToken cancellationToken)
    {
        foreach (var link in new[] { _linkA, _linkB })
        {
            if (link.IsOpen) continue;
            if (_physicalPortNextAttemptUtc.TryGetValue(link, out var due) &&
                DateTime.UtcNow < due) continue;

            try
            {
                await link.OpenAsync(link.Role, cancellationToken).ConfigureAwait(false);
                _physicalPortFailureCount.Remove(link);
                _physicalPortNextAttemptUtc.Remove(link);
                if (_options.BaseSettings.StartupDelayMs > 0)
                    await Task.Delay(_options.BaseSettings.StartupDelayMs, cancellationToken).ConfigureAwait(false);
                SetControllerState(Iec101RedundancyControllerState.Recovering,
                    $"IEC-101 {link.Name} COM reopened — RTU secondary unverified",
                    "Fresh reader and FCB epoch. Only addressed FC9 can prove this link before data, GI, or commands.");
            }
            catch (Iec101PhysicalLinkFault fault) when (!cancellationToken.IsCancellationRequested)
            {
                await HandlePhysicalPortFaultAsync(fault).ConfigureAwait(false);
            }
        }
    }

    private async Task BootstrapActiveLinkAsync(CancellationToken cancellationToken)
    {
        if (_active is null || _standby is null)
        {
            throw new InvalidOperationException("IEC-101 dual-link active/standby election failed.");
        }

        SetControllerState(Iec101RedundancyControllerState.ElectingActive, "IEC-101 active link elected", $"Active={_active.Name}; Standby={_standby.Name}.");
        // Arm operator-authorized first-Connect GI even when COM A is initially
        // unavailable. Consume before the first TX attempt; never replay it
        // after an ambiguous send, failover or COM epoch.
        _startupGiPending = _options.BaseSettings.SendGeneralInterrogationOnConnect;

        if (!_active.IsOpen)
        {
            // Preserve only the not-yet-attempted first-Connect GI; a later
            // healthy active owner must first pass addressed FC9 proof.
            if (_standby.IsOpen)
            {
                await _standby.RequestLinkStatusAsync("Initial standby proof with active COM unavailable", cancellationToken).ConfigureAwait(false);
                _lastStandbySupervisionUtc = DateTime.UtcNow;
            }
            SetControllerState(Iec101RedundancyControllerState.Degraded,
                "IEC-101 elected active COM unavailable — independently recovering",
                "Standby link will keep FC9 supervision; no application TX until a verified active is elected.",
                "Warning");
            return;
        }

        if (_options.BaseSettings.ResetRemoteLinkOnConnect)
        {
            await _active.ResetRemoteLinkAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_options.BaseSettings.ResetFcbOnConnect)
        {
            await _active.ResetFcbAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_standby.IsOpen)
        {
            await _standby.RequestLinkStatusAsync("Initial standby health probe", cancellationToken).ConfigureAwait(false);
            _lastStandbySupervisionUtc = DateTime.UtcNow;
        }
        // COM-open is not a validated RTU link on either channel.
        if (!_active.IsHealthy)
            await _active.RequestLinkStatusAsync("Initial active IEC-101 secondary proof", cancellationToken).ConfigureAwait(false);

        if (_options.BaseSettings.SendClockSyncOnConnect && _active.IsHealthy)
        {
            await _active.SendClockSyncAsync("Dual-link startup clock synchronization on active link", cancellationToken).ConfigureAwait(false);
            _counters.ClockSyncCommands++;
        }

        if (_startupGiPending && _active.IsHealthy)
        {
            SetControllerState(Iec101RedundancyControllerState.BootstrappingApplicationImage,
                "Bootstrapping IEC-101 application image", "General Interrogation is sent only on the active link.");
            await TryDispatchPendingStartupGiAsync(
                "Dual-link startup station interrogation",
                "Dual-link startup GI",
                "Dual-link startup post-GI Class 2 verification sweep",
                cancellationToken).ConfigureAwait(false);
        }

        // An open COM port or checksum-valid primary echo is not evidence that a
        // secondary station answered. Do not report a healthy controller during
        // startup until both channels have a validated secondary response.
        if (!_active.IsHealthy || !_standby.IsHealthy)
        {
            SetControllerState(
                Iec101RedundancyControllerState.Degraded,
                "IEC-101 dual-link secondary response not verified",
                $"Active={_active.Name}, validated={_active.IsHealthy}; standby={_standby.Name}, validated={_standby.IsHealthy}. " +
                "Check RX rejection diagnostics, PRM, Link Address and physical serial routing.",
                "Warning");
        }
        else
        {
            SetControllerState(Iec101RedundancyControllerState.Healthy, "IEC-101 dual-link redundancy running",
                $"Active={_active.Name}; standby={_standby.Name}; both secondary links validated.");
        }
    }

    private async Task RunOneSchedulerCycleAsync(CancellationToken cancellationToken)
    {
        await ReopenPhysicalPortsIfDueAsync(cancellationToken).ConfigureAwait(false);
        if (_active is null || _standby is null)
        {
            SetControllerState(Iec101RedundancyControllerState.NoAvailableLink, "No IEC-101 active link available", "Controller has no elected active link.", "Error");
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            return;
        }

        // A physically closed active cannot send any data service. Verify B
        // with FC9 and promote only after independent addressed secondary proof.
        if (!_active.IsOpen)
        {
            if (_standby.IsOpen)
            {
                await SuperviseStandbyAsync(cancellationToken).ConfigureAwait(false);
                if (!_active.IsOpen && _standby.CanRescueFailedActive(
                        _options.StandbyFailureThreshold, BuildRecentGoodRescueWindow()))
                    await TryFailoverAsync("Active COM absent; healthy standby proved by FC9",
                        cancellationToken, bypassStabilizationGuard: true).ConfigureAwait(false);
            }
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Reopened active has no inherited health and must pass FC9 first.
        if (!_active.LinkState.LastGoodResponseUtc.HasValue)
        {
            // One failed FC9 already marks a timeout-suspect active. Preserve
            // the existing fast standby rescue instead of giving active a
            // second FC9 attempt before evaluating a healthy independent B.
            if (ActiveNeedsRescue() && _standby.IsOpen)
            {
                var failingOwner = _active;
                await SuperviseStandbyAsync(cancellationToken).ConfigureAwait(false);
                if (ReferenceEquals(_active, failingOwner) &&
                    _standby.CanRescueFailedActive(
                        _options.StandbyFailureThreshold, BuildRecentGoodRescueWindow()))
                    await TryFailoverAsync("Active secondary silent; independently verified standby rescue",
                        cancellationToken, bypassStabilizationGuard: true).ConfigureAwait(false);
                return;
            }

            // A newly opened physical link has no stale RTU readiness. Only
            // addressed FC9 can establish its first good secondary response.
            await _active.RequestLinkStatusAsync("Unverified active COM FC9 secondary proof", cancellationToken).ConfigureAwait(false);
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

        if (await TryRetryGiUsingObservedCommonAddressAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // ACD Class-1 drain must not starve the first Connect GI. Keep the
        // intent pending unless a real GI dispatch was attempted; an early
        // link/owner guard exit is not a transmitted command.
        if (await TryDispatchPendingStartupGiAsync(
            "Deferred startup GI after verified active secondary",
            "Deferred dual-link startup GI",
            "Deferred dual-link Class 2 verification",
            cancellationToken).ConfigureAwait(false))
            return;

        if (_active.LinkState.Acd)
        {
            var ownerAtDrain = _active;
            var yieldedToClass2 = await DrainClass1OnActiveAsync("ACD=1 event data pending on active link", stopWhenGiEnds: false, cancellationToken).ConfigureAwait(false);
            await EvaluateActiveHealthAsync("Class 1 drain", cancellationToken).ConfigureAwait(false);
            // Under sustained ACD, periodically supervise standby using FC9 only.
            if (ReferenceEquals(_active, ownerAtDrain) && _standby is not null &&
                DateTime.UtcNow - _lastStandbySupervisionUtc >= _options.StandbySupervisionInterval)
            {
                await SuperviseStandbyAsync(cancellationToken).ConfigureAwait(false);
            }

            // No Class 2 on standby or a newly promoted owner; no DFC-busy poll.
            if (yieldedToClass2 && ReferenceEquals(_active, ownerAtDrain) &&
                _active.IsHealthy && !_active.LinkState.Dfc && _active.LinkState.Acd &&
                _options.BaseSettings.MaxConsecutiveClass1BeforeClass2 > 0 &&
                DateTime.UtcNow - _lastClass2PollUtc >=
                    TimeSpan.FromMilliseconds(_options.BaseSettings.Class2PollIntervalMs))
            {
                _counters.Class2Requests++;
                await _active.RequestClass2Async(
                    "ACD Class 1 fairness yield to Class 2 on active A/B link",
                    cancellationToken).ConfigureAwait(false);
                _lastClass2PollUtc = DateTime.UtcNow;
                await EvaluateActiveHealthAsync("Class 2 fairness", cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        var now = DateTime.UtcNow;
        if (ActiveNeedsRescue())
        {
            await SuperviseStandbyAsync(cancellationToken).ConfigureAwait(false);
            if (_active is null || !ActiveNeedsRescue())
            {
                return;
            }

            if (_standby is not null && _standby.CanRescueFailedActive(_options.StandbyFailureThreshold, BuildRecentGoodRescueWindow()))
            {
                await TryFailoverAsync("Active link failed and standby has emergency rescue proof", cancellationToken, bypassStabilizationGuard: true).ConfigureAwait(false);
                return;
            }
        }

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
        // Do not consume operator intents while the physical active owner is
        // unverified, DFC-busy, or midway through an A/B promotion.
        var owner = _active;
        if (owner is null || _switchInProgress || !owner.IsHealthy ||
            !owner.HasVerifiedCommandAuthority)
            return false;

        if (!_controlCommands.TryDequeue(out var queued))
            return false;

        var request = queued.Request;
        var ownerMatches = queued.OwnerAtQueue.LinkName is null ||
            queued.OwnerAtQueue.Matches(owner.Name, owner.CommandAuthorityEpoch,
                Volatile.Read(ref _activeGeneration));
        if (!ownerMatches ||
            (IsMutatingCommand(request) &&
             (!queued.VerifiedWhenQueued ||
              DateTime.UtcNow - queued.QueuedUtc > TimeSpan.FromSeconds(30))))
        {
            RaiseFinding(FindingSeverity.Warning, "IEC101-DUAL-COMMAND-STALE-OWNER",
                "IEC-101 operator command rejected: stale active-link ownership",
                $"{request.Summary}; owner-at-queue={queued.OwnerAtQueue.LinkName ?? "none"}; " +
                $"current-active={owner.Name}; verified-at-queue={queued.VerifiedWhenQueued}; " +
                $"age={(DateTime.UtcNow - queued.QueuedUtc).TotalSeconds:0.0}s.",
                "Queued commands must not be replayed after active link switch, reconnect or uncertain link proof.",
                "Reissue the explicit command on the freshly verified active link.");
            return true;
        }

        // Selection authority belongs to this exact physical active COM and
        // is never borrowed from the standby or a previous A/B owner.
        if (!owner.CanDispatchSboExecute(request))
        {
            RaiseFinding(FindingSeverity.Warning, "IEC101-DUAL-SBO-EXECUTE-NOT-SELECTED",
                "IEC-101 dual-link SBO execute blocked before physical transmission",
                $"{request.Summary}; active={owner.Name}.",
                "No current positive, matching SELECT ACTCON on this active physical COM, or SELECT intent conflicts with the encoded S/E bit.",
                "Issue SELECT and verify its ACTCON on the same active channel, then deliberately EXECUTE.");
            return true;
        }

        // Commands retain their explicitly selected CA, or the configured CA.
        // A CA inferred from observed process traffic is for bounded GI retry only.
        AddEvent(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.Unknown,
            State = Iec103MasterState.GeneralInterrogation,
            Category = Iec101RedundancyEventKind.CommandDispatchedOnActive.ToString(),
            DataClass = $"Command · {owner.Name}/Active",
            Summary = $"IEC-101 command routed to active link {owner.Name}",
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

        await owner.SendControlCommandAsync(request, cancellationToken).ConfigureAwait(false);
        if (owner.LinkState.Acd)
        {
            await DrainClass1OnActiveAsync("Command feedback drain on active link", stopWhenGiEnds: false, cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    private async Task<bool> DrainClass1OnActiveAsync(string reason, bool stopWhenGiEnds, CancellationToken cancellationToken)
    {
        if (_active is null)
        {
            return false;
        }

        _counters.Class1DrainBursts++;
        var drained = 0;
        var yieldedToClass2 = false;
        var stoppedByGiEnd = false;
        while (!cancellationToken.IsCancellationRequested && drained < _options.BaseSettings.MaxClass1DrainFrames)
        {
            if (!stopWhenGiEnds && drained > 0 &&
                _options.BaseSettings.MaxConsecutiveClass1BeforeClass2 > 0 &&
                drained % _options.BaseSettings.MaxConsecutiveClass1BeforeClass2 == 0 &&
                DateTime.UtcNow - _lastClass2PollUtc >=
                    TimeSpan.FromMilliseconds(_options.BaseSettings.Class2PollIntervalMs))
            {
                yieldedToClass2 = true;
                break;
            }

            var beforeGood = _active.LinkState.LastGoodResponseUtc;
            _counters.Class1Requests++;
            var response = await _active.RequestClass1Async(reason, cancellationToken).ConfigureAwait(false);
            drained++;
            _counters.Class1DrainFrames++;

            if (response.TimedOut || beforeGood == _active.LinkState.LastGoodResponseUtc)
            {
                break;
            }

            if (response.MatchedGiActterm)
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

        return yieldedToClass2;
    }

    private async Task SuperviseStandbyAsync(CancellationToken cancellationToken)
    {
        if (_standby is null || !_standby.IsOpen)
        {
            return;
        }

        var standbyWasLatched = _standby.State is Iec101RedundancyChannelState.FailedLatched or Iec101RedundancyChannelState.Recovering;
        if (standbyWasLatched
            && !ActiveNeedsRescue()
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
            if (ActiveNeedsRescue() && _standby.CanRescueFailedActive(_options.StandbyFailureThreshold, BuildRecentGoodRescueWindow()))
            {
                _standby.MarkRecoveredAsStandby($"{_standby.Name} passed an emergency recovery probe while active {_active?.Name ?? "-"} was failing.");
                SetControllerState(Iec101RedundancyControllerState.Recovering, "IEC-101 standby ready to rescue failed active link", $"Standby={_standby.Name}; active={_active?.Name ?? "-"}; good probes={_standby.LinkState.ConsecutiveGoodResponses}.");
                await TryFailoverAsync($"Standby {_standby.Name} recovered while active {_active?.Name ?? "-"} is failed/timed out", cancellationToken, bypassStabilizationGuard: true).ConfigureAwait(false);
                return;
            }

            if (_standby.LinkState.ConsecutiveGoodResponses < _options.StandbyRecoveryGoodResponseThreshold)
            {
                _standby.MarkRecoveryProbeSucceeded(_options.StandbyRecoveryGoodResponseThreshold);
                SetControllerState(Iec101RedundancyControllerState.Recovering, "IEC-101 standby recovery probe succeeded", $"{_standby.Name} good probes={_standby.LinkState.ConsecutiveGoodResponses}/{_options.StandbyRecoveryGoodResponseThreshold}.");
                return;
            }

            _standby.MarkRecoveredAsStandby($"{_standby.Name} has met the standby recovery threshold with {_standby.LinkState.ConsecutiveGoodResponses} consecutive good supervision responses.");
            // Recovery of standby does not establish full dual-link health if active
            // is still failing. The following rescue probe decides whether to switch.
            if (_active?.IsHealthy == true)
            {
                SetControllerState(Iec101RedundancyControllerState.Healthy, "IEC-101 standby link recovered",
                    $"Active={_active.Name}; standby={_standby.Name}; both links validated; failback policy={_options.FailbackPolicy}.");
            }
            else
            {
                SetControllerState(Iec101RedundancyControllerState.Recovering, "IEC-101 standby recovered; active unhealthy",
                    $"Active={_active?.Name ?? "-"} has no current good secondary response; standby={_standby.Name} recovered. Assessing failover.",
                    "Warning");
            }
            if (ActiveNeedsRescue())
            {
                await TryFailoverAsync($"Standby {_standby.Name} recovered while active {_active?.Name ?? "-"} is failed/timed out", cancellationToken, bypassStabilizationGuard: true).ConfigureAwait(false);
                return;
            }

            await ConsiderAutoFailbackAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (response.Succeeded && ActiveNeedsRescue() && _standby.CanRescueFailedActive(_options.StandbyFailureThreshold, BuildRecentGoodRescueWindow()))
        {
            await TryFailoverAsync($"Standby {_standby.Name} is healthy while active {_active?.Name ?? "-"} is failed/timed out", cancellationToken, bypassStabilizationGuard: true).ConfigureAwait(false);
            return;
        }

        if (_active?.IsHealthy == true
            && _standby.IsHealthy
            && _controllerState is Iec101RedundancyControllerState.Degraded or Iec101RedundancyControllerState.NoAvailableLink or Iec101RedundancyControllerState.Recovering)
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

        // Overall dual-link health requires both channels to have proven a valid
        // secondary response. A recovering active link alone cannot certify standby.
        if (_active.IsHealthy
            && _standby?.IsHealthy == true
            && _controllerState is Iec101RedundancyControllerState.Degraded or Iec101RedundancyControllerState.NoAvailableLink or Iec101RedundancyControllerState.Recovering)
        {
            SetControllerState(Iec101RedundancyControllerState.Healthy, "IEC-101 active link recovered", $"Active={_active.Name}; standby={_standby.Name}; both secondary links verified; reason={reason}.");
        }

        if (_active.LinkState.ConsecutiveFailures >= _options.ActiveFailureThreshold)
        {
            var failedActiveName = _active.Name;
            var insideStabilizationWindow = DateTime.UtcNow - _lastFailoverUtc < _options.AntiPingPongWindow;

            // Cascaded failure case: A failed to B, then B times out while A is still in
            // recovery/standby. Do not let normal active Class-2 scheduling or the
            // anti-ping-pong window leave the newly failed active link stuck forever.
            // First force a standby health probe; a successful probe will call
            // TryFailoverAsync(..., bypassStabilizationGuard: true) from
            // SuperviseStandbyAsync and rescue service immediately.
            if (insideStabilizationWindow && _standby is not null)
            {
                _lastStandbySupervisionUtc = DateTime.MinValue;
                await SuperviseStandbyAsync(cancellationToken).ConfigureAwait(false);
                if (_active is null || !string.Equals(_active.Name, failedActiveName, StringComparison.Ordinal))
                {
                    return;
                }

                if (_standby.CanRescueFailedActive(_options.StandbyFailureThreshold, BuildRecentGoodRescueWindow()))
                {
                    await TryFailoverAsync(reason + " · emergency standby rescue proof", cancellationToken, bypassStabilizationGuard: true).ConfigureAwait(false);
                    return;
                }
            }

            await TryFailoverAsync(reason, cancellationToken, bypassStabilizationGuard: false).ConfigureAwait(false);
        }
    }

    private async Task TryFailoverAsync(string reason, CancellationToken cancellationToken, bool bypassStabilizationGuard)
    {
        if (_switchInProgress || _active is null || _standby is null)
        {
            return;
        }

        var activeFailureEmergency = IsActiveFailureEmergency(_active, _standby);
        var insideStabilizationWindow = DateTime.UtcNow - _lastFailoverUtc < _options.AntiPingPongWindow;
        var activeHardFailed = _active.State is Iec101RedundancyChannelState.TimeoutSuspect or Iec101RedundancyChannelState.FailedLatched
            || _active.LinkState.ConsecutiveFailures >= _options.ActiveFailureThreshold;
        if (!bypassStabilizationGuard && insideStabilizationWindow && !activeFailureEmergency)
        {
            if (activeHardFailed)
            {
                AddEvent(new Iec103MasterEvidenceEvent
                {
                    Direction = FrameDirection.Unknown,
                    State = Iec103MasterState.TimeoutRecovery,
                    Category = Iec101RedundancyEventKind.StateChanged.ToString(),
                    DataClass = "Redundancy",
                    Summary = "IEC-101 active failure awaiting standby rescue proof",
                    Detail = $"Active={_active.Name}; standby={_standby.Name}; reason={reason}; last failover={_lastFailoverUtc:O}; active failures={_active.LinkState.ConsecutiveFailures}.",
                    OperatorMessage = "Active link is failing inside the stabilization window; standby supervision is prioritized before changing ownership again.",
                    ProtocolMeaning = "Anti-ping-pong protection must not lock the controller on a failed active link, but the old active still needs a fresh standby health proof before rescue promotion.",
                    OperatorAction = "Wait for the next standby supervision row. If it succeeds, the controller will rescue failover without waiting for the anti-ping-pong window.",
                    ProtocolMode = Iec60870ProtocolMode.Iec101,
                    SignalGroup = "IEC-101 Dual Link"
                });
                _lastClass2PollUtc = DateTime.UtcNow;
                _lastStandbySupervisionUtc = DateTime.MinValue;
                SetControllerState(Iec101RedundancyControllerState.Recovering, "IEC-101 standby rescue supervision required", $"Active={_active.Name}; standby={_standby.Name}; reason={reason}.");
                return;
            }

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
            _lastClass2PollUtc = DateTime.MinValue;
            _lastStandbySupervisionUtc = DateTime.MinValue;
            return;
        }

        if (activeFailureEmergency && !bypassStabilizationGuard && DateTime.UtcNow - _lastFailoverUtc < _options.AntiPingPongWindow)
        {
            AddEvent(new Iec103MasterEvidenceEvent
            {
                Direction = FrameDirection.Unknown,
                State = Iec103MasterState.TimeoutRecovery,
                Category = Iec101RedundancyEventKind.StateChanged.ToString(),
                DataClass = "Redundancy",
                Summary = "IEC-101 anti-ping-pong guard bypassed for active-link failure",
                Detail = $"Active={_active.Name}; standby={_standby.Name}; reason={reason}; active failures={_active.LinkState.ConsecutiveFailures}; standby good={_standby.LinkState.ConsecutiveGoodResponses}.",
                OperatorMessage = "The active link is failing and the standby has enough health evidence to rescue service.",
                ProtocolMeaning = "Anti-ping-pong protection must not leave a failed active link latched when the standby can carry traffic.",
                OperatorAction = "Use the following failover evidence to verify cascaded redundancy behavior.",
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                SignalGroup = "IEC-101 Dual Link"
            });
        }

        _switchInProgress = true;
        var sw = Stopwatch.StartNew();
        var from = _active;
        var to = _standby;
        var fromWasFailed = from.LinkState.ConsecutiveFailures >= _options.ActiveFailureThreshold
            || from.State is Iec101RedundancyChannelState.TimeoutSuspect
                or Iec101RedundancyChannelState.FailedLatched;
        try
        {
            SetControllerState(Iec101RedundancyControllerState.Switching, "IEC-101 failover started", $"{from.Name} → {to.Name}. Reason: {reason}.", Iec101RedundancyEventKind.FailoverStarted.ToString());
            _imageTracker.MarkStale();

            if (!to.CanRescueFailedActive(_options.StandbyFailureThreshold, BuildRecentGoodRescueWindow()))
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
                _lastStandbySupervisionUtc = DateTime.MinValue;
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
            Interlocked.Increment(ref _activeGeneration);
            var discardedSwitchRequests = _manualFailoverRequests.Clear();
            if (discardedSwitchRequests > 0)
                RaiseFinding(FindingSeverity.Warning, "IEC101-DUAL-STALE-FAILOVER-REQUESTS",
                    "Old manual failover requests discarded after active ownership changed",
                    $"Discarded pending switch requests={discardedSwitchRequests}; active={to.Name}.",
                    "A previously queued switchover must not reverse the newly selected owner.",
                    "Issue a new manual failover if another physical switch is intended.");
            // Old-owner CA evidence and GI intent cannot cross A/B promotion.
            _observedCa.Reset();
            _learnedCommonAddress = null;
            _retriedGiWithObservedCommonAddress = false;
            _giRequestedOnActiveOwner = false;
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

    private bool IsActiveFailureEmergency(Iec101DualLinkChannel active, Iec101DualLinkChannel standby)
    {
        var activeFailed = active.State is Iec101RedundancyChannelState.TimeoutSuspect or Iec101RedundancyChannelState.FailedLatched
            || active.LinkState.ConsecutiveFailures >= _options.ActiveFailureThreshold;
        if (!activeFailed)
        {
            return false;
        }

        return standby.CanRescueFailedActive(_options.StandbyFailureThreshold, BuildRecentGoodRescueWindow());
    }

    private TimeSpan BuildRecentGoodRescueWindow()
    {
        var timeoutMs = Math.Max(250, _options.BaseSettings.ResponseTimeoutMs);
        var standbyMs = Math.Max(250, _options.StandbySupervisionInterval.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Math.Max(timeoutMs * 4, standbyMs * 3));
    }

    private bool ActiveNeedsRescue()
        => _active is not null
           && (_active.State is Iec101RedundancyChannelState.TimeoutSuspect or Iec101RedundancyChannelState.FailedLatched
               || _active.LinkState.ConsecutiveFailures >= _options.ActiveFailureThreshold);

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

    // One first-Connect GI intention belongs to the session, not a physical
    // COM epoch. Consume conservatively at the first attempted GI dispatch
    // (counter advances before driver I/O), never on an eligibility check.
    // Failed or ambiguous writes are NOT auto-replayed on recovery/failover.
    private async Task<bool> TryDispatchPendingStartupGiAsync(
        string reason, string lifecycle, string sweepReason, CancellationToken cancellationToken)
    {
        if (!_startupGiPending || _active is null || !_active.IsHealthy || ActiveNeedsRescue())
            return false;

        var attemptsBefore = _counters.GiCommands;
        try
        {
            await RunGeneralInterrogationLifecycleOnActiveAsync(
                reason, lifecycle, sweepReason, commonAddress: null,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (_counters.GiCommands > attemptsBefore)
                _startupGiPending = false;
        }
        return _counters.GiCommands > attemptsBefore;
    }

    private async Task RunGeneralInterrogationLifecycleOnActiveAsync(string commandReason, string lifecycleLabel, string sweepReason, int? commonAddress, CancellationToken cancellationToken)
    {
        if (_active is null)
        {
            return;
        }

        if (!_active.IsHealthy)
        {
            RaiseFinding(FindingSeverity.Warning, "IEC101-DUAL-GI-LINK-UNVERIFIED",
                "Dual-link GI deferred: active SICAM secondary not verified",
                $"Active={_active.Name}; COM={_active.Endpoint.PortName}; Link={_active.Endpoint.LinkAddress}; " +
                $"RX rejected={_active.LinkState.RejectedFrames}; verified secondary={_active.LinkState.LastGoodResponseUtc.HasValue}.",
                "An open COM port or sent GI frame does not prove RTU reception.",
                "Inspect both links' IEC101-RX-* diagnostics and correct actual cable/RTU role before GI.");
            return;
        }

        var effectiveCommonAddress = ResolveRuntimeCommonAddress(commonAddress);
        _imageTracker.MarkGiStarted(DateTime.UtcNow);
        // Entered only for configured startup, deliberate manual or post-switch GI.
        _giRequestedOnActiveOwner = true;
        _counters.GiCommands++;
        var response = await _active.SendGeneralInterrogationAsync(commandReason, cancellationToken, qualifier: 20, commonAddress: effectiveCommonAddress).ConfigureAwait(false);
        if (response.TimedOut || response.Frame is null ||
            (response.Asdu is null && !response.Succeeded))
        {
            RaiseFinding(FindingSeverity.Warning, "IEC101-DUAL-GI-UNCONFIRMED",
                "Dual-link SICAM GI not confirmed",
                $"Active={_active.Name}; CA={effectiveCommonAddress}; " +
                $"RX rejected={_active.LinkState.RejectedFrames}; response timed out={response.TimedOut}.",
                "A sent GI frame is not evidence of accepted GI or process values.",
                "Inspect PRM, link address, and actual RX from active COM. Do not blind-drain Class 1.");
            PublishApplicationImageMilestone();
            return;
        }

        if (IsNegativeConfirmation(response, expectedTypeId: 100))
        {
            var caText = FormatCommonAddress(effectiveCommonAddress);
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
            await RunGroupInterrogationFallbackOnActiveAsync($"{lifecycleLabel} negative station GI", effectiveCommonAddress, cancellationToken).ConfigureAwait(false);
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

        var linkRepliedGroups = 0;
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
                // Link-layer FC0/E5 is not application ACTCON; do not label
                // an attempted group as an RTU-accepted interrogation.
                linkRepliedGroups++;
                await DrainClass1OnActiveAsync($"Group GI QOI={qoi} follow-up drain on active link", stopWhenGiEnds: true, cancellationToken).ConfigureAwait(false);
            }

            if (linkRepliedGroups > 0 && (negativeGroups + noResponseGroups) >= Math.Max(4, linkRepliedGroups + 2))
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
            $"Groups with link replies={linkRepliedGroups}; application acceptance requires matched ACTCON; negative/no-response={negativeGroups + noResponseGroups}. Continuing Class 2/background polling.");
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
        var unansweredStreak = 0;
        var userDataBefore = _active.LinkState.UserDataResponses;
        var maxSweeps = Math.Clamp(_options.BaseSettings.MaxClass1DrainFrames / 2, 8, 32);
        for (var i = 0; i < maxSweeps && !cancellationToken.IsCancellationRequested; i++)
        {
            var beforeNoData = _active.LinkState.NoDataResponses;
            var beforeUserData = _active.LinkState.UserDataResponses;

            _counters.Class2Requests++;
            var exchange = await _active.RequestClass2Async(reason, cancellationToken).ConfigureAwait(false);
            _lastClass2PollUtc = DateTime.UtcNow;
            unansweredStreak = exchange.TimedOut ? unansweredStreak + 1 : 0;
            if (unansweredStreak >= 2)
            {
                RaiseFinding(FindingSeverity.Warning, "IEC101-DUAL-POSTGI-RTU-SILENT",
                    "Dual-link post-GI background verification stopped",
                    $"Active={_active.Name}; two Class 2 polls got no valid secondary reply.",
                    "Polling cannot prove an RTU data snapshot in the absence of responses.",
                    "Inspect link diagnostics and RTU configuration, then retry GI on verified link.");
                break;
            }

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

    private void ObserveApplicationAsdu(Iec10xAsduDecode? asdu, DateTime utcNow,
        bool matchedGiActterm, string physicalSource)
    {
        // A/B standby may supervise FC9, but it cannot certify the active owner's
        // application image, CA evidence, or GI lifecycle.
        if (_active is null || !string.Equals(_active.Name, physicalSource, StringComparison.Ordinal))
            return;
        _imageTracker.Observe(asdu, utcNow, matchedGiActterm);
        _observedCa.Observe(asdu);
        if (_learnedCommonAddress.HasValue && _observedCa.HasConflictingStations)
        {
            // A previously observed single-station CA has become ambiguous.
            // Never retain this advisory GI target once another station appears.
            var formerCa = _learnedCommonAddress.Value;
            _learnedCommonAddress = null;
            RaiseFinding(
                FindingSeverity.Warning,
                "IEC101-DUAL-CA-AMBIGUOUS",
                "IEC-101 observed CA invalidated by multi-station traffic",
                $"Former GI retry CA={formerCa}; valid process ASDUs now identify multiple CAs.",
                "Station identity can no longer be inferred from received process traffic.",
                "Use the configured CA or explicitly choose the GI/command target after verifying the RTU station configuration.");
        }
        if (matchedGiActterm)
        {
            AddApplicationImageEvent(Iec101RedundancyEventKind.PostSwitchGiCompleted, "IEC-101 source-matched GI ACTTERM observed", $"Application image={_imageTracker.State}; objects={_imageTracker.ObjectCount}.");
        }
    }

    private async Task<bool> TryRetryGiUsingObservedCommonAddressAsync(CancellationToken cancellationToken)
    {
        var observedCandidate = _observedCa.GiRetryCandidate;
        var active = _active;
        if (active is null || !active.IsHealthy || active.LinkState.Dfc ||
            !_giRequestedOnActiveOwner || _retriedGiWithObservedCommonAddress ||
            !observedCandidate.HasValue)
        {
            return false;
        }

        var observedCa = observedCandidate.GetValueOrDefault();
        var configuredCa = _options.BaseSettings.CommonAddress;
        if (observedCa == configuredCa)
        {
            return false;
        }

        _retriedGiWithObservedCommonAddress = true;
        _learnedCommonAddress = observedCa;
        // Never rewrite configured station CA based on passive observation.

        SetControllerState(
            Iec101RedundancyControllerState.BootstrappingApplicationImage,
            "IEC-101 dual-link observed CA learned",
            $"Live process ASDUs are using CA={observedCa}, while configured GI CA={configuredCa}. Retrying GI on active link with observed CA.",
            "Warning");
        RaiseFinding(
            FindingSeverity.Warning,
            "IEC101-DUAL-CA-AUTO-LEARNED",
            "IEC-101 dual-link observed CA used for GI retry only",
            $"Configured CA={configuredCa}; observed process ASDU CA={observedCa}; active={active.Name}.",
            "Station GI can be retried using observed process evidence; this never proves a safe command target.",
            "Verify the station CA in setup before controls. Automatic CA observations affect GI retry only, never operator command routing.");

        await RunGeneralInterrogationLifecycleOnActiveAsync(
            $"Dual-link GI retry using observed CA {observedCa}",
            $"Observed CA {observedCa} dual-link GI",
            $"Observed CA {observedCa} post-GI Class 2 verification sweep",
            observedCa,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private int? ResolveRuntimeCommonAddress(int? requestedCommonAddress)
        => requestedCommonAddress; // Null uses configured CA; auto-learn is retry-only.

    private string FormatCommonAddress(int? requestedCommonAddress)
        => (ResolveRuntimeCommonAddress(requestedCommonAddress) ??
            _options.BaseSettings.CommonAddress)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

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

    private void AddApplicationImageEvent(Iec101RedundancyEventKind kind, string summary, string detail)
    {
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
        _counters.RejectedFrames = _linkA.LinkState.RejectedFrames + _linkB.LinkState.RejectedFrames;
        _counters.TxEchoFrames = _linkA.LinkState.TxEchoFrames + _linkB.LinkState.TxEchoFrames;
        _counters.ForeignPrimaryFrames = _linkA.LinkState.ForeignPrimaryFrames + _linkB.LinkState.ForeignPrimaryFrames;
        _counters.LinkAddressMismatchFrames = _linkA.LinkState.LinkAddressMismatchFrames + _linkB.LinkState.LinkAddressMismatchFrames;
        _counters.NoDataResponses = Math.Max(_counters.NoDataResponses, _linkA.LinkState.NoDataResponses + _linkB.LinkState.NoDataResponses);
        _counters.UserDataResponses = Math.Max(_counters.UserDataResponses, _linkA.LinkState.UserDataResponses + _linkB.LinkState.UserDataResponses);
    }

    private void BuildPostRunFindings()
    {
        if (_failoverJournal.Count == 0)
        {
            AddEvent(new Iec103MasterEvidenceEvent
            {
                Direction = FrameDirection.Unknown,
                State = Iec103MasterState.Stopped,
                Category = Iec101RedundancyEventKind.StateChanged.ToString(),
                DataClass = "Redundancy Assessment",
                Summary = "IEC-101 dual-link session ended without failover",
                Detail = "No failover journal entries were recorded. This is normal when the operator stops a healthy session or no active-link failure was injected.",
                OperatorMessage = "No redundancy failover was assessed in this run.",
                ProtocolMeaning = "No failover journal entries were recorded; treat this as context, not an issue.",
                OperatorAction = "For FAT/SAT redundancy proof, deliberately interrupt the active link and verify failover evidence appears in the timeline/report.",
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                SignalGroup = "IEC-101 Dual Link"
            });
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
        var finding = new Iec103MasterFinding
        {
            Severity = severity,
            Id = id,
            Title = title,
            Evidence = evidence,
            Impact = impact,
            Recommendation = recommendation
        };
        // UI threads may queue commands while the serial scheduler emits its
        // own findings. Keep the deduplication and List mutation atomic.
        lock (_findingGate)
        {
            if (_findings.Any(x => x.Id == id && x.Evidence == evidence)) return;
            _findings.Add(finding);
        }
        // Never invoke UI subscribers while holding an internal collection lock.
        FindingRaised?.Invoke(this, finding);
    }

    private Iec103MasterFinding[] SnapshotFindings()
    {
        lock (_findingGate) return _findings.ToArray();
    }

    private long AddEvent(Iec103MasterEvidenceEvent item)
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
            PhysicalSource = item.PhysicalSource,
            PhysicalStreamId = item.PhysicalStreamId,
            RawStreamOffset = item.RawStreamOffset,
            PhysicalFrameSequenceNumber = item.PhysicalFrameSequenceNumber,
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
            CauseDetails = item.CauseDetails,
            CommandDetails = item.CommandDetails,
            QualityDetails = item.QualityDetails,
            AsduDetails = item.AsduDetails,
            InformationObjectDetails = item.InformationObjectDetails,
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
        _counters.EvidenceEventsDroppedFromMemory += _events.Append(enriched);
        if (Iec101SoeObservationPolicy.IsAcceptedSpontaneousDigitalObject(enriched))
            _counters.ObservedSpontaneousDigitalObjects++;
        EvidenceReceived?.Invoke(this, enriched);
        return enriched.SequenceNumber;
    }

    private void PublishSnapshot() => SnapshotChanged?.Invoke(this, CreateSnapshot());
}
