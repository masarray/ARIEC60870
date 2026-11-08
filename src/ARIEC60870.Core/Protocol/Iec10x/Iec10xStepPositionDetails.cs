// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>
/// IEC VTI: bit 7 is transient, lower seven bits are signed [-64..63].
/// One authoritative conversion shared by the value decoder and forensic UI.
/// </summary>
public sealed record Iec10xStepPositionDetails(int Value, bool Transient, byte Raw)
{
    public static Iec10xStepPositionDetails Decode(byte raw)
    {
        var value = raw & 0x7F;
        if ((value & 0x40) != 0) value -= 128;
        return new Iec10xStepPositionDetails(value, (raw & 0x80) != 0, raw);
    }
}
