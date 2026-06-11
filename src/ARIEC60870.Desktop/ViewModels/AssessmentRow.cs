// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

public sealed class AssessmentRow
{
    public AssessmentRow(Iec103AssessmentItem item)
    {
        Area = item.Area;
        Status = item.Status.ToString();
        Title = item.Title;
        Evidence = item.Evidence;
        Recommendation = item.Recommendation;
    }

    public string Area { get; }
    public string Status { get; }
    public string Title { get; }
    public string Evidence { get; }
    public string Recommendation { get; }
}
