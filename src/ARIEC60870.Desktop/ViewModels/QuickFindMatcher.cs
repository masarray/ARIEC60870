// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0
using ARIEC60870.Master.Protocol.Iec10x;
namespace ARIEC60870.Desktop.ViewModels;

/// <summary>Pure filtering over bounded UI projections; no capture or protocol changes.</summary>
public static class QuickFindMatcher
{
    private static bool Has(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    public static bool MatchesText(string? query, string? a = null, string? b = null,
        string? c = null, string? d = null, string? e = null, string? f = null,
        string? g = null, string? h = null, string? i = null, string? j = null,
        string? k = null, string? l = null)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query.Trim();
        return Has(a,q) || Has(b,q) || Has(c,q) || Has(d,q) || Has(e,q) ||
               Has(f,q) || Has(g,q) || Has(h,q) || Has(i,q) || Has(j,q) ||
               Has(k,q) || Has(l,q);
    }
    public static bool MatchesTrace(EvidenceRow r, string? query, string scope)
    {
        if (scope switch
        {
            "TX / SCADA" => r.Direction != "TX",
            "RX / RTU" => r.Direction != "RX",
            "Commands" => r.Source.CommandDetails is null &&
                !(int.TryParse(r.TypeId, out var ti) && ti is (>= 45 and <= 51 or >= 58 and <= 64)),
            "GI" => r.TypeId != "100" && !Has(r.ProtocolTraceMeaning,"GI") &&
                !Has(r.ProtocolTraceMeaning,"interrogation"),
            "Negative / errors" => r.Source.CauseDetails?.Negative != true &&
                !Has(r.Category,"warning") && !Has(r.Category,"error"),
            _ => false
        }) return false;
        return MatchesText(query,r.ProtocolTraceHeader,r.ProtocolTraceMeaning,r.RawHex,
            r.IoAddress,r.CommonAddress,r.CotCode,r.TypeId,r.SignalOrAddress,
            r.Category,r.Time,r.Source.PhysicalSource,r.Source.EvidenceReference);
    }
    public static bool MatchesEvent(RelayEventRow r, string? q) =>
        MatchesText(q,r.Signal,r.Address,r.Type,r.CotLabel,r.Reason,r.NewValue,
            r.DisplayValue,r.Quality,r.PhysicalSource,r.TypeId,r.EvidenceReference,r.RelayTime);
    public static bool MatchesValue(ValueRow r, string? q) =>
        MatchesText(q,r.Signal,r.Address,r.Value,r.Quality,r.Group,r.Type,
            r.CotLabel,r.PhysicalSource,r.IoAddress,r.CommonAddress,r.FunInf,r.TypeId);
    public static bool MatchesControl(Iec10xCommandCasebook.CommandCase r, string? q) =>
        MatchesText(q,r.CaseId.ToString(),r.Address,r.Target,r.Status.ToString(),
            r.Source,r.Interpretation,r.Protocol,r.FirstTime.ToString());
    public static bool MatchesGi(Iec10xGiCasebook.GiCase r, string? q) =>
        MatchesText(q,r.CaseId.ToString(),r.Description,r.Status.ToString(),
            r.Source,r.Summary,r.Protocol,r.Coverage.ToString());
}
