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
        Reason = item.EdgeReason;
        Fun = item.FunctionType.HasValue ? item.FunctionType.Value.ToString(CultureInfo.InvariantCulture) : "-";
        Inf = item.InformationNumber.HasValue ? item.InformationNumber.Value.ToString(CultureInfo.InvariantCulture) : "-";
        FunInf = Fun == "-" && Inf == "-" ? "-" : $"{Fun}/{Inf}";
        TypeId = item.TypeId.HasValue ? item.TypeId.Value.ToString(CultureInfo.InvariantCulture) : "-";
        Type = item.SignalType;
        Cot = item.CauseOfTransmission;
        CommonAddress = item.CommonAddress.HasValue ? item.CommonAddress.Value.ToString(CultureInfo.InvariantCulture) : "-";
        IoAddress = item.InformationObjectAddress.HasValue ? item.InformationObjectAddress.Value.ToString(CultureInfo.InvariantCulture) : "-";
        Address = Protocol is "101" or "104" ? (IoAddress == "-" ? "-" : "IOA " + IoAddress) : (FunInf == "-" ? "-" : "FUN/INF " + FunInf);
        Quality = string.IsNullOrWhiteSpace(item.QualityText) ? "-" : item.QualityText;
        Mapped = item.IsMapped ? "Yes" : "No";
        RawHex = item.RawHex;
    }

    public long Sequence { get; }
    public string Protocol { get; }
    public string RelayTime { get; }
    public string Signal { get; }
    public string Previous { get; }
    public string NewValue { get; }
    public string Reason { get; }
    public string Fun { get; }
    public string Inf { get; }
    public string FunInf { get; }
    public string TypeId { get; }
    public string Type { get; }
    public string Cot { get; }
    public string CommonAddress { get; }
    public string IoAddress { get; }
    public string Address { get; }
    public string Quality { get; }
    public string Mapped { get; }
    public string RawHex { get; }

    private static string BuildSoeTime(Iec103RelayEventLogEntry item)
    {
        if (string.IsNullOrWhiteSpace(item.RelayTimeText) ||
            item.RelayTimeText == "-" ||
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
