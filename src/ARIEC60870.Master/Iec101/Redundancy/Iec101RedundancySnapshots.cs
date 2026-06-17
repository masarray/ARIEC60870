// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Iec101.Redundancy;

public sealed class Iec101RedundancyChannelSnapshot
{
    public string Name { get; init; } = string.Empty;
    public string PortName { get; init; } = string.Empty;
    public int LinkAddress { get; init; }
    public Iec101RedundancyChannelRole Role { get; init; }
    public Iec101RedundancyChannelState State { get; init; }
    public bool IsOpen { get; init; }
    public bool IsHealthy { get; init; }
    public bool Acd { get; init; }
    public bool Dfc { get; init; }
    public bool FrameCountBit { get; init; }
    public DateTime? LastGoodResponseUtc { get; init; }
    public DateTime? LastTimeoutUtc { get; init; }
    public int ConsecutiveTimeouts { get; init; }
    public int ConsecutiveFailures { get; init; }
    public int TxFrames { get; init; }
    public int RxFrames { get; init; }
    public int Class1Requests { get; init; }
    public int Class2Requests { get; init; }
    public int StandbySupervisionRequests { get; init; }
    public int NoDataResponses { get; init; }
    public int UserDataResponses { get; init; }
    public int ChecksumErrors { get; init; }
    public int MalformedFrames { get; init; }
}

public sealed class Iec101RedundancySessionSnapshot
{
    public Iec101RedundancyControllerState ControllerState { get; init; }
    public string ActiveLinkName { get; init; } = string.Empty;
    public string StandbyLinkName { get; init; } = string.Empty;
    public Iec101ApplicationImageState ApplicationImageState { get; init; }
    public int ApplicationImageObjectCount { get; init; }
    public DateTime? LastGiStartedUtc { get; init; }
    public DateTime? LastGiCompletedUtc { get; init; }
    public DateTime? LastFailoverUtc { get; init; }
    public int LastFailoverLatencyMs { get; init; }
    public int FailoverCount { get; init; }
    public string LastFailoverFromLink { get; init; } = string.Empty;
    public string LastFailoverToLink { get; init; } = string.Empty;
    public string LastFailoverReason { get; init; } = string.Empty;
    public bool LastFailoverCompleted { get; init; }
    public DateTime? LastStandbySupervisionUtc { get; init; }
    public Iec101RedundancyChannelSnapshot LinkA { get; init; } = new();
    public Iec101RedundancyChannelSnapshot LinkB { get; init; } = new();
}
