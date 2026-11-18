// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Mapping;

/// <summary>
/// Operator-supplied SCADA database range examples, NOT approved per-IOA
/// calibrations. Never apply by type/category alone or modify active commands.
/// A station engineer must explicitly choose a reference for one point.
/// </summary>
public enum Iec10xScadaRangeId
{
    ReactiveDiriSendiri,
    ReactiveLab,
    PoaqFeedback
}

public sealed record Iec10xScadaRangeReference(
    Iec10xScadaRangeId Id,
    string Label,
    double X0,
    double X100,
    double Y0,
    double Y100,
    string Unit);

public static class Iec10xScadaRangeReferences
{
    /// <summary>
    /// For the specific 31-point CA105 reference only. The handwritten
    /// "Q Diri Sendiri" and "Q Lab" rows are NOT mapped to a particular
    /// FEEDER/KOPEL/TRAFO IOA by the supplied document.
    /// The corrected POAQ feedback 0..1200 is a later operator statement;
    /// it is not printed in the earlier handwritten 0..1000 note.
    /// X100=1 matches the reviewed conceptual NVA mapping (not raw 32767).
    /// </summary>
    public static bool TryForPoint(Iec10xPointMappingEntry point,
        Iec10xScadaRangeId reference, out Iec10xScadaRangeReference? range)
    {
        range = null;
        if (point is null || point.Ca != 105 ||
            !string.Equals(point.CommandPolicy, "MonitorOnly",
                StringComparison.OrdinalIgnoreCase))
            return false;

        if (reference == Iec10xScadaRangeId.PoaqFeedback)
        {
            if (point.Ioa != 790449 || point.TypeId != 9) return false;
            range = new(reference, "POAQ feedback · 0–1200",
                0, 1, 0, 1200, "MW");
            return true;
        }

        if (point.TypeId != 11 ||
            point.Ioa is not (790447 or 790439 or 790443))
            return false;

        var limit = reference switch
        {
            Iec10xScadaRangeId.ReactiveDiriSendiri => 2078d,
            Iec10xScadaRangeId.ReactiveLab => 260d,
            _ => 0d
        };
        if (limit == 0) return false;
        range = new(reference,
            reference == Iec10xScadaRangeId.ReactiveDiriSendiri
                ? "Q Diri Sendiri · ±2078" : "Q Lab · ±260",
            -32768, 32767, -limit, limit, "MVar");
        return true;
    }
}
