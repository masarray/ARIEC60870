// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Interpretive display projection for ONE selected frame. Uses the authoritative
/// decoded ASDU object array and never guesses untransmitted SQ=1 address bytes,
/// quality, scaling, or relay timestamps. Never used on the receive/poll path.
/// </summary>
public sealed record DecodedPointRow(
    int Index, string Address, string Value, string Quality, string RelayTime,
    string RawHex, string Provenance)
{
    public static IReadOnlyList<DecodedPointRow> FromAsdu(Iec10xAsduDecode? asdu)
    {
        if (asdu is null || asdu.Objects.Count == 0)
            return Array.Empty<DecodedPointRow>();

        return asdu.Objects.Select(obj =>
        {
            var quality = obj.QualityDetails?.DisplayText ??
                (string.IsNullOrWhiteSpace(obj.QualityText) ? "Not provided" : obj.QualityText);
            if (obj.Issues.Count > 0)
                quality = "Decode warning · " + quality;
            var raw = obj.RawBytes.Count == 0 ? "No element bytes" :
                Convert.ToHexString(obj.RawBytes.ToArray());
            var source = asdu.IsSequence && obj.Index > 1
                ? "SQ=1 · IOA implied by sequence; not present as address bytes"
                : obj.InformationObjectOffset >= 0
                    ? "IOA address bytes at ASDU offset " +
                      obj.InformationObjectOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "IOA offset unavailable; never infer raw address bytes";
            return new DecodedPointRow(
                obj.Index,
                "IOA " + obj.InformationObjectAddress.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(obj.ShortValue) ? "Not decoded" : obj.ShortValue,
                quality,
                string.IsNullOrWhiteSpace(obj.TimestampText) ? "No device timestamp" : obj.TimestampText,
                raw,
                source);
        }).ToArray();
    }
}

/// <summary>
/// Observation-only focus. All original frames remain in the bounded Trace
/// source and raw journal; this class only hides routine link polling from a
/// selectable view. Unknown, malformed, and source-invalid frames stay visible.
/// </summary>
public static class ObservationFrameFocus
{
    public static bool IsImportant(EvidenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var source = row.Source;

        if (source.AsduDetails is not null || source.CommandDetails is not null)
            return true;
        if (source.CauseDetails is { Negative: true } or { Test: true })
            return true;
        // A checksum-valid frame can still violate the selected IEC link-address
        // width or ASDU structure. Preserve raw diagnostic/issues before the
        // routine-FC9 / Class1 / Class2 filter is allowed to suppress anything.
        if (source.Frame is { IsChecksumValid: false } or { IsLengthValid: false } ||
            source.Frame?.Format == Ft12FrameFormat.Malformed ||
            source.Frame?.Issues.Count > 0 ||
            row.Category.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
            row.Category.Contains("error", StringComparison.OrdinalIgnoreCase))
            return true;
        if (source.Frame?.IsSingleCharacterNack == true)
            return true;
        if (source.Frame?.LinkControl is { } link)
        {
            if (link.Acd == true || link.Dfc == true)
                return true;
            // Only unambiguous routine IEC-101/103 link traffic is de-emphasized.
            // E5 is contextual: without a matched request it must remain visible.
            if (!link.Prm && link.FunctionCode == 9)
                return false; // ordinary NO DATA response, no ACD/DFC
            if (link.Prm && link.FunctionCode is 9 or 10 or 11)
                return false; // status, Class 1 / Class 2 request
        }

        // Trace can contain valuable unknown bytes and IEC-104 supervisory data.
        // Avoid masking anything not conclusively classified as routine poll.
        return true;
    }
}
