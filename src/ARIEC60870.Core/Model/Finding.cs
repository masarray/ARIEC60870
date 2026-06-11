// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Model;

public sealed class Finding
{
    public string Id { get; init; } = string.Empty;
    public FindingSeverity Severity { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Impact { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public IReadOnlyList<FrameEvidence> Evidence { get; init; } = Array.Empty<FrameEvidence>();
}

public sealed class FrameEvidence
{
    public int LineNumber { get; init; }
    public string Timestamp { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Hex { get; init; } = string.Empty;
    public string Meaning { get; init; } = string.Empty;
}
