// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Model;

public sealed class TraceLogEntry
{
    public int LineNumber { get; init; }
    public string SourceLine { get; init; } = string.Empty;
    public TimeSpan? Timestamp { get; init; }
    public string TimestampText { get; init; } = string.Empty;
    public string PortName { get; init; } = string.Empty;
    public FrameDirection Direction { get; init; } = FrameDirection.Unknown;
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<byte> RawBytes { get; init; } = Array.Empty<byte>();
}
