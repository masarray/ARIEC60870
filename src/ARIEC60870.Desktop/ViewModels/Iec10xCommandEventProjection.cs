// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Thin, typed projection of real command ASDUs to the existing Events workspace.
/// Keeps protocol parsing and lifecycle authority in Master; this is only a view fact.
/// </summary>
public static class Iec10xCommandEventProjection
{
    public static Iec103RelayEventLogEntry? Project(Iec103MasterEvidenceEvent item, long? caseId = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104) ||
            item.Direction is not (FrameDirection.MasterToSlave or FrameDirection.SlaveToMaster) ||
            item.Frame is { IsChecksumValid: false })
            return null;
        var asdu = item.AsduDetails;
        if (asdu is null || asdu.Issues.Count > 0 ||
            asdu.TypeId is not (>= 45 and <= 51 or >= 58 and <= 64 or >= 100 and <= 107))
            return null;

        var title = Iec101TraceNarrator.ApplicationTitle(asdu, item.Direction);
        var isMaster = item.Direction == FrameDirection.MasterToSlave;
        var commandLabel = asdu.TypeId switch
        {
            100 => "General Interrogation",
            101 => "Counter Interrogation",
            102 => "Read Command",
            103 => "Clock Synchronization",
            104 or 107 => "Test Command",
            105 => "Reset Process",
            _ => "Control command"
        };
        var target = asdu.InformationObjectAddress.HasValue
            ? "IOA " + asdu.InformationObjectAddress.Value : "CA " + asdu.CommonAddress;
        var signal = commandLabel + " · " + target;
        var qualifier = asdu.CommandDetails?.ValueMeaning ?? string.Empty;
        // CP56Time2a belongs to the original command ASDU, not the USB/PC receipt.
        // Keep invalid-time explicitly marked; only use host time when wire time is absent.
        var decodedTime = item.InformationObjectDetails?.TimestampText;
        if (string.IsNullOrWhiteSpace(decodedTime))
            decodedTime = asdu.FirstObject?.TimestampText;
        if (string.IsNullOrWhiteSpace(decodedTime))
            decodedTime = asdu.TimestampText;
        var hasDeviceTime = !string.IsNullOrWhiteSpace(decodedTime);
        var visibleTime = hasDeviceTime ? decodedTime! :
            "PC " + item.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff",
                System.Globalization.CultureInfo.InvariantCulture);
        return new Iec103RelayEventLogEntry
        {
            EvidenceSequenceNumber = item.SequenceNumber,
            PhysicalSource = item.PhysicalSource,
            EvidenceReference = item.EvidenceReference,
            RelayTimeText = visibleTime,
            RelayTimeInvalid = hasDeviceTime && (
                item.RelayTimestampInvalid ||
                decodedTime!.Contains("invalid", StringComparison.OrdinalIgnoreCase)),
            ArrivalTimeUtc = item.TimestampUtc,
            SignalName = signal,
            SignalGroup = "Commands",
            SignalType = "Command",
            NewValue = title +
                (caseId.HasValue ? " · Case #" + caseId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "") +
                (string.IsNullOrWhiteSpace(qualifier) ? "" : " · " + qualifier),
            EdgeReason = isMaster ? "Observed Master TX" : "Observed RTU RX",
            CauseOfTransmission = asdu.CotNameWithFlags,
            AsduType = asdu.TypeName,
            RawHex = item.RawHex,
            ProtocolMode = item.ProtocolMode,
            CommonAddress = asdu.CommonAddress,
            InformationObjectAddress = asdu.InformationObjectAddress,
            TypeId = asdu.TypeId,
            CauseDetails = asdu.CauseDetails,
            QualityText = "N/A (command)"
        };
    }
}
