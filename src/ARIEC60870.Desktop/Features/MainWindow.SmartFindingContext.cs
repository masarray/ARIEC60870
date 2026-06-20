// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using System.Linq;
using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private bool ShouldSuppressFindingForOperatorStop(Iec103MasterFinding finding)
    {
        if (finding is null)
        {
            return true;
        }

        var text = SmartFindingText(finding);
        if (_operatorDisconnectInProgress)
        {
            // The operator clicked Disconnect. Do not convert expected controller
            // shutdown bookkeeping into an issue. Real faults raised before the
            // button was pressed are still allowed through.
            if (ContainsAnySmartContext(text,
                "no failover", "session completed without failover", "auto failback is enabled",
                "stopped by cancellation", "closing", "stopped", "transport was closed", "operation canceled"))
            {
                return true;
            }
        }

        // These are configuration/status notes, not issues. They can remain in
        // the session log or redundancy timeline, but they should not pollute
        // Smart Findings or the ISSUES chip.
        if (finding.Severity == FindingSeverity.Info && ContainsAnySmartContext(text,
            "no failover", "session completed without failover", "auto failback is enabled", "healthy during the run"))
        {
            return true;
        }

        return false;
    }

    private static bool IsActionableIssue(FindingRow row)
        => row is not null
           && (row.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)
               || row.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase));

    private int CountActionableIssueRows()
        => FindingRows?.Count(IsActionableIssue) ?? 0;

    private int CountActionableIssues(Iec103MasterFinding[] findings)
        => findings.Count(finding => finding.Severity is FindingSeverity.Error or FindingSeverity.Warning
                                    && !ShouldSuppressFindingForOperatorStop(finding));

    private void UpdateFindingCountChip()
    {
        if (FindingCountText is null)
        {
            return;
        }

        FindingCountText.Text = CountActionableIssueRows().ToString(CultureInfo.InvariantCulture);
    }

    private static string SmartFindingText(Iec103MasterFinding finding)
        => string.Join(" ", finding.Id, finding.Title, finding.Evidence, finding.Impact, finding.Recommendation);

    private static bool ContainsAnySmartContext(string text, params string[] needles)
        => needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
