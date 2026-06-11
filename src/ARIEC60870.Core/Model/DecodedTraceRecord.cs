// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Model;

public sealed class DecodedTraceRecord
{
    public TraceLogEntry Entry { get; init; } = new();
    public Ft12FrameDecode Frame { get; init; } = new();

    public string DirectionText => Entry.Direction switch
    {
        FrameDirection.MasterToSlave => "Master → Slave",
        FrameDirection.SlaveToMaster => "Slave → Master",
        _ => "Unknown"
    };
}
