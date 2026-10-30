// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>
/// Evidence-only, bounded command casebook. Consumes the already decoded IEC-10x
/// envelope and object bytes; it never sends, polls, guesses missing frames or
/// re-decodes a differently configured ASDU. One instance per capture/session.
/// </summary>
public sealed class Iec10xCommandCasebook
{
    private const int MaxActive = 128;
    private readonly int _capacity;
    private readonly List<CommandCase> _history = new();
    private readonly Dictionary<CommandKey, State> _active = new();
    private long _nextId;

    private readonly record struct CommandKey(Iec60870ProtocolMode Protocol, string Source,
        int? Link, int Ca, int Ioa, int Type, int? Originator);

    private sealed class State
    {
        public required CommandKey Key { get; init; }
        public required CommandCase Current { get; set; }
        public required string Target { get; init; }
        public required int? ExpectedDigital { get; init; }
        public int? FeedbackIoa { get; set; }
        public bool SelectAccepted { get; set; }
        public bool Executed { get; set; }
        public bool ExecuteAccepted { get; set; }
        public bool Terminated { get; set; }
        public bool FeedbackMatched { get; set; }
        public bool Ambiguous { get; set; }
        public bool Negative { get; set; }
        public bool LastRequestSelect { get; set; }
    }

    public sealed record CommandStage(long Sequence, string EvidenceReference, DateTime HostUtc,
        string Direction, string Phase, int? Cot, string Explanation, string RawHex);

    public sealed record CommandCase(
        long CaseId, string Protocol, string Source, int? LinkAddress, int CommonAddress,
        int Ioa, int TypeId, string Target, string Status, string Interpretation,
        long FirstSequence, long LastSequence, DateTime FirstUtc,
        IReadOnlyList<CommandStage> Stages, int? FeedbackIoa)
    {
        public string Address => $"CA {CommonAddress} · IOA {Ioa} · TI {TypeId}";
        public string CaseLabel => $"#{CaseId} · {Address}";
        public string FirstTime => FirstUtc.ToLocalTime().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
        public string ToEvidenceText()
        {
            var b = new StringBuilder();
            b.AppendLine($"# Control case #{CaseId} — {Status}");
            b.AppendLine($"Protocol: {Protocol} | Source: {Source} | Link: {LinkAddress?.ToString() ?? "N/A"}");
            b.AppendLine($"Type: {TypeId} | CA: {CommonAddress} | IOA: {Ioa} | Target: {Target}");
            b.AppendLine($"Feedback IOA: {FeedbackIoa?.ToString() ?? "not configured"}");
            b.AppendLine($"Meaning: {Interpretation}");
            b.AppendLine("Evidence stages (observed, host timestamps; not calibrated wire-edge time):");
            foreach (var s in Stages)
            {
                b.AppendLine($"- {s.HostUtc:O} | {s.Direction} | {s.Phase} | COT {s.Cot?.ToString() ?? "?"} | seq {s.Sequence} | {s.EvidenceReference}");
                b.AppendLine($"  {s.Explanation}");
                if (!string.IsNullOrWhiteSpace(s.RawHex)) b.AppendLine($"  HEX: {s.RawHex}");
            }
            b.AppendLine("A link ACK, ACTCON, or ACTTERM alone never proves physical switchgear operation.");
            return b.ToString();
        }
    }

    public Iec10xCommandCasebook(int capacity = 240)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Count => _history.Count;
    public int ActiveCount => _active.Count;
    public IReadOnlyList<CommandCase> Snapshot() => _history.ToArray();

    /// <summary>Exact source-evidence lookup for Event Log and diagnostics. Never use
    /// LastOrDefault as a substitute: responses can update any older case.</summary>
    public CommandCase? FindByEvidence(Iec103MasterEvidenceEvent item)
    {
        var reference = item.EvidenceReference;
        for (var i = _history.Count - 1; i >= 0; i--)
        {
            var c = _history[i];
            if (c.Protocol != (item.ProtocolMode == Iec60870ProtocolMode.Iec101 ? "IEC-101" : "IEC-104") ||
                c.Source != Normalize(item.PhysicalSource))
                continue;
            if (c.Stages.Any(s => s.Sequence == item.SequenceNumber &&
                s.EvidenceReference == reference))
                return c;
        }
        return null;
    }

    public void Reset()
    {
        _active.Clear();
        _history.Clear();
        _nextId = 0;
    }

    /// <summary>Returns true only when a typed command case or matched feedback changes.
    /// feedbackIoa must come from an explicit engineer-approved point mapping.</summary>
    public bool Observe(Iec103MasterEvidenceEvent item, int? feedbackIoa = null)
    {
        if (item.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104))
            return false;
        if (item.Direction is not (FrameDirection.MasterToSlave or FrameDirection.SlaveToMaster))
            return false;

        var commandType = item.TypeId is >= 45 and <= 51 or >= 58 and <= 64;
        if (!commandType)
            return item.Direction == FrameDirection.SlaveToMaster && ObserveDigitalFeedback(item);

        if (!item.TypeId.HasValue || !item.CommonAddressNumber.HasValue ||
            !item.InformationObjectAddress.HasValue || item.CauseDetails is not { } cot ||
            item.CommandDetails is not { } command || command.Select is null)
            return false;

        // A checksum-invalid / malformed / TEST telegram is preserved in the raw trace
        // but must not alter an operational case, even when it has plausible header values.
        if (item.Frame?.IsChecksumValid == false ||
            item.AsduDetails?.Issues.Count > 0 || item.AsduDetails?.ObjectCount is > 1 or 0 ||
            string.Equals(item.Category, "RX Warning", StringComparison.OrdinalIgnoreCase) ||
            cot.Test)
            return false;

        var key = new CommandKey(item.ProtocolMode, Normalize(item.PhysicalSource),
            item.ProtocolMode == Iec60870ProtocolMode.Iec101 ? item.LinkAddress : null,
            item.CommonAddressNumber.Value, item.InformationObjectAddress.Value,
            item.TypeId.Value, cot.OriginatorAddress);
        var select = command.Select.Value;
        var phase = select ? "SELECT" : "EXECUTE";
        var target = command.ValueMeaning;
        _active.TryGetValue(key, out var state);

        if (item.Direction == FrameDirection.MasterToSlave)
        {
            if (cot.Code == 8)
            {
                if (state is null) return false;
                Update(state, item, "CANCEL request", "Master requested deactivation; wait for RTU response.",
                    "Cancellation requested", "Cancellation is not execution.");
                return true;
            }
            if (cot.Code != 6 || cot.Negative) return false;

            if (state is null)
            {
                // Never overwrite an unresolved case. Keep bounded parallel identities,
                // preserving ambiguous evidence rather than silently pairing last-wins.
                if (_active.Count >= MaxActive)
                {
                    var oldest = _active.Values.OrderBy(x => x.Current.FirstSequence).First();
                    Update(oldest, item, "Observation limit", "Pending case evicted from bounded index.",
                        "Observed incomplete", "Index limit reached; original raw archive remains authoritative.");
                    _active.Remove(oldest.Key);
                }
                var c = NewCase(item, key, target, feedbackIoa, select ? "SELECT sent" : "EXECUTE sent",
                    "Waiting for source-matched RTU ACTCON; link ACK is not application acceptance.");
                state = new State { Key = key, Current = c, Target = target,
                    ExpectedDigital = ExpectedDigitalState(command), FeedbackIoa = feedbackIoa,
                    LastRequestSelect = select, Executed = !select };
                _active.Add(key, state);
                return true;
            }

            if (state.Ambiguous || state.Executed || !state.LastRequestSelect ||
                !state.SelectAccepted || select || !string.Equals(state.Target, target, StringComparison.Ordinal))
            {
                state.Ambiguous = true;
                Update(state, item, phase + " overlapping", "Unresolved/repeated or changed-target Master request.",
                    "Ambiguous", "More than one request can match a response; no unique success verdict.");
                return true;
            }

            state.Executed = true;
            state.LastRequestSelect = false;
            state.ExecuteAccepted = false;
            if (feedbackIoa.HasValue) state.FeedbackIoa = feedbackIoa;
            Update(state, item, "EXECUTE sent", "Following accepted SELECT; awaiting EXECUTE ACTCON.",
                "EXECUTE sent", "SELECT acceptance does not prove operation.");
            return true;
        }

        if (state is null)
        {
            _ = NewCase(item, key, target, feedbackIoa, "Unmatched RTU response",
                "No source/CA/IOA/Type/OA/link-matched Master request observed in this bounded session.",
                "Unmatched response");
            return true;
        }

        if (state.Ambiguous || select != state.LastRequestSelect ||
            !string.Equals(target, state.Target, StringComparison.Ordinal))
        {
            state.Ambiguous = true;
            Update(state, item, "RTU reply ambiguous", "Reply echo or phase mismatches Master request.",
                "Ambiguous", "Verify the original request/response and concurrent Masters.");
            return true;
        }

        if (cot.Negative || cot.Code is >= 44 and <= 47)
        {
            state.Negative = true;
            Update(state, item, "RTU rejection", $"COT={cot.Code}, P/N={(cot.Negative ? 1 : 0)}; application rejection observed.",
                "RTU rejected", "Check local/remote mode, interlocks, qualifier and target mapping; cause is not proven.");
            _active.Remove(key);
            return true;
        }

        if (cot.Code == 9)
        {
            Update(state, item, "RTU DEACTCON", "Deactivation confirmation, not control execution.",
                "Cancelled", "No physical operation proved.");
            _active.Remove(key);
            return true;
        }

        if (cot.Code == 7)
        {
            if (select) state.SelectAccepted = true;
            else state.ExecuteAccepted = true;
            Update(state, item, select ? "SELECT ACTCON" : "EXECUTE ACTCON",
                "Positive application activation confirmation (P/N=0).",
                select ? "SELECT accepted" : "EXECUTE accepted",
                "ACTCON confirms application acceptance only; wait for ACTTERM and matching feedback.");
            return true;
        }

        if (cot.Code == 10)
        {
            if (!state.ExecuteAccepted || select)
            {
                Update(state, item, "RTU ACTTERM without EXECUTE ACTCON",
                    "An application-phase completion cannot be paired to a confirmed execute.",
                    "Observed incomplete", "Original ACTCON may be missing from this capture.");
                return true;
            }
            state.Terminated = true;
            Update(state, item, "EXECUTE ACTTERM", "Application activation ended.",
                state.FeedbackMatched ? "Feedback matched" : "ACTTERM observed",
                state.FeedbackMatched
                    ? "Source-matched feedback reached expected digital state; this still does not independently prove physical equipment."
                    : "No verified target feedback yet; physical state remains unverified.");
            if (state.FeedbackMatched) _active.Remove(key);
            return true;
        }
        return false;
    }

    /// <summary>Mark older unfinished cases incomplete only with an explicitly selected
    /// observation-window boundary; a partial archive alone is not a device timeout.</summary>
    public void EndObservation()
    {
        foreach (var state in _active.Values)
        {
            if (state.Current.Status is "Ambiguous" or "RTU rejected" or "Cancelled") continue;
            Replace(state, state.Current with { Status = "Observed incomplete",
                Interpretation = "Observation window ended without full source-matched command and target feedback evidence. Not an RTU failure verdict." });
        }
        _active.Clear();
    }

    private bool ObserveDigitalFeedback(Iec103MasterEvidenceEvent item)
    {
        if (item.TypeId is not (1 or 3 or 30 or 31) ||
            item.AsduDetails is not { Issues: { Count: 0 } } asdu || asdu.CauseDetails?.Test == true ||
            item.Frame?.IsChecksumValid == false || item.CauseDetails?.Test == true)
            return false;

        var matched = false;
        foreach (var state in _active.Values.ToArray())
        {
            if (!state.Executed || !state.ExecuteAccepted || state.Ambiguous ||
                !state.FeedbackIoa.HasValue || !state.ExpectedDigital.HasValue ||
                state.Key.Protocol != item.ProtocolMode ||
                !string.Equals(state.Key.Source, Normalize(item.PhysicalSource), StringComparison.Ordinal) ||
                state.Key.Ca != asdu.CommonAddress ||
                (item.ProtocolMode == Iec60870ProtocolMode.Iec101 &&
                 state.Key.Link.HasValue && item.LinkAddress.HasValue &&
                 state.Key.Link != item.LinkAddress))
                continue;

            // All objects in SQ=0 and SQ=1 use the canonical decoded per-object IOA.
            var obj = asdu.Objects.FirstOrDefault(x => x.InformationObjectAddress == state.FeedbackIoa.Value);
            if (obj is null) continue;
            var q = obj.QualityDetails;
            if (q is not { Invalid: false } || q.Blocked == true || q.Substituted == true ||
                q.NotTopical == true || obj.Issues.Count > 0 || obj.RawBytes.Count == 0)
                continue;

            var observed = item.TypeId is 1 or 30 ? obj.RawBytes[0] & 1 : obj.RawBytes[0] & 3;
            var expected = state.ExpectedDigital.Value;
            var agrees = observed == expected;
            state.FeedbackMatched |= agrees;
            Update(state, item, agrees ? "Feedback matched" : "Feedback mismatch",
                $"Mapped IOA {state.FeedbackIoa}: observed digital code={observed}, expected={expected}, quality valid.",
                agrees ? state.Terminated ? "Feedback matched" : "Feedback matched · ACTTERM pending" : "Feedback mismatch",
                agrees
                    ? "Matching observed RTU digital state; not independently verified physical switchgear position."
                    : "The mapped feedback is not at the requested target; investigate relay/RTU/plant state.");
            if (agrees && state.Terminated) _active.Remove(state.Key);
            matched = true;
        }
        return matched;
    }

    private static int? ExpectedDigitalState(Iec10xCommandDetails command)
    {
        if (!command.QualifierRaw.HasValue) return null;
        var bits = command.QualifierRaw.Value;
        return command.TypeId switch
        {
            45 or 58 => bits & 1,
            46 or 59 when (bits & 3) is 1 or 2 => bits & 3,
            _ => null
        };
    }

    private CommandCase NewCase(Iec103MasterEvidenceEvent item, CommandKey key, string target,
        int? feedbackIoa, string phase, string explanation, string? status = null)
    {
        var id = ++_nextId;
        var c = new CommandCase(id, item.ProtocolMode == Iec60870ProtocolMode.Iec101 ? "IEC-101" : "IEC-104",
            key.Source, key.Link, key.Ca, key.Ioa, key.Type, target,
            status ?? phase, explanation, item.SequenceNumber, item.SequenceNumber, item.TimestampUtc,
            new[] { Stage(item, phase, explanation) }, feedbackIoa);
        _history.Add(c);
        if (_history.Count > _capacity) _history.RemoveAt(0);
        return c;
    }

    private void Update(State state, Iec103MasterEvidenceEvent item, string phase,
        string explanation, string status, string meaning)
    {
        var current = state.Current;
        var stages = current.Stages.Count < 24
            ? current.Stages.Append(Stage(item, phase, explanation)).ToArray()
            : current.Stages.Skip(1).Append(Stage(item, phase, explanation)).ToArray();
        Replace(state, current with { Status = status, Interpretation = meaning,
            LastSequence = item.SequenceNumber, Stages = stages });
    }

    private void Replace(State state, CommandCase next)
    {
        state.Current = next;
        var index = _history.FindIndex(c => c.CaseId == next.CaseId);
        if (index >= 0) _history[index] = next;
    }

    private static CommandStage Stage(Iec103MasterEvidenceEvent item, string phase, string explanation)
        => new(item.SequenceNumber, item.EvidenceReference, item.TimestampUtc, item.DirectionText, phase,
            item.CauseDetails?.Code, explanation, item.RawHex);

    private static string Normalize(string? source)
        => string.IsNullOrWhiteSpace(source) ? "SINGLE" : source.Trim().ToUpperInvariant();
}
