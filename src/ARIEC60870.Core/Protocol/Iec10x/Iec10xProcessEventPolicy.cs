// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>
/// One pure IEC-101/104 process-observation policy shared by active Master,
/// passive capture writer, and evidence replay. Caller MUST separately verify
/// protocol, RX role, frame integrity, ASDU/object validity and source identity.
/// This is observation classification, not a physical state proof.
/// </summary>
public static class Iec10xProcessEventPolicy
{
    public static bool IsDigitalProcessType(int? typeId)
        => typeId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 30 or 31 or 32 or 33;

    public static bool IsMeasurementOrCounterType(int? typeId)
        => typeId is 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 21 or 34 or 35 or 36 or 37;

    public static bool IsReceivedProcessType(int? typeId, int? cause)
        => (IsDigitalProcessType(typeId) || IsMeasurementOrCounterType(typeId)) &&
            cause is 3 or 11 or 12;

    public static bool ShouldRecordProcessType(int? typeId, int? cause, bool changed,
        bool completeTypedMeasurementPayload = true)
        => IsReceivedProcessType(typeId, cause) ||
            (completeTypedMeasurementPayload && IsMeasurementOrCounterType(typeId) &&
                cause is >= 1 and <= 36) ||
            (changed && typeId is 1 or 2 or 3 or 4 or 30 or 31);
}
