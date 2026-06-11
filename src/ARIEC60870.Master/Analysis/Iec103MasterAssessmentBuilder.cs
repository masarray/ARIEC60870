// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Master.Analysis;

public static class Iec103MasterAssessmentBuilder
{
    public static Iec103MasterAssessment Build(
        Iec103MasterSettings settings,
        Iec103MasterCounters counters,
        IReadOnlyList<Iec103MasterEvidenceEvent> events,
        IReadOnlyList<Iec103MasterFinding> findings,
        IReadOnlyList<Iec103ValuePoint> valuePoints,
        IReadOnlyList<Iec103RelayEventLogEntry> eventLog,
        bool completedNormally)
    {
        var items = new List<Iec103AssessmentItem>();

        items.Add(BuildCompletionItem(completedNormally));
        items.Add(BuildCommunicationActivityItem(counters));
        items.Add(BuildFrameQualityItem(counters));
        items.Add(BuildStartupGiItem(settings, counters));
        items.Add(BuildPollingPolicyItem(counters));
        items.Add(BuildTimeoutItem(settings, counters));
        items.Add(BuildValueAcquisitionItem(counters, valuePoints));
        items.Add(BuildEventLogTimestampItem(eventLog));
        items.Add(BuildMappingCoverageItem(settings, valuePoints));
        items.Add(BuildFindingItem(findings));

        var overall = items.Any(x => x.Status == Iec103AssessmentStatus.Fail)
            ? Iec103AssessmentStatus.Fail
            : items.Any(x => x.Status == Iec103AssessmentStatus.Warning)
                ? Iec103AssessmentStatus.Warning
                : Iec103AssessmentStatus.Pass;

        var scoreItems = items.Where(x => x.Status != Iec103AssessmentStatus.Info).ToArray();
        var score = scoreItems.Length == 0
            ? 0
            : (int)Math.Round(scoreItems.Average(x => x.Status switch
            {
                Iec103AssessmentStatus.Pass => 100,
                Iec103AssessmentStatus.Warning => 60,
                Iec103AssessmentStatus.Fail => 0,
                _ => 0
            }));

        var summary = overall switch
        {
            Iec103AssessmentStatus.Pass => "Session behavior is healthy for a basic IEC-103 master test.",
            Iec103AssessmentStatus.Warning => "Session completed with warnings. Review evidence before accepting the test.",
            Iec103AssessmentStatus.Fail => "Session contains failure conditions. Do not accept the test without correction.",
            _ => "Session assessment is informational."
        };

        return new Iec103MasterAssessment
        {
            OverallStatus = overall,
            Score = score,
            Summary = summary,
            Items = items
        };
    }

    private static Iec103AssessmentItem BuildCompletionItem(bool completedNormally)
    {
        return completedNormally
            ? Pass("Session", "Session completed without master fault", "The master session reached a controlled stop/completion state.", "No action required.")
            : Fail("Session", "Session faulted", "The master session ended with a fault condition.", "Check serial settings, relay address, wiring, converter, and exception details in the evidence trace.");
    }

    private static Iec103AssessmentItem BuildCommunicationActivityItem(Iec103MasterCounters c)
    {
        if (c.TxFrames == 0)
        {
            return Fail("Communication", "No master transmit frame", "TX frames=0.", "Start a master test and verify that the transport can open/write.");
        }

        if (c.RxFrames == 0)
        {
            return Fail("Communication", "No relay response received", $"TX={c.TxFrames}, RX=0.", "Verify COM port, baudrate/parity, link address, relay IEC-103 mode, RS485 polarity, and converter direction control.");
        }

        return Pass("Communication", "Bidirectional communication observed", $"TX={c.TxFrames}, RX={c.RxFrames}.", "No action required.");
    }

    private static Iec103AssessmentItem BuildFrameQualityItem(Iec103MasterCounters c)
    {
        if (c.ChecksumErrors > 0 || c.MalformedFrames > 0)
        {
            return Fail("Frame quality", "Invalid frame quality detected", $"Checksum errors={c.ChecksumErrors}, malformed frames={c.MalformedFrames}.", "Check serial parameters, wiring, converter quality, termination, grounding, and line noise.");
        }

        return Pass("Frame quality", "All received frames passed basic FT1.2 validation", $"Checksum errors={c.ChecksumErrors}, malformed frames={c.MalformedFrames}.", "No action required.");
    }

    private static Iec103AssessmentItem BuildStartupGiItem(Iec103MasterSettings settings, Iec103MasterCounters c)
    {
        if (!settings.SendGeneralInterrogationOnConnect)
        {
            return Info("GI", "General Interrogation disabled", "GI on connect was disabled by user settings.", "Enable GI for FAT/SAT baseline value acquisition when appropriate.");
        }

        if (c.GiCommands == 0)
        {
            return Warning("GI", "GI command was not sent", "GI on connect is enabled but GI command counter is zero.", "Review startup sequence and evidence trace.");
        }

        if (c.GiEndResponses == 0)
        {
            return Warning("GI", "GI END was not observed", $"GI commands={c.GiCommands}, GI END responses={c.GiEndResponses}.", "Increase test duration, check Common Address, and inspect Class 1 follow-up evidence after GI.");
        }

        return Pass("GI", "General Interrogation completed", $"GI commands={c.GiCommands}, GI END responses={c.GiEndResponses}.", "No action required.");
    }

    private static Iec103AssessmentItem BuildPollingPolicyItem(Iec103MasterCounters c)
    {
        if (c.Class1DrainLimitReached > 0)
        {
            return Warning("Polling", "Class 1 drain limit reached", $"Drain limit reached={c.Class1DrainLimitReached}, Class 1 requests={c.Class1Requests}.", "Verify whether relay event queue is large, ACD is stuck, or MaxClass1DrainFrames is too low for the test.");
        }

        if (c.Class1Requests > 0 && c.Class2Requests == 0 && c.NoDataResponses > c.UserDataResponses)
        {
            return Warning("Polling", "Class 1 activity without Class 2 background cycle", $"Class 1={c.Class1Requests}, Class 2={c.Class2Requests}, NO DATA={c.NoDataResponses}.", "Normal running should use Class 2/background polling and only drain Class 1 when ACD=1 or during bounded GI follow-up.");
        }

        if (c.Class2Requests > 0)
        {
            return Pass("Polling", "SCADA-style polling pattern observed", $"Class 2 normal polls={c.Class2Requests}, Class 1 requests={c.Class1Requests}, Class 1 drain bursts={c.Class1DrainBursts}.", "No action required.");
        }

        return Info("Polling", "No normal Class 2 cycle observed", $"Class 2 requests={c.Class2Requests}.", "For longer FAT/SAT tests, run beyond startup/GI so Class 2 normal polling is visible.");
    }

    private static Iec103AssessmentItem BuildTimeoutItem(Iec103MasterSettings settings, Iec103MasterCounters c)
    {
        if (c.Timeouts == 0)
        {
            return Pass("Timing", "No response timeout", $"Timeouts=0, average response={c.AverageResponseTimeMs:F1} ms, max response={c.MaxResponseTimeMs} ms.", "No action required.");
        }

        if (c.MaxConsecutiveTimeouts >= settings.MaxConsecutiveTimeoutsBeforeResetFcb)
        {
            return Fail("Timing", "Timeout burst exceeded recovery threshold", $"Timeouts={c.Timeouts}, max consecutive={c.MaxConsecutiveTimeouts}, threshold={settings.MaxConsecutiveTimeoutsBeforeResetFcb}.", "Check relay availability, link address, serial settings, RS485 converter direction control, and wiring.");
        }

        return Warning("Timing", "Response timeout observed", $"Timeouts={c.Timeouts}, max consecutive={c.MaxConsecutiveTimeouts}.", "Review timing evidence and consider increasing timeout only after confirming serial settings are correct.");
    }

    private static Iec103AssessmentItem BuildValueAcquisitionItem(Iec103MasterCounters c, IReadOnlyList<Iec103ValuePoint> valuePoints)
    {
        if (valuePoints.Count > 0)
        {
            return Pass("Values", "Relay value/status data acquired", $"Value points={valuePoints.Count}, DPI events={c.DpiEvents}.", "No action required.");
        }

        if (c.RxFrames > 0)
        {
            return Warning("Values", "No value/status point was decoded", $"RX={c.RxFrames}, user data={c.UserDataResponses}, DPI events={c.DpiEvents}.", "Verify GI response, Class 1 drain, supported ASDU types, and relay IEC-103 configuration.");
        }

        return Info("Values", "No value/status data", "No relay data was available because no RX frame was received.", "Resolve communication first.");
    }

    private static Iec103AssessmentItem BuildEventLogTimestampItem(IReadOnlyList<Iec103RelayEventLogEntry> eventLog)
    {
        if (eventLog.Count == 0)
        {
            return Info("Event log", "No relay edge event recorded", "Event log contains no state-change or spontaneous edge event. This can be normal for a quiet relay.", "Use GI/Value Viewer for current status; trigger a known relay event if SOE/event validation is required.");
        }

        var missingOrInvalid = eventLog.Count(x => x.RelayTimeInvalid || string.IsNullOrWhiteSpace(x.RelayTimeText) || x.RelayTimeText.Contains("No relay timestamp", StringComparison.OrdinalIgnoreCase));
        if (missingOrInvalid > 0)
        {
            return Warning("Event log", "Relay event timestamp incomplete or invalid", $"Event log entries={eventLog.Count}, missing/invalid relay timestamps={missingOrInvalid}.", "Verify relay clock, clock sync policy, and ASDU time validity before using event log as SOE evidence.");
        }

        return Pass("Event log", "Relay-timestamped edge events recorded", $"Event log entries={eventLog.Count}; all entries have valid relay timestamp text.", "No action required.");
    }

    private static Iec103AssessmentItem BuildMappingCoverageItem(Iec103MasterSettings settings, IReadOnlyList<Iec103ValuePoint> valuePoints)
    {
        if (string.IsNullOrWhiteSpace(settings.MappingProfilePath))
        {
            return Info("Mapping", "No user mapping profile loaded", "Signal names use raw FUN/INF fallback.", "Load a project mapping profile when final FAT/SAT reports require human-readable signal names.");
        }

        if (valuePoints.Count == 0)
        {
            return Info("Mapping", "Mapping profile loaded but no values were decoded", $"Profile file={settings.MappingProfilePath}.", "Validate mapping coverage after value/status data is received.");
        }

        var mapped = valuePoints.Count(x => x.IsMapped);
        var coverage = (double)mapped / valuePoints.Count;
        if (coverage >= 0.80)
        {
            return Pass("Mapping", "Good user mapping coverage", $"Mapped value points={mapped}/{valuePoints.Count} ({coverage:P0}).", "No action required.");
        }

        return Warning("Mapping", "Low user mapping coverage", $"Mapped value points={mapped}/{valuePoints.Count} ({coverage:P0}).", "Update the project mapping profile so Value Viewer, Event Log, and report show approved signal names instead of raw FUN/INF fallback.");
    }

    private static Iec103AssessmentItem BuildFindingItem(IReadOnlyList<Iec103MasterFinding> findings)
    {
        var errors = findings.Count(x => x.Severity == FindingSeverity.Error);
        var warnings = findings.Count(x => x.Severity == FindingSeverity.Warning);

        if (errors > 0)
        {
            return Fail("Findings", "Error finding raised", $"Error findings={errors}, warning findings={warnings}.", "Review the Findings tab and correct all error findings before accepting the session.");
        }

        if (warnings > 0)
        {
            return Warning("Findings", "Warning finding raised", $"Warning findings={warnings}.", "Review all warning findings and decide whether they are acceptable for this test case.");
        }

        return Pass("Findings", "No error/warning finding", "No Error or Warning finding was raised.", "No action required.");
    }

    private static Iec103AssessmentItem Pass(string area, string title, string evidence, string recommendation) =>
        new() { Area = area, Status = Iec103AssessmentStatus.Pass, Title = title, Evidence = evidence, Recommendation = recommendation };

    private static Iec103AssessmentItem Warning(string area, string title, string evidence, string recommendation) =>
        new() { Area = area, Status = Iec103AssessmentStatus.Warning, Title = title, Evidence = evidence, Recommendation = recommendation };

    private static Iec103AssessmentItem Fail(string area, string title, string evidence, string recommendation) =>
        new() { Area = area, Status = Iec103AssessmentStatus.Fail, Title = title, Evidence = evidence, Recommendation = recommendation };

    private static Iec103AssessmentItem Info(string area, string title, string evidence, string recommendation) =>
        new() { Area = area, Status = Iec103AssessmentStatus.Info, Title = title, Evidence = evidence, Recommendation = recommendation };
}
