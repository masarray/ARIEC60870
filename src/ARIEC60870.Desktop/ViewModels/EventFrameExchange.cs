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

    public static bool IsClassPoll(Iec103MasterEvidenceEvent frame) =>
        frame.ProtocolMode == Iec60870ProtocolMode.Iec101 &&
        frame.Direction == FrameDirection.MasterToSlave &&
        !string.IsNullOrWhiteSpace(frame.RawHex) &&
        frame.Frame?.LinkControl is { Prm: true, FunctionCode: 10 or 11 };

    public static bool IsObservedPollReply(Iec103MasterEvidenceEvent request,
        Iec103MasterEvidenceEvent reply)
    {
        if (!IsClassPoll(request) ||
            reply.ProtocolMode != Iec60870ProtocolMode.Iec101 ||
            reply.Direction != FrameDirection.SlaveToMaster ||
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
        if (!TryLinkKey(reply, out var key)) return null;
        var frames = chronologicalFrames as IReadOnlyList<Iec103MasterEvidenceEvent> ??
            chronologicalFrames.ToArray();
        for (var i = frames.Count - 1; i >= 0; i--)
        {
            var candidate = frames[i];
            if (candidate.Direction != FrameDirection.MasterToSlave ||
                !TryLinkKey(candidate, out var candidateKey) || candidateKey != key ||
                candidate.TimestampUtc > reply.TimestampUtc)
                continue;
            return IsObservedPollReply(candidate, reply) ? candidate : null;
        }
        return null;
    }

    private static string NormalizeSource(string? source) =>
        string.IsNullOrWhiteSpace(source) ? "SINGLE" : source.Trim().ToUpperInvariant();
}
