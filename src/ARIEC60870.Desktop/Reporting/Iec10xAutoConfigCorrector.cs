// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.Reporting;

internal static class Iec10xAutoConfigCorrector
{
    public static Iec10xAutoConfigSuggestion Analyze(
        IReadOnlyList<EvidenceRow> rows,
        IReadOnlyList<KeyValuePair<string, string>> setup,
        Iec60870ProtocolMode protocolMode)
    {
        var current = CurrentConfigSnapshot.FromSetup(setup, protocolMode);
        var proposed = current.Clone();
        var reasons = new List<string>();
        var confidence = 0;

        var observedCommonAddress = Dominant(rows.Where(IsRx).Select(row => ParsePositiveInt(row.CommonAddress)).Where(value => value.HasValue).Select(value => value!.Value));
        if (observedCommonAddress.HasValue)
        {
            proposed.CommonAddress = observedCommonAddress.Value;
            reasons.Add($"Device traffic consistently uses CA={observedCommonAddress.Value}.");
            confidence += 2;
        }

        var observedLinkAddress = Dominant(rows
            .Where(row => IsTx(row) || IsRx(row))
            .Select(row => ParsePositiveInt(row.LinkAddress))
            .Where(value => value.HasValue)
            .Select(value => value!.Value));
        if (observedLinkAddress.HasValue && protocolMode == Iec60870ProtocolMode.Iec101)
        {
            proposed.LinkAddress = observedLinkAddress.Value;
            reasons.Add($"Observed IEC-101 link address={observedLinkAddress.Value} in the evidence window.");
            confidence += 1;
        }

        var inferredProfile = protocolMode switch
        {
            Iec60870ProtocolMode.Iec101 => InferIec101Profile(rows),
            Iec60870ProtocolMode.Iec104 => InferIec104Profile(rows),
            _ => null
        };

        if (inferredProfile is not null)
        {
            if (inferredProfile.LinkAddressSize.HasValue && protocolMode == Iec60870ProtocolMode.Iec101)
            {
                proposed.LinkAddressSize = inferredProfile.LinkAddressSize.Value;
            }

            if (inferredProfile.CauseOfTransmissionSize.HasValue)
            {
                proposed.CauseOfTransmissionSize = inferredProfile.CauseOfTransmissionSize.Value;
            }

            if (inferredProfile.CommonAddressSize.HasValue)
            {
                proposed.CommonAddressSize = inferredProfile.CommonAddressSize.Value;
            }

            if (inferredProfile.InformationObjectAddressSize.HasValue)
            {
                proposed.InformationObjectAddressSize = inferredProfile.InformationObjectAddressSize.Value;
            }

            reasons.Add(inferredProfile.Reason);
            confidence += inferredProfile.Score >= 18 ? 4 : inferredProfile.Score >= 10 ? 3 : 2;
        }

        var baudRate = ReadSetupInt(setup, "Baud");
        var currentClass2Interval = ReadSetupInt(setup, "Class 2 interval")
            ?? ReadSetupInt(setup, "Class 2 interval (ms)");
        if (protocolMode == Iec60870ProtocolMode.Iec101 && baudRate.HasValue && currentClass2Interval.HasValue)
        {
            var recommended = baudRate.Value <= 1200 ? 1000 : baudRate.Value <= 2400 ? 750 : 500;
            if (currentClass2Interval.Value < recommended)
            {
                proposed.Class2IntervalMs = recommended;
                reasons.Add($"Low-speed IEC-101 serial link ({baudRate.Value} bps) benefits from a Class 2 interval of at least {recommended} ms.");
                confidence += 1;
            }
        }

        var changes = BuildChanges(current, proposed);
        var summary = reasons.Count == 0
            ? "No higher-confidence configuration correction is available from the current evidence scope."
            : string.Join(" ", reasons);
        var normalizedConfidence = changes.Count == 0
            ? "No change"
            : confidence >= 6 ? "High"
            : confidence >= 4 ? "Medium"
            : "Low";

        return new Iec10xAutoConfigSuggestion(current, proposed, changes, summary, normalizedConfidence, confidence, inferredProfile);
    }

    public static bool MatchesCurrentProfileSizes(CurrentConfigSnapshot current, InferredIec10xProfile? inferred)
    {
        if (inferred is null)
        {
            return false;
        }

        return (!inferred.LinkAddressSize.HasValue || current.LinkAddressSize == inferred.LinkAddressSize.Value)
               && (!inferred.CauseOfTransmissionSize.HasValue || current.CauseOfTransmissionSize == inferred.CauseOfTransmissionSize.Value)
               && (!inferred.CommonAddressSize.HasValue || current.CommonAddressSize == inferred.CommonAddressSize.Value)
               && (!inferred.InformationObjectAddressSize.HasValue || current.InformationObjectAddressSize == inferred.InformationObjectAddressSize.Value);
    }

    public static bool MatchesCurrentCommonAddress(CurrentConfigSnapshot current, IReadOnlyList<EvidenceRow> rows)
    {
        var observedCommonAddress = Dominant(rows.Where(IsRx).Select(row => ParsePositiveInt(row.CommonAddress)).Where(value => value.HasValue).Select(value => value!.Value));
        return observedCommonAddress.HasValue && current.CommonAddress == observedCommonAddress.Value;
    }

    private static IReadOnlyList<Iec10xAutoConfigFieldChange> BuildChanges(CurrentConfigSnapshot current, CurrentConfigSnapshot proposed)
    {
        var changes = new List<Iec10xAutoConfigFieldChange>();
        AddChange(changes, current.CommonAddress, proposed.CommonAddress, "CommonAddress", "Common address");
        AddChange(changes, current.LinkAddress, proposed.LinkAddress, "LinkAddress", "Link address");
        AddChange(changes, current.LinkAddressSize, proposed.LinkAddressSize, "LinkAddressSize", "Link length");
        AddChange(changes, current.CauseOfTransmissionSize, proposed.CauseOfTransmissionSize, "CotSize", "COT size");
        AddChange(changes, current.CommonAddressSize, proposed.CommonAddressSize, "CaSize", "CA size");
        AddChange(changes, current.InformationObjectAddressSize, proposed.InformationObjectAddressSize, "IoaSize", "IOA size");
        AddChange(changes, current.Class2IntervalMs, proposed.Class2IntervalMs, "Class2Interval", "Class 2 interval");
        return changes;
    }

    private static void AddChange(List<Iec10xAutoConfigFieldChange> changes, int? current, int? proposed, string key, string label)
    {
        if (!proposed.HasValue || current == proposed)
        {
            return;
        }

        changes.Add(new Iec10xAutoConfigFieldChange(key, label, current.HasValue ? current.Value.ToString(CultureInfo.InvariantCulture) : "-", proposed.Value.ToString(CultureInfo.InvariantCulture)));
    }

    private static InferredIec10xProfile? InferIec101Profile(IReadOnlyList<EvidenceRow> rows)
    {
        var candidates = new Dictionary<string, AggregateCandidate>(StringComparer.Ordinal);
        var frameRows = rows.Where(row => row.ProtocolMode == "101" && LooksLikeVariable101Frame(row.RawHex)).ToArray();
        var rxFrameRows = frameRows.Where(row => row.Direction.Equals("RX", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (rxFrameRows.Length > 0)
        {
            frameRows = rxFrameRows;
        }

        foreach (var row in frameRows)
        {
            foreach (var linkSize in new[] { 1, 2 })
            {
                foreach (var cotSize in new[] { 1, 2 })
                {
                    foreach (var caSize in new[] { 1, 2 })
                    {
                        foreach (var ioaSize in new[] { 1, 2, 3 })
                        {
                            var candidate = Score101Candidate(row, linkSize, cotSize, caSize, ioaSize);
                            if (candidate is null || candidate.Score < 5)
                            {
                                continue;
                            }

                            var key = $"{linkSize}:{cotSize}:{caSize}:{ioaSize}";
                            if (!candidates.TryGetValue(key, out var aggregate))
                            {
                                aggregate = new AggregateCandidate(linkSize, cotSize, caSize, ioaSize);
                                candidates.Add(key, aggregate);
                            }

                            aggregate.Score += candidate.Score;
                            aggregate.SupportRows++;
                            if (!string.IsNullOrWhiteSpace(candidate.Reason) && aggregate.Reasons.Count < 4)
                            {
                                aggregate.Reasons.Add(candidate.Reason);
                            }
                        }
                    }
                }
            }
        }

        var best = candidates.Values
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.SupportRows)
            .FirstOrDefault();

        if (best is null || best.SupportRows == 0 || best.Score < 8)
        {
            return null;
        }

        var reason = best.Reasons.Count > 0
            ? string.Join(" ", best.Reasons)
            : $"Frame evidence fits IEC-101 profile Link={best.LinkAddressSize}, COT={best.CauseOfTransmissionSize}, CA={best.CommonAddressSize}, IOA={best.InformationObjectAddressSize}.";

        return new InferredIec10xProfile(best.LinkAddressSize, best.CauseOfTransmissionSize, best.CommonAddressSize, best.InformationObjectAddressSize, best.Score, reason);
    }

    private static InferredIec10xProfile? InferIec104Profile(IReadOnlyList<EvidenceRow> rows)
    {
        var candidates = new Dictionary<string, AggregateCandidate>(StringComparer.Ordinal);
        var frameRows = rows.Where(row => row.ProtocolMode == "104" && LooksLikeIec104Frame(row.RawHex)).ToArray();
        var rxFrameRows = frameRows.Where(row => row.Direction.Equals("RX", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (rxFrameRows.Length > 0)
        {
            frameRows = rxFrameRows;
        }

        foreach (var row in frameRows)
        {
            foreach (var cotSize in new[] { 1, 2 })
            {
                foreach (var caSize in new[] { 1, 2 })
                {
                    foreach (var ioaSize in new[] { 1, 2, 3 })
                    {
                        var candidate = Score104Candidate(row, cotSize, caSize, ioaSize);
                        if (candidate is null || candidate.Score < 5)
                        {
                            continue;
                        }

                        var key = $"{cotSize}:{caSize}:{ioaSize}";
                        if (!candidates.TryGetValue(key, out var aggregate))
                        {
                            aggregate = new AggregateCandidate(null, cotSize, caSize, ioaSize);
                            candidates.Add(key, aggregate);
                        }

                        aggregate.Score += candidate.Score;
                        aggregate.SupportRows++;
                        if (!string.IsNullOrWhiteSpace(candidate.Reason) && aggregate.Reasons.Count < 4)
                        {
                            aggregate.Reasons.Add(candidate.Reason);
                        }
                    }
                }
            }
        }

        var best = candidates.Values
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.SupportRows)
            .FirstOrDefault();

        if (best is null || best.SupportRows == 0 || best.Score < 8)
        {
            return null;
        }

        var reason = best.Reasons.Count > 0
            ? string.Join(" ", best.Reasons)
            : $"Frame evidence fits IEC-104 profile COT={best.CauseOfTransmissionSize}, CA={best.CommonAddressSize}, IOA={best.InformationObjectAddressSize}.";

        return new InferredIec10xProfile(null, best.CauseOfTransmissionSize, best.CommonAddressSize, best.InformationObjectAddressSize, best.Score, reason);
    }

    private static CandidateScore? Score101Candidate(EvidenceRow row, int linkSize, int cotSize, int caSize, int ioaSize)
    {
        var bytes = ParseHex(row.RawHex);
        if (bytes.Count < 8 || bytes[0] != 0x68 || bytes[3] != 0x68)
        {
            return null;
        }

        var asduStart = 5 + linkSize;
        if (bytes.Count <= asduStart + 1)
        {
            return null;
        }

        var typeId = bytes[asduStart];
        var offset = asduStart + 2;
        if (bytes.Count < offset + cotSize + caSize + ioaSize + 2)
        {
            return null;
        }

        var cotCode = bytes[offset] & 0x3F;
        offset += cotSize;
        var commonAddress = ReadLittleEndian(bytes, offset, caSize);
        offset += caSize;
        var ioa = ReadLittleEndian(bytes, offset, ioaSize);
        var payloadOffset = offset + ioaSize;
        var linkAddress = ReadLittleEndian(bytes, 5, linkSize);

        var score = ScoreCandidate(row, typeId, cotCode, commonAddress, ioa, linkAddress, bytes, payloadOffset);
        if (score == 0)
        {
            return null;
        }

        var reason = BuildProfileReason(row, "IEC-101", linkSize, cotSize, caSize, ioaSize, commonAddress, ioa);
        return new CandidateScore(score, reason);
    }

    private static CandidateScore? Score104Candidate(EvidenceRow row, int cotSize, int caSize, int ioaSize)
    {
        var bytes = ParseHex(row.RawHex);
        if (bytes.Count < 8 || bytes[0] != 0x68)
        {
            return null;
        }

        var asduStart = 6;
        if (bytes.Count <= asduStart + 1)
        {
            return null;
        }

        var typeId = bytes[asduStart];
        var offset = asduStart + 2;
        if (bytes.Count < offset + cotSize + caSize + ioaSize)
        {
            return null;
        }

        var cotCode = bytes[offset] & 0x3F;
        offset += cotSize;
        var commonAddress = ReadLittleEndian(bytes, offset, caSize);
        offset += caSize;
        var ioa = ReadLittleEndian(bytes, offset, ioaSize);
        var payloadOffset = offset + ioaSize;

        var score = ScoreCandidate(row, typeId, cotCode, commonAddress, ioa, null, bytes, payloadOffset);
        if (score == 0)
        {
            return null;
        }

        var reason = BuildProfileReason(row, "IEC-104", null, cotSize, caSize, ioaSize, commonAddress, ioa);
        return new CandidateScore(score, reason);
    }

    private static int ScoreCandidate(EvidenceRow row, int typeId, int cotCode, int commonAddress, int ioa, int? linkAddress, IReadOnlyList<byte> bytes, int payloadOffset)
    {
        var score = 0;
        var expectedTypeId = ParsePositiveInt(row.TypeId);
        var expectedCot = ParsePositiveInt(row.CotCode);
        var expectedCa = ParsePositiveInt(row.CommonAddress);
        var expectedIoa = ParsePositiveInt(row.IoAddress);
        var expectedLinkAddress = ParsePositiveInt(row.LinkAddress);

        if (IsKnownTypeId(typeId))
        {
            score += 3;
        }
        else
        {
            return 0;
        }

        if (IsKnownCot(cotCode))
        {
            score += 2;
        }
        else
        {
            score -= 3;
        }

        if (commonAddress is > 0 and <= 65535)
        {
            score += 1;
        }

        if (row.Direction.Equals("RX", StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }

        if (typeId == 100)
        {
            if (ioa == 0)
            {
                score += 4;
            }

            if (payloadOffset < bytes.Count - 2 && bytes[payloadOffset] is 20 or 0x14)
            {
                score += 5;
            }

            if (cotCode is 6 or 7 or 10)
            {
                score += 2;
            }
        }
        else if (ioa >= 0 && ioa <= 0xFFFFFF)
        {
            score += 1;
        }

        if (expectedTypeId.HasValue && expectedTypeId.Value == typeId)
        {
            score += 2;
        }

        if (expectedCot.HasValue && expectedCot.Value == cotCode)
        {
            score += 1;
        }

        if (expectedCa.HasValue && expectedCa.Value == commonAddress)
        {
            score += 1;
        }

        if (expectedIoa.HasValue && expectedIoa.Value == ioa)
        {
            score += 1;
        }

        if (expectedLinkAddress.HasValue && linkAddress.HasValue && expectedLinkAddress.Value == linkAddress.Value)
        {
            score += 1;
        }

        return Math.Max(0, score);
    }

    private static bool IsKnownTypeId(int typeId)
        => typeId is 1 or 3 or 5 or 7 or 9 or 11 or 13 or 15 or 20 or 21
            or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40
            or 45 or 46 or 47 or 48 or 49 or 50 or 51
            or 58 or 59 or 60 or 61 or 62 or 63 or 64
            or 100 or 101 or 102 or 103 or 104 or 105 or 107;

    private static bool IsKnownCot(int cotCode)
        => cotCode is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10
            or 20 or 44 or 45 or 46 or 47;

    private static string BuildProfileReason(EvidenceRow row, string protocol, int? linkSize, int cotSize, int caSize, int ioaSize, int commonAddress, int ioa)
    {
        var prefix = linkSize.HasValue
            ? $"{protocol} evidence row #{row.Sequence} fits Link={linkSize.Value}, COT={cotSize}, CA={caSize}, IOA={ioaSize}"
            : $"{protocol} evidence row #{row.Sequence} fits COT={cotSize}, CA={caSize}, IOA={ioaSize}";
        return $"{prefix}; decoded CA={commonAddress}, IOA={ioa}.";
    }

    private static bool LooksLikeVariable101Frame(string? rawHex)
    {
        var bytes = ParseHex(rawHex);
        return bytes.Count >= 8 && bytes[0] == 0x68 && bytes[3] == 0x68;
    }

    private static bool LooksLikeIec104Frame(string? rawHex)
    {
        var bytes = ParseHex(rawHex);
        return bytes.Count >= 8 && bytes[0] == 0x68;
    }

    private static IReadOnlyList<byte> ParseHex(string? rawHex)
    {
        if (string.IsNullOrWhiteSpace(rawHex))
        {
            return Array.Empty<byte>();
        }

        var values = new List<byte>();
        foreach (var token in rawHex.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = token.Trim();
            if (clean.Equals("RAW", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("TX", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("RX", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("HEX", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (clean.Length > 2)
            {
                continue;
            }

            if (byte.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static int ReadLittleEndian(IReadOnlyList<byte> bytes, int offset, int length)
    {
        var value = 0;
        for (var i = 0; i < length && offset + i < bytes.Count; i++)
        {
            value |= bytes[offset + i] << (8 * i);
        }

        return value;
    }

    private static bool IsRx(EvidenceRow row) => row.Direction.Equals("RX", StringComparison.OrdinalIgnoreCase);
    private static bool IsTx(EvidenceRow row) => row.Direction.Equals("TX", StringComparison.OrdinalIgnoreCase);

    private static int? ReadSetupInt(IReadOnlyList<KeyValuePair<string, string>> setup, string key)
    {
        var item = setup.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return ParsePositiveInt(item.Value);
    }

    private static int? ParsePositiveInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(ch => char.IsDigit(ch) || ch == '-').ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static int? Dominant(IEnumerable<int> values)
        => values.GroupBy(value => value).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).Select(group => (int?)group.Key).FirstOrDefault();

    internal sealed class Iec10xAutoConfigSuggestion
    {
        public Iec10xAutoConfigSuggestion(CurrentConfigSnapshot current, CurrentConfigSnapshot proposed, IReadOnlyList<Iec10xAutoConfigFieldChange> changes, string summary, string confidence, int confidenceScore, InferredIec10xProfile? inferredProfile)
        {
            Current = current;
            Proposed = proposed;
            Changes = changes;
            Summary = summary;
            Confidence = confidence;
            ConfidenceScore = confidenceScore;
            InferredProfile = inferredProfile;
        }

        public CurrentConfigSnapshot Current { get; }
        public CurrentConfigSnapshot Proposed { get; }
        public IReadOnlyList<Iec10xAutoConfigFieldChange> Changes { get; }
        public string Summary { get; }
        public string Confidence { get; }
        public int ConfidenceScore { get; }
        public InferredIec10xProfile? InferredProfile { get; }
        public bool HasChanges => Changes.Count > 0;
    }

    internal sealed class Iec10xAutoConfigFieldChange
    {
        public Iec10xAutoConfigFieldChange(string key, string label, string currentValue, string proposedValue)
        {
            Key = key;
            Label = label;
            CurrentValue = currentValue;
            ProposedValue = proposedValue;
        }

        public string Key { get; }
        public string Label { get; }
        public string CurrentValue { get; }
        public string ProposedValue { get; }
    }

    internal sealed class CurrentConfigSnapshot
    {
        public int? CommonAddress { get; set; }
        public int? LinkAddress { get; set; }
        public int? LinkAddressSize { get; set; }
        public int? CauseOfTransmissionSize { get; set; }
        public int? CommonAddressSize { get; set; }
        public int? InformationObjectAddressSize { get; set; }
        public int? Class2IntervalMs { get; set; }
        public Iec60870ProtocolMode ProtocolMode { get; set; }

        public CurrentConfigSnapshot Clone()
            => new()
            {
                CommonAddress = CommonAddress,
                LinkAddress = LinkAddress,
                LinkAddressSize = LinkAddressSize,
                CauseOfTransmissionSize = CauseOfTransmissionSize,
                CommonAddressSize = CommonAddressSize,
                InformationObjectAddressSize = InformationObjectAddressSize,
                Class2IntervalMs = Class2IntervalMs,
                ProtocolMode = ProtocolMode
            };

        public static CurrentConfigSnapshot FromSetup(IReadOnlyList<KeyValuePair<string, string>> setup, Iec60870ProtocolMode protocolMode)
            => new()
            {
                ProtocolMode = protocolMode,
                CommonAddress = ReadSetupInt(setup, "Common address"),
                LinkAddress = ReadSetupInt(setup, "Link address"),
                LinkAddressSize = ReadSetupSizePart(setup, 0),
                CauseOfTransmissionSize = ReadSetupSizePart(setup, 1),
                CommonAddressSize = ReadSetupSizePart(setup, 2),
                InformationObjectAddressSize = ReadSetupSizePart(setup, 3),
                Class2IntervalMs = ReadSetupInt(setup, "Class 2 interval") ?? ReadSetupInt(setup, "Class 2 interval (ms)")
            };

        private static int? ReadSetupSizePart(IReadOnlyList<KeyValuePair<string, string>> setup, int index)
        {
            var item = setup.FirstOrDefault(pair => pair.Key.Equals("COT / CA / IOA size", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(item.Value))
            {
                return null;
            }

            var parts = item.Value.Split('/').Select(part => ParsePositiveInt(part)).ToArray();
            if (parts.Length == 3)
            {
                return index switch
                {
                    1 => parts[0],
                    2 => parts[1],
                    3 => parts[2],
                    _ => ReadSetupInt(setup, "Link address size")
                };
            }

            return index == 0 ? ReadSetupInt(setup, "Link address size") : null;
        }
    }

    internal sealed class InferredIec10xProfile
    {
        public InferredIec10xProfile(int? linkAddressSize, int? causeOfTransmissionSize, int? commonAddressSize, int? informationObjectAddressSize, int score, string reason)
        {
            LinkAddressSize = linkAddressSize;
            CauseOfTransmissionSize = causeOfTransmissionSize;
            CommonAddressSize = commonAddressSize;
            InformationObjectAddressSize = informationObjectAddressSize;
            Score = score;
            Reason = reason;
        }

        public int? LinkAddressSize { get; }
        public int? CauseOfTransmissionSize { get; }
        public int? CommonAddressSize { get; }
        public int? InformationObjectAddressSize { get; }
        public int Score { get; }
        public string Reason { get; }
    }

    private sealed class AggregateCandidate
    {
        public AggregateCandidate(int? linkAddressSize, int causeOfTransmissionSize, int commonAddressSize, int informationObjectAddressSize)
        {
            LinkAddressSize = linkAddressSize;
            CauseOfTransmissionSize = causeOfTransmissionSize;
            CommonAddressSize = commonAddressSize;
            InformationObjectAddressSize = informationObjectAddressSize;
        }

        public int? LinkAddressSize { get; }
        public int CauseOfTransmissionSize { get; }
        public int CommonAddressSize { get; }
        public int InformationObjectAddressSize { get; }
        public int Score { get; set; }
        public int SupportRows { get; set; }
        public List<string> Reasons { get; } = new();
    }

    private sealed class CandidateScore
    {
        public CandidateScore(int score, string reason)
        {
            Score = score;
            Reason = reason;
        }

        public int Score { get; }
        public string Reason { get; }
    }
}
