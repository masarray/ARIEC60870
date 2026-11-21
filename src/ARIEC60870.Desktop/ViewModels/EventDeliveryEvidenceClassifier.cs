// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Conservative on-wire event delivery *evidence*, not a claim about RTU queue
/// internals. Neither IEC-101 nor IEC-104 carries a standard "buffered/live"
/// bit. COT3 can report both newly produced and previously queued events.
/// A large device-vs-PC time gap is only a hint until clocks are verified.
/// </summary>
public sealed record EventDeliveryEvidence(string Label, string Basis);

public static class EventDeliveryEvidenceClassifier
{
    private static readonly string[] AbsoluteTimeFormats =
    [
        "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss.ff",
        "yyyy-MM-dd HH:mm:ss.f", "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss"
    ];

    public static EventDeliveryEvidence Classify(Iec103RelayEventLogEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.TransactionKind is "Command" or "GI")
            return new("Transaction", "Control/GI stage, not an RTU process buffer marker.");

        if (item.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104) ||
            item.CauseDetails is not { } cause)
            return new("Unknown", "No typed IEC-101/104 COT evidence for the delivery mode.");

        if (cause.Test || cause.Negative)
            return new("Unverified", "TEST or negative COT: do not infer live/buffered process delivery.");

        switch (cause.Code)
        {
            case 1: return new("Cyclic", "COT1 periodic; not proof of live or buffered history.");
            case 2: return new("Background", "COT2 background scan; not a spontaneous buffer marker.");
            case 11:
            case 12:
                return new("Return info", "COT11/12 return information; buffering is not identified.");
            case >= 20 and <= 36:
                return new("GI data", "Interrogated snapshot; not a spontaneous event.");
            case not 3:
                return new("Unknown", "COT does not identify buffering.");
        }

        var text = item.RelayTimeText?.Trim() ?? string.Empty;
        if (item.RelayTimeInvalid || string.IsNullOrEmpty(text) ||
            text.StartsWith("PC ", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
            !DateTime.TryParseExact(text, AbsoluteTimeFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var deviceLocal) ||
            item.ArrivalTimeUtc == DateTime.UnixEpoch)
        {
            return new("Spontaneous", "COT3 observed. No valid absolute device time to assess delivery delay.");
        }

        var delay = item.ArrivalTimeUtc.ToUniversalTime() - deviceLocal.ToUniversalTime();
        if (delay >= TimeSpan.FromSeconds(30))
            return new("Delayed?", "COT3 with device time at least 30 s before PC arrival; clock/time-zone mismatch or buffering are both possible.");
        if (delay.Duration() <= TimeSpan.FromSeconds(5))
            return new("Near-live?", "COT3 with device time within 5 s of PC arrival; clocks and actual buffer path remain unverified.");

        return new("Spontaneous", "COT3; device/host time difference is inconclusive without clock validation.");
    }
}
