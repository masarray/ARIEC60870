// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Core.Mapping;

/// <summary>
/// Read-only engineering projection of trusted IEC-101/104 measured information
/// elements. The original IEC ASDU, source IOA, QDS and archived bytes are
/// never modified; GI, commands and physical feedback never use this value.
/// </summary>
public static class Iec10xEngineeringValue
{
    public static bool IsMeasurementType(int typeId) =>
        typeId is 9 or 10 or 21 or 34 or 11 or 12 or 35 or 13 or 14 or 36;

    public static bool TryFormat(Iec10xPointMappingEntry point,
        Iec10xAsduDecode? asdu, Iec10xInformationObject? obj, out string text)
    {
        text = string.Empty;
        if (point is null || asdu is not { IsHeaderValid: true, Issues.Count: 0 } ||
            asdu.IsOpaqueVendorSpecific || asdu.ObjectCount != asdu.Objects.Count ||
            obj is not { Issues.Count: 0 } ||
            point.Ioa != obj.InformationObjectAddress ||
            point.Ca is int expectedCa && expectedCa != asdu.CommonAddress ||
            !IsMeasurementType(asdu.TypeId) ||
            !string.Equals(point.CommandPolicy, "MonitorOnly", StringComparison.OrdinalIgnoreCase) ||
            point.TypeId is int expected &&
            expected != asdu.TypeId &&
            !Iec10xPointTypeFamily.SameObservationFamily(expected, asdu.TypeId))
            return false;

        var bytes = obj.RawBytes;
        if (bytes.Count < 2) return false;
        var type = asdu.TypeId;
        double sourceValue;
        if (type is 9 or 10 or 21 or 34)
        {
            // IEC NVA F16: signed fixed point in [-1, 1), not a raw
            // engineering-unit integer. FullScale is project/RTU-specific.
            sourceValue = (short)(bytes[0] | bytes[1] << 8) / 32768d;
            sourceValue *= point.NormalizedFullScale;
        }
        else if (type is 11 or 12 or 35)
        {
            // IEC SVA I16: RTU-configured scale is not transmitted in ASDU.
            sourceValue = (short)(bytes[0] | bytes[1] << 8);
        }
        else
        {
            // IEC IEEE754 R32; never transform NaN/Infinity to a "Good" value.
            if (bytes.Count < 4) return false;
            var bits = bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24;
            sourceValue = BitConverter.Int32BitsToSingle(bits);
        }

        if (!double.IsFinite(sourceValue) || !double.IsFinite(point.Scale) ||
            !double.IsFinite(point.Offset) || !double.IsFinite(point.RatioPrimary) ||
            !double.IsFinite(point.RatioSecondary) ||
            !double.IsFinite(point.NormalizedFullScale) ||
            point.RatioPrimary <= 0 || point.RatioSecondary <= 0 ||
            point.NormalizedFullScale <= 0)
            return false;

        // A project may configure the per-point RTU resolution (Scale),
        // optionally a primary/secondary ratio, and a zero/offset correction.
        // Defaults (1, 1/1, 0) preserve existing unmapped/raw behavior.
        var engineering = sourceValue * point.Scale *
            (point.RatioPrimary / point.RatioSecondary) + point.Offset;
        if (!double.IsFinite(engineering)) return false;

        var rendered = engineering.ToString("0.######", CultureInfo.InvariantCulture);
        text = string.IsNullOrWhiteSpace(point.Unit) ? rendered : rendered + " " + point.Unit.Trim();
        return true;
    }
}
