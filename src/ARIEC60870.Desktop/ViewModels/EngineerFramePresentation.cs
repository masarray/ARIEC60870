// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Deterministic on-wire observation annotations shared by the line list and
/// selected-frame interpretation. No message-string or HEX keyword heuristics.
/// Inapplicable and missing flags are never silently represented as zero.
/// </summary>
public static class EngineerFramePresentation
{
    public static string Alerts(Iec103MasterEvidenceEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var frame = source.Frame;
        var asdu = source.AsduDetails;
        var result = new List<string>(12);

        if (frame is { IsChecksumValid: false }) result.Add("Checksum invalid");
        if (frame is { IsLengthValid: false }) result.Add("Length invalid");
        if (frame?.Issues.Count > 0) result.Add("Link decode warning");
        if (frame?.IsSingleCharacterNack == true) result.Add("Link NACK");

        var link = frame?.LinkControl;
        if (link is { Prm: false, Acd: true }) result.Add("ACD=1 · Class 1 pending");
        if (link is { Prm: false, Dfc: true }) result.Add("DFC=1 · Data flow restricted");

        if (asdu?.IsNegativeConfirm == true || source.CauseDetails?.Negative == true)
            result.Add("COT negative confirmation");
        if (asdu?.IsTest == true || source.CauseDetails?.Test == true)
            result.Add("TEST=1");
        if (asdu?.CauseOfTransmission is >= 44 and <= 47)
            result.Add("Application protocol error");
        if (asdu?.Issues.Count > 0) result.Add("ASDU decode warning");

        if (asdu is not null)
        {
            var objects = asdu.Objects;
            if (objects.Any(o => o.QualityDetails?.Invalid == true)) result.Add("IV=1 · Invalid value");
            if (objects.Any(o => o.QualityDetails?.Overflow == true)) result.Add("OV=1 · Overflow");
            if (objects.Any(o => o.QualityDetails?.NotTopical == true)) result.Add("NT=1 · Not topical");
            if (objects.Any(o => o.QualityDetails?.Blocked == true)) result.Add("BL=1 · Blocked");
            if (objects.Any(o => o.QualityDetails?.Substituted == true)) result.Add("SB=1 · Substituted");
            if (objects.Any(o => o.QualityDetails?.Carry == true)) result.Add("CY=1 · Counter carry");
            if (objects.Any(o => o.QualityDetails?.Adjusted == true)) result.Add("CA=1 · Counter adjusted");
            if (objects.Any(o => o.Issues.Count > 0)) result.Add("Object decode warning");
            if (objects.Any(o => o.Cp56TimeDetails is { IsValid: false }))
                result.Add("TIME IV/DATE · Invalid device time");
            if (objects.Any(o => o.Cp56TimeDetails is { SubstitutedFlag: true }))
                result.Add("GEN=1 · Substituted device time");
            if (objects.Any(o => o.Cp56TimeDetails is { UnexpectedReservedBits: true }))
                result.Add("Time reserved bits set");
        }
        return string.Join("   ·   ", result);
    }

    public static bool HasAlerts(Iec103MasterEvidenceEvent source) =>
        Alerts(source).Length > 0;

    public static string TrustSummary(Iec103MasterEvidenceEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var frame = source.Frame;
        var asdu = source.AsduDetails;

        string linkStatus = frame is null ? "Link validation: not available" :
            !frame.IsChecksumValid || !frame.IsLengthValid ? "Link integrity: failed" :
            "Link integrity: checksum and length valid";

        if (asdu is null)
            return linkStatus + "   ·   Process quality: not encoded   ·   Device time: not encoded";

        var objects = asdu.Objects;
        var withQuality = objects.Where(o => o.QualityDetails is not null).ToArray();
        string quality = withQuality.Length == 0
            ? "Process quality: not encoded / unknown"
            : withQuality.Any(o => o.QualityDetails!.Invalid)
                ? "Process quality: IV=1 invalid"
                : withQuality.Length == objects.Count && objects.All(o => o.Issues.Count == 0)
                    ? "Process quality: IV=0 (not marked invalid)"
                    : "Process quality: partial evidence";

        var tagged = objects.Where(o => o.Cp56TimeDetails is not null).ToArray();
        string time;
        if (tagged.Length > 0)
        {
            var invalid = tagged.Any(o => !o.Cp56TimeDetails!.IsValid);
            var substituted = tagged.Any(o => o.Cp56TimeDetails!.SubstitutedFlag);
            time = "CP56 device time: " + (invalid ? "invalid" : "valid") +
                " · " + (substituted ? "substituted (GEN=1)" : "genuine (GEN=0)");
        }
        else if (objects.Any(o => !string.IsNullOrWhiteSpace(o.TimestampText)))
            time = "Device time: partial / other tag (no verified CP56 genuine flag)";
        else
            time = "Device time: not encoded";
        return linkStatus + "   ·   " + quality + "   ·   " + time;
    }
}
