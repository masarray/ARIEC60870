// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Core.Mapping;

/// <summary>
/// Minimal verified measurement for indexing 50k+ events without reparsing
/// every physical FT1.2/APCI frame during each historical search.
/// OriginalValue is decoder-owned, not a previously calibrated display.
/// </summary>
public sealed record Iec10xEngineeringSample(
    int TypeId, int CommonAddress, int Ioa, double SourceNumber, string OriginalValue);

/// <summary>
/// Read-only engineering projection of trusted IEC-101/104 measured elements.
/// No scaling may alter original bytes, IEC quality or active command/GI proof.
/// </summary>
public static class Iec10xEngineeringValue
{
    public static bool IsMeasurementType(int typeId) =>
        typeId is 9 or 10 or 21 or 34 or 11 or 12 or 35 or 13 or 14 or 36;

    public static Iec10xEngineeringSample? Capture(
        Iec10xAsduDecode? asdu, Iec10xInformationObject? obj)
    {
        if (asdu is not { IsHeaderValid: true, Issues.Count: 0 } ||
            asdu.IsOpaqueVendorSpecific ||
            asdu.ObjectCount != asdu.Objects.Count ||
            obj is not { Issues.Count: 0 } ||
            !IsMeasurementType(asdu.TypeId) ||
            !asdu.Objects.Any(x => ReferenceEquals(x, obj)))
            return null;

        var bytes = obj.RawBytes;
        if (bytes.Count < 2) return null;
        double number;
        if (asdu.TypeId is 9 or 10 or 21 or 34)
        {
            // IEC NVA F16 signed normalized fraction in [-1, 1).
            number = (short)(bytes[0] | bytes[1] << 8) / 32768d;
        }
        else if (asdu.TypeId is 11 or 12 or 35)
        {
            // IEC SVA I16, no engineering-unit resolution on wire.
            number = (short)(bytes[0] | bytes[1] << 8);
        }
        else
        {
            // IEC R32 IEEE754 source, preserve full float precision.
            if (bytes.Count < 4) return null;
            var bits = bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24;
            number = BitConverter.Int32BitsToSingle(bits);
        }

        if (!double.IsFinite(number)) return null;
        return new Iec10xEngineeringSample(asdu.TypeId, asdu.CommonAddress,
            obj.InformationObjectAddress, number, obj.ShortValue);
    }

    public static bool TryFormat(Iec10xPointMappingEntry point,
        Iec10xAsduDecode? asdu, Iec10xInformationObject? obj, out string text)
        => TryFormat(point, Capture(asdu, obj), out text);

    public static bool TryFormat(Iec10xPointMappingEntry point,
        Iec10xEngineeringSample? sample, out string text)
    {
        text = string.Empty;
        if (point is null || sample is null ||
            point.Ioa != sample.Ioa ||
            point.Ca is int expectedCa && expectedCa != sample.CommonAddress ||
            !string.Equals(point.CommandPolicy, "MonitorOnly", StringComparison.OrdinalIgnoreCase) ||
            point.TypeId is int expected &&
                expected != sample.TypeId &&
                !Iec10xPointTypeFamily.SameObservationFamily(expected, sample.TypeId) ||
            !IsMeasurementType(sample.TypeId) ||
            !double.IsFinite(sample.SourceNumber) ||
            !double.IsFinite(point.Scale) ||
            !double.IsFinite(point.Offset) ||
            !double.IsFinite(point.RatioPrimary) ||
            !double.IsFinite(point.RatioSecondary) ||
            !double.IsFinite(point.NormalizedFullScale) ||
            point.RatioPrimary <= 0 ||
            point.RatioSecondary <= 0 ||
            point.NormalizedFullScale <= 0)
            return false;

        var fullScale = sample.TypeId is 9 or 10 or 21 or 34
            ? point.NormalizedFullScale : 1d;
        var engineering = sample.SourceNumber * fullScale *
            point.Scale * (point.RatioPrimary / point.RatioSecondary) + point.Offset;
        if (!double.IsFinite(engineering)) return false;

        // Fixed six decimals silently rounded e.g. 1E-7 to zero; use
        // significant digits with scientific notation as necessary.
        var rendered = engineering.ToString("G12", CultureInfo.InvariantCulture);
        text = string.IsNullOrWhiteSpace(point.Unit)
            ? rendered : rendered + " " + point.Unit.Trim();
        return true;
    }
}
