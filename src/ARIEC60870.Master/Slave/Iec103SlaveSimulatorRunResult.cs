// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Slave;

public sealed class Iec103SlaveSimulatorRunResult
{
    public Iec103SlaveSimulatorSettings Settings { get; init; } = new();
    public Iec103SlaveSimulatorCounters Counters { get; init; } = new();
    public IReadOnlyList<Iec103SlaveSimulatorEvent> Events { get; init; } = Array.Empty<Iec103SlaveSimulatorEvent>();
    public DateTime StartedUtc { get; init; }
    public DateTime FinishedUtc { get; init; }
    public bool CompletedNormally { get; init; }
    public string CompletionReason { get; init; } = string.Empty;
}
