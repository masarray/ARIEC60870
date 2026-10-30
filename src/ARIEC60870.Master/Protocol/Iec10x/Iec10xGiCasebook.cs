// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>
/// Bounded, evidence-only GI conversation ledger shared by active IEC-101/104 and passive
/// IEC-101 observations. Consumes the authoritative typed ASDU; never re-parses bytes,
/// sends commands or equates ACTTERM with measured-value coverage.
/// </summary>
public sealed class Iec10xGiCasebook
{
    private const int MaxActive = 64;
    private const int MaxUniqueIoas = 2048;
    private const int MaxSeenFrames = 8192;
    private readonly int _capacity;
    private readonly List<GiCase> _history = new();
    private readonly Dictionary<GiKey, GiState> _active = new();
    private long _nextId;

    private readonly record struct GiKey(Iec60870ProtocolMode Protocol, string Source,
        int? Link, int Ca, int Originator, int Qoi);

    private sealed class GiState
    {
        public required GiKey Key { get; init; }
        public required GiCase Current { get; set; }
        public readonly HashSet<int> UniqueIoas = new();
        public readonly HashSet<(long Sequence, string Reference)> Seen = new();
        public int DataFrames;
        public int DataObjects;
        public int InvalidQuality;
        public int Class1Requests;
        public int Class1UserData;
        public bool Actcon;
        public bool Ambiguous;
        public bool Truncated;
    }

    public sealed record GiStage(long Sequence, string EvidenceReference,
        DateTime HostUtc, string Direction, string Phase, string RawHex);

    public sealed record GiCase(long CaseId, string Protocol, string Source, int? LinkAddress,
        int CommonAddress, int Originator, int Qoi, string Status,
        int DataFrames, int DataObjects, int UniqueIoas, int InvalidQualityObjects,
        int Class1Requests, int Class1Responses, bool CountLimited,
        long FirstSequence, long LastSequence, IReadOnlyList<GiStage> Stages)
    {
        public string Description => (Qoi == 20 ? "Station GI" : $"Group GI {Qoi - 20}") +
            $" · CA {CommonAddress} · {Source}";
        public string Coverage => "Unknown — expected IOA mapping and complete capture not proven";
        public string Summary => $"{Description} · {Status} · data {DataFrames} frames / " +
            $"{DataObjects} objects / {UniqueIoas} unique IOAs · invalid quality {InvalidQualityObjects}" +
            (CountLimited ? " · counting limit reached" : "");
    }

    public sealed record GiMilestone(GiCase Case, string Phase);

    public Iec10xGiCasebook(int capacity = 160)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int ActiveCount => _active.Count;
    public IReadOnlyList<GiCase> Snapshot() => _history.ToArray();

    public void Reset()
    {
        _history.Clear();
        _active.Clear();
        _nextId = 0;
    }

    /// <summary>Returns only major lifecycle evidence for Event Log; GI data updates case
    /// counters but does not spam SOE with every repeated snapshot.</summary>
    public GiMilestone? Observe(Iec103MasterEvidenceEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104) ||
            item.Direction is not (FrameDirection.MasterToSlave or FrameDirection.SlaveToMaster) ||
            item.Frame is { IsChecksumValid: false } ||
            item.Frame is { IsLengthValid: false } ||
            item.AsduDetails?.Issues.Count > 0 ||
            string.Equals(item.Category, "RX Warning", StringComparison.OrdinalIgnoreCase))
            return null;

        var source = Normalize(item.PhysicalSource);
        var link = item.ProtocolMode == Iec60870ProtocolMode.Iec101 ? item.LinkAddress : null;
        var asdu = item.AsduDetails;

        if (item.ProtocolMode == Iec60870ProtocolMode.Iec101 && item.Frame?.LinkControl is { } lc &&
            asdu is null && lc.IsPrimaryRequestClass1)
        {
            var candidates = ActiveOnPhysicalSource(item.ProtocolMode, source, link);
            if (candidates.Length == 1) candidates[0].Class1Requests++;
            return null;
        }

        if (asdu is null || asdu.CauseDetails is not { } cot || cot.Test)
            return null;

        if (asdu.TypeId == 100 && cot.Code is 6 or 7 or 10)
        {
            var qoi = asdu.CommandDetails?.QualifierValue;
            if (qoi is not >= 20 and <= 36) return null;
            var key = new GiKey(item.ProtocolMode, source, link, asdu.CommonAddress,
                cot.OriginatorAddress ?? asdu.OriginatorAddress, qoi.Value);
            if (cot.Code == 6 && item.Direction == FrameDirection.MasterToSlave)
                return Begin(item, key);
            if (item.Direction != FrameDirection.SlaveToMaster) return null;
            if (!_active.TryGetValue(key, out var existing))
                return Orphan(item, key, cot.Code == 10 ? "Unmatched GI ACTTERM" : "Unmatched GI ACTCON");
            if (!RecordOnce(existing, item)) return null;
            if (existing.Ambiguous)
                return Advance(existing, item, "Ambiguous GI response", "Ambiguous", remove: cot.Code == 10);
            if (cot.Negative)
                return Advance(existing, item, "Negative GI confirmation", "GI rejected", remove: true);
            if (cot.Code == 7)
            {
                existing.Actcon = true;
                return Advance(existing, item, "Positive GI ACTCON", "GI acknowledged");
            }
            return Advance(existing, item, "GI ACTTERM",
                existing.Actcon ? "ACTTERM observed · coverage unknown" :
                    "ACTTERM without observed ACTCON · incomplete",
                remove: true);
        }

        if (item.Direction != FrameDirection.SlaveToMaster ||
            cot.Code is not (>= 20 and <= 36)) return null;

        // Interrogated data can share an ASDU with multiple decoded SQ=0/SQ=1 IOAs.
        // Never attribute data across CA, source, or two overlapping GI transactions.
        var matches = _active.Values.Where(s => s.Key.Protocol == item.ProtocolMode &&
            s.Key.Source == source && s.Key.Link == link &&
            s.Key.Ca == asdu.CommonAddress &&
            s.Key.Originator == (cot.OriginatorAddress ?? asdu.OriginatorAddress) &&
            (s.Key.Qoi == 20 || s.Key.Qoi == cot.Code)).ToArray();
        if (matches.Length != 1)
        {
            if (matches.Length > 1)
                foreach (var s in matches) MarkAmbiguous(s);
            return null;
        }

        var state = matches[0];
        if (state.Ambiguous || !RecordOnce(state, item)) return null;
        state.DataFrames++;
        state.DataObjects += asdu.Objects.Count;
        foreach (var obj in asdu.Objects)
        {
            if (obj.QualityDetails?.Invalid == true || obj.Issues.Count > 0) state.InvalidQuality++;
            if (state.UniqueIoas.Count < MaxUniqueIoas)
                state.UniqueIoas.Add(obj.InformationObjectAddress);
            else if (!state.UniqueIoas.Contains(obj.InformationObjectAddress))
                state.Truncated = true;
        }
        if (item.ProtocolMode == Iec60870ProtocolMode.Iec101 &&
            item.Frame?.LinkControl is { IsSecondaryUserData: true } &&
            string.Equals(item.DataClass, "Class 1", StringComparison.OrdinalIgnoreCase))
            state.Class1UserData++;

        // Preserve first data evidence, but avoid O(N) stage growth and Event rows.
        if (state.DataFrames == 1)
            Append(state, item, "First interrogated data");
        else
            Sync(state);
        return null;
    }

    public void EndObservation()
    {
        foreach (var state in _active.Values)
        {
            state.Current = state.Current with {
                Status = state.Ambiguous ? "Ambiguous" : "Observed incomplete · capture window ended"
            };
            Sync(state);
        }
        _active.Clear();
    }

    private GiMilestone Begin(Iec103MasterEvidenceEvent item, GiKey key)
    {
        if (_active.TryGetValue(key, out var prior))
        {
            MarkAmbiguous(prior);
            Append(prior, item, "Repeated GI activation");
            return new GiMilestone(prior.Current, "Repeated GI request · ambiguous");
        }
        foreach (var other in _active.Values.Where(s => s.Key.Protocol == key.Protocol &&
            s.Key.Source == key.Source && s.Key.Link == key.Link && s.Key.Ca == key.Ca))
            MarkAmbiguous(other);

        if (_active.Count >= MaxActive)
        {
            var oldest = _active.Values.OrderBy(x => x.Current.FirstSequence).First();
            oldest.Current = oldest.Current with { Status = "Observed incomplete · index limit" };
            Sync(oldest);
            _active.Remove(oldest.Key);
        }
        var c = new GiCase(++_nextId,
            key.Protocol == Iec60870ProtocolMode.Iec101 ? "IEC-101" : "IEC-104",
            key.Source, key.Link, key.Ca, key.Originator, key.Qoi,
            "GI requested", 0, 0, 0, 0, 0, 0, false,
            item.SequenceNumber, item.SequenceNumber, new[] { Stage(item, "GI request") });
        _history.Add(c);
        if (_history.Count > _capacity) _history.RemoveAt(0);
        var s = new GiState { Key = key, Current = c };
        s.Seen.Add((item.SequenceNumber, item.EvidenceReference));
        _active[key] = s;
        return new GiMilestone(c, "GI request");
    }

    private GiMilestone Orphan(Iec103MasterEvidenceEvent item, GiKey key, string phase)
    {
        var c = new GiCase(++_nextId,
            key.Protocol == Iec60870ProtocolMode.Iec101 ? "IEC-101" : "IEC-104",
            key.Source, key.Link, key.Ca, key.Originator, key.Qoi,
            "Unmatched response · request not observed", 0, 0, 0, 0, 0, 0, false,
            item.SequenceNumber, item.SequenceNumber, new[] { Stage(item, phase) });
        _history.Add(c);
        if (_history.Count > _capacity) _history.RemoveAt(0);
        return new GiMilestone(c, phase);
    }

    private GiMilestone Advance(GiState s, Iec103MasterEvidenceEvent item,
        string phase, string status, bool remove = false)
    {
        s.Current = s.Current with { Status = status };
        Append(s, item, phase);
        if (remove) _active.Remove(s.Key);
        return new GiMilestone(s.Current, phase);
    }

    private void Append(GiState s, Iec103MasterEvidenceEvent item, string phase)
    {
        var stages = s.Current.Stages.Count < 24 ?
            s.Current.Stages.Append(Stage(item, phase)).ToArray() :
            s.Current.Stages.Skip(1).Append(Stage(item, phase)).ToArray();
        s.Current = s.Current with { LastSequence = item.SequenceNumber, Stages = stages };
        Sync(s);
    }

    private void Sync(GiState s)
    {
        s.Current = s.Current with {
            DataFrames = s.DataFrames, DataObjects = s.DataObjects,
            UniqueIoas = s.UniqueIoas.Count, InvalidQualityObjects = s.InvalidQuality,
            Class1Requests = s.Class1Requests, Class1Responses = s.Class1UserData,
            CountLimited = s.Truncated
        };
        var index = _history.FindIndex(c => c.CaseId == s.Current.CaseId);
        if (index >= 0) _history[index] = s.Current;
    }

    private bool RecordOnce(GiState s, Iec103MasterEvidenceEvent item)
    {
        var identity = (item.SequenceNumber, item.EvidenceReference);
        if (s.Seen.Contains(identity)) return false;
        if (s.Seen.Count >= MaxSeenFrames)
        {
            s.Truncated = true;
            // Avoid unbounded memory; counting from here is incomplete, not verified coverage.
            return false;
        }
        return s.Seen.Add(identity);
    }

    private void MarkAmbiguous(GiState s)
    {
        s.Ambiguous = true;
        s.Current = s.Current with { Status = "Ambiguous · overlapping GI" };
        Sync(s);
    }

    private GiState[] ActiveOnPhysicalSource(Iec60870ProtocolMode protocol, string source, int? link) =>
        _active.Values.Where(s => s.Key.Protocol == protocol &&
            s.Key.Source == source && s.Key.Link == link).ToArray();

    private static GiStage Stage(Iec103MasterEvidenceEvent item, string phase) =>
        new(item.SequenceNumber, item.EvidenceReference, item.TimestampUtc,
            item.DirectionText, phase, item.RawHex);

    private static string Normalize(string? source) =>
        string.IsNullOrWhiteSpace(source) ? "SINGLE" : source.Trim().ToUpperInvariant();
}
