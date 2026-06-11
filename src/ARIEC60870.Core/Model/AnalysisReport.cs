// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Model;

public sealed class AnalysisReport
{
    public string SourceFile { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;
    public TrafficSummary Summary { get; init; } = new();
    public IReadOnlyList<DecodedTraceRecord> Records { get; init; } = Array.Empty<DecodedTraceRecord>();
    public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();
}
