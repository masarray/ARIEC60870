// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>
/// Canonical structural IEC 60870-5-104 APCI validator. This checks on-wire
/// framing and control-field invariants only, not application ASDU semantics
/// or receive-session sequence/window ownership.
/// </summary>
public static class Iec104ApciWireLayout
{
    public static bool TryValidate(IReadOnlyList<byte> raw, out string? issue)
    {
        issue = null;
        if (raw.Count < 6)
        {
            issue = "APDU too short for four APCI control octets.";
            return false;
        }

        if (raw[0] != 0x68)
        {
            issue = $"Invalid IEC-104 start byte 0x{raw[0]:X2}; expected 0x68.";
            return false;
        }

        var declaredLength = raw[1];
        if (declaredLength is < 4 or > 253)
        {
            issue = $"APDU length outside IEC-104 range 4..253: {declaredLength}.";
            return false;
        }

        if (declaredLength != raw.Count - 2)
        {
            issue = $"APDU length mismatch. Declared={declaredLength}, actual={raw.Count - 2}.";
            return false;
        }

        var c0 = raw[2];
        var c1 = raw[3];
        var c2 = raw[4];
        var c3 = raw[5];
        if ((c0 & 0x01) == 0)
        {
            if (declaredLength == 4)
            {
                issue = "I-format APDU has no ASDU.";
                return false;
            }

            if ((c2 & 0x01) != 0)
            {
                issue = "I-format N(R) reserved low bit must be zero.";
                return false;
            }

            return true;
        }

        if ((c0 & 0x03) == 0x01)
        {
            if (declaredLength != 4 || c0 != 0x01 || c1 != 0 || (c2 & 1) != 0)
            {
                issue = "S-format requires four control octets, 0x01 0x00 and even N(R).";
                return false;
            }

            return true;
        }

        if (declaredLength != 4 || c1 != 0 || c2 != 0 || c3 != 0)
        {
            issue = "U-format requires exactly four control octets with reserved octets zero.";
            return false;
        }

        if (c0 is not (0x07 or 0x0B or 0x13 or 0x23 or 0x43 or 0x83))
        {
            issue = $"Unsupported or conflicting U-format function 0x{c0:X2}.";
            return false;
        }

        return true;
    }
}
