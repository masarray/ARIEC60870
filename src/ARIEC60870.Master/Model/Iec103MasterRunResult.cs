// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Model;

public sealed class Iec103MasterRunResult
{
    public string ProductName { get; init; } = "ARIEC60870";
    public string ProductMode { get; init; } = "IEC 60870-5-103 Single Connection Master Tester";
    public Iec103MasterSettings Settings { get; init; } = Iec103MasterSettings.CreateDefault();
    public Iec103MasterCounters Counters { get; init; } = new();
    public IReadOnlyList<Iec103MasterEvidenceEvent> Events { get; init; } = Array.Empty<Iec103MasterEvidenceEvent>();
    public IReadOnlyList<Iec103MasterFinding> Findings { get; init; } = Array.Empty<Iec103MasterFinding>();
    public IReadOnlyList<Iec103ValuePoint> ValuePoints { get; init; } = Array.Empty<Iec103ValuePoint>();
    public IReadOnlyList<Iec103RelayEventLogEntry> EventLog { get; init; } = Array.Empty<Iec103RelayEventLogEntry>();
    public Iec103MasterAssessment Assessment { get; init; } = new();
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;
    public DateTime FinishedUtc { get; init; } = DateTime.UtcNow;
    public TimeSpan Duration => FinishedUtc - StartedUtc;
    public bool CompletedNormally { get; init; }
    public string CompletionReason { get; init; } = string.Empty;
}
