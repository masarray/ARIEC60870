// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Model;

public enum Iec103AssessmentStatus
{
    Pass,
    Warning,
    Fail,
    Info
}

public sealed class Iec103MasterAssessment
{
    public Iec103AssessmentStatus OverallStatus { get; init; } = Iec103AssessmentStatus.Info;
    public int Score { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<Iec103AssessmentItem> Items { get; init; } = Array.Empty<Iec103AssessmentItem>();
}

public sealed class Iec103AssessmentItem
{
    public string Area { get; init; } = string.Empty;
    public Iec103AssessmentStatus Status { get; init; } = Iec103AssessmentStatus.Info;
    public string Title { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
}
