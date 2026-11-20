// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Thin, typed projection of real command ASDUs to the existing Events workspace.
/// Keeps protocol parsing and lifecycle authority in Master; this is only a view fact.
/// </summary>
public static class Iec10xCommandEventProjection
{
    public static Iec103RelayEventLogEntry? Project(Iec103MasterEvidenceEvent item, long? caseId = null, string? mappedSignalName = null, string? matchedStagePhase = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104) ||
            item.Direction is not (FrameDirection.MasterToSlave or FrameDirection.SlaveToMaster) ||
            item.Frame is { IsChecksumValid: false })
            return null;
        var asdu = item.AsduDetails;
        // TI100 GI belongs exclusively to the GI milestone projection, avoiding
        // duplicate command+GI rows for the same on-wire application fact.
        if (asdu is null || asdu.Issues.Count > 0 || asdu.TypeId == 100 ||
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
        // Label only; original TX/RX command target and lifecycle stay on-wire.
        var signal = string.IsNullOrWhiteSpace(mappedSignalName)
            ? commandLabel + " · " + target
            : mappedSignalName + " · " + target;
        var qualifier = asdu.CommandDetails?.ValueMeaning ?? string.Empty;
        var step = StepText(asdu, item.Direction, matchedStagePhase);
        // CP56Time2a belongs to the original command ASDU, not the USB/PC receipt.
        // Keep invalid-time explicitly marked; only use host time when wire time is absent.
        var decodedTime = item.InformationObjectDetails?.TimestampText;
        if (string.IsNullOrWhiteSpace(decodedTime))
            decodedTime = asdu.FirstObject?.TimestampText;
        if (string.IsNullOrWhiteSpace(decodedTime))
            decodedTime = asdu.TimestampText;
        var hasDeviceTime = !string.IsNullOrWhiteSpace(decodedTime);
        var visibleTime = hasDeviceTime ? decodedTime! :
            item.TimestampUtc == DateTime.UnixEpoch ? "PC time unavailable (legacy capture)" :
            "PC " + item.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff",
                System.Globalization.CultureInfo.InvariantCulture);
        return new Iec103RelayEventLogEntry
        {
            EvidenceSequenceNumber = item.SequenceNumber,
            PhysicalSource = item.PhysicalSource,
            EvidenceReference = item.EvidenceReference,
            TransactionCaseId = caseId,
            TransactionKind = caseId.HasValue ? "Command" : string.Empty,
            RelayTimeText = visibleTime,
            RelayTimeInvalid = hasDeviceTime && (
                item.RelayTimestampInvalid ||
                decodedTime!.Contains("invalid", StringComparison.OrdinalIgnoreCase)),
            ArrivalTimeUtc = item.TimestampUtc,
            SignalName = signal,
            SignalGroup = "Commands",
            SignalType = "Command",
            NewValue = step +
                (caseId.HasValue ? " · Case #" + caseId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "") +
                (string.IsNullOrWhiteSpace(qualifier) ? "" : " · " + qualifier),
            EdgeReason = (isMaster ? "Observed Master TX · " : "Observed RTU RX · ") + title,
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

    /// <summary>
    /// An ACTCON telegram is a received protocol fact. Only the casebook's
    /// exact-matched stage can elevate that fact to a correlated acceptance.
    /// This method never interprets ACTCON as physical CB operation.
    /// </summary>
    private static string StepText(Iec10xAsduDecode asdu, FrameDirection direction, string? matchedStage)
    {
        var select = asdu.CommandDetails?.Select;
        var action = select == true ? "SELECT" : select == false ? "EXECUTE" : "Command";
        var cot = asdu.CauseDetails?.Code ?? asdu.CauseOfTransmission;
        var negative = asdu.CauseDetails?.Negative == true || asdu.IsNegativeConfirm;
        var test = asdu.CauseDetails?.Test == true;
        string step;

        if (matchedStage?.Contains("overlapping", StringComparison.OrdinalIgnoreCase) == true)
            step = "Repeated " + action + " · review";
        else if (matchedStage?.Contains("ambiguous", StringComparison.OrdinalIgnoreCase) == true ||
                 matchedStage?.Contains("out of order", StringComparison.OrdinalIgnoreCase) == true)
            step = "Reply mismatch · review";
        else if (direction == FrameDirection.MasterToSlave)
            step = cot switch
            {
                6 => action + " sent",
                8 => "CANCEL requested",
                _ => "Master command COT " + cot
            };
        else if (negative || cot is >= 44 and <= 47)
            step = "Command rejected · COT " + cot;
        else
            step = cot switch
            {
                7 when matchedStage is "SELECT ACTCON" or "EXECUTE ACTCON" => action + " accepted · ACTCON",
                7 => action + " ACTCON received · unpaired",
                9 when matchedStage == "RTU DEACTCON" => "Cancellation confirmed",
                9 => "DEACTCON received · unpaired",
                10 when matchedStage == "EXECUTE ACTTERM" => "EXECUTE ended · ACTTERM",
                10 => "ACTTERM received · unpaired",
                _ => "RTU command COT " + cot
            };

        // Keep control state separate from the protocol phase. A positive
        // reply is never a verified physical Open/Close indication.
        var q = asdu.CommandDetails?.QualifierRaw;
        var state = asdu.TypeId switch
        {
            45 or 58 when q.HasValue => (q.Value & 1) == 1 ? "ON" : "OFF",
            46 or 59 when q.HasValue => (q.Value & 3) switch
            {
                1 => "OFF", 2 => "ON", _ => "invalid DCS"
            },
            47 or 60 when q.HasValue => (q.Value & 3) switch
            {
                1 => "LOWER", 2 => "HIGHER", _ => "invalid RCS"
            },
            _ => string.Empty
        };
        if (state.Length > 0) step += " · " + state;
        if (test) step = "TEST (non-operational) · " + step;
        return step;
    }
}
