// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Mapping;

/// <summary>
/// Same IEC10x information kind with different time-tag representation.
/// This is only a display-name compatibility rule, never a decoder,
/// an automatic protocol-profile override, or proof of timed SOE conformity.
/// </summary>
public static class Iec10xPointTypeFamily
{
    public static bool SameObservationFamily(int expectedType, int receivedType)
    {
        static int Family(int ti) => ti switch
        {
            1 or 2 or 30 => 1,       // single point
            3 or 4 or 31 => 3,       // double point
            5 or 6 or 32 => 5,       // step position
            7 or 8 or 33 => 7,       // bitstring
            9 or 10 or 34 => 9,     // normalized
            11 or 12 or 35 => 11,   // scaled
            13 or 14 or 36 => 13,   // short float
            15 or 16 or 37 => 15,   // counters
            _ => -1
        };
        var family = Family(expectedType);
        return family >= 0 && family == Family(receivedType);
    }
}
