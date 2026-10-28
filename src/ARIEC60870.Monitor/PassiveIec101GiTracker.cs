// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Monitor;

public enum PassiveGiKind
{
    Activation,
    ActivationConfirmed,
    FirstInterrogatedData,
    Completed,
    NegativeConfirmation,
    UnmatchedResponse,
    DataBeforeConfirmation,
    RepeatedActivation,
    Superseded,
    TimedOut,
    PendingAtCaptureEnd,
    CapacityEviction
}

public sealed record PassiveGiEvent(
    PassiveGiKind Kind, DateTime EstimatedUtc, byte StreamId, long StreamOffset,
    byte RequestStreamId, long RequestOffset, int CommonAddress, int OriginatorAddress,
    int Qualifier, int InterrogatedDataFrames, bool IsNegative, string Meaning);

public sealed record PassiveGiOptions(
    int LinkAddressWidth = 1, int CotWidth = 2, int CommonAddressWidth = 2,
    int InformationAddressWidth = 3, int MaxTrackedSessions = 64,
    int TimeoutMilliseconds = 120000)
{
    public void Validate()
    {
        if (LinkAddressWidth is < 0 or > 2 || CotWidth is < 1 or > 2 ||
            CommonAddressWidth is < 1 or > 2 || InformationAddressWidth is < 1 or > 3 ||
            MaxTrackedSessions is < 1 or > 1024 ||
            TimeoutMilliseconds is < 1000 or > 1800000)
            throw new ArgumentOutOfRangeException(nameof(LinkAddressWidth),
                "Unsupported IEC-101 GI address widths or bounded session/timeout configuration.");
    }
}

/// <summary>
/// Passive GI lifecycle observation over complete validated FT1.2 frames.
/// No WPF, active-master polling, serial TX or wall-clock timers.
/// A gap or missing reply is only 'not observed', not proof of physical fault.
/// </summary>
public sealed class PassiveIec101GiTracker
{
    private readonly PassiveGiOptions _options;
    private readonly Dictionary<GiKey, GiPending> _pending = new();
    private readonly long[] _lastOffset = { -1, -1 };
    private DateTime _lastUtc = DateTime.MinValue;

    private readonly record struct GiKey(byte MasterStream, int CommonAddress, int Originator);
    private sealed class GiPending
    {
        public required PassiveObservedFrame Request { get; init; }
        public required int Qualifier { get; init; }
        public bool Confirmed;
        public int DataFrames;
        public bool FirstDataEmitted;
        public DateTime LastSeen;
    }

    public PassiveIec101GiTracker(PassiveGiOptions? options = null)
    {
        _options = options ?? new PassiveGiOptions();
        _options.Validate();
    }

    public int PendingCount => _pending.Count;

    public void Observe(PassiveObservedFrame frame, Action<PassiveGiEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(emit);
        if (frame.StreamId > 1 || frame.EstimatedUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("GI tracker requires two physical RX streams and UTC timestamps.");

        // Respect original byte ordering within each source. Cross-USB host time
        // cannot establish exact wire-edge ordering between the two COM adapters.
        if (frame.StreamOffset <= _lastOffset[frame.StreamId]) return;
        _lastOffset[frame.StreamId] = frame.StreamOffset;
        if (frame.EstimatedUtc > _lastUtc) _lastUtc = frame.EstimatedUtc;
        AdvanceTime(_lastUtc, emit);
        if (!TryReadAsdu(frame, out var type, out var cause, out var isNegative,
                out var commonAddress, out var originator, out var qualifier)) return;

        if (type == 100 && cause == 6)
        {
            var key = new GiKey(frame.StreamId, commonAddress, originator);
            if (_pending.TryGetValue(key, out var previous))
            {
                if (previous.Qualifier == qualifier &&
                    previous.Request.RawBytes.AsSpan().SequenceEqual(frame.RawBytes))
                {
                    Emit(emit, PassiveGiKind.RepeatedActivation, frame, previous, key, isNegative,
                        "Repeated GI activation with the same request bytes; not counted as a new successful GI.");
                    return;
                }
                Emit(emit, PassiveGiKind.Superseded, frame, previous, key, false,
                    "New GI activation observed before the previous activation terminated.");
                _pending.Remove(key);
            }
            if (_pending.Count >= _options.MaxTrackedSessions)
            {
                var oldest = _pending.OrderBy(x => x.Value.LastSeen).First();
                Emit(emit, PassiveGiKind.CapacityEviction, frame, oldest.Value, oldest.Key, false,
                    "Tracked GI capacity reached; previous incomplete GI evicted with explicit evidence.");
                _pending.Remove(oldest.Key);
            }
            var pending = new GiPending { Request = frame, Qualifier = qualifier, LastSeen = frame.EstimatedUtc };
            _pending.Add(key, pending);
            Emit(emit, PassiveGiKind.Activation, frame, pending, key, isNegative,
                "GI Type 100 / COT 6 activation observed; this does not prove a redundant-link switchover.");
            return;
        }

        // Confirmation / termination is an RTU response to the opposite COM.
        if (type == 100 && cause is 7 or 10)
        {
            var key = new GiKey((byte)(frame.StreamId ^ 1), commonAddress, originator);
            if (!_pending.TryGetValue(key, out var pending) || pending.Qualifier != qualifier)
            {
                emit(new PassiveGiEvent(PassiveGiKind.UnmatchedResponse, frame.EstimatedUtc,
                    frame.StreamId, frame.StreamOffset, key.MasterStream, -1, commonAddress,
                    originator, qualifier, 0, isNegative,
                    "GI confirmation/termination lacks a matching captured activation; recording may begin mid-session."));
                return;
            }

            if (cause == 7)
            {
                if (isNegative)
                {
                    Emit(emit, PassiveGiKind.NegativeConfirmation, frame, pending, key, true,
                        "RTU returned negative GI activation confirmation (Type 100 / COT 7 with P/N flag).");
                    _pending.Remove(key);
                    return;
                }
                if (!pending.Confirmed)
                {
                    pending.Confirmed = true;
                    pending.LastSeen = frame.EstimatedUtc;
                    Emit(emit, PassiveGiKind.ActivationConfirmed, frame, pending, key, false,
                        "RTU confirmed GI activation (Type 100 / COT 7).");
                }
                return;
            }

            Emit(emit, PassiveGiKind.Completed, frame, pending, key, isNegative,
                isNegative
                    ? "GI activation termination carries negative flag; review RTU behavior."
                    : "GI ACTTERM observed (Type 100 / COT 10). This ends the observed GI lifecycle.");
            _pending.Remove(key);
            return;
        }

        // Interrogated values have COT 20..36 and are NOT Type 100. Record
        // the first matching data telegram; aggregate later frames cheaply.
        if (type == 100 || cause is < 20 or > 36) return;
        var replyKey = new GiKey((byte)(frame.StreamId ^ 1), commonAddress, originator);
        if (!_pending.TryGetValue(replyKey, out var active)) return;
        if (active.Qualifier != 20 && active.Qualifier != cause) return;
        active.DataFrames++;
        active.LastSeen = frame.EstimatedUtc;
        if (active.FirstDataEmitted) return;
        active.FirstDataEmitted = true;
        Emit(emit, active.Confirmed ? PassiveGiKind.FirstInterrogatedData :
                PassiveGiKind.DataBeforeConfirmation, frame, active, replyKey, false,
            active.Confirmed
                ? "First data frame with interrogated COT observed after GI ACTCON."
                : "GI data observed before ACTCON in host receive order; cross-COM timestamp uncertainty applies.");
    }

    public void AdvanceTime(DateTime estimatedUtc, Action<PassiveGiEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        foreach (var entry in _pending.ToArray())
        {
            if ((estimatedUtc - entry.Value.Request.EstimatedUtc).TotalMilliseconds <
                _options.TimeoutMilliseconds) continue;
            Emit(emit, PassiveGiKind.TimedOut, entry.Value.Request, entry.Value,
                entry.Key, false, entry.Value.Confirmed
                    ? "GI activation confirmed but no matching ACTTERM was observed within the configured window."
                    : "GI activation observed but no matching ACTCON/ACTTERM within the configured window.");
            _pending.Remove(entry.Key);
        }
    }

    public void Complete(Action<PassiveGiEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        foreach (var entry in _pending)
            Emit(emit, PassiveGiKind.PendingAtCaptureEnd, entry.Value.Request, entry.Value,
                entry.Key, false, "Capture ended with an incomplete GI sequence; not proof of a timeout.");
        _pending.Clear();
    }

    private bool TryReadAsdu(PassiveObservedFrame frame, out byte type,
        out int cause, out bool isNegative, out int ca, out int originator, out int qualifier)
    {
        type = 0; cause = 0; isNegative = false; ca = 0; originator = 0; qualifier = 0;
        if (!frame.ChecksumValid || frame.Format != "Variable") return false;
        var raw = frame.RawBytes;
        var start = 5 + _options.LinkAddressWidth;
        var asduLength = raw.Length - start - 2;
        var headerLength = 2 + _options.CotWidth + _options.CommonAddressWidth;
        if (raw.Length < start + 2 || raw[0] != 0x68 ||
            raw[1] != raw[2] || raw[3] != 0x68 ||
            raw[1] != 1 + _options.LinkAddressWidth + asduLength ||
            raw[^1] != 0x16 || asduLength < headerLength) return false;

        type = raw[start];
        var vsq = raw[start + 1];
        var cotRaw = raw[start + 2];
        cause = cotRaw & 0x3F;
        isNegative = (cotRaw & 0x40) != 0;
        var at = start + 2 + _options.CotWidth;
        originator = _options.CotWidth == 2 ? raw[start + 3] : 0;
        for (var i = 0; i < _options.CommonAddressWidth; i++)
            ca |= raw[at + i] << (8 * i);
        at += _options.CommonAddressWidth;

        // Type100 must contain exactly one object (VSQ=1) with IOA=0
        // followed by QOI. Refuse malformed GI controls rather than guessing.
        if (type == 100)
        {
            if (vsq != 1 || asduLength != headerLength + _options.InformationAddressWidth + 1)
                return false;
            for (var i = 0; i < _options.InformationAddressWidth; i++)
                if (raw[at + i] != 0) return false;
            qualifier = raw[at + _options.InformationAddressWidth];
        }
        return true;
    }

    private static void Emit(Action<PassiveGiEvent> sink, PassiveGiKind kind,
        PassiveObservedFrame frame, GiPending pending, GiKey key, bool negative, string message)
        => sink(new PassiveGiEvent(kind, frame.EstimatedUtc, frame.StreamId, frame.StreamOffset,
            pending.Request.StreamId, pending.Request.StreamOffset,
            key.CommonAddress, key.Originator, pending.Qualifier,
            pending.DataFrames, negative, message));
}
