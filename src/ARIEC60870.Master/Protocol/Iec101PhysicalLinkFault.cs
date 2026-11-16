// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;

namespace ARIEC60870.Master.Protocol;

/// <summary>
/// Distinguishes exceptions at a physical COM Open/Write/Read boundary from
/// ordinary FT1.2 timeout, DFC busy, and application/scheduler failures.
/// Any byte delivery for an interrupted TX is unknown and must not be replayed.
/// </summary>
internal sealed class Iec101PhysicalLinkFault : IOException
{
    public Iec101PhysicalLinkFault(string physicalSource, Exception cause)
        : base($"IEC-101 physical COM {physicalSource} failed: {cause.Message}", cause)
    {
        PhysicalSource = physicalSource;
    }

    public string PhysicalSource { get; }
}
