// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

public sealed class RelayEventRow
{
    public RelayEventRow(Iec103RelayEventLogEntry item)
    {
        Sequence = item.EvidenceSequenceNumber;
        PhysicalSource = item.PhysicalSource;
        EvidenceReference = item.EvidenceReference;
        TransactionCaseId = item.TransactionCaseId;
        TransactionKind = item.TransactionKind;
        Protocol = item.ProtocolMode switch
        {
            Iec60870ProtocolMode.Iec101 => "101",
            Iec60870ProtocolMode.Iec104 => "104",
            _ => "103"
        };
        RelayTime = BuildSoeTime(item);
        Signal = item.SignalName;
        Previous = string.IsNullOrWhiteSpace(item.PreviousValue) ? "-" : item.PreviousValue;
        NewValue = item.NewValue;
        DisplayValue = FormatDisplayValue(item.TypeId, item.SignalType, NewValue);
        Reason = item.EdgeReason;
        Fun = item.FunctionType.HasValue ? item.FunctionType.Value.ToString(CultureInfo.InvariantCulture) : "-";
        Inf = item.InformationNumber.HasValue ? item.InformationNumber.Value.ToString(CultureInfo.InvariantCulture) : "-";
        FunInf = Fun == "-" && Inf == "-" ? "-" : $"{Fun}/{Inf}";
        TypeId = item.TypeId.HasValue ? item.TypeId.Value.ToString(CultureInfo.InvariantCulture) : "-";
        Type = item.SignalType;
        Cot = item.CauseOfTransmission;
        CotLabel = item.CauseDetails is { } cause ? $"{cause.Code} · {cause.Phase}" : Cot;
        CotChips = ProtocolFlagPresentation.Cot(item.CauseDetails);
        CotDetail = item.CauseDetails?.Detail ?? Cot;
        CommonAddress = item.CommonAddress.HasValue ? item.CommonAddress.Value.ToString(CultureInfo.InvariantCulture) : "-";
        IoAddress = item.InformationObjectAddress.HasValue ? item.InformationObjectAddress.Value.ToString(CultureInfo.InvariantCulture) : "-";
        Address = Protocol is "101" or "104"
            ? (IoAddress == "-" ? "-" : (string.IsNullOrWhiteSpace(PhysicalSource) ? "" : PhysicalSource + " · ") + "IOA " + IoAddress)
            : (FunInf == "-" ? "-" : "FUN/INF " + FunInf);
        Quality = string.IsNullOrWhiteSpace(item.QualityText) ? "-" : item.QualityText;
        QualityChips = ProtocolFlagPresentation.Quality(item.QualityDetails, Quality, Protocol is "101" or "104");
        QualityDetail = item.QualityDetails?.BitDetails ?? Quality;
        Mapped = item.IsMapped ? "Yes" : "No";
        RawHex = item.RawHex;
    }

    public long Sequence { get; }
    public string PhysicalSource { get; }
    public string EvidenceReference { get; }
    public long? TransactionCaseId { get; }
    public string TransactionKind { get; }
    public string Protocol { get; }
    public string RelayTime { get; }
    public string DisplayRelayTime => RelayTime == "no timestamp" ? "—" : RelayTime;
    public string Signal { get; }
    public string Previous { get; }
    public string NewValue { get; }
    public string DisplayValue { get; }
    public string Reason { get; }
    public string Fun { get; }
    public string Inf { get; }
    public string FunInf { get; }
    public string TypeId { get; }
    public string Type { get; }
    public string Cot { get; }
    public string CotLabel { get; }
    public string CotDetail { get; }
    public IReadOnlyList<ProtocolFlagChip> CotChips { get; }
    public string CommonAddress { get; }
    public string IoAddress { get; }
    public string Address { get; }
    public string Quality { get; }
    public string QualityDetail { get; }
    public IReadOnlyList<ProtocolFlagChip> QualityChips { get; }
    public string Mapped { get; }
    public string RawHex { get; }

    /// <summary>Human-facing state label. Original evidence value remains unchanged.</summary>
    public static string FormatDisplayValue(int? typeId, string? signalType, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "-";
        var value = raw.Trim();
        var pointType = typeId is 1 or 30 ? "Single Point" :
            typeId is 3 or 31 ? "Double Point" :
            signalType?.Contains("Double Point", StringComparison.OrdinalIgnoreCase) == true ? "Double Point" :
            signalType?.Contains("Single Point", StringComparison.OrdinalIgnoreCase) == true ? "Single Point" : null;

        var equal = value.IndexOf('=');
        if (equal > 0)
        {
            var prefix = value[..equal].Trim();
            if (prefix.Equals("SP", StringComparison.OrdinalIgnoreCase)) pointType = "Single Point";
            else if (prefix.Equals("DP", StringComparison.OrdinalIgnoreCase)) pointType = "Double Point";
            else return raw;
            value = value[(equal + 1)..].Trim();
        }
        if (pointType is null) return raw;
        var state = value.ToUpperInvariant() switch
        {
            "ON" or "CLOSED" or "1" => "ON",
            "OFF" or "OPEN" or "0" => "OFF",
            "INTERMEDIATE" or "TRANSIENT" => "INTERMEDIATE",
            "INDETERMINATE" or "UNKNOWN" => "INDETERMINATE",
            _ => null
        };
        return state is null ? raw : pointType + " " + state;
    }

    private static string BuildSoeTime(Iec103RelayEventLogEntry item)
    {
        if (string.IsNullOrWhiteSpace(item.RelayTimeText) ||
            item.RelayTimeText == "-" ||
            item.RelayTimeText.Contains("no timestamp", System.StringComparison.OrdinalIgnoreCase) ||
            item.RelayTimeText.Contains("No relay timestamp", System.StringComparison.OrdinalIgnoreCase))
        {
            return "no timestamp";
        }

        if (item.RelayTimeText.Contains('-', System.StringComparison.Ordinal))
        {
            return item.RelayTimeText;
        }

        var localDate = item.ArrivalTimeUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return localDate + " " + item.RelayTimeText;
    }
}
