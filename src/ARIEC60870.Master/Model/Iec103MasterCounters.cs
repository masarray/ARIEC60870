// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Model;

public sealed class Iec103MasterCounters
{
    public int TxFrames { get; set; }
    public int RxFrames { get; set; }
    public int Class1Requests { get; set; }
    public int Class2Requests { get; set; }
    public int NoDataResponses { get; set; }
    public int AckResponses { get; set; }
    public int NackResponses { get; set; }
    public int UserDataResponses { get; set; }
    public int ResetRemoteLinkCommands { get; set; }
    public int ResetFcbCommands { get; set; }
    public int GiCommands { get; set; }
    public int GiEndResponses { get; set; }
    public int ClockSyncCommands { get; set; }
    public int Timeouts { get; set; }
    public int TimeoutRecoveries { get; set; }
    public int TransportExceptions { get; set; }
    public int BusyResponses { get; set; }
    public int ChecksumErrors { get; set; }
    public int MalformedFrames { get; set; }
    public int Class1DrainBursts { get; set; }
    public int Class1DrainFrames { get; set; }
    public int Class1DrainStoppedByNoData { get; set; }
    public int Class1DrainStoppedByAcdClear { get; set; }
    public int Class1DrainLimitReached { get; set; }
    public int DpiEvents { get; set; }
    public int IdentificationResponses { get; set; }
    public int UnknownAsduResponses { get; set; }
    public int ConsecutiveTimeouts { get; set; }
    public int MaxConsecutiveTimeouts { get; set; }
    public long TotalResponseTimeMs { get; set; }
    public int TimedResponses { get; set; }
    public int MaxResponseTimeMs { get; set; }

    public long EvidenceEventsDroppedFromMemory { get; set; }
    public long RelayEventsDroppedFromMemory { get; set; }
    public long FindingsDroppedFromMemory { get; set; }

    public double AverageResponseTimeMs => TimedResponses == 0 ? 0 : (double)TotalResponseTimeMs / TimedResponses;
}
