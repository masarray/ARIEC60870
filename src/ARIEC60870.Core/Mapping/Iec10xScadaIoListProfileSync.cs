// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace ARIEC60870.Core.Mapping;

/// <summary>
/// Operator-initiated, guarded reconciliation against the bundled 31-point
/// SCADA I/O list. It never edits the live profile or rewrites IEC evidence,
/// control policy, station identity, or approved analog calibration.
/// </summary>
public static class Iec10xScadaIoListProfileSync
{
    public sealed record Preview(
        Iec10xPointMappingProfile UpdatedProfile,
        int MatchedPoints,
        int RenamedPoints,
        int UpdatedBusinessMetadata,
        int AppliedOperatorReactiveRanges,
        int AppliedDocumentedNormalizedRanges)
    {
        public bool HasChanges =>
            RenamedPoints > 0 || UpdatedBusinessMetadata > 0 ||
            AppliedOperatorReactiveRanges > 0 || AppliedDocumentedNormalizedRanges > 0;
    }

    public static Preview PrepareCopy(
        Iec10xPointMappingProfile existing,
        Iec10xPointMappingProfile bundledReference)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(bundledReference);
        existing.Validate();
        bundledReference.Validate();

        // This is only the known document's 31-point CA=105 database.
        // A different station/vendor template must not be silently trusted.
        if (bundledReference.CommonAddress != 105 ||
            bundledReference.Points.Count != 31 ||
            bundledReference.Points.Any(p => p.Ca != 105 || p.TypeId is null))
            throw new InvalidOperationException(
                "The bundled SCADA 101 I/O reference must contain exactly 31 explicit CA=105 points.");

        var copy = JsonSerializer.Deserialize<Iec10xPointMappingProfile>(
            JsonSerializer.Serialize(existing))
            ?? throw new InvalidOperationException("Unable to copy the current IOA profile.");

        var sourceByIdentity = bundledReference.Points.ToDictionary(
            p => (p.Ioa, p.TypeId!.Value));
        var matched = 0;
        var renamed = 0;
        var metadata = 0;
        var reactive = 0;
        var normalized = 0;
        foreach (var target in copy.Points)
        {
            // Match the *actual* station and *exact* IEC Type ID. In
            // particular, no guessed CA aliases or GI/control type families.
            if ((target.Ca ?? copy.CommonAddress) != 105 ||
                target.TypeId is not int typeId ||
                !sourceByIdentity.TryGetValue((target.Ioa, typeId), out var source))
                continue;

            matched++;
            if (!string.Equals(target.Name, source.Name, StringComparison.Ordinal))
            {
                target.Name = source.Name;
                renamed++;
            }
            if (!string.Equals(target.BayType, source.BayType, StringComparison.Ordinal) ||
                !string.Equals(target.Mnemonic, source.Mnemonic, StringComparison.Ordinal) ||
                !string.Equals(target.TestCategory, source.TestCategory, StringComparison.Ordinal))
            {
                target.BayType = source.BayType;
                target.Mnemonic = source.Mnemonic;
                target.TestCategory = source.TestCategory;
                metadata++;
            }

            // The operator now explicitly resolves the two handwritten Q
            // data items to three bay-specific source identities:
            // FEEDER=Diri Sendiri ±2078; KOPEL SOGI3 and TRAFO SOGI4=
            // Q Lab ±260. Explicit Sync may upgrade a *legacy identity only*.
            // Never replace already calibrated Factor/TwoPoint (including
            // one without a matching station), custom bounds or commands.
            var qLimit = target.Ioa switch
            {
                790447 => 2078d,
                790439 or 790443 => 260d,
                _ => 0d
            };
            if (typeId == 11 && qLimit > 0d &&
                string.Equals(source.CommandPolicy, "MonitorOnly",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(target.CommandPolicy, "MonitorOnly",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(source.ScalingMode, "TwoPoint",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(source.Unit, "MVar", StringComparison.OrdinalIgnoreCase) &&
                source.X0 == -32768d && source.X100 == 32767d &&
                source.Y0 == -qLimit && source.Y100 == qLimit &&
                (string.Equals(target.Unit, "counts", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(target.Unit, "MVar", StringComparison.OrdinalIgnoreCase)) &&
                IsUncalibratedIdentity(target) &&
                target.X0 is null && target.X100 is null &&
                target.Y0 is null && target.Y100 is null &&
                target.EngineeringMin is null && target.EngineeringMax is null)
            {
                target.Unit = source.Unit;
                target.ScalingMode = "TwoPoint";
                target.X0 = source.X0;
                target.X100 = source.X100;
                target.Y0 = source.Y0;
                target.Y100 = source.Y100;
                target.EngineeringMin = source.Y0;
                target.EngineeringMax = source.Y100;
                reactive++;
            }

            // Latest operator correction identifies POAQ TI9 as 0..1200
            // feedback, while POOP TI48 command remains 0..1000.
            // Upgrade *only* a legacy
            // identity NVA->MW presentation with no stored endpoints. Never
            // override a custom project full-scale or two-point calibration.
            if (target.Ioa == 790449 && typeId == 9 &&
                string.Equals(source.ScalingMode, "TwoPoint", StringComparison.OrdinalIgnoreCase) &&
                source.X0 == 0d && source.X100 == 1d &&
                source.Y0 == 0d && source.Y100 == 1200d &&
                string.Equals(target.Unit, "MW", StringComparison.OrdinalIgnoreCase) &&
                IsUncalibratedIdentity(target) &&
                target.X0 is null && target.X100 is null &&
                target.Y0 is null && target.Y100 is null)
            {
                target.ScalingMode = "TwoPoint";
                target.X0 = source.X0;
                target.X100 = source.X100;
                target.Y0 = source.Y0;
                target.Y100 = source.Y100;
                normalized++;
            }
        }

        copy.Validate();
        return new Preview(copy, matched, renamed, metadata, reactive, normalized);
    }

    private static bool IsUncalibratedIdentity(Iec10xPointMappingEntry point) =>
        (string.IsNullOrWhiteSpace(point.ScalingMode) ||
         string.Equals(point.ScalingMode, "Factor", StringComparison.OrdinalIgnoreCase)) &&
        point.Scale == 1d && point.Offset == 0d &&
        point.RatioPrimary == 1d && point.RatioSecondary == 1d &&
        point.NormalizedFullScale == 1d;
}
