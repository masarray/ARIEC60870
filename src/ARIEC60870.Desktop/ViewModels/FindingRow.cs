// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

public sealed class FindingRow
{
    public FindingRow(Iec103MasterFinding finding)
    {
        Source = finding;
        Time = finding.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
        Severity = finding.Severity.ToString();
        Id = finding.Id;
        Title = finding.Title;
        Evidence = finding.Evidence;
        Impact = finding.Impact;
        Recommendation = finding.Recommendation;
    }

    public Iec103MasterFinding Source { get; }
    public string Time { get; }
    public string Severity { get; }
    public string Id { get; }
    public string Title { get; }
    public string Evidence { get; }
    public string Impact { get; }
    public string Recommendation { get; }
}
