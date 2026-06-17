// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Iec101.Redundancy;

public enum Iec101RedundancyChannelRole
{
    None = 0,
    Active = 1,
    Standby = 2
}

public enum Iec101RedundancyChannelState
{
    Closed = 0,
    Opening = 1,
    LinkResetting = 2,
    LinkStatusChecking = 3,
    ActivePolling = 4,
    StandbySupervising = 5,
    TimeoutSuspect = 6,
    FailedLatched = 7,
    Recovering = 8,
    Promoting = 9,
    Demoting = 10
}

public enum Iec101RedundancyControllerState
{
    Created = 0,
    OpeningLinks = 1,
    ElectingActive = 2,
    BootstrappingApplicationImage = 3,
    Healthy = 4,
    Degraded = 5,
    Switching = 6,
    Recovering = 7,
    NoAvailableLink = 8,
    Stopping = 9,
    Stopped = 10,
    Faulted = 11
}

public enum Iec101RedundancyEventKind
{
    StateChanged = 0,
    ChannelOpened = 1,
    ChannelClosed = 2,
    LinkStatusRequested = 3,
    StandbySupervisionSent = 4,
    StandbySupervisionConfirmed = 5,
    ActiveTimeout = 6,
    StandbyTimeout = 7,
    FailoverStarted = 8,
    FailoverCompleted = 9,
    FailoverRejected = 10,
    RecoveryStarted = 11,
    RecoveryCompleted = 12,
    RecoveryProbeSucceeded = 13,
    AutoFailbackRequested = 14,
    AutoFailbackBlocked = 15,
    PostSwitchGiStarted = 16,
    PostSwitchGiCompleted = 17,
    ApplicationImageReady = 18,
    ApplicationImagePartial = 19,
    ApplicationImageStale = 20,
    CommandDispatchedOnActive = 21,
    CommandBlockedOnStandby = 22,
    ManualFailoverRequested = 23,
    ManualFailoverBlocked = 24,
    ManualInterrogationRequested = 25
}

public enum Iec101PostSwitchGiPolicy
{
    Required = 0,
    OptionalIfApplicationImageFresh = 1,
    ManualOnly = 2,
    Disabled = 3
}

public enum Iec101ApplicationImageState
{
    Empty = 0,
    Building = 1,
    Ready = 2,
    Partial = 3,
    Stale = 4
}


public enum Iec101DualLinkFailbackPolicy
{
    ManualOnly = 0,
    PreferredLinkAfterStableRecovery = 1
}
