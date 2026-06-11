// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;

namespace ARIEC60870.Master.Model;

public sealed class Iec103MasterFinding
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public FindingSeverity Severity { get; init; } = FindingSeverity.Info;
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public string Impact { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
}
