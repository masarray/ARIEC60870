// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol;

/// <summary>
/// Identifies the physical active-link command authority and a specific A/B election.
/// Reusing a link name or COM port after reconnect is a different owner.
/// </summary>
public readonly record struct Iec101OwnerStamp(
    string? LinkName, long AuthorityEpoch, int ElectionGeneration)
{
    public bool Matches(string currentName, long currentEpoch, int currentElection)
        => !string.IsNullOrEmpty(LinkName) &&
           string.Equals(LinkName, currentName, StringComparison.Ordinal) &&
           AuthorityEpoch == currentEpoch &&
           ElectionGeneration == currentElection;
}
