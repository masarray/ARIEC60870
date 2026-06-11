// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Slave;

public sealed class Iec103SlaveSimulatorCounters
{
    public int RxFrames { get; internal set; }
    public int TxFrames { get; internal set; }
    public int Class1Requests { get; internal set; }
    public int Class2Requests { get; internal set; }
    public int ResetFcbRequests { get; internal set; }
    public int ResetLinkRequests { get; internal set; }
    public int GiRequests { get; internal set; }
    public int ClockSyncRequests { get; internal set; }
    public int AckResponses { get; internal set; }
    public int NoDataResponses { get; internal set; }
    public int UserDataResponses { get; internal set; }
    public int DfcBusyResponses { get; internal set; }
    public int BadChecksumResponses { get; internal set; }
    public int MalformedRxFrames { get; internal set; }
    public int UnknownRequests { get; internal set; }
    public int Class1QueueMaxDepth { get; internal set; }
    public int ProtectionCycles { get; internal set; }
    public int PickupEvents { get; internal set; }
    public int TripEvents { get; internal set; }
    public int AutoResets { get; internal set; }
    public int CommandResets { get; internal set; }
    public int CurrentFrames { get; internal set; }
}

