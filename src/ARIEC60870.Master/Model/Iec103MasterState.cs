// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Model;

public enum Iec103MasterState
{
    Created = 0,
    OpeningTransport = 1,
    Connected = 2,
    StartupDelay = 3,
    ResetRemoteLink = 4,
    ResetFcb = 5,
    ClockSynchronization = 6,
    GeneralInterrogation = 7,
    GiFollowUpDrain = 8,
    NormalClass2Polling = 9,
    Class1EventDrain = 10,
    BusyBackoff = 11,
    TimeoutRecovery = 12,
    Stopping = 13,
    Stopped = 14,
    Faulted = 15
}
