// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// IEC-101 Class 1/2 poll-to-reply association based on observed wire direction,
/// link/function, physical source, station address and ordering. This is a
/// request/reply observation, NOT proof of when the RTU originally created an SOE.
/// No nearest IOA, fabricated TX or cross-link timestamp matching is permitted.
/// </summary>
public static class EventFrameExchange
{
    private static readonly TimeSpan MaximumObservedReplyGap = TimeSpan.FromSeconds(45);

    public static bool TryLinkKey(Iec103MasterEvidenceEvent frame,
        out (string Source, int Link) key)
    {
        key = default;
        if (frame.ProtocolMode != Iec60870ProtocolMode.Iec101 ||
            (frame.LinkAddress ?? frame.Frame?.LinkAddress) is not int link)
            return false;
        key = (NormalizeSource(frame.PhysicalSource), link);
        return true;
    }

    public static bool IsUnaddressedSecondaryReply(Iec103MasterEvidenceEvent frame) =>
        frame.ProtocolMode == Iec60870ProtocolMode.Iec101 &&
        frame.Direction == FrameDirection.SlaveToMaster &&
        (frame.Frame?.IsSingleCharacterAck == true ||
         frame.Frame?.IsSingleCharacterNack == true);

    public static bool SamePhysicalSource(string? left, string? right) =>
        string.Equals(NormalizeSource(left), NormalizeSource(right),
            StringComparison.Ordinal);

    public static bool IsClassPoll(Iec103MasterEvidenceEvent frame) =>
        frame.ProtocolMode == Iec60870ProtocolMode.Iec101 &&
        frame.Direction == FrameDirection.MasterToSlave &&
        !string.IsNullOrWhiteSpace(frame.RawHex) &&
        frame.Frame is
        {
            IsChecksumValid: true, IsLengthValid: true,
            LinkControl: { Prm: true, FunctionCode: 10 or 11 }
        };

    /// <summary>Two object projections must share a proven physical frame,
    /// not merely a timestamp, signal name, station or raw HEX. A missing
    /// frame identity is NOT a license to deduplicate different RX telegrams.</summary>
    public static bool SamePhysicalResponse(Iec103MasterEvidenceEvent left,
        Iec103MasterEvidenceEvent right)
    {
        if (left.ProtocolMode != right.ProtocolMode ||
            left.Direction != FrameDirection.SlaveToMaster ||
            right.Direction != FrameDirection.SlaveToMaster ||
            !TryLinkKey(left, out var a) || !TryLinkKey(right, out var b) || a != b)
            return false;

        if (left.PhysicalStreamId.HasValue || right.PhysicalStreamId.HasValue)
            return left.PhysicalStreamId.HasValue && right.PhysicalStreamId.HasValue &&
                left.RawStreamOffset.HasValue && right.RawStreamOffset.HasValue &&
                left.PhysicalStreamId == right.PhysicalStreamId &&
                left.RawStreamOffset == right.RawStreamOffset;

        var leftPhysical = left.PhysicalFrameSequenceNumber ?? left.SequenceNumber;
        var rightPhysical = right.PhysicalFrameSequenceNumber ?? right.SequenceNumber;
        return leftPhysical > 0 && rightPhysical > 0 &&
            leftPhysical == rightPhysical;
    }

    public static bool IsObservedPollReply(Iec103MasterEvidenceEvent request,
        Iec103MasterEvidenceEvent reply)
    {
        if (!IsClassPoll(request) ||
            reply.ProtocolMode != Iec60870ProtocolMode.Iec101 ||
            reply.Direction != FrameDirection.SlaveToMaster ||
            // A secondary user-data reply is required. A primary unsolicited
            // telegram or ACK is not evidence of this particular poll response.
            reply.Frame is not
            {
                IsChecksumValid: true, IsLengthValid: true,
                LinkControl: { Prm: false, FunctionCode: 8 }
            } ||
            string.IsNullOrWhiteSpace(reply.RawHex) ||
            !TryLinkKey(request, out var source) ||
            !TryLinkKey(reply, out var destination) || source != destination)
            return false;

        var age = reply.TimestampUtc - request.TimestampUtc;
        if (age < TimeSpan.Zero || age > MaximumObservedReplyGap) return false;
        var physicalSequence = reply.PhysicalFrameSequenceNumber ?? reply.SequenceNumber;
        return physicalSequence <= 0 || request.SequenceNumber <= 0 ||
            request.SequenceNumber < physicalSequence;
    }

    /// <summary>
    /// Optional fallback from the bounded live trace. Never skip a later
    /// non-polling TX to associate an older poll with an unrelated response.
    /// </summary>
    public static Iec103MasterEvidenceEvent? FindLatestObservedPoll(
        Iec103MasterEvidenceEvent reply,
        IEnumerable<Iec103MasterEvidenceEvent> chronologicalFrames)
    {
        if (!TryLinkKey(reply, out var key) ||
            reply.Direction != FrameDirection.SlaveToMaster) return null;
        var frames = chronologicalFrames as IReadOnlyList<Iec103MasterEvidenceEvent> ??
            chronologicalFrames.ToArray();
        // The exact RX must still be in this bounded chronological Trace.
        // Without it, the surrounding TX/RX order is unknowable.
        var targetIndex = -1;
        for (var i = frames.Count - 1; i >= 0; i--)
        {
            if (!SamePhysicalResponse(frames[i], reply)) continue;
            targetIndex = i;
            break;
        }
        if (targetIndex < 0) return null;

        for (var i = targetIndex - 1; i >= 0; i--)
        {
            var candidate = frames[i];
            // E5 has no link address: it may already have completed the poll.
            // Never jump across it to invent an older TX/RX association.
            if (IsUnaddressedSecondaryReply(candidate) &&
                SamePhysicalSource(candidate.PhysicalSource, reply.PhysicalSource))
                return null;
            if (!TryLinkKey(candidate, out var candidateKey) || candidateKey != key)
                continue;
            if (candidate.Direction == FrameDirection.SlaveToMaster)
            {
                // A previous, different reply already consumed the old poll.
                if (SamePhysicalResponse(candidate, reply)) continue;
                return null;
            }
            if (candidate.Direction == FrameDirection.MasterToSlave)
                return IsObservedPollReply(candidate, reply) ? candidate : null;
        }
        return null;
    }

    private static string NormalizeSource(string? source) =>
        string.IsNullOrWhiteSpace(source) ? "SINGLE" : source.Trim().ToUpperInvariant();
}
