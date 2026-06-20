// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Master.Analysis;

/// <summary>
/// Learns safe IEC-101/104 interoperability settings from captured protocol evidence.
/// The corrector is intentionally field-focused: it only suggests configuration fields
/// that can be validated from frame structure and decoded ASDU consistency.
/// </summary>
public static class Iec10xAutoConfigCorrector
{
    private const int MaxSamples = 240;

    public static Iec10xAutoConfigResult Analyze(
        Iec103MasterSettings settings,
        IReadOnlyList<Iec103MasterEvidenceEvent> events,
        IReadOnlyList<Iec103MasterFinding>? findings = null)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        events ??= Array.Empty<Iec103MasterEvidenceEvent>();
        var samples = events
            .Where(item => !string.IsNullOrWhiteSpace(item.RawHex))
            .Select(item => new Iec10xAutoConfigFrameSample(
                item.ProtocolMode,
                item.Direction,
                item.RawHex,
                item.SequenceNumber,
                item.ResponseTimeMs,
                string.Join(" ", item.Summary, item.Detail, item.OperatorMessage, item.ProtocolMeaning, item.OperatorAction)))
            .ToArray();

        return Analyze(settings, samples, findings);
    }

    public static Iec10xAutoConfigResult Analyze(
        Iec103MasterSettings settings,
        IReadOnlyList<Iec10xAutoConfigFrameSample> samples,
        IReadOnlyList<Iec103MasterFinding>? findings = null)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        samples ??= Array.Empty<Iec10xAutoConfigFrameSample>();
        findings ??= Array.Empty<Iec103MasterFinding>();

        var corrected = CloneSettings(settings);
        if (settings.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104))
        {
            return new Iec10xAutoConfigResult
            {
                CorrectedSettings = corrected,
                Suggestions = Array.Empty<Iec10xAutoConfigSuggestion>(),
                Summary = "Auto config correction is only available for IEC-101/104 addressing/profile fields."
            };
        }

        var usableSamples = samples
            .Where(sample => sample.ProtocolMode == settings.ProtocolMode)
            .Where(sample => sample.Direction == FrameDirection.SlaveToMaster || sample.Direction == FrameDirection.Unknown)
            .Where(sample => !string.IsNullOrWhiteSpace(sample.RawHex))
            .Take(MaxSamples)
            .ToArray();

        if (usableSamples.Length == 0)
        {
            return new Iec10xAutoConfigResult
            {
                CorrectedSettings = corrected,
                Suggestions = Array.Empty<Iec10xAutoConfigSuggestion>(),
                Summary = "No RX frame evidence is available for auto configuration correction."
            };
        }

        var current = Evaluate(settings.ProtocolMode, usableSamples, settings.LinkAddressSize, settings.CauseOfTransmissionSize, settings.CommonAddressSize, settings.InformationObjectAddressSize);
        var candidates = BuildCandidates(settings.ProtocolMode)
            .Select(candidate => Evaluate(settings.ProtocolMode, usableSamples, candidate.LinkAddressSize, candidate.CotSize, candidate.CaSize, candidate.IoaSize))
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.AsduCount)
            .ThenBy(candidate => Math.Abs(candidate.LinkAddressSize - settings.LinkAddressSize)
                + Math.Abs(candidate.CotSize - settings.CauseOfTransmissionSize)
                + Math.Abs(candidate.CaSize - settings.CommonAddressSize)
                + Math.Abs(candidate.IoaSize - settings.InformationObjectAddressSize))
            .ToArray();

        var best = candidates.FirstOrDefault() ?? current;
        var hasProfileFinding = HasProfileSizeSymptom(findings) || current.IssueCount > best.IssueCount || current.Score <= 0;
        var profileReliable = IsProfileCandidateReliable(current, best, hasProfileFinding);
        var suggestions = new List<Iec10xAutoConfigSuggestion>();

        if (profileReliable)
        {
            AddProfileSuggestion(suggestions, corrected, nameof(Iec103MasterSettings.LinkAddressSize), "Link address size", settings.LinkAddressSize, best.LinkAddressSize, best, current, settings.ProtocolMode == Iec60870ProtocolMode.Iec101);
            AddProfileSuggestion(suggestions, corrected, nameof(Iec103MasterSettings.CauseOfTransmissionSize), "COT size", settings.CauseOfTransmissionSize, best.CotSize, best, current, true);
            AddProfileSuggestion(suggestions, corrected, nameof(Iec103MasterSettings.CommonAddressSize), "CA size", settings.CommonAddressSize, best.CaSize, best, current, true);
            AddProfileSuggestion(suggestions, corrected, nameof(Iec103MasterSettings.InformationObjectAddressSize), "IOA size", settings.InformationObjectAddressSize, best.IoaSize, best, current, true);
        }

        var addressStats = profileReliable ? best : current;
        AddObservedAddressSuggestions(settings, corrected, suggestions, addressStats, current, profileReliable || HasAddressFinding(findings));
        AddTimingSuggestion(settings, corrected, suggestions, findings);

        var ordered = suggestions
            .GroupBy(item => item.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => ConfidenceRank(item.Confidence)).ThenBy(item => item.Priority).First())
            .OrderBy(item => item.Priority)
            .ToArray();

        return new Iec10xAutoConfigResult
        {
            CorrectedSettings = corrected,
            Suggestions = ordered,
            Summary = ordered.Length == 0
                ? $"No safe auto-correctable config field was proven. Current score={current.Score}, best score={best.Score}."
                : $"{ordered.Length} auto-correctable config field(s) found. Current score={current.Score}, best score={best.Score}."
        };
    }

    private static IEnumerable<ProfileCandidate> BuildCandidates(Iec60870ProtocolMode mode)
    {
        var linkSizes = mode == Iec60870ProtocolMode.Iec101 ? new[] { 1, 2 } : new[] { 1 };
        foreach (var linkSize in linkSizes)
        {
            for (var cot = 1; cot <= 2; cot++)
            {
                for (var ca = 1; ca <= 2; ca++)
                {
                    for (var ioa = 1; ioa <= 3; ioa++)
                    {
                        yield return new ProfileCandidate(linkSize, cot, ca, ioa);
                    }
                }
            }
        }
    }

    private static CandidateStats Evaluate(Iec60870ProtocolMode mode, IReadOnlyList<Iec10xAutoConfigFrameSample> samples, int linkSize, int cotSize, int caSize, int ioaSize)
    {
        linkSize = mode == Iec60870ProtocolMode.Iec101 ? Math.Clamp(linkSize, 1, 2) : 1;
        cotSize = Math.Clamp(cotSize, 1, 2);
        caSize = Math.Clamp(caSize, 1, 2);
        ioaSize = Math.Clamp(ioaSize, 1, 3);

        var stats = new CandidateStats(linkSize, cotSize, caSize, ioaSize);
        foreach (var sample in samples)
        {
            var raw = ParseHex(sample.RawHex);
            if (raw.Length == 0)
            {
                continue;
            }

            if (mode == Iec60870ProtocolMode.Iec101)
            {
                EvaluateIec101Frame(stats, sample, raw, linkSize, cotSize, caSize, ioaSize);
            }
            else if (mode == Iec60870ProtocolMode.Iec104)
            {
                EvaluateIec104Frame(stats, sample, raw, cotSize, caSize, ioaSize);
            }
        }

        return stats;
    }

    private static void EvaluateIec101Frame(CandidateStats stats, Iec10xAutoConfigFrameSample sample, byte[] raw, int linkSize, int cotSize, int caSize, int ioaSize)
    {
        var decoded = new Ft12Parser(linkSize).Decode(raw);
        if (decoded.Format == Ft12FrameFormat.Malformed || !decoded.IsChecksumValid || !decoded.IsLengthValid)
        {
            stats.Score -= 8;
            stats.IssueCount++;
            return;
        }

        stats.ValidFrameCount++;
        stats.FirstSequence = stats.FirstSequence == 0 ? sample.SequenceNumber : Math.Min(stats.FirstSequence, sample.SequenceNumber);
        stats.Score += decoded.Format == Ft12FrameFormat.VariableLength ? 8 : 4;

        if (decoded.LinkAddress.HasValue)
        {
            AddHit(stats.LinkAddressHits, decoded.LinkAddress.Value);
        }

        if (decoded.AsduBytes.Count == 0)
        {
            return;
        }

        var asdu = new Iec10xAsduDecoder(cotSize, caSize, ioaSize).Decode(decoded.AsduBytes);
        EvaluateAsdu(stats, asdu);
    }

    private static void EvaluateIec104Frame(CandidateStats stats, Iec10xAutoConfigFrameSample sample, byte[] raw, int cotSize, int caSize, int ioaSize)
    {
        var decoded = new Iec104ApduParser(cotSize, caSize, ioaSize).Decode(raw);
        if (!decoded.IsValid && decoded.Format == "Malformed")
        {
            stats.Score -= 8;
            stats.IssueCount++;
            return;
        }

        stats.ValidFrameCount++;
        stats.FirstSequence = stats.FirstSequence == 0 ? sample.SequenceNumber : Math.Min(stats.FirstSequence, sample.SequenceNumber);
        stats.Score += decoded.Format == "I" ? 7 : 3;
        if (decoded.Issues.Count > 0)
        {
            stats.Score -= Math.Min(8, decoded.Issues.Count * 2);
            stats.IssueCount += decoded.Issues.Count;
        }

        if (decoded.Asdu is not null)
        {
            EvaluateAsdu(stats, decoded.Asdu);
        }
    }

    private static void EvaluateAsdu(CandidateStats stats, Iec10xAsduDecode asdu)
    {
        stats.AsduCount++;

        if (IsKnownTypeId(asdu.TypeId))
        {
            stats.KnownTypeCount++;
            stats.Score += 10;
        }
        else
        {
            stats.Score -= 8;
        }

        if (IsKnownCot(asdu.CauseOfTransmission))
        {
            stats.KnownCotCount++;
            stats.Score += 5;
        }
        else
        {
            stats.Score -= 4;
        }

        if (asdu.ObjectCount is > 0 and <= 64)
        {
            stats.Score += 3;
        }
        else if (asdu.ObjectCount == 0)
        {
            stats.Score -= 4;
        }
        else
        {
            stats.Score -= 2;
        }

        if (asdu.CommonAddress > 0 && asdu.CommonAddress <= 65535)
        {
            stats.Score += 3;
            AddHit(stats.CommonAddressHits, asdu.CommonAddress);
        }
        else if (asdu.CommonAddress == 0)
        {
            stats.Score -= 2;
        }

        if (asdu.Issues.Count > 0)
        {
            stats.IssueCount += asdu.Issues.Count;
            stats.Score -= Math.Min(18, asdu.Issues.Count * 4);
        }

        foreach (var obj in asdu.Objects.Take(8))
        {
            if (obj.InformationObjectAddress >= 0 && obj.InformationObjectAddress <= 16_777_215)
            {
                stats.Score += 1;
            }

            if (asdu.TypeId == 100 && obj.InformationObjectAddress == 0)
            {
                stats.Score += 4;
            }
            else if (IsProcessType(asdu.TypeId) && obj.InformationObjectAddress > 0 && obj.InformationObjectAddress < 1_000_000)
            {
                stats.Score += 4;
            }
            else if (obj.InformationObjectAddress >= 1_000_000 && IsProcessType(asdu.TypeId))
            {
                stats.Score -= 2;
            }

            if (obj.Issues.Count == 0)
            {
                stats.GoodObjectCount++;
                stats.Score += 2;
            }
            else
            {
                stats.IssueCount += obj.Issues.Count;
                stats.Score -= Math.Min(8, obj.Issues.Count * 2);
            }

            if (!string.IsNullOrWhiteSpace(obj.ValueText))
            {
                stats.Score += 1;
            }

            if (!string.IsNullOrWhiteSpace(obj.QualityText))
            {
                stats.Score += 1;
            }
        }
    }

    private static bool IsProfileCandidateReliable(CandidateStats current, CandidateStats best, bool hasProfileFinding)
    {
        if (best.AsduCount == 0)
        {
            return false;
        }

        var profileChanged = best.LinkAddressSize != current.LinkAddressSize
            || best.CotSize != current.CotSize
            || best.CaSize != current.CaSize
            || best.IoaSize != current.IoaSize;
        if (!profileChanged)
        {
            return false;
        }

        var delta = best.Score - current.Score;
        var issueImprovement = current.IssueCount - best.IssueCount;
        var knownImprovement = (best.KnownTypeCount + best.KnownCotCount + best.GoodObjectCount) - (current.KnownTypeCount + current.KnownCotCount + current.GoodObjectCount);
        var requiredDelta = hasProfileFinding ? 5 : 10;
        return delta >= requiredDelta && (issueImprovement > 0 || knownImprovement > 0 || best.AsduCount >= 2);
    }

    private static void AddProfileSuggestion(
        List<Iec10xAutoConfigSuggestion> suggestions,
        Iec103MasterSettings corrected,
        string fieldName,
        string displayName,
        int currentValue,
        int bestValue,
        CandidateStats best,
        CandidateStats current,
        bool enabled)
    {
        if (!enabled || currentValue == bestValue)
        {
            return;
        }

        ApplyField(corrected, fieldName, bestValue);
        suggestions.Add(new Iec10xAutoConfigSuggestion
        {
            Code = "IEC10X-AUTO-PROFILE-" + fieldName.ToUpperInvariant(),
            FieldName = fieldName,
            DisplayName = displayName,
            OldValue = currentValue.ToString(CultureInfo.InvariantCulture),
            NewValue = bestValue.ToString(CultureInfo.InvariantCulture),
            Confidence = best.AsduCount >= 2 || best.Score - current.Score >= 14 ? "High" : "Medium",
            Evidence = $"Best profile score {best.Score} vs current {current.Score}; decoded ASDUs={best.AsduCount}; issues current/best={current.IssueCount}/{best.IssueCount}.",
            Reason = $"The decoded ASDU fields become more stable with {displayName}={bestValue}.",
            Priority = fieldName == nameof(Iec103MasterSettings.LinkAddressSize) ? 10 : 20
        });
    }

    private static void AddObservedAddressSuggestions(
        Iec103MasterSettings settings,
        Iec103MasterSettings corrected,
        List<Iec10xAutoConfigSuggestion> suggestions,
        CandidateStats best,
        CandidateStats current,
        bool allowSingleEvidence)
    {
        var ca = Dominant(best.CommonAddressHits);
        if (ca.HasValue && ca.Value > 0 && ca.Value != settings.CommonAddress)
        {
            var hits = best.CommonAddressHits[ca.Value];
            if (hits >= 2 || allowSingleEvidence)
            {
                corrected.CommonAddress = ca.Value;
                suggestions.Add(new Iec10xAutoConfigSuggestion
                {
                    Code = "IEC10X-AUTO-COMMON-ADDRESS",
                    FieldName = nameof(Iec103MasterSettings.CommonAddress),
                    DisplayName = "Common address",
                    OldValue = settings.CommonAddress.ToString(CultureInfo.InvariantCulture),
                    NewValue = ca.Value.ToString(CultureInfo.InvariantCulture),
                    Confidence = hits >= 2 ? "High" : "Medium",
                    Evidence = $"Dominant RX ASDU CA={ca.Value} observed {hits} time(s); configured CA={settings.CommonAddress}.",
                    Reason = "The slave is returning valid ASDU traffic with a different common address.",
                    Priority = 5
                });
            }
        }

        if (settings.ProtocolMode != Iec60870ProtocolMode.Iec101)
        {
            return;
        }

        var linkAddress = Dominant(best.LinkAddressHits);
        if (linkAddress.HasValue && linkAddress.Value >= 0 && linkAddress.Value != settings.LinkAddress)
        {
            var hits = best.LinkAddressHits[linkAddress.Value];
            if (hits >= 2 || (allowSingleEvidence && best.Score > current.Score))
            {
                corrected.LinkAddress = linkAddress.Value;
                suggestions.Add(new Iec10xAutoConfigSuggestion
                {
                    Code = "IEC101-AUTO-LINK-ADDRESS",
                    FieldName = nameof(Iec103MasterSettings.LinkAddress),
                    DisplayName = "Link address",
                    OldValue = settings.LinkAddress.ToString(CultureInfo.InvariantCulture),
                    NewValue = linkAddress.Value.ToString(CultureInfo.InvariantCulture),
                    Confidence = hits >= 2 ? "High" : "Medium",
                    Evidence = $"Dominant RX FT1.2 link address={linkAddress.Value} observed {hits} time(s); configured link address={settings.LinkAddress}.",
                    Reason = "The outstation response envelope uses a different link address than the current setup.",
                    Priority = 6
                });
            }
        }
    }

    private static void AddTimingSuggestion(Iec103MasterSettings settings, Iec103MasterSettings corrected, List<Iec10xAutoConfigSuggestion> suggestions, IReadOnlyList<Iec103MasterFinding> findings)
    {
        if (settings.ProtocolMode != Iec60870ProtocolMode.Iec101 || !HasClass2TimingFinding(findings))
        {
            return;
        }

        var estimated = EstimatePracticalClass2CycleMs(settings);
        if (settings.Class2PollIntervalMs >= estimated)
        {
            return;
        }

        corrected.Class2PollIntervalMs = estimated;
        suggestions.Add(new Iec10xAutoConfigSuggestion
        {
            Code = "IEC101-AUTO-CLASS2-INTERVAL",
            FieldName = nameof(Iec103MasterSettings.Class2PollIntervalMs),
            DisplayName = "Class 2 interval",
            OldValue = settings.Class2PollIntervalMs.ToString(CultureInfo.InvariantCulture),
            NewValue = estimated.ToString(CultureInfo.InvariantCulture),
            Confidence = "High",
            Evidence = $"Configured Class 2 interval={settings.Class2PollIntervalMs} ms; estimated practical serial cycle≈{estimated} ms at {settings.BaudRate} bps.",
            Reason = "The configured background polling interval is below the physical serial throughput estimate.",
            Priority = 40
        });
    }

    private static int EstimatePracticalClass2CycleMs(Iec103MasterSettings settings)
    {
        var bitsPerByte = 1 + settings.DataBits + (settings.Parity == System.IO.Ports.Parity.None ? 0 : 1) + (settings.StopBits == System.IO.Ports.StopBits.Two ? 2 : 1);
        var requestBytes = 4 + Math.Max(0, settings.LinkAddressSize);
        var typicalResponseBytes = 16 + Math.Max(0, settings.LinkAddressSize) + settings.CommonAddressSize + settings.CauseOfTransmissionSize + settings.InformationObjectAddressSize + 12;
        var baud = Math.Max(300, settings.BaudRate);
        var wireMs = (int)Math.Ceiling((requestBytes + typicalResponseBytes) * bitsPerByte * 1000.0 / baud);
        var turnaroundMs = baud <= 1200 ? 220 : baud <= 2400 ? 140 : 70;
        return Math.Max(50, wireMs + turnaroundMs + settings.Class1DrainDelayMs);
    }

    private static void ApplyField(Iec103MasterSettings settings, string fieldName, int value)
    {
        if (fieldName == nameof(Iec103MasterSettings.LinkAddressSize))
        {
            settings.LinkAddressSize = value;
        }
        else if (fieldName == nameof(Iec103MasterSettings.CauseOfTransmissionSize))
        {
            settings.CauseOfTransmissionSize = value;
        }
        else if (fieldName == nameof(Iec103MasterSettings.CommonAddressSize))
        {
            settings.CommonAddressSize = value;
        }
        else if (fieldName == nameof(Iec103MasterSettings.InformationObjectAddressSize))
        {
            settings.InformationObjectAddressSize = value;
        }
    }

    private static bool HasProfileSizeSymptom(IReadOnlyList<Iec103MasterFinding> findings)
        => findings.Any(finding => ContainsAny(JoinFinding(finding), "profile size", "asdu decode", "cot size", "ca size", "ioa size", "invalid vsq", "unknown type", "payload ended", "trailing asdu"));

    private static bool HasAddressFinding(IReadOnlyList<Iec103MasterFinding> findings)
        => findings.Any(finding => ContainsAny(JoinFinding(finding), "common address", "unknown ca", "link address", "unknown address", "ca mismatch"));

    private static bool HasClass2TimingFinding(IReadOnlyList<Iec103MasterFinding> findings)
        => findings.Any(finding => ContainsAny(JoinFinding(finding), "class 2 interval", "effective cycle", "serial throughput", "below practical"));

    private static string JoinFinding(Iec103MasterFinding finding)
        => string.Join(" ", finding.Id, finding.Title, finding.Evidence, finding.Impact, finding.Recommendation);

    private static bool ContainsAny(string text, params string[] needles)
        => needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static int? Dominant(Dictionary<int, int> hits)
        => hits.Count == 0
            ? null
            : hits.OrderByDescending(item => item.Value).ThenBy(item => item.Key).Select(item => (int?)item.Key).FirstOrDefault();

    private static void AddHit(Dictionary<int, int> hits, int value)
    {
        hits.TryGetValue(value, out var count);
        hits[value] = count + 1;
    }

    private static bool IsKnownTypeId(int typeId)
        => typeId is >= 1 and <= 16
            or >= 30 and <= 37
            or >= 45 and <= 51
            or >= 58 and <= 64
            or 70
            or >= 100 and <= 107;

    private static bool IsProcessType(int typeId)
        => typeId is >= 1 and <= 16 or >= 30 and <= 37;

    private static bool IsKnownCot(int cot)
        => cot is >= 1 and <= 13 or >= 20 and <= 47;

    private static int ConfidenceRank(string confidence)
        => confidence.Equals("High", StringComparison.OrdinalIgnoreCase) ? 3
            : confidence.Equals("Medium", StringComparison.OrdinalIgnoreCase) ? 2
            : 1;

    private static byte[] ParseHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<byte>();
        }

        var bytes = new List<byte>();
        foreach (var rawToken in value.Split(new[] { ' ', '\t', '\r', '\n', ',', ';', ':', '|', '·' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = rawToken.Trim('[', ']', '(', ')', '{', '}', '.', '-');
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                token = token[2..];
            }

            if (token.Length == 2 && byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
            {
                bytes.Add(parsed);
            }
        }

        return bytes.ToArray();
    }

    private static Iec103MasterSettings CloneSettings(Iec103MasterSettings source)
        => new()
        {
            ProtocolMode = source.ProtocolMode,
            TcpHost = source.TcpHost,
            TcpPort = source.TcpPort,
            CauseOfTransmissionSize = source.CauseOfTransmissionSize,
            CommonAddressSize = source.CommonAddressSize,
            InformationObjectAddressSize = source.InformationObjectAddressSize,
            LinkAddressSize = source.LinkAddressSize,
            TransmissionMode = source.TransmissionMode,
            Iec104T0TimeoutMs = source.Iec104T0TimeoutMs,
            Iec104T1AckTimeoutMs = source.Iec104T1AckTimeoutMs,
            Iec104T2AckDelayMs = source.Iec104T2AckDelayMs,
            Iec104T3TestIntervalMs = source.Iec104T3TestIntervalMs,
            Iec104KMaxUnacknowledged = source.Iec104KMaxUnacknowledged,
            Iec104WReceiveWindow = source.Iec104WReceiveWindow,
            PortName = source.PortName,
            BaudRate = source.BaudRate,
            DataBits = source.DataBits,
            Parity = source.Parity,
            StopBits = source.StopBits,
            LinkAddress = source.LinkAddress,
            CommonAddress = source.CommonAddress,
            UseSimulatedSlave = source.UseSimulatedSlave,
            TargetProfile = source.TargetProfile,
            MappingProfilePath = source.MappingProfilePath,
            ResponseTimeoutMs = source.ResponseTimeoutMs,
            Class2PollIntervalMs = source.Class2PollIntervalMs,
            Class1DrainDelayMs = source.Class1DrainDelayMs,
            BusyBackoffMs = source.BusyBackoffMs,
            StartupDelayMs = source.StartupDelayMs,
            MaxClass1DrainFrames = source.MaxClass1DrainFrames,
            MaxConsecutiveClass1BeforeClass2 = source.MaxConsecutiveClass1BeforeClass2,
            MaxConsecutiveTimeoutsBeforeResetFcb = source.MaxConsecutiveTimeoutsBeforeResetFcb,
            TimeoutRecoveryBackoffMs = source.TimeoutRecoveryBackoffMs,
            ResetRemoteLinkOnConnect = source.ResetRemoteLinkOnConnect,
            ResetFcbOnConnect = source.ResetFcbOnConnect,
            SendGeneralInterrogationOnConnect = source.SendGeneralInterrogationOnConnect,
            SendClockSyncOnConnect = source.SendClockSyncOnConnect,
            RequestClass2ImmediatelyAfterStartup = source.RequestClass2ImmediatelyAfterStartup,
            ResetFcbAfterTimeoutBurst = source.ResetFcbAfterTimeoutBurst,
            IncludeLocalPathsInReports = source.IncludeLocalPathsInReports,
            MaxRetainedEvidenceEvents = source.MaxRetainedEvidenceEvents,
            MaxRetainedRelayEvents = source.MaxRetainedRelayEvents,
            MaxRetainedFindings = source.MaxRetainedFindings
        };

    private sealed record ProfileCandidate(int LinkAddressSize, int CotSize, int CaSize, int IoaSize);

    private sealed class CandidateStats
    {
        public CandidateStats(int linkAddressSize, int cotSize, int caSize, int ioaSize)
        {
            LinkAddressSize = linkAddressSize;
            CotSize = cotSize;
            CaSize = caSize;
            IoaSize = ioaSize;
        }

        public int LinkAddressSize { get; }
        public int CotSize { get; }
        public int CaSize { get; }
        public int IoaSize { get; }
        public int Score { get; set; }
        public int ValidFrameCount { get; set; }
        public int AsduCount { get; set; }
        public int KnownTypeCount { get; set; }
        public int KnownCotCount { get; set; }
        public int IssueCount { get; set; }
        public int GoodObjectCount { get; set; }
        public long FirstSequence { get; set; }
        public Dictionary<int, int> CommonAddressHits { get; } = new();
        public Dictionary<int, int> LinkAddressHits { get; } = new();
    }
}

public sealed record Iec10xAutoConfigFrameSample(
    Iec60870ProtocolMode ProtocolMode,
    FrameDirection Direction,
    string RawHex,
    long SequenceNumber,
    int? ResponseTimeMs = null,
    string Context = "");

public sealed class Iec10xAutoConfigSuggestion
{
    public string Code { get; init; } = string.Empty;
    public string FieldName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string OldValue { get; init; } = string.Empty;
    public string NewValue { get; init; } = string.Empty;
    public string Confidence { get; init; } = "Medium";
    public string Evidence { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public int Priority { get; init; }
}

public sealed class Iec10xAutoConfigResult
{
    public Iec103MasterSettings CorrectedSettings { get; init; } = Iec103MasterSettings.CreateDefault();
    public IReadOnlyList<Iec10xAutoConfigSuggestion> Suggestions { get; init; } = Array.Empty<Iec10xAutoConfigSuggestion>();
    public string Summary { get; init; } = string.Empty;
    public bool HasSuggestions => Suggestions.Count > 0;
}
