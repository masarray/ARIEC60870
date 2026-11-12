// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;

namespace ARIEC60870.Master.Iec101.Redundancy;

/// <summary>Tags one recoverable physical transport fault with its actual A/B owner.</summary>
internal sealed class Iec101PhysicalLinkFault : IOException
{
    public Iec101PhysicalLinkFault(string linkName, Exception innerException)
        : base($"IEC-101 physical link {linkName} failed: {innerException.Message}", innerException)
    {
        LinkName = linkName;
    }

    public string LinkName { get; }
}
