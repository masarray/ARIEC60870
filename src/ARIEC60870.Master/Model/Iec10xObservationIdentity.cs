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
            : $"{(int)item.ProtocolMode}|seq:{item.PhysicalFrameSequenceNumber ?? item.SequenceNumber}|object:{index}";
    }

    /// <summary>Parse an exact passive physical-frame identity. The wildcard
    /// is a legal frame-level SELECT reference, never a wildcard for stream
    /// or byte offset. A numeric object index identifies the same frame.</summary>
    public static bool TryParsePassiveFrameReference(string? reference, out byte stream, out long offset)
    {
        stream = 0;
        offset = 0;
        if (string.IsNullOrWhiteSpace(reference)) return false;
        var parts = reference.Split('|');
        if (parts.Length != 4 || parts[0] != "101" ||
            !parts[1].StartsWith("stream:", StringComparison.Ordinal) ||
            !byte.TryParse(parts[1]["stream:".Length..], NumberStyles.None,
                CultureInfo.InvariantCulture, out stream) || stream >= 4 ||
            !parts[2].StartsWith("byte:", StringComparison.Ordinal) ||
            !long.TryParse(parts[2]["byte:".Length..], NumberStyles.None,
                CultureInfo.InvariantCulture, out offset) ||
            !parts[3].StartsWith("object:", StringComparison.Ordinal))
            return false;
        var objectPart = parts[3]["object:".Length..];
        return objectPart == "*" || (int.TryParse(objectPart, NumberStyles.None,
            CultureInfo.InvariantCulture, out var objectIndex) && objectIndex >= 0);
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
        // Both roots refer to one original physical telegram. Active sibling RX
        // object projections carry their original frame sequence as the reference
        // root, even though their own event sequence legitimately differs.
        return true;
    }

    private static string FrameRoot(string reference)
    {
        var i = reference.LastIndexOf("|object:", StringComparison.Ordinal);
        return i < 0 ? reference : reference[..i];
    }

    private static string NormalizeSource(string? source)
        => string.IsNullOrWhiteSpace(source) ? "SINGLE" : source.Trim();

    // IEC101/104 process type families. IEC103 has its own FUN/INF decoder.
    public static bool IsDigitalProcessType(int? typeId)
        => typeId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 30 or 31 or 32 or 33;

    public static bool IsMeasurementOrCounterType(int? typeId)
        => typeId is 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 34 or 35 or 36 or 37;

    // A valid APCI/FT1.2 envelope does NOT prove the contained ASDU or a
    // specific information element was decoded completely.
    public static bool HasTrustedDecodedPayload(Iec103MasterEvidenceEvent item)
        => item.Frame?.IsChecksumValid != false &&
           !string.Equals(item.Category, "RX Warning", StringComparison.OrdinalIgnoreCase) &&
           (item.AsduDetails?.Issues.Count ?? 0) == 0 &&
           (item.InformationObjectDetails?.Issues.Count ?? 0) == 0;

    /// <summary>Each validated spontaneous/return-information object is a
    /// factual observation even if it repeats its previous state/value.</summary>
    public static bool IsReceivedProcessEvent(Iec103MasterEvidenceEvent item)
        => item.Direction == FrameDirection.SlaveToMaster
            && item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104
            && (IsDigitalProcessType(item.TypeId) || IsMeasurementOrCounterType(item.TypeId))
            && item.InformationObjectAddress.HasValue
            && item.CauseOfTransmission is 3 or 11 or 12
            && HasTrustedDecodedPayload(item);

    public static bool ShouldRecordMeasurementObservation(Iec103MasterEvidenceEvent item)
        => item.Direction == FrameDirection.SlaveToMaster &&
           item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104 &&
           IsMeasurementOrCounterType(item.TypeId) &&
           item.InformationObjectAddress.HasValue &&
           item.CauseOfTransmission is >= 1 and <= 36 &&
           item.AsduDetails is { Issues.Count: 0 } &&
           item.InformationObjectDetails is { Issues.Count: 0 } &&
           HasTrustedDecodedPayload(item);

    public static bool ShouldRecordProcessEvent(Iec103MasterEvidenceEvent item, bool changed)
        => HasTrustedDecodedPayload(item) &&
           (IsReceivedProcessEvent(item) ||
            ShouldRecordMeasurementObservation(item) ||
            (IsDigitalProcessType(item.TypeId) && ShouldRecordDigitalEvent(item, changed)));

    public static bool IsReceivedDigitalEvent(Iec103MasterEvidenceEvent item)
        => item.Direction == FrameDirection.SlaveToMaster
            && (item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104)
            && item.TypeId is 1 or 2 or 3 or 4 or 30 or 31
            && item.InformationObjectAddress.HasValue
            && item.CauseOfTransmission is 3 or 11 or 12
            && HasTrustedDecodedPayload(item);

    /// <summary>Preserve actual spontaneous observations, and known GI/cyclic state
    /// transitions. Never make SOE events from TX, unknown, or unchanged snapshots.</summary>
    public static bool ShouldRecordDigitalEvent(Iec103MasterEvidenceEvent item, bool changed)
        => IsReceivedDigitalEvent(item) ||
           (changed && item.Direction == FrameDirection.SlaveToMaster &&
            (item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104) &&
            item.TypeId is 1 or 2 or 3 or 4 or 30 or 31 &&
            item.InformationObjectAddress.HasValue &&
            HasTrustedDecodedPayload(item));

    public static string EventReason(Iec103MasterEvidenceEvent item, bool hasBaseline, bool changed)
    {
        var cause = item.CauseDetails?.Phase ?? item.CauseName;
        if (IsReceivedProcessEvent(item))
            return $"{cause} · {(changed ? "state change" : hasBaseline ? "repeat observation" : "first observation")}";
        return changed ? $"{cause} · state change" : cause;
    }
}
