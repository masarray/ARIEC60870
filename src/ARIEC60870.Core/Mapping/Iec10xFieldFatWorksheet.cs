// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace ARIEC60870.Core.Mapping;

/// <summary>
/// A read-only, operator-requested field FAT worksheet from the current Signal List.
/// This is a blank observation checklist, never simulated or approved field evidence.
/// It does not contain serial/TCP connection secrets or issue IEC commands.
/// </summary>
public static class Iec10xFieldFatWorksheet
{
    private static readonly string[] Columns =
    [
        "Profile", "CA", "IOA", "IEC Type ID", "Signal", "Bay",
        "Business TYPE", "Mnemonic", "IEC role", "Command policy",
        "Scaling mode", "Unit", "X0", "X100", "Y0", "Y100",
        "Scale", "Offset", "Ratio primary", "Ratio secondary",
        "Normalized full-scale", "Calibration review (not approval)",
        "Test intent", "Observed UTC", "Observed raw decoded",
        "Observed display value", "Independent RTU/SCADA reference",
        "IEC COT", "IEC quality", "Source/link", "Original frame evidence",
        "Calibration authority", "Result", "Engineer notes"
    ];

    public static string ExportCsv(Iec10xPointMappingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        if (!profile.HasPoints)
            throw new InvalidOperationException("Field FAT worksheet needs a nonempty Signal List.");

        var csv = new StringBuilder();
        AppendRow(csv, Columns);
        foreach (var point in profile.Points
                     .OrderBy(p => p.Ca ?? profile.CommonAddress ?? -1)
                     .ThenBy(p => p.Ioa)
                     .ThenBy(p => p.TypeId ?? -1))
        {
            var isMeasured = point.TypeId is int ti && Iec10xEngineeringValue.IsMeasurementType(ti) &&
                string.Equals(point.CommandPolicy, "MonitorOnly", StringComparison.OrdinalIgnoreCase);
            var twoPoint = isMeasured && string.Equals(point.ScalingMode, "TwoPoint", StringComparison.OrdinalIgnoreCase);
            var factor = isMeasured && !twoPoint;
            var normalized = point.TypeId is 9 or 10 or 21 or 34;
            var calibrationReview = !isMeasured
                ? "Not applicable - observation only"
                : point.TypeId is 11 or 12 or 35 &&
                  string.Equals(point.Unit, "counts", StringComparison.OrdinalIgnoreCase)
                    ? "Raw signed counts - gateway unit conversion not approved"
                    : twoPoint
                        ? "TwoPoint configured - verify independent source approval"
                        : "Factor configured - verify independent source approval";
            var testIntent = isMeasured ? "Analog read-only spot check" :
                !string.Equals(point.CommandPolicy, "MonitorOnly", StringComparison.OrdinalIgnoreCase)
                    ? "Observe command/feedback only - NO TX"
                    : "Digital read-only observation";
            AppendRow(csv,
            [
                Text(profile.ProfileName),
                Num(point.Ca ?? profile.CommonAddress),
                Num(point.Ioa),
                Num(point.TypeId),
                Text(point.Name),
                Text(point.BayType),
                Text(point.TestCategory),
                Text(point.Mnemonic),
                Text(point.SignalType),
                Text(point.CommandPolicy),
                Text(isMeasured ? twoPoint ? "TwoPoint" : "Factor" : "N/A"),
                Text(point.Unit),
                Num(twoPoint ? point.X0 : null),
                Num(twoPoint ? point.X100 : null),
                Num(twoPoint ? point.Y0 : null),
                Num(twoPoint ? point.Y100 : null),
                Num(factor ? point.Scale : null),
                Num(factor ? point.Offset : null),
                Num(factor ? point.RatioPrimary : null),
                Num(factor ? point.RatioSecondary : null),
                Num(factor && normalized ? point.NormalizedFullScale : null),
                Text(calibrationReview),
                Text(testIntent),
                "", "", "", "", "", "", "", "", "",
                Text("NOT TESTED"),
                ""
            ]);
        }
        return csv.ToString();
    }

    private static string Text(string? value)
    {
        value ??= string.Empty;
        // CSV quoting alone does not prevent spreadsheet formula injection.
        // Textual project names and Signal List metadata are untrusted input.
        // Prefix a leading Excel formula marker (including after whitespace)
        // with an apostrophe. Numeric fields below are typed separately.
        var trimmed = value.TrimStart(' ', '\t', '\r', '\n');
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0], StringComparison.Ordinal) ||
            value.Length > 0 && value[0] is '\t' or '\r' or '\n')
            value = "'" + value;
        return value;
    }

    private static string Num(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Num(double? value) =>
        value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;

    private static void AppendRow(StringBuilder csv, IReadOnlyList<string> cells)
    {
        if (cells.Count != Columns.Length)
            throw new InvalidOperationException("Field FAT worksheet schema mismatch.");
        for (var i = 0; i < cells.Count; i++)
        {
            if (i != 0) csv.Append(',');
            csv.Append('"').Append(cells[i].Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }
        csv.Append("\r\n");
    }
}
