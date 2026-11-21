// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Stateful, source-qualified *inference* from successive real RX events.
/// A CP56 timestamp is not a universal RTU buffer flag. Comparing absolute
/// device and PC wall clocks is forbidden: time zone/offset can differ by hours.
/// Instead, compare elapsed device-event time to elapsed host RX time.
/// An observable catch-up burst is evidence of replay/backlog, not RTU FIFO proof.
/// </summary>
public sealed class EventDeliverySequenceClassifier
{
    private const int MaxSources = 64;
    private const double MaxPairHostSeconds = 8;
    private const double MinDeviceAdvanceSeconds = 3;
    private const double CatchupRatio = 3;
    private readonly Dictionary<SourceKey, SourceState> _sources = new();

    public void Reset() => _sources.Clear();

    public EventDeliveryEvidence Observe(Iec103RelayEventLogEntry item,
        long? physicalFrameSequence = null, string? connectionInstance = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var semantic = EventDeliveryEvidenceClassifier.Classify(item);
        if (semantic.Label != "Spontaneous" ||
            !EventDeliveryEvidenceClassifier.TryDeviceTimestamp(item, out var device))
            return semantic;

        var key = new SourceKey(item.ProtocolMode, item.PhysicalSource ?? string.Empty,
            item.CommonAddress, connectionInstance ?? string.Empty);
        // A multi-object ASDU creates several object-level sequence numbers;
        // the physical frame sequence supplied by canonical decoder wins.
        var frameSequence = physicalFrameSequence is > 0
            ? physicalFrameSequence.Value : item.EvidenceSequenceNumber;
        if (!_sources.TryGetValue(key, out var state))
        {
            if (_sources.Count >= MaxSources)
            {
                // Bounded state, independent from the Event History length.
                var oldest = _sources.MinBy(pair => pair.Value.LastArrivalUtc);
                _sources.Remove(oldest.Key);
            }
            state = new SourceState();
            _sources[key] = state;
        }

        var host = item.ArrivalTimeUtc.ToUniversalTime();
        if (frameSequence > 0 &&
            state.LastFrameSequence == frameSequence &&
            state.LastFrameSequence > 0)
        {
            // Every object in one ASDU shares the physical frame. Never
            // count sibling IOAs as independent confirmation of backlog.
            return state.LastDecision;
        }

        var label = semantic;
        if (state.HasObservation)
        {
            var hostDelta = (host - state.LastArrivalUtc).TotalSeconds;
            var deviceDelta = (device - state.LastDeviceTime).TotalSeconds;
            var fasterThanReceipt =
                hostDelta > 0 && hostDelta <= MaxPairHostSeconds &&
                deviceDelta >= MinDeviceAdvanceSeconds &&
                deviceDelta >= hostDelta * CatchupRatio;

            state.CatchupPairs = fasterThanReceipt
                ? Math.Min(3, state.CatchupPairs + 1) : 0;

            if (state.CatchupPairs >= 2)
            {
                label = new EventDeliveryEvidence("Buffer",
                    $"Inferred backlog: two or more successive distinct RX frames advanced device event time faster than host receipt " +
                    $"(latest device Δ={deviceDelta.ToString("0.###", CultureInfo.InvariantCulture)} s, " +
                    $"host Δ={hostDelta.ToString("0.###", CultureInfo.InvariantCulture)} s). " +
                    "Not a standard IEC buffered bit or RTU FIFO proof.");
            }
            // A non-catch-up pair immediately removes the buffer cue.
            // Persistent clock offsets and slow normal FC10 polling are benign.
        }

        state.HasObservation = true;
        state.LastArrivalUtc = host;
        state.LastDeviceTime = device;
        state.LastFrameSequence = frameSequence;
        state.LastDecision = label;
        return label;
    }

    private readonly record struct SourceKey(
        Iec60870ProtocolMode Protocol, string PhysicalSource, int? CommonAddress,
        string ConnectionInstance);

    private sealed class SourceState
    {
        public bool HasObservation { get; set; }
        public DateTime LastArrivalUtc { get; set; }
        public DateTime LastDeviceTime { get; set; }
        public long LastFrameSequence { get; set; }
        public int CatchupPairs { get; set; }
        public EventDeliveryEvidence LastDecision { get; set; } =
            new("Spontaneous", "No sequence evidence of buffered delivery.");
    }
}
