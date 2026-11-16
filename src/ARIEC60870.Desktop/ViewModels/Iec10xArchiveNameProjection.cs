// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Mapping;
using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Restores each archived wire frame exactly once, then establishes CA
/// uniqueness across its trusted physical source before any display-name
/// alias is admitted. This is a bounded preview projection, not a new decoder.
/// </summary>
public static class Iec10xArchiveNameProjection
{
    private readonly record struct SourceKey(Iec60870ProtocolMode Protocol,
        string PhysicalSource, string TcpConnection);

    public static EvidenceRow[] Project(IReadOnlyList<CaptureFrameSnapshot> frames,
        Iec10xPointDisplayIndex names)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(names);

        var decoded = new Iec103MasterEvidenceEvent[frames.Count];
        var firstCa = new Dictionary<SourceKey, int>();
        var conflicted = new HashSet<SourceKey>();

        for (var i = 0; i < frames.Count; i++)
        {
            var item = EvidenceRow.RestoreCaptureSource(frames[i]);
            decoded[i] = item;
            if (!IsTrustedCaObservation(item)) continue;

            var key = Key(item);
            var ca = item.CommonAddressNumber!.Value;
            if (!firstCa.TryAdd(key, ca) && firstCa[key] != ca)
                conflicted.Add(key);
        }

        var projected = new EvidenceRow[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            var item = decoded[i];
            var allowAlias = IsTrustedCaObservation(item) &&
                item.IsRelayValue && !conflicted.Contains(Key(item));
            projected[i] = new EvidenceRow(frames[i], names, item, allowAlias);
        }
        return projected;
    }

    private static bool IsTrustedCaObservation(Iec103MasterEvidenceEvent item)
        => item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104 &&
           item.Direction == FrameDirection.SlaveToMaster &&
           item.CommonAddressNumber.HasValue &&
           item.AsduDetails is { IsHeaderValid: true, IsTest: false, Issues.Count: 0 } &&
           Iec10xObservationIdentity.HasTrustedDecodedPayload(item);

    private static SourceKey Key(Iec103MasterEvidenceEvent item)
        => new(item.ProtocolMode,
            string.IsNullOrWhiteSpace(item.PhysicalSource)
                ? "SINGLE" : item.PhysicalSource.Trim().ToUpperInvariant(),
            item.ProtocolMode == Iec60870ProtocolMode.Iec104
                ? item.ConnectionInstanceId : string.Empty);
}
