// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Analysis;
using ARIEC60870.Core.Reporting;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Reporting;
using Xunit;

namespace ARIEC60870.Reporting.Tests;

public sealed class MarkdownReportRegressionTests
{
    [Fact]
    public void OfflineAnalyzerReportContainsStablePublicSections()
    {
        var root = TestRepository.FindRoot();
        var sampleTrace = File.ReadAllText(Path.Combine(root.FullName, "samples", "sample_iec103_trace.log"));
        var report = new Iec103TraceAnalyzer().AnalyzeText(sampleTrace, "sample_iec103_trace.log");

        var markdown = new MarkdownReportWriter().Write(report);

        Assert.Contains("# ARIEC60870 Analysis Report", markdown, StringComparison.Ordinal);
        Assert.Contains("## Traffic Summary", markdown, StringComparison.Ordinal);
        Assert.Contains("## Engineering Findings", markdown, StringComparison.Ordinal);
        Assert.Contains("## Decoded Traffic Preview", markdown, StringComparison.Ordinal);
        Assert.Contains("Total frames", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterReportUsesSanitizedSettingsSnapshotForPublicEvidence()
    {
        var settings = new Iec103MasterSettings
        {
            MappingProfilePath = @"C:\Customer\Secret Project\profiles\bay-a.json",
            IncludeLocalPathsInReports = false
        }.CreateReportSnapshot();

        var result = new Iec103MasterRunResult
        {
            Settings = settings,
            Counters = new Iec103MasterCounters { TxFrames = 1, RxFrames = 1 },
            Assessment = new Iec103MasterAssessment { OverallStatus = Iec103AssessmentStatus.Info, Summary = "Sanitized evidence snapshot." },
            CompletedNormally = true,
            CompletionReason = "Unit test"
        };

        var markdown = new MasterMarkdownReportWriter().Write(result);

        Assert.Contains("bay-a.json", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret Project", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Customer", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MasterReportEscapesMarkdownTablePipesInValueViewerAndEventLog()
    {
        var result = new Iec103MasterRunResult
        {
            Settings = Iec103MasterSettings.CreateDefault(),
            Counters = new Iec103MasterCounters { TxFrames = 1, RxFrames = 1 },
            Assessment = new Iec103MasterAssessment { OverallStatus = Iec103AssessmentStatus.Info, Summary = "Escaping test." },
            ValuePoints = new[]
            {
                new Iec103ValuePoint
                {
                    SignalName = "CB|Feeder A",
                    DisplayValue = "OPEN|TRIP",
                    SignalGroup = "Bay|A",
                    RawHex = "68 00"
                }
            },
            EventLog = new[]
            {
                new Iec103RelayEventLogEntry
                {
                    EvidenceSequenceNumber = 1,
                    SignalName = "Protection|Trip",
                    PreviousValue = "Normal|Healthy",
                    NewValue = "Trip|Operate",
                    RawHex = "68 01"
                }
            }
        };

        var markdown = new MasterMarkdownReportWriter().Write(result);

        Assert.Contains("CB\\|Feeder A", markdown, StringComparison.Ordinal);
        Assert.Contains("OPEN\\|TRIP", markdown, StringComparison.Ordinal);
        Assert.Contains("Protection\\|Trip", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| CB|Feeder A |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterReportStatesWhenEvidenceTraceIsRowLimited()
    {
        var result = new Iec103MasterRunResult
        {
            Settings = Iec103MasterSettings.CreateDefault(),
            Counters = new Iec103MasterCounters { TxFrames = 5, RxFrames = 5 },
            Assessment = new Iec103MasterAssessment { OverallStatus = Iec103AssessmentStatus.Info, Summary = "Limit test." },
            Events = Enumerable.Range(1, 5)
                .Select(i => new Iec103MasterEvidenceEvent
                {
                    SequenceNumber = i,
                    Category = "Info",
                    Summary = "Evidence row " + i,
                    RawHex = "10 09 01 0A 16"
                })
                .ToArray()
        };

        var markdown = new MasterMarkdownReportWriter().Write(result, maxEvents: 2);

        Assert.Contains("Trace table limited to 2 retained events", markdown, StringComparison.Ordinal);
        Assert.Contains("Evidence row 1", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Evidence row 5 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterReportDiagnosticAppendixKeepsExceptionTypeVisible()
    {
        var result = new Iec103MasterRunResult
        {
            Settings = Iec103MasterSettings.CreateDefault(),
            Counters = new Iec103MasterCounters { TxFrames = 1, RxFrames = 0, TransportExceptions = 1 },
            Assessment = new Iec103MasterAssessment { OverallStatus = Iec103AssessmentStatus.Warning, Summary = "Diagnostic test." },
            Events = new[]
            {
                new Iec103MasterEvidenceEvent
                {
                    SequenceNumber = 1,
                    Category = "Diagnostic Warning",
                    Summary = "Timeout while waiting for relay",
                    Detail = "No frame before response timeout.",
                    ExceptionType = "TimeoutException",
                    ExceptionMessage = "Synthetic timeout"
                }
            }
        };

        var markdown = new MasterMarkdownReportWriter().Write(result);

        Assert.Contains("## Diagnostics appendix", markdown, StringComparison.Ordinal);
        Assert.Contains("TimeoutException", markdown, StringComparison.Ordinal);
        Assert.Contains("Timeout while waiting for relay", markdown, StringComparison.Ordinal);
    }
}

file static class TestRepository
{
    public static DirectoryInfo FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ARIEC60870.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            throw new InvalidOperationException("Repository root could not be located from test output directory.");
        }

        return current;
    }
}
