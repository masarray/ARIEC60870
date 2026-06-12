// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

public sealed class CaptureFrameSnapshot
{
    public long Sequence { get; set; }
    public string Time { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string ProtocolName { get; set; } = string.Empty;
    public string ProtocolMode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DataClass { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string SignalOrAddress { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string AsduType { get; set; } = string.Empty;
    public string TypeId { get; set; } = string.Empty;
    public string Cot { get; set; } = string.Empty;
    public string CotCode { get; set; } = string.Empty;
    public string LinkAddress { get; set; } = string.Empty;
    public string CommonAddress { get; set; } = string.Empty;
    public string Ioa { get; set; } = string.Empty;
    public string Acd { get; set; } = string.Empty;
    public string Dfc { get; set; } = string.Empty;
    public string RelayTime { get; set; } = string.Empty;
    public string ResponseTime { get; set; } = string.Empty;
    public string Meaning { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string RawHex { get; set; } = string.Empty;
    public string ProtocolTraceTitle { get; set; } = string.Empty;
    public string ProtocolTraceMeaning { get; set; } = string.Empty;
    public string ProtocolTraceRaw { get; set; } = string.Empty;
    public string ProtocolTraceMeta { get; set; } = string.Empty;
}
