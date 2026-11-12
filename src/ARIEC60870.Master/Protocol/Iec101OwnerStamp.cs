// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol;

/// <summary>
/// An operator intent belongs to one exact active-link election and COM epoch;
/// returning to the same visible A/B name never resurrects old controls.
/// </summary>
public readonly record struct Iec101OwnerStamp(string? PhysicalSource, long LinkEpoch, int ActiveGeneration)
{
    public bool Matches(string physicalSource, long linkEpoch, int activeGeneration)
        => string.Equals(PhysicalSource, physicalSource, StringComparison.Ordinal)
           && LinkEpoch == linkEpoch
           && ActiveGeneration == activeGeneration;
}
