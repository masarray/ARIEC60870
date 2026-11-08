// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Typed, presentation-only attention ordering for the physical-frame ledger.
/// Every RX/TX frame, including malformed/undecoded evidence, remains visible.
/// Do not infer protocol state from descriptions, display text or HEX searches.
/// </summary>
public static class FrameTraceVisualPolicy
{
    public static string Classify(Iec103MasterEvidenceEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var frame = source.Frame;
        var asdu = source.AsduDetails;
        var control = frame?.LinkControl;

        // Inspect canonical fields ONLY. All applicable quality alarms count,
        // including blocked/substituted/stale values and BCR carry/adjustment.
        // A plausible checksum alone does not certify the information objects.
        if (frame is { IsChecksumValid: false } or { IsLengthValid: false } ||
            frame?.Issues.Count > 0 || asdu?.Issues.Count > 0 ||
            frame?.IsSingleCharacterNack == true ||
            control?.Dfc == true ||
            control is { Prm: false, FunctionCode: 1 } ||
            asdu?.IsNegativeConfirm == true || asdu?.IsTest == true ||
            asdu?.CauseOfTransmission is >= 44 and <= 47 ||
            source.CauseDetails?.Negative == true || source.CauseDetails?.Test == true ||
            source.CauseDetails?.Code is >= 44 and <= 47 ||
            asdu?.Objects.Any(o =>
                o.Issues.Count > 0 ||
                (o.QualityDetails is { } q &&
                    (q.Invalid || q.Overflow == true || q.Blocked == true ||
                     q.Substituted == true || q.NotTopical == true ||
                     q.Carry == true || q.Adjusted == true)) ||
                (o.Cp56TimeDetails is { } t &&
                    (!t.IsValid || t.SubstitutedFlag || t.UnexpectedReservedBits ||
                     t.DayOfWeekMatchesCalendar == false))) == true)
            return "Attention";

        if (asdu is not null)
        {
            if (asdu.IsControlCommand || asdu.TypeId is >= 45 and <= 51 or >= 58 and <= 64 ||
                asdu.TypeId is >= 100 and <= 107)
                return "Command";
            if (asdu.CauseOfTransmission is >= 20 and <= 36)
                return "GI";
            if (asdu.Objects.Count > 0) return "Data";
            return "Service";
        }

        if (source.CommandDetails is not null) return "Command";
        if (source.IsRelayValue || source.IsRelayEdgeEvent) return "Data";

        // ACD=1 is not routine even when FC9 says Class 2 is empty:
        // the RTU still signals Class 1 pending.
        if (control?.Acd == true) return "Attention";
        if (frame?.IsSingleCharacterAck == true) return "Routine";
        if (control is { Prm: true, FunctionCode: 9 or 10 or 11 } ||
            control is { Prm: false, FunctionCode: 0 or 9 or 11 })
            return "Routine";
        return "Unknown";
    }

    public static string Ink(string direction, string classification)
    {
        var muted = classification == "Routine";
        return direction switch
        {
            "TX" => muted ? "#425D79" : "#173E68",
            "RX" => muted ? "#3F6551" : "#18583B",
            _ => muted ? "#526070" : "#344456"
        };
    }
}
