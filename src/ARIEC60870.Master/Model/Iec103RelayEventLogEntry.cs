// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Model;

/// <summary>
/// Relay event log entry. The visible event time is the relay timestamp carried
/// inside IEC-103 ASDU time fields, not the PC arrival time. Arrival time is kept
/// only as forensic metadata.
/// </summary>
public sealed class Iec103RelayEventLogEntry
{
    public long EvidenceSequenceNumber { get; init; }
    public string RelayTimeText { get; init; } = string.Empty;
    public bool RelayTimeInvalid { get; init; }
    public DateTime ArrivalTimeUtc { get; init; } = DateTime.UtcNow;
    public bool IsMapped { get; init; }
    public string SignalName { get; init; } = string.Empty;
    public string SignalGroup { get; init; } = "Unmapped";
    public string SignalType { get; init; } = string.Empty;
    public int? FunctionType { get; init; }
    public int? InformationNumber { get; init; }
    public string PreviousValue { get; init; } = string.Empty;
    public string NewValue { get; init; } = string.Empty;
    public string EdgeReason { get; init; } = string.Empty;
    public string CauseOfTransmission { get; init; } = string.Empty;
    public string AsduType { get; init; } = string.Empty;
    public string RawHex { get; init; } = string.Empty;
    public Iec60870ProtocolMode ProtocolMode { get; init; } = Iec60870ProtocolMode.Iec103;
    public int? CommonAddress { get; init; }
    public int? InformationObjectAddress { get; init; }
    public int? TypeId { get; init; }
    public string QualityText { get; init; } = string.Empty;
}
