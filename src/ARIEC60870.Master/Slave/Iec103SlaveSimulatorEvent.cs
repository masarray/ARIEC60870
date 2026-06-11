// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Slave;

public sealed class Iec103SlaveSimulatorEvent
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string Direction { get; init; } = "STATE";
    public string DataClass { get; init; } = "-";
    public string Summary { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string RawHex { get; init; } = string.Empty;
}
