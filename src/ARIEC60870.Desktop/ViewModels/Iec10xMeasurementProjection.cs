// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Mapping;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// One fail-closed Values projection for the live RX path and a cached
/// Signal Map re-projection. Only canonical trusted IEC elements can produce
/// engineering units. Never present a source formatter's guessed MW/MVar
/// as though the selected Signal Map calibration had been applied.
/// </summary>
public static class Iec10xMeasurementProjection
{
    public static string Render(Iec10xPointMappingEntry? strictPoint,
        Iec10xAsduDecode? asdu, Iec10xInformationObject? obj, string fallback)
    {
        if (strictPoint is null)
            return obj is { Issues.Count: 0 } ? obj.ShortValue : fallback;

        if (Iec10xEngineeringValue.TryFormat(strictPoint, asdu, obj, out var engineering))
            return engineering;

        if (strictPoint.TypeId is not int ti ||
            !Iec10xEngineeringValue.IsMeasurementType(ti))
            return fallback;

        // A decoded information element may exist even when its complete
        // enclosing ASDU cannot be trusted. This text is *not* engineering
        // data and must never acquire the configured point.Unit suffix.
        var source = obj is { Issues.Count: 0 } ? obj.ShortValue : string.Empty;
        if (string.IsNullOrWhiteSpace(source))
            source = "no trusted numerical source";
        return source + " [scaling not verified]";
    }
}
