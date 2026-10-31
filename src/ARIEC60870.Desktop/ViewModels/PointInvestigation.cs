// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>Source-backed point investigation. No IOA/time-based substitute frame.</summary>
public static class PointInvestigation
{
    public static bool MatchesScope(ValueRow point, string scope) => scope switch
    {
        "Quality flags" => point.QualityFlags is { } q &&
            q.ActiveBadges.Any(badge => badge != "Good"),
        "Unmapped" => point.Mapped != "Yes",
        "Recently changed" => point.IsRecentlyChanged,
        "No device time" => point.RelayTime is "no timestamp" or "-",
        _ => true
    };

    public static bool HasOriginalReference(ValueRow point) =>
        !string.IsNullOrWhiteSpace(point.EvidenceReference);

    public static string Describe(ValueRow point)
    {
        ArgumentNullException.ThrowIfNull(point);
        var address = point.Protocol is "101" or "104"
            ? "CA " + point.CommonAddress + " / IOA " + point.IoAddress
            : "FUN / INF " + point.FunInf;
        var quality = point.Quality == "-" || string.IsNullOrWhiteSpace(point.Quality)
            ? "not provided" : point.Quality;
        var time = point.RelayTime is "no timestamp" or "-" ? "not transmitted/unknown" : point.RelayTime;
        var origin = HasOriginalReference(point)
            ? "Source frame reference present"
            : "Original not linked (unobserved/mapped placeholder)";
        return point.Signal + " = " + point.Value + " · " + address +
            " · Quality: " + quality + " · Relay time: " + time + " · " + origin;
    }

    public static EvidenceRow? FindOriginal(IEnumerable<EvidenceRow> frames, ValueRow point)
    {
        if (!HasOriginalReference(point)) return null;
        var protocol = point.Protocol switch
        {
            "101" => Iec60870ProtocolMode.Iec101,
            "104" => Iec60870ProtocolMode.Iec104,
            _ => Iec60870ProtocolMode.Iec103
        };
        return frames.FirstOrDefault(frame =>
            Iec10xObservationIdentity.SamePhysicalFrame(frame.Source,
                point.EvidenceReference, point.PhysicalSource, protocol, 0));
    }
}
