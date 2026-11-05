// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Parsing;

/// <summary>The common IEC-101/103 FT1.2 wire envelope, before any protocol-specific ASDU semantics.</summary>
public enum Ft12WireKind { Single, Fixed, Variable }

/// <summary>
/// Verified, allocation-free FT1.2 layout. Only structurally complete frames are returned.
/// A2 is a recognizable one-character envelope, NOT an IEC-101 protocol-level NACK.
/// Neither wire validity nor a checksum establishes an application-level event.
/// </summary>
public readonly record struct Ft12WireLayout(
    Ft12WireKind Kind, byte Control, int? LinkAddress,
    int AsduOffset, int AsduLength, int LinkAddressWidth)
{
    public static bool TryParse(ReadOnlySpan<byte> raw, int linkAddressWidth, out Ft12WireLayout layout)
    {
        layout = default;
        if (linkAddressWidth is < 0 or > 2) return false;
        if (raw.Length == 1 && raw[0] is 0xE5 or 0xA2)
        {
            layout = new Ft12WireLayout(Ft12WireKind.Single, 0, null, 0, 0, linkAddressWidth);
            return true;
        }

        if (raw.Length >= 4 && raw[0] == 0x10)
        {
            if (raw.Length != 4 + linkAddressWidth || raw[^1] != 0x16)
                return false;
            var sum = (int)raw[1];
            var address = 0;
            for (var i = 0; i < linkAddressWidth; i++)
            {
                sum += raw[2 + i];
                address |= raw[2 + i] << (8 * i);
            }
            if ((byte)sum != raw[^2]) return false;
            layout = new Ft12WireLayout(Ft12WireKind.Fixed, raw[1],
                linkAddressWidth == 0 ? null : address, 0, 0, linkAddressWidth);
            return true;
        }

        if (raw.Length < 7 || raw.Length > 261 || raw[0] != 0x68 ||
            raw[1] != raw[2] || raw[3] != 0x68 ||
            raw[1] < 1 + linkAddressWidth || raw.Length != raw[1] + 6 ||
            raw[^1] != 0x16)
            return false;

        var checksum = 0;
        for (var i = 4; i < raw.Length - 2; i++) checksum += raw[i];
        if ((byte)checksum != raw[^2]) return false;

        var link = 0;
        for (var i = 0; i < linkAddressWidth; i++)
            link |= raw[5 + i] << (8 * i);
        layout = new Ft12WireLayout(Ft12WireKind.Variable, raw[4],
            linkAddressWidth == 0 ? null : link,
            5 + linkAddressWidth, raw[1] - 1 - linkAddressWidth, linkAddressWidth);
        return true;
    }
}
