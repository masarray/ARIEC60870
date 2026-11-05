// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Monitor;

/// <summary>
/// Single-writer IEC-101 process observation classifier. Runs on the complete
/// capture stream, independent of the lossy WPF frame preview. Type/quality
/// decoding comes only from the canonical IEC10x Core codec.
/// </summary>
public sealed class PassiveIec101ProcessEventClassifier
{
    private const int MaxStateKeys = 65536;
    private readonly int _linkWidth;
    private readonly Iec10xAsduDecoder _decoder;
    private readonly Dictionary<StateKey, string> _previous = new();
    private long _untrackedStateObservations;

    private readonly record struct StateKey(int Circuit, int Ca, int Ioa, int TypeId);

    public PassiveIec101ProcessEventClassifier(
        int linkWidth, int cotWidth, int caWidth, int ioaWidth)
    {
        if (linkWidth is < 0 or > 2 || cotWidth is < 1 or > 2 ||
            caWidth is < 1 or > 2 || ioaWidth is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(linkWidth));
        _linkWidth = linkWidth;
        _decoder = new Iec10xAsduDecoder(cotWidth, caWidth, ioaWidth);
    }

    /// <summary>
    /// State tracking can be exhausted under abnormal cardinality. Such new
    /// keys remain untracked (no fabricated GI/cyclic change), while spontaneous
    /// observations remain eligible. This counter makes the limitation explicit.
    /// </summary>
    public long UntrackedStateObservations => _untrackedStateObservations;

    /// <summary>Appends the ONE-based ordinal of each accepted source object.</summary>
    public void Observe(PassiveObservedFrame frame, List<int> acceptedObjectIndexes)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(acceptedObjectIndexes);
        if (!frame.ChecksumValid || frame.Format != "Variable" ||
            !Ft12WireLayout.TryParse(frame.RawBytes,
                frame.DetectedLinkAddressWidth ?? _linkWidth, out var wire) ||
            wire.Kind != Ft12WireKind.Variable || wire.AsduLength == 0 ||
            (wire.Control & 0x40) != 0) // Primary = Master TX, never an RTU SOE.
            return;

        // An FT1.2 checksum is NOT enough to establish a decodable ASDU.
        var asdu = _decoder.Decode(
            frame.RawBytes.AsSpan(wire.AsduOffset, wire.AsduLength).ToArray());
        if (asdu.Issues.Count > 0 || asdu.ObjectCount != asdu.Objects.Count ||
            asdu.ObjectCount == 0)
            return;

        var digital = Iec10xObservationIdentity.IsDigitalProcessType(asdu.TypeId);
        var measurement = Iec10xObservationIdentity.IsMeasurementOrCounterType(asdu.TypeId);
        // Keep parity with the supported IEC-101 process families of the
        // existing Values/Event projector. Unsupported/vendor-private stays raw.
        if ((!digital && !measurement) ||
            asdu.TypeId is not (>= 1 and <= 16 or >= 30 and <= 32 or >= 34 and <= 37))
            return;

        foreach (var obj in asdu.Objects)
        {
            if (obj.Issues.Count > 0 || obj.InformationObjectAddress is < 0 or > 0xFFFFFF)
                continue;
            var eventByCot = asdu.CauseOfTransmission is 3 or 11 or 12;
            var measurementObservation = measurement && asdu.CauseOfTransmission is >= 1 and <= 36;
            var changed = false;
            // The only cyclic/GI change events are known SP/DP values, never
            // a guessed edge from missing baseline or unsupported quality.
            if (asdu.TypeId is 1 or 2 or 3 or 4 or 30 or 31)
            {
                var key = new StateKey(frame.StreamId / 2, asdu.CommonAddress,
                    obj.InformationObjectAddress, asdu.TypeId);
                if (_previous.TryGetValue(key, out var last))
                {
                    changed = !string.Equals(last, obj.ShortValue, StringComparison.Ordinal);
                    _previous[key] = obj.ShortValue;
                }
                else if (_previous.Count < MaxStateKeys)
                    _previous.Add(key, obj.ShortValue);
                else
                    _untrackedStateObservations++;
            }

            if (eventByCot || measurementObservation ||
                (changed && asdu.CauseOfTransmission is >= 1 and <= 36))
                acceptedObjectIndexes.Add(obj.Index);
        }
    }
}
