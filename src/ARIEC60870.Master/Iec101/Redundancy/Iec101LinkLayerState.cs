// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Iec101.Redundancy;

public sealed class Iec101LinkLayerState
{
    public bool FrameCountBit { get; set; }
    public bool Acd { get; set; }
    public bool Dfc { get; set; }
    public DateTime? LastGoodResponseUtc { get; set; }
    public DateTime? LastTimeoutUtc { get; set; }
    public int ConsecutiveTimeouts { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int TxFrames { get; set; }
    public int RxFrames { get; set; }
    public int Class1Requests { get; set; }
    public int Class2Requests { get; set; }
    public int StandbySupervisionRequests { get; set; }
    public int NoDataResponses { get; set; }
    public int UserDataResponses { get; set; }
    public int ChecksumErrors { get; set; }
    public int MalformedFrames { get; set; }

    public void MarkGoodResponse(DateTime utcNow)
    {
        LastGoodResponseUtc = utcNow;
        ConsecutiveTimeouts = 0;
        ConsecutiveFailures = 0;
    }

    public void MarkTimeout(DateTime utcNow)
    {
        LastTimeoutUtc = utcNow;
        ConsecutiveTimeouts++;
        ConsecutiveFailures++;
    }
}
