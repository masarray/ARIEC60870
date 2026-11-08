// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Monitor;

public sealed record PassiveProcessEventCandidate(int ObjectIndex, int TypeId,
    int Cot, int CommonAddress, int Ioa, string Value, string Quality,
    string DeviceTime, bool Negative, bool Test, bool Changed, bool HadBaseline);

/// <summary>
/// Latest trusted process observation per physical circuit / CA / IOA.
/// This is an independent Values snapshot, never an Event/SOE selection rule.
/// The original frame and object ordinal survive a lossy UI frame preview.
/// </summary>
public sealed record PassiveLatestPoint(long Revision, PassiveObservedFrame Frame,
    int ObjectIndex, int TypeId, int CommonAddress, int Ioa,
    bool WasChanged, bool HadBaseline);

public sealed record PassiveLatestPointPage(long ThroughRevision, long LatestRevision,
    int PointCount, long CapacityRejected, IReadOnlyList<PassiveLatestPoint> Points);

/// <summary>
/// Writer-owned accounting for decoded source facts, not upstream FIFO delivery.
/// Live snapshots may be in-flight; final snapshots balance exactly.
/// </summary>
public sealed record PassiveProcessDecodeCounters(
    long FramesObserved, long NonVariableFrames, long InvalidVariableFrames,
    long PrimaryFrames, long SecondaryFrames, long AsdusDecoded,
    long AsdusRejected, long ObjectsDecoded, long ObjectsAccepted,
    long ObjectsExcluded, long UntrackedStateObservations)
{
    public bool FramesBalance => FramesObserved ==
        NonVariableFrames + InvalidVariableFrames + PrimaryFrames + SecondaryFrames;
    public bool AsdusBalance => SecondaryFrames == AsdusDecoded + AsdusRejected;
    public bool ObjectsBalance => ObjectsDecoded == ObjectsAccepted + ObjectsExcluded;
}

/// <summary>
/// Single-writer IEC-101 process observation classifier. Runs on the complete
/// capture stream, independent of the lossy WPF frame preview. Type/quality
/// decoding comes only from the canonical IEC10x Core codec.
/// </summary>
public sealed class PassiveIec101ProcessEventClassifier
{
    private const int MaxStateKeys = 65536;
    private const int MaxLatestPoints = 65536;
    private readonly object _latestGate = new();
    private readonly Dictionary<LatestKey, PassiveLatestPoint> _latestPoints = new();
    // Version index is bounded by live point cardinality, not observation count.
    // Pages are O(log N + page) rather than sorting every point on every UI tick.
    private readonly SortedSet<long> _latestRevisions = new();
    private readonly Dictionary<long, PassiveLatestPoint> _latestByRevision = new();
    private long _latestRevision;
    private long _latestCapacityRejected;
    private readonly record struct LatestKey(int Circuit, int CommonAddress, int Ioa);
    private readonly int _linkWidth;
    private readonly Iec10xAsduDecoder _decoder;
    private readonly Dictionary<StateKey, string> _previous = new();
    private long _untrackedStateObservations;
    private long _framesObserved;
    private long _nonVariableFrames;
    private long _invalidVariableFrames;
    private long _primaryFrames;
    private long _secondaryFrames;
    private long _asdusDecoded;
    private long _asdusRejected;
    private long _objectsDecoded;
    private long _objectsAccepted;
    private long _objectsExcluded;

    private readonly record struct StateKey(int Circuit, int Ca, int Ioa, int TypeId);

    public PassiveIec101ProcessEventClassifier(
        int linkWidth, int cotWidth, int caWidth, int ioaWidth)
    {
        if (linkWidth is < 0 or > 2 || cotWidth is < 1 or > 2 ||
            caWidth is < 1 or > 2 || ioaWidth is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(linkWidth));
        _linkWidth = linkWidth;
        _decoder = new Iec10xAsduDecoder(cotWidth, caWidth, ioaWidth);
    }

    /// <summary>
    /// State tracking can be exhausted under abnormal cardinality. Such new
    /// keys remain untracked (no fabricated GI/cyclic change), while spontaneous
    /// observations remain eligible. This counter makes the limitation explicit.
    /// </summary>
    public long UntrackedStateObservations => Interlocked.Read(ref _untrackedStateObservations);

    /// <summary>
    /// Pages changed *latest* points in source-publisher revision order. A slow GUI
    /// may coalesce intermediate values, but never misses a distinct latest point.
    /// No growing update queue, no Dispatcher work and no source-frame preview dependency.
    /// </summary>
    public PassiveLatestPointPage ReadLatestPointsAfter(long revision, int limit = 120)
    {
        if (revision < 0 || limit is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(revision));
        lock (_latestGate)
        {
            if (revision >= _latestRevision)
                return new PassiveLatestPointPage(_latestRevision, _latestRevision,
                    _latestPoints.Count, _latestCapacityRejected, Array.Empty<PassiveLatestPoint>());
            var page = _latestRevisions.GetViewBetween(revision + 1, _latestRevision)
                .Take(limit).Select(version => _latestByRevision[version]).ToArray();
            return new PassiveLatestPointPage(page.Length > 0 ? page[^1].Revision : _latestRevision,
                _latestRevision, _latestPoints.Count, _latestCapacityRejected, page);
        }
    }

    private void UpdateLatestPoint(PassiveObservedFrame frame, int objectIndex,
        int typeId, int ca, int ioa, bool changed, bool hadBaseline)
    {
        var key = new LatestKey(frame.StreamId / 2, ca, ioa);
        lock (_latestGate)
        {
            if (!_latestPoints.ContainsKey(key) && _latestPoints.Count >= MaxLatestPoints)
            {
                _latestCapacityRejected++;
                return;
            }
            if (_latestPoints.TryGetValue(key, out var previous))
            {
                _latestRevisions.Remove(previous.Revision);
                _latestByRevision.Remove(previous.Revision);
            }
            var latest = new PassiveLatestPoint(++_latestRevision, frame,
                objectIndex, typeId, ca, ioa, changed, hadBaseline);
            _latestPoints[key] = latest;
            _latestRevisions.Add(latest.Revision);
            _latestByRevision.Add(latest.Revision, latest);
        }
    }

    public PassiveProcessDecodeCounters GetCounters() => new(
        Interlocked.Read(ref _framesObserved),
        Interlocked.Read(ref _nonVariableFrames),
        Interlocked.Read(ref _invalidVariableFrames),
        Interlocked.Read(ref _primaryFrames),
        Interlocked.Read(ref _secondaryFrames),
        Interlocked.Read(ref _asdusDecoded),
        Interlocked.Read(ref _asdusRejected),
        Interlocked.Read(ref _objectsDecoded),
        Interlocked.Read(ref _objectsAccepted),
        Interlocked.Read(ref _objectsExcluded),
        Interlocked.Read(ref _untrackedStateObservations));

    /// <summary>Appends the ONE-based ordinal of each accepted source object.</summary>
    public void Observe(PassiveObservedFrame frame, List<PassiveProcessEventCandidate> acceptedObjectIndexes)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(acceptedObjectIndexes);
        Interlocked.Increment(ref _framesObserved);
        if (frame.Format != "Variable")
        {
            Interlocked.Increment(ref _nonVariableFrames);
            return;
        }
        if (!frame.ChecksumValid ||
            !Ft12WireLayout.TryParse(frame.RawBytes,
                frame.DetectedLinkAddressWidth ?? _linkWidth, out var wire) ||
            wire.Kind != Ft12WireKind.Variable)
        {
            Interlocked.Increment(ref _invalidVariableFrames);
            return;
        }
        if ((wire.Control & 0x40) != 0)
        {
            Interlocked.Increment(ref _primaryFrames);
            return;
        }
        Interlocked.Increment(ref _secondaryFrames);
        if (wire.AsduLength == 0)
        {
            Interlocked.Increment(ref _asdusRejected);
            return;
        }

        // An FT1.2 checksum is NOT enough to establish a decodable ASDU.
        var asdu = _decoder.Decode(
            frame.RawBytes.AsSpan(wire.AsduOffset, wire.AsduLength).ToArray());
        if (asdu.Issues.Count > 0 || asdu.ObjectCount != asdu.Objects.Count ||
            asdu.ObjectCount == 0)
        {
            Interlocked.Increment(ref _asdusRejected);
            return;
        }
        Interlocked.Increment(ref _asdusDecoded);
        Interlocked.Add(ref _objectsDecoded, asdu.Objects.Count);

        var digital = Iec10xProcessEventPolicy.IsDigitalProcessType(asdu.TypeId);
        var measurement = Iec10xProcessEventPolicy.IsMeasurementOrCounterType(asdu.TypeId);
        // Keep parity with the supported IEC-101 process families of the
        // existing Values/Event projector. Unsupported/vendor-private stays raw.
        if ((!digital && !measurement) ||
            asdu.TypeId is not (>= 1 and <= 16 or >= 30 and <= 37))
        {
            Interlocked.Add(ref _objectsExcluded, asdu.Objects.Count);
            return;
        }

        foreach (var obj in asdu.Objects)
        {
            if (obj.Issues.Count > 0 || obj.InformationObjectAddress is < 0 or > 0xFFFFFF)
            {
                Interlocked.Increment(ref _objectsExcluded);
                continue;
            }
            var changed = false;
            var hadBaseline = false;
            // The only cyclic/GI change events are known SP/DP values, never
            // a guessed edge from missing baseline or unsupported quality.
            // Invalid/blocked/substituted/not-topical, TEST and NEG are
            // observations, never credible prior physical-state baselines.
            var credibleState = !asdu.IsTest && !asdu.IsNegativeConfirm &&
                obj.QualityDetails is
                { Invalid: false, Blocked: not true, NotTopical: not true, Substituted: not true };
            if (credibleState && asdu.TypeId is 1 or 2 or 3 or 4 or 30 or 31)
            {
                var key = new StateKey(frame.StreamId / 2, asdu.CommonAddress,
                    obj.InformationObjectAddress, asdu.TypeId);
                if (_previous.TryGetValue(key, out var last))
                {
                    hadBaseline = true;
                    changed = !string.Equals(last, obj.ShortValue, StringComparison.Ordinal);
                    _previous[key] = obj.ShortValue;
                }
                else if (_previous.Count < MaxStateKeys)
                    _previous.Add(key, obj.ShortValue);
                else
                    Interlocked.Increment(ref _untrackedStateObservations);
            }

            // Snapshot observations and event selection are independent. A first
            // GI COT20 digital baseline is a real current point even when it did
            // not change. TEST / negative observations must not replace live state.
            if (!asdu.IsTest && !asdu.IsNegativeConfirm &&
                asdu.CauseOfTransmission is 1 or 2 or 3 or 11 or 12 or (>= 20 and <= 36))
                UpdateLatestPoint(frame, obj.Index, asdu.TypeId, asdu.CommonAddress,
                    obj.InformationObjectAddress, changed, hadBaseline);

            if (Iec10xProcessEventPolicy.ShouldRecordProcessType(
                asdu.TypeId, asdu.CauseOfTransmission, changed))
            {
                acceptedObjectIndexes.Add(new PassiveProcessEventCandidate(obj.Index,
                    asdu.TypeId, asdu.CauseOfTransmission, asdu.CommonAddress,
                    obj.InformationObjectAddress, obj.ShortValue, obj.QualityText,
                    obj.TimestampText, asdu.IsNegativeConfirm, asdu.IsTest, changed,
                    hadBaseline));
                Interlocked.Increment(ref _objectsAccepted);
            }
            else
                Interlocked.Increment(ref _objectsExcluded);
        }
    }
}
