// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Iec101.Redundancy;

public sealed class Iec101FailoverJournalEntry
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string FromLink { get; init; } = string.Empty;
    public string ToLink { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public int LatencyMs { get; init; }
    public bool Completed { get; init; }
    public string Detail { get; init; } = string.Empty;
}
