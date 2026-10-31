// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>
/// Validate a recorded, authoritative IEC-104 APCI snapshot against its
/// original six on-wire octets. This is NOT an ASDU decoder: no object
/// allocation, reparse, WPF dependency, or parallel protocol interpretation.
/// The Master/session parser owns the original APCI semantic fields.
/// </summary>
public static class Iec104ApciEvidenceVerifier
{
    public static bool Matches(IReadOnlyList<byte> raw, string? format,
        int? sendSequence, int? receiveSequence, string? uFormatName)
    {
        if (raw.Count < 6 || raw.Count > 255 || raw[0] != 0x68 ||
            raw[1] != raw.Count - 2 || raw[1] > 253)
            return false;

        var c0 = raw[2];
        var c1 = raw[3];
        var c2 = raw[4];
        var c3 = raw[5];
        return format switch
        {
            "I" => (c0 & 1) == 0 && (c2 & 1) == 0 &&
                sendSequence.HasValue && receiveSequence.HasValue &&
                sendSequence.Value == ((c1 << 8) | c0) >> 1 &&
                receiveSequence.Value == ((c3 << 8) | c2) >> 1,
            "S" => raw.Count == 6 && c0 == 1 && c1 == 0 && (c2 & 1) == 0 &&
                receiveSequence.HasValue &&
                receiveSequence.Value == ((c3 << 8) | c2) >> 1,
            "U" => raw.Count == 6 && (c0 & 3) == 3 &&
                c1 == 0 && c2 == 0 && c3 == 0 &&
                string.Equals(uFormatName, Iec104ApduParser.DecodeU(c0),
                    StringComparison.Ordinal),
            _ => false
        };
    }
}
