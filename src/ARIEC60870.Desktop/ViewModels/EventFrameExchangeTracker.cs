// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Owns IEC-101 request/reply evidence independently of the lossy WPF Trace preview.
/// A physical secondary response consumes the one outstanding master poll for its
/// source/link, including NO DATA/ACK/invalid replies. Every decoded IOA projected
/// from that SAME physical response can still reference its single matched TX.
/// Never infer request ownership from a nearby IOA, timestamp or unrelated link.
/// </summary>
public sealed class EventFrameExchangeTracker
{
    private sealed class LinkSlot
    {
        public Iec103MasterEvidenceEvent? PendingPoll;
        public Iec103MasterEvidenceEvent? LastResponse;
        public Iec103MasterEvidenceEvent? MatchedPoll;
        public long Touch;
    }

    private readonly Dictionary<(string Source, int Link), LinkSlot> _links = new();
    private readonly int _maxTrackedLinks;
    private long _nextTouch;

    public EventFrameExchangeTracker(int maxTrackedLinks = 256)
    {
        if (maxTrackedLinks is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(maxTrackedLinks));
        _maxTrackedLinks = maxTrackedLinks;
    }

    public int TrackedLinkCount => _links.Count;

    public void Reset()
    {
        _links.Clear();
        _nextTouch = 0;
    }

    /// <summary>Observe one real physical telegram, not a protocol state note.
    /// Repeated per-object projections of one frame are idempotent.</summary>
    public void Observe(Iec103MasterEvidenceEvent frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!EventFrameExchange.TryLinkKey(frame, out var key) ||
            string.IsNullOrWhiteSpace(frame.RawHex) ||
            frame.Direction is not (FrameDirection.MasterToSlave or FrameDirection.SlaveToMaster))
            return;

        if (!_links.TryGetValue(key, out var slot))
        {
            if (_links.Count >= _maxTrackedLinks)
            {
                var oldest = _links.MinBy(item => item.Value.Touch);
                _links.Remove(oldest.Key);
            }
            slot = new LinkSlot();
            _links.Add(key, slot);
        }
        slot.Touch = ++_nextTouch;

        if (frame.Direction == FrameDirection.MasterToSlave)
        {
            // A different master command invalidates an earlier unconsumed
            // Class 1/2 poll. A retransmission is a new observed TX, never
            // a reason to keep associating an older request.
            slot.PendingPoll = EventFrameExchange.IsClassPoll(frame) ? frame : null;
            slot.LastResponse = null;
            slot.MatchedPoll = null;
            return;
        }

        if (slot.LastResponse is not null &&
            EventFrameExchange.SamePhysicalResponse(slot.LastResponse, frame))
            return;

        slot.LastResponse = frame;
        slot.MatchedPoll = slot.PendingPoll is { } request &&
            EventFrameExchange.IsObservedPollReply(request, frame)
                ? request : null;
        // Even an FC9/no-data or malformed secondary telegram closes the old
        // request's eligibility for future RX frames. Missing or uncertain
        // evidence is reported as unmatched, not forcibly joined.
        slot.PendingPoll = null;
    }

    /// <summary>Returns an actually observed TX only if this event's physical
    /// RX is the response that consumed it. Safe for multi-IOA fan-out.</summary>
    public Iec103MasterEvidenceEvent? RequestForEvent(Iec103MasterEvidenceEvent reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.Direction != FrameDirection.SlaveToMaster ||
            !EventFrameExchange.TryLinkKey(reply, out var key) ||
            string.IsNullOrWhiteSpace(reply.RawHex))
            return null;
        Observe(reply); // also supports a direct, indexed event without a preview row
        var slot = _links[key];
        return slot.LastResponse is not null &&
            EventFrameExchange.SamePhysicalResponse(slot.LastResponse, reply)
                ? slot.MatchedPoll : null;
    }
}
