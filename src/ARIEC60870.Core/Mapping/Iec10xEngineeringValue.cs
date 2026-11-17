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
            obj.Index < 1 || obj.Index > asdu.Objects.Count ||
            !ReferenceEquals(asdu.Objects[obj.Index - 1], obj))
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

        double engineering;
        if (string.Equals(point.ScalingMode, "TwoPoint", StringComparison.OrdinalIgnoreCase))
        {
            // Do not mix normalized full-scale, CT/VT ratio, multiplier or
            // offset with an explicitly calibrated two-point map.
            // Endpoint X values refer to the actual decoded source domain:
            // IEC NVA [-1,1), SVA signed I16, or IEEE float.
            if (point.TypeId is not int configuredType ||
                !IsMeasurementType(configuredType) ||
                point.X0 is not double x0 || point.X100 is not double x100 ||
                point.Y0 is not double y0 || point.Y100 is not double y100 ||
                !double.IsFinite(x0) || !double.IsFinite(x100) ||
                !double.IsFinite(y0) || !double.IsFinite(y100) ||
                x0 == x100 || !double.IsFinite(x100 - x0) ||
                !double.IsFinite(y100 - y0))
                return false;
            var slope = (y100 - y0) / (x100 - x0);
            if (!double.IsFinite(slope)) return false;
            // Intentionally extrapolate beyond the source range, consistent
            // with SCADA two-point scaling; never silently clamp or change QDS.
            engineering = y0 + (sample.SourceNumber - x0) * slope;
        }
        else if (string.IsNullOrWhiteSpace(point.ScalingMode) ||
            string.Equals(point.ScalingMode, "Factor", StringComparison.OrdinalIgnoreCase))
        {
            var fullScale = sample.TypeId is 9 or 10 or 21 or 34
                ? point.NormalizedFullScale : 1d;
            engineering = sample.SourceNumber * fullScale *
                point.Scale * (point.RatioPrimary / point.RatioSecondary) + point.Offset;
        }
        else return false;
        if (!double.IsFinite(engineering)) return false;

        // Fixed six decimals silently rounded e.g. 1E-7 to zero; use
        // significant digits with scientific notation as necessary.
        var rendered = engineering.ToString("G12", CultureInfo.InvariantCulture);
        text = string.IsNullOrWhiteSpace(point.Unit)
            ? rendered : rendered + " " + point.Unit.Trim();
        return true;
    }
}
