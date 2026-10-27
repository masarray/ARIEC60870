// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Monitor;

public enum PassiveTransactionKind
{
    Matched,
    NegativeReply,
    RetryObserved,
    ResponseTimeout,
    RequestSuperseded,
    UnmatchedReply,
    IncompatibleReply,
    AmbiguousSingleReply,
    InvalidFrame,
    DirectionConflict,
    SourceOrderConflict,
    DfcBusy,
    AcdDemand,
    ReusedConfirmedFcb,
    CapacityEviction,
    PendingAtEnd
}

/// <summary>Source offsets point to original binary journals, not to a bounded GUI row index.</summary>
public sealed record PassiveTransactionEvent(
    PassiveTransactionKind Kind, DateTime EstimatedUtc, int? LinkAddress,
    byte ObservedStream, long ObservedOffset, byte? RequestStream, long? RequestOffset,
    int? RequestFunction, int? ReplyFunction, bool? Fcb, bool? Fcv, bool? Acd,
    bool? Dfc, int Attempts, double? HostDelayMilliseconds, string Meaning);

public sealed record PassiveTransactionOptions(
    int LinkAddressWidth = 1, int ResponseTimeoutMilliseconds = 3000,
    int HostReorderToleranceMilliseconds = 100, int MaxTrackedLinks = 64,
    string LinkMode = "Auto")
{
    public void Validate()
    {
        if (LinkAddressWidth is < 0 or > 2 ||
            ResponseTimeoutMilliseconds is < 100 or > 120000 ||
            HostReorderToleranceMilliseconds is < 0 or > 2000 ||
            MaxTrackedLinks is < 1 or > 1024 ||
            LinkMode is not ("Auto" or "Unbalanced" or "Balanced"))
            throw new ArgumentOutOfRangeException(nameof(LinkAddressWidth),
                "Invalid passive link width, timeouts, link mode or resource limit.");
    }
}

/// <summary>
/// One-thread incremental IEC-101 *observation* state machine. Has no TX methods,
/// timers, WPF references or active Master coupling. Consume a complete two-direction
/// frame stream in host-receipt order; replay and live writers share these rules.
/// A single-character reply is addressless and MUST NOT be arbitrarily paired
/// when multiple candidates are open.
/// </summary>
public sealed class PassiveIec101TransactionCorrelator
{
    private readonly PassiveTransactionOptions _options;
    private readonly Dictionary<LinkKey, LinkState> _links = new();
    private readonly long[] _lastSourceOffset = { -1, -1 };
    private DateTime _latestUtc = DateTime.MinValue;
    private long _inputCount;
    private long _matchedCount;
    private long _timeoutCount;
    private long _retryCount;

    private readonly record struct LinkKey(byte PrimaryStream, int LinkAddress);

    private sealed class Pending
    {
        public required PassiveObservedFrame Frame { get; init; }
        public required int Function { get; init; }
        public required bool Fcb { get; init; }
        public required bool Fcv { get; init; }
        public int Attempts { get; set; } = 1;
    }

    private sealed class LinkState
    {
        public Pending? Pending;
        public bool? LastAcknowledgedFcb;
        public int? LastAcknowledgedFunction;
        public DateTime LastSeen;
    }

    public PassiveIec101TransactionCorrelator(PassiveTransactionOptions? options = null)
    {
        _options = options ?? new PassiveTransactionOptions();
        _options.Validate();
    }

    public long FramesSeen => _inputCount;
    public long Matched => _matchedCount;
    public long TimedOut => _timeoutCount;
    public long Retries => _retryCount;
    public int TrackedLinks => _links.Count;
    public int PendingCount => _links.Values.Count(x => x.Pending is not null);

    public void Observe(PassiveObservedFrame frame, Action<PassiveTransactionEvent> output)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(output);
        if (frame.StreamId > 1 || frame.EstimatedUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Correlator requires stream 0/1 and UTC observation timestamps.");
        _inputCount++;
        if (frame.StreamOffset <= _lastSourceOffset[frame.StreamId])
        {
            Emit(output, PassiveTransactionKind.SourceOrderConflict, frame, null, null,
                "Non-increasing original byte offset on the same RX source. Correlation skipped.");
            return;
        }
        _lastSourceOffset[frame.StreamId] = frame.StreamOffset;
        if (frame.EstimatedUtc > _latestUtc) _latestUtc = frame.EstimatedUtc;
        AdvanceTime(_latestUtc, output);

        if (!frame.ChecksumValid || frame.Format is "Partial" or not ("Fixed" or "Variable" or "Single"))
        {
            Emit(output, PassiveTransactionKind.InvalidFrame, frame, null, null,
                "Incomplete or invalid FT1.2 frame; not used to advance transactions.");
            return;
        }

        var raw = frame.RawBytes;
        if (frame.Format == "Single")
        {
            if (raw.Length != 1 || raw[0] is not (0xE5 or 0xA2))
            {
                Emit(output, PassiveTransactionKind.InvalidFrame, frame, null, null,
                    "Unknown single character; cannot resolve an exchange.");
                return;
            }
            MatchSingle(frame, raw[0] == 0xA2, output);
            return;
        }

        var index = frame.Format == "Fixed" ? 1 : 4;
        var minLength = (frame.Format == "Fixed" ? 4 : 6) + _options.LinkAddressWidth;
        if (raw.Length < minLength)
        {
            Emit(output, PassiveTransactionKind.InvalidFrame, frame, null, null,
                "Link frame too short for configured address width.");
            return;
        }
        var control = raw[index];
        var prm = (control & 0x40) != 0;
        var fc = control & 0x0f;
        var address = 0;
        for (var i = 0; i < _options.LinkAddressWidth; i++)
            address |= raw[index + i + 1] << (8 * i);

        if (prm)
        {
            ObservePrimary(frame, address, fc, (control & 0x20) != 0,
                (control & 0x10) != 0, output);
        }
        else
        {
            ObserveSecondary(frame, address, fc, (control & 0x20) != 0,
                (control & 0x10) != 0, output);
        }
    }

    public void AdvanceTime(DateTime estimatedUtc, Action<PassiveTransactionEvent> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var pair in _links)
        {
            var pending = pair.Value.Pending;
            if (pending is null) continue;
            if ((estimatedUtc - pending.Frame.EstimatedUtc).TotalMilliseconds <
                _options.ResponseTimeoutMilliseconds) continue;
            Emit(output, PassiveTransactionKind.ResponseTimeout, pending.Frame,
                pair.Key.LinkAddress, pending,
                "No compatible reply was observed before the configured host-receipt timeout. This is not proof of a physical link fault.");
            pair.Value.Pending = null;
            _timeoutCount++;
        }
    }

    public void Complete(Action<PassiveTransactionEvent> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var pair in _links)
        {
            var pending = pair.Value.Pending;
            if (pending is null) continue;
            Emit(output, PassiveTransactionKind.PendingAtEnd, pending.Frame, pair.Key.LinkAddress,
                pending, "Capture ended before a compatible reply was observed; not classified as a timeout.");
            pair.Value.Pending = null;
        }
    }

    private void ObservePrimary(PassiveObservedFrame frame, int addr, int fc, bool fcb,
        bool fcv, Action<PassiveTransactionEvent> output)
    {
        if (_options.LinkMode == "Unbalanced" && frame.StreamId == 1)
            Emit(output, PassiveTransactionKind.DirectionConflict, frame, addr, null,
                "PRM=1 primary frame was observed on the configured RTU transmitter input; check tap labels, echo or link mode.");

        // Unconfirmed transmission does not require a secondary acknowledgment.
        if (fc == 4) return;
        if (fc is not (0 or 1 or 3 or 7 or 9 or 10 or 11)) return;

        var key = new LinkKey(frame.StreamId, addr);
        if (!_links.TryGetValue(key, out var slot))
        {
            if (_links.Count >= _options.MaxTrackedLinks)
            {
                var oldest = _links.OrderBy(x => x.Value.LastSeen).First();
                if (oldest.Value.Pending is { } lost)
                    Emit(output, PassiveTransactionKind.CapacityEviction, lost.Frame,
                        oldest.Key.LinkAddress, lost,
                        "Tracked link limit reached; outstanding request evicted, not silently dropped.");
                _links.Remove(oldest.Key);
            }
            slot = new LinkState();
            _links.Add(key, slot);
        }
        slot.LastSeen = frame.EstimatedUtc;

        if (slot.Pending is { } previous)
        {
            if (fcv && previous.Fcv && fcb == previous.Fcb &&
                fc == previous.Function && previous.Frame.RawBytes.AsSpan().SequenceEqual(frame.RawBytes))
            {
                previous.Attempts++;
                _retryCount++;
                Emit(output, PassiveTransactionKind.RetryObserved, frame, addr, previous,
                    "Identical FCV/FCB request retransmitted without an observed compatible response. Earliest request time retained.");
                return;
            }
            Emit(output, PassiveTransactionKind.RequestSuperseded, frame, addr, previous,
                "Another primary request arrived before the previous exchange had a compatible reply; response absence is not yet proven.");
        }

        if (fc is 0 or 7)
        {
            // Link reset makes earlier FCB continuity comparisons inapplicable.
            slot.LastAcknowledgedFcb = null;
            slot.LastAcknowledgedFunction = null;
        }
        else if (fcv && slot.LastAcknowledgedFcb == fcb &&
                 slot.LastAcknowledgedFunction == fc)
            Emit(output, PassiveTransactionKind.ReusedConfirmedFcb, frame, addr, null,
                "Previously acknowledged FCB reused for the same service without an observed reset; inspect trace before treating as a fault.");

        slot.Pending = new Pending { Frame = frame, Function = fc, Fcb = fcb, Fcv = fcv };
    }

    private void ObserveSecondary(PassiveObservedFrame frame, int addr, int fc, bool acd,
        bool dfc, Action<PassiveTransactionEvent> output)
    {
        var key = new LinkKey((byte)(frame.StreamId ^ 1), addr);
        _links.TryGetValue(key, out var slot);
        var pending = slot?.Pending;
        if (pending is null)
        {
            Emit(output, PassiveTransactionKind.UnmatchedReply, frame, addr, null,
                "Secondary reply observed without a tracked request; capture may have begun mid-exchange.");
        }
        else if (!IsCompatible(pending.Function, fc))
        {
            Emit(output, PassiveTransactionKind.IncompatibleReply, frame, addr, pending,
                "Secondary function is incompatible with the pending request; not force-matched.");
        }
        else if (IsOutsideCorrelationWindow(frame.EstimatedUtc, pending.Frame.EstimatedUtc))
        {
            Emit(output, PassiveTransactionKind.UnmatchedReply, frame, addr, pending,
                "Reply timestamp is outside the correlation window; USB cross-port ordering may be uncertain.");
        }
        else
        {
            CompleteMatch(frame, addr, slot!, pending, fc, acd, dfc, output);
        }

        if (dfc)
            Emit(output, PassiveTransactionKind.DfcBusy, frame, addr, pending,
                "RTU secondary control reports DFC=1 (busy); do not treat this as positive acknowledgment.");
        if (acd)
            Emit(output, PassiveTransactionKind.AcdDemand, frame, addr, pending,
                "RTU secondary control reports ACD=1: Class 1 data may be pending. Not a fault by itself.");
    }

    private void MatchSingle(PassiveObservedFrame frame, bool negative,
        Action<PassiveTransactionEvent> output)
    {
        var candidates = _links.Where(x =>
            x.Key.PrimaryStream != frame.StreamId && x.Value.Pending is not null).ToArray();
        if (candidates.Length == 0)
        {
            Emit(output, PassiveTransactionKind.UnmatchedReply, frame, null, null,
                "Single-character ACK/NACK without a tracked request; link address unavailable.");
            return;
        }
        if (candidates.Length != 1)
        {
            Emit(output, PassiveTransactionKind.AmbiguousSingleReply, frame, null, null,
                "Addressless ACK/NACK could match multiple open exchanges; refusing to choose arbitrarily.");
            return;
        }
        var c = candidates[0];
        var pending = c.Value.Pending!;
        if (IsOutsideCorrelationWindow(frame.EstimatedUtc, pending.Frame.EstimatedUtc))
        {
            Emit(output, PassiveTransactionKind.UnmatchedReply, frame, c.Key.LinkAddress, pending,
                "Single-character reply outside host timing window.");
            return;
        }
        // E5 is ACK, not proof that a Class 1/Class 2 poll had no data. A2 is NACK.
        CompleteMatch(frame, c.Key.LinkAddress, c.Value, pending,
            negative ? 1 : 0, false, false, output, isSingle: true);
    }

    private void CompleteMatch(PassiveObservedFrame response, int addr, LinkState slot,
        Pending pending, int replyFc, bool acd, bool dfc,
        Action<PassiveTransactionEvent> output, bool isSingle = false)
    {
        var negative = replyFc is 1 or 14 or 15;
        var delay = (response.EstimatedUtc - pending.Frame.EstimatedUtc).TotalMilliseconds;
        var code = negative ? PassiveTransactionKind.NegativeReply : PassiveTransactionKind.Matched;
        var description = isSingle
            ? (negative ? "Single-character NACK (A2)" : "Single-character ACK (E5): not evidence of no requested data")
            : replyFc switch
            {
                8 => "Secondary user data response",
                9 => "Secondary no requested data (FC9)",
                11 => "Secondary link status response (FC11)",
                0 => "Secondary ACK",
                1 => "Secondary NACK",
                14 or 15 => "Secondary link service error",
                _ => "Secondary response"
            };
        Emit(output, code, response, addr, pending,
            description + "; timestamps are per-adapter host receive estimates, not wire-edge latency.",
            replyFc, acd, dfc, delay < 0 ? null : delay);
        if (!negative && !dfc)
        {
            _matchedCount++;
            if (pending.Fcv)
            {
                slot.LastAcknowledgedFcb = pending.Fcb;
                slot.LastAcknowledgedFunction = pending.Function;
            }
        }
        slot.Pending = null;
        slot.LastSeen = response.EstimatedUtc;
    }

    private bool IsOutsideCorrelationWindow(DateTime reply, DateTime request)
    {
        var gap = (reply - request).TotalMilliseconds;
        return gap < -_options.HostReorderToleranceMilliseconds ||
               gap > _options.ResponseTimeoutMilliseconds;
    }

    private static bool IsCompatible(int request, int reply) => request switch
    {
        0 or 1 or 7 or 3 => reply is 0 or 1 or 14 or 15,
        9 => reply is 11 or 1 or 14 or 15,
        10 or 11 => reply is 8 or 9 or 1 or 14 or 15,
        _ => false
    };

    private static void Emit(Action<PassiveTransactionEvent> output,
        PassiveTransactionKind kind, PassiveObservedFrame observed, int? address,
        Pending? pending, string description, int? replyFc = null,
        bool? acd = null, bool? dfc = null, double? delay = null)
    {
        output(new PassiveTransactionEvent(kind, observed.EstimatedUtc, address,
            observed.StreamId, observed.StreamOffset,
            pending?.Frame.StreamId, pending?.Frame.StreamOffset,
            pending?.Function, replyFc, pending?.Fcb, pending?.Fcv,
            acd, dfc, pending?.Attempts ?? 0, delay, description));
    }
}
