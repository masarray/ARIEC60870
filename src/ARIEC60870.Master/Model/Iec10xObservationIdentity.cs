// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Core.Model;

namespace ARIEC60870.Master.Model;

/// <summary>Physical point identity, independent of logical user mapping or display alias.
/// Scope is a single active session / passive capture; different links and CAs are distinct.</summary>
public static class Iec10xObservationIdentity
{
    public static string ValueKey(Iec60870ProtocolMode protocol, string? channel, int? ca, int ioa)
    {
        if (protocol is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104))
            throw new ArgumentOutOfRangeException(nameof(protocol));
        if (ioa is < 0 or > 0xFFFFFF) throw new ArgumentOutOfRangeException(nameof(ioa));
        if (ca is < 0 or > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(ca));
        var source = string.IsNullOrWhiteSpace(channel) ? "SINGLE" : channel.Trim().ToUpperInvariant();
        return $"{(int)protocol}|{source.Length}:{source}|CA:{(ca?.ToString(CultureInfo.InvariantCulture) ?? "?")}|IOA:{ioa}";
    }

    /// <summary>Session/capture-scoped evidence reference. Original passive byte offsets
    /// are NOT equivalent to active master event sequence numbers.</summary>
    public static string EvidenceReference(Iec103MasterEvidenceEvent item)
    {
        var index = item.InformationObjectDetails?.Index ?? 0;
        return item.PhysicalStreamId is int stream && item.RawStreamOffset is long offset
            ? $"{(int)item.ProtocolMode}|stream:{stream}|byte:{offset}|object:{index}"
            : $"{(int)item.ProtocolMode}|seq:{item.SequenceNumber}|object:{index}";
    }

    /// <summary>Match a clicked event or transaction stage to the ORIGINAL physical
    /// frame, never by IOA or display row index. Passive sibling objects use the
    /// same stream/byte offset; active events must retain the exact frame sequence.</summary>
    public static bool SamePhysicalFrame(Iec103MasterEvidenceEvent frame, string evidenceRef,
        string physicalSource, Iec60870ProtocolMode protocol, long sequence)
    {
        if (frame.ProtocolMode != protocol ||
            !string.Equals(NormalizeSource(frame.PhysicalSource), NormalizeSource(physicalSource),
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(evidenceRef) ||
            frame.Direction is not (FrameDirection.MasterToSlave or FrameDirection.SlaveToMaster))
            return false;

        var frameRef = FrameRoot(frame.EvidenceReference);
        var soughtRef = FrameRoot(evidenceRef);
        if (!string.Equals(frameRef, soughtRef, StringComparison.Ordinal)) return false;
        // Active sequence is a frame identity. Passive stream+offset is stronger;
        // per-object projection sequence can differ from the physical frame event.
        return soughtRef.Contains("|stream:", StringComparison.Ordinal) ||
            frame.SequenceNumber == sequence;
    }

    private static string FrameRoot(string reference)
    {
        var i = reference.LastIndexOf("|object:", StringComparison.Ordinal);
        return i < 0 ? reference : reference[..i];
    }

    private static string NormalizeSource(string? source)
        => string.IsNullOrWhiteSpace(source) ? "SINGLE" : source.Trim();

    public static bool IsReceivedDigitalEvent(Iec103MasterEvidenceEvent item)
        => item.Direction == FrameDirection.SlaveToMaster
            && (item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104)
            && item.TypeId is 1 or 2 or 3 or 4 or 30 or 31
            && item.InformationObjectAddress.HasValue
            && item.CauseOfTransmission is 3 or 11 or 12
            && item.Frame?.IsChecksumValid != false
            && !string.Equals(item.Category, "RX Warning", StringComparison.OrdinalIgnoreCase);

    /// <summary>Preserve actual spontaneous observations, and known GI/cyclic state
    /// transitions. Never make SOE events from TX, unknown, or unchanged snapshots.</summary>
    public static bool ShouldRecordDigitalEvent(Iec103MasterEvidenceEvent item, bool changed)
        => IsReceivedDigitalEvent(item) ||
           (changed && item.Direction == FrameDirection.SlaveToMaster &&
            (item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104) &&
            item.TypeId is 1 or 2 or 3 or 4 or 30 or 31 &&
            item.InformationObjectAddress.HasValue &&
            item.Frame?.IsChecksumValid != false &&
            !string.Equals(item.Category, "RX Warning", StringComparison.OrdinalIgnoreCase));

    public static string EventReason(Iec103MasterEvidenceEvent item, bool hasBaseline, bool changed)
    {
        var cause = item.CauseDetails?.Phase ?? item.CauseName;
        if (IsReceivedDigitalEvent(item))
            return $"{cause} · {(changed ? "state change" : hasBaseline ? "repeat observation" : "first observation")}";
        return changed ? $"{cause} · state change" : cause;
    }
}
