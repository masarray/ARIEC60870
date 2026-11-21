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

        return new("Spontaneous",
            "COT3 spontaneous. Individual timestamps and their absolute PC offset do not identify RTU buffering; sequence evidence is required.");
    }

    /// <summary>
    /// Parsed CP56Time2a is an RTU wall clock. Keep Kind=Unspecified:
    /// the RTU time zone is unknown, so only intra-source elapsed intervals
    /// may be compared with elapsed PC reception intervals.
    /// </summary>
    internal static bool TryDeviceTimestamp(Iec103RelayEventLogEntry item, out DateTime timestamp)
    {
        var text = item.RelayTimeText?.Trim() ?? string.Empty;
        timestamp = default;
        return !item.RelayTimeInvalid &&
               !string.IsNullOrWhiteSpace(text) &&
               !text.StartsWith("PC ", StringComparison.OrdinalIgnoreCase) &&
               !text.Contains("invalid", StringComparison.OrdinalIgnoreCase) &&
               item.ArrivalTimeUtc != DateTime.UnixEpoch &&
               DateTime.TryParseExact(text, AbsoluteTimeFormats,
                   CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
    }
}
