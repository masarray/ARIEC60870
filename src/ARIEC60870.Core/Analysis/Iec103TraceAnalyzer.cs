// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;

namespace ARIEC60870.Core.Analysis;

public sealed class Iec103TraceAnalyzer
{
    private readonly HexTraceExtractor _extractor = new();
    private readonly Ft12Parser _parser = new();

    public AnalysisReport AnalyzeFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Input trace file was not found.", filePath);
        }

        var text = File.ReadAllText(filePath);
        return AnalyzeText(text, Path.GetFileName(filePath));
    }

    public AnalysisReport AnalyzeText(string text, string sourceName = "inline")
    {
        var entries = _extractor.Extract(text);
        var records = entries
            .Select(entry => new DecodedTraceRecord
            {
                Entry = entry,
                Frame = _parser.Decode(entry.RawBytes)
            })
            .ToList();

        var summary = BuildSummary(text, records);
        var findings = BuildFindings(records, summary);

        return new AnalysisReport
        {
            SourceFile = sourceName,
            GeneratedAt = DateTimeOffset.Now,
            Summary = summary,
            Records = records,
            Findings = findings
        };
    }

    private static TrafficSummary BuildSummary(string text, IReadOnlyList<DecodedTraceRecord> records)
    {
        var asduTypes = records
            .Select(r => r.Frame.Asdu?.TypeId)
            .Where(x => x.HasValue)
            .GroupBy(x => x!.Value)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());

        return new TrafficSummary
        {
            TotalLines = CountLines(text),
            TotalFrames = records.Count,
            FixedFrames = records.Count(r => r.Frame.Format == Ft12FrameFormat.FixedLength),
            VariableFrames = records.Count(r => r.Frame.Format == Ft12FrameFormat.VariableLength),
            SingleCharacterFrames = records.Count(r => r.Frame.Format == Ft12FrameFormat.SingleCharacter),
            MalformedFrames = records.Count(r => r.Frame.Format == Ft12FrameFormat.Malformed),
            ChecksumErrors = records.Count(r => !r.Frame.IsChecksumValid),
            Class1Requests = records.Count(r => r.Frame.LinkControl?.IsPrimaryRequestClass1 == true),
            Class2Requests = records.Count(r => r.Frame.LinkControl?.IsPrimaryRequestClass2 == true),
            NoDataResponses = records.Count(r => r.Frame.LinkControl?.IsSecondaryNoData == true),
            ResetFcbCommands = records.Count(r => r.Frame.LinkControl?.IsPrimaryResetFcb == true),
            AckResponses = records.Count(r => r.Frame.LinkControl?.IsSecondaryAck == true),
            SecondaryUserDataFrames = records.Count(r => r.Frame.LinkControl?.IsSecondaryUserData == true),
            AsduTypeCounts = asduTypes
        };
    }

    private static IReadOnlyList<Finding> BuildFindings(IReadOnlyList<DecodedTraceRecord> records, TrafficSummary summary)
    {
        var findings = new List<Finding>();
        AddChecksumFinding(findings, records, summary);
        AddExcessiveClass1PollingFinding(findings, records);
        AddNoDataDominanceFinding(findings, records, summary);
        AddResetFcbStormFinding(findings, records, summary);
        AddGiSequenceFinding(findings, records);
        AddUnknownAsduFinding(findings, records);
        AddDpiBurstFinding(findings, records);
        return findings;
    }

    private static void AddChecksumFinding(List<Finding> findings, IReadOnlyList<DecodedTraceRecord> records, TrafficSummary summary)
    {
        if (summary.ChecksumErrors == 0) return;

        var evidence = records
            .Where(r => !r.Frame.IsChecksumValid)
            .Take(5)
            .Select(ToEvidence)
            .ToArray();

        findings.Add(new Finding
        {
            Id = "IEC103-FRAME-001",
            Severity = FindingSeverity.Error,
            Title = "Checksum or frame validation errors detected",
            Summary = $"{summary.ChecksumErrors} frame(s) failed checksum or structure validation.",
            Impact = "The capture may contain noise, dropped bytes, wrong serial settings, or malformed frames. Semantic interpretation can become unreliable.",
            Recommendation = "Verify baudrate/parity/stop bit, RS-485 wiring/turnaround timing, and capture quality before making protocol-behavior conclusions.",
            Evidence = evidence
        });
    }

    private static void AddExcessiveClass1PollingFinding(List<Finding> findings, IReadOnlyList<DecodedTraceRecord> records)
    {
        var pairs = new List<DecodedTraceRecord>();
        DecodedTraceRecord? previous = null;

        foreach (var current in records)
        {
            if (current.Frame.LinkControl?.IsPrimaryRequestClass1 == true &&
                previous?.Frame.LinkControl?.IsSecondaryNoData == true &&
                previous.Frame.LinkControl.Acd != true)
            {
                pairs.Add(current);
            }

            previous = current;
        }

        if (pairs.Count < 10) return;

        var first = pairs.First();
        var last = pairs.Last();
        var evidence = new List<FrameEvidence>();
        evidence.AddRange(pairs.Take(3).Select(ToEvidence));
        evidence.Add(ToEvidence(last));

        findings.Add(new Finding
        {
            Id = "IEC103-LINK-001",
            Severity = FindingSeverity.Warning,
            Title = "Excessive Class 1 polling while slave returns NO DATA",
            Summary = $"Detected {pairs.Count} Class 1 request(s) sent immediately after a slave NO DATA response with ACD=0. Window: {first.Entry.TimestampText} to {last.Entry.TimestampText}.",
            Impact = "The frames may be protocol-valid, but the polling policy is noisy. It wastes serial bandwidth, hides useful events inside low-value traffic, and makes troubleshooting harder.",
            Recommendation = "Use ACD-driven Class 1 event-drain. When the slave repeatedly returns NO DATA with ACD=0, back off Class 1 polling and continue normal Class 2/background scan until event data is indicated.",
            Evidence = evidence
        });
    }

    private static void AddNoDataDominanceFinding(List<Finding> findings, IReadOnlyList<DecodedTraceRecord> records, TrafficSummary summary)
    {
        if (summary.TotalFrames < 20) return;
        if (summary.NoDataRatio < 0.30) return;

        var evidence = records
            .Where(r => r.Frame.LinkControl?.IsSecondaryNoData == true)
            .Take(4)
            .Select(ToEvidence)
            .ToArray();

        findings.Add(new Finding
        {
            Id = "IEC103-LINK-002",
            Severity = FindingSeverity.Info,
            Title = "Trace dominated by NO DATA responses",
            Summary = $"NO DATA responses: {summary.NoDataResponses} of {summary.TotalFrames} frames ({summary.NoDataRatio:P1}).",
            Impact = "The link is alive, but most traffic does not carry useful application data. The capture needs behavior analysis, not only hex viewing.",
            Recommendation = "Review polling interval, Class 1/Class 2 policy, and whether the master waits for ACD before draining event traffic.",
            Evidence = evidence
        });
    }

    private static void AddResetFcbStormFinding(List<Finding> findings, IReadOnlyList<DecodedTraceRecord> records, TrafficSummary summary)
    {
        if (summary.ResetFcbCommands < 5) return;

        var evidence = records
            .Where(r => r.Frame.LinkControl?.IsPrimaryResetFcb == true)
            .Take(5)
            .Select(ToEvidence)
            .ToArray();

        findings.Add(new Finding
        {
            Id = "IEC103-LINK-003",
            Severity = FindingSeverity.Warning,
            Title = "Repeated Reset FCB commands detected",
            Summary = $"Master sent {summary.ResetFcbCommands} Reset FCB command(s).",
            Impact = "Repeated FCB reset can be normal during startup/recovery, but frequent resets in steady state may indicate link-layer sequencing, timeout, retry, or readiness problems.",
            Recommendation = "Separate startup recovery from steady-state operation. Verify timeout/retry policy and ensure Class 1 polling does not start aggressively before the relay is ready.",
            Evidence = evidence
        });
    }

    private static void AddGiSequenceFinding(List<Finding> findings, IReadOnlyList<DecodedTraceRecord> records)
    {
        var giStart = records.Where(r => r.Frame.Asdu?.TypeId == 7).ToList();
        var giEnd = records.Where(r => r.Frame.Asdu?.TypeId == 8).ToList();
        if (giStart.Count == 0 && giEnd.Count == 0) return;

        var severity = giStart.Count > 0 && giEnd.Count == 0 ? FindingSeverity.Warning : FindingSeverity.Info;
        var title = giStart.Count > 0 && giEnd.Count == 0 ? "General Interrogation started but no GI END was detected" : "General Interrogation sequence detected";
        var summary = giStart.Count > 0 && giEnd.Count > 0
            ? $"Detected {giStart.Count} GI request(s) and {giEnd.Count} GI END message(s)."
            : $"Detected {giStart.Count} GI request(s) and {giEnd.Count} GI END message(s).";

        var evidence = giStart.Concat(giEnd).Take(6).Select(ToEvidence).ToArray();

        findings.Add(new Finding
        {
            Id = "IEC103-ASDU-001",
            Severity = severity,
            Title = title,
            Summary = summary,
            Impact = severity == FindingSeverity.Warning
                ? "A missing GI END makes it difficult to prove that the relay completed its interrogation response."
                : "The analyzer can reconstruct GI lifecycle and separate useful GI response data from background polling noise.",
            Recommendation = severity == FindingSeverity.Warning
                ? "Check whether the relay sent GI termination later, whether the capture ended too early, or whether the master interrupted the interrogation sequence."
                : "Use the GI window to group DPI/measurement events and produce a cleaner commissioning report.",
            Evidence = evidence
        });
    }

    private static void AddUnknownAsduFinding(List<Finding> findings, IReadOnlyList<DecodedTraceRecord> records)
    {
        var unknown = records.Where(r => r.Frame.Asdu?.Status == DecodeStatus.Unknown).ToList();
        if (unknown.Count == 0) return;

        var typeList = unknown
            .Select(r => r.Frame.Asdu!.TypeId)
            .GroupBy(x => x)
            .Select(g => $"Type {g.Key}: {g.Count()}")
            .ToArray();

        findings.Add(new Finding
        {
            Id = "IEC103-ASDU-002",
            Severity = FindingSeverity.Warning,
            Title = "Unknown or vendor/private ASDU detected",
            Summary = $"Detected {unknown.Count} unknown/vendor-specific ASDU frame(s): {string.Join(", ", typeList)}.",
            Impact = "The frame is structurally valid, but engineering meaning depends on IEC-103 profile/vendor mapping. Without mapping, engineers still see hex without relay semantics.",
            Recommendation = "Keep raw payload, add a device mapping profile, and decode FUN/INF/object layout when reliable vendor documentation or legal test vectors are available.",
            Evidence = unknown.Take(5).Select(ToEvidence).ToArray()
        });
    }

    private static void AddDpiBurstFinding(List<Finding> findings, IReadOnlyList<DecodedTraceRecord> records)
    {
        var dpi = records.Where(r => r.Frame.Asdu?.TypeId is 1 or 2).ToList();
        if (dpi.Count == 0) return;

        findings.Add(new Finding
        {
            Id = "IEC103-ASDU-003",
            Severity = FindingSeverity.Info,
            Title = "DPI event/status messages detected",
            Summary = $"Detected {dpi.Count} DPI(TM)/DPI(RT) message(s) carrying FUN/INF, state, and timestamp context.",
            Impact = "This is useful protection/status payload. The analyzer should group these messages by GI/event window and map FUN/INF to relay-specific signal names where possible.",
            Recommendation = "Add a profile mapping file for the relay family so decoded FUN/INF values become human-readable signal labels.",
            Evidence = dpi.Take(6).Select(ToEvidence).ToArray()
        });
    }

    private static FrameEvidence ToEvidence(DecodedTraceRecord record)
    {
        var frame = record.Frame;
        var meaning = frame.ShortMeaning;
        if (frame.LinkControl is not null) meaning += $" ({frame.LinkControl.BitSummary})";
        if (frame.Asdu is not null)
        {
            meaning += $"; Type={frame.Asdu.TypeId}, COT={frame.Asdu.CauseOfTransmission}, CA={frame.Asdu.CommonAddress}";
            if (frame.Asdu.FunctionType.HasValue) meaning += $", FUN={frame.Asdu.FunctionType}";
            if (frame.Asdu.InformationNumber.HasValue) meaning += $", INF={frame.Asdu.InformationNumber}";
            if (frame.Asdu.Dpi.HasValue) meaning += $", DPI={frame.Asdu.Dpi} ({frame.Asdu.DpiText})";
            if (frame.Asdu.Time is not null) meaning += $", Time={frame.Asdu.Time}";
        }

        return new FrameEvidence
        {
            LineNumber = record.Entry.LineNumber,
            Timestamp = record.Entry.TimestampText,
            Direction = record.DirectionText,
            Label = record.Entry.Label,
            Hex = frame.Hex,
            Meaning = meaning
        };
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = 1;
        foreach (var c in text)
        {
            if (c == '\n') count++;
        }
        return count;
    }
}
