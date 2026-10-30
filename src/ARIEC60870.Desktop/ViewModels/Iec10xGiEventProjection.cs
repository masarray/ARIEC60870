// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>UI-only event projection of a typed, source-scoped GI milestone.
/// No ASDU decode or GI state machine may live here.</summary>
public static class Iec10xGiEventProjection
{
    public static Iec103RelayEventLogEntry Project(Iec103MasterEvidenceEvent frame,
        Iec10xGiCasebook.GiMilestone milestone)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(milestone);
        var c = milestone.Case;
        return new Iec103RelayEventLogEntry
        {
            EvidenceSequenceNumber = frame.SequenceNumber,
            EvidenceReference = frame.EvidenceReference,
            TransactionCaseId = c.CaseId,
            TransactionKind = "GI",
            PhysicalSource = frame.PhysicalSource,
            ArrivalTimeUtc = frame.TimestampUtc,
            RelayTimeText = frame.TimestampUtc == DateTime.UnixEpoch
                ? "PC time unavailable (legacy capture)"
                : "PC " + frame.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff",
                    System.Globalization.CultureInfo.InvariantCulture),
            SignalName = "GI #" + c.CaseId + " · " + c.Description,
            SignalGroup = "GI",
            SignalType = "GI",
            NewValue = milestone.Phase + " · " + c.Status +
                (milestone.Phase == "GI ACTTERM" ?
                    $" · {c.DataFrames} frames / {c.DataObjects} objects / {c.UniqueIoas} unique IOAs · coverage unknown" : ""),
            EdgeReason = frame.DirectionText + " observed · source-matched evidence",
            CauseOfTransmission = frame.AsduDetails?.CotNameWithFlags ?? "",
            AsduType = frame.AsduType ?? "",
            RawHex = frame.RawHex,
            ProtocolMode = frame.ProtocolMode,
            CommonAddress = c.CommonAddress,
            InformationObjectAddress = 0,
            TypeId = 100,
            CauseDetails = frame.CauseDetails,
            QualityText = "N/A (GI lifecycle)"
        };
    }
}
