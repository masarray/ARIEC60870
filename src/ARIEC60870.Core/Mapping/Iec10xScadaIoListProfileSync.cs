// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace ARIEC60870.Core.Mapping;

/// <summary>
/// Operator-initiated, name-only reconciliation against the bundled 31-point
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
        int ResetUnverifiedReactiveUnits,
        int AppliedDocumentedNormalizedRanges,
        int AppliedScadaReactiveRanges = 0)
    {
        public bool HasChanges =>
            RenamedPoints > 0 || UpdatedBusinessMetadata > 0 ||
            ResetUnverifiedReactiveUnits > 0 || AppliedDocumentedNormalizedRanges > 0 ||
            AppliedScadaReactiveRanges > 0;
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
        var rawUnit = 0;
        var normalized = 0;
        var reactive = 0;
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

            // The old example misleadingly assigned MVar to an uncalibrated
            // TI11 signed integer. Change only that legacy identity setup,
            // after an explicit operator confirmation; retain *all* custom
            // Factor/TwoPoint engineering definitions and their units.
            if (typeId == 11 && string.Equals(source.Unit, "counts", StringComparison.Ordinal) &&
                string.Equals(target.Unit, "MVar", StringComparison.OrdinalIgnoreCase) &&
                IsUncalibratedIdentity(target))
            {
                target.Unit = "counts";
                rawUnit++;
            }

            // The bundled operator SCADA reference now carries an explicit,
            // still-provisional Q Diri Sendiri range for all three sample bays.
            // The real SCADA Q data item -> IOA association is not verified:
            // require the existing explicit Sync preview and protected Save As.
            // Only a legacy *identity* Q can be upgraded. An existing site
            // Factor or TwoPoint calibration must never be touched.
            if (typeId == 11 && target.Ioa is 790447 or 790439 or 790443 &&
                string.Equals(source.ScalingMode, "TwoPoint", StringComparison.OrdinalIgnoreCase) &&
                source.X0 == -32768d && source.X100 == 32767d &&
                source.Y0 == -2078d && source.Y100 == 2078d &&
                string.Equals(source.Unit, "MVar", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(target.Unit, "counts", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(target.Unit, "MVar", StringComparison.OrdinalIgnoreCase)) &&
                IsUncalibratedIdentity(target) &&
                target.X0 is null && target.X100 is null &&
                target.Y0 is null && target.Y100 is null)
            {
                target.ScalingMode = "TwoPoint";
                target.X0 = source.X0;
                target.X100 = source.X100;
                target.Y0 = source.Y0;
                target.Y100 = source.Y100;
                target.Unit = source.Unit;
                target.EngineeringMin = source.EngineeringMin;
                target.EngineeringMax = source.EngineeringMax;
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
        return new Preview(copy, matched, renamed, metadata, rawUnit, normalized, reactive);
    }

    private static bool IsUncalibratedIdentity(Iec10xPointMappingEntry point) =>
        (string.IsNullOrWhiteSpace(point.ScalingMode) ||
         string.Equals(point.ScalingMode, "Factor", StringComparison.OrdinalIgnoreCase)) &&
        point.Scale == 1d && point.Offset == 0d &&
        point.RatioPrimary == 1d && point.RatioSecondary == 1d &&
        point.NormalizedFullScale == 1d;
}
