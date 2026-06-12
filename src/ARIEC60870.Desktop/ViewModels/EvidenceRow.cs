// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;
using ARIEC60870.Core.Mapping;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

public sealed partial class EvidenceRow
{
    public EvidenceRow(Iec103MasterEvidenceEvent item, Iec10xPointMappingEntry? ioaPoint = null)
    {
        Source = item;
        Sequence = item.SequenceNumber;
        Time = item.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        Direction = item.DirectionText;
        State = item.State.ToString();
        ProtocolMode = item.ProtocolMode switch
        {
            Iec60870ProtocolMode.Iec101 => "101",
            Iec60870ProtocolMode.Iec104 => "104",
            _ => "103"
        };
        ProtocolName = item.ProtocolMode switch
        {
            Iec60870ProtocolMode.Iec101 => "IEC-101",
            Iec60870ProtocolMode.Iec104 => "IEC-104",
            _ => "IEC-103"
        };
        DataClass = item.DataClass;
        ResponseTime = item.ResponseTimeMs.HasValue ? item.ResponseTimeMs.Value + " ms" : "-";
        Summary = item.Summary;
        Detail = item.Detail;
        OperatorMessage = string.IsNullOrWhiteSpace(item.OperatorMessage) ? item.Summary : item.OperatorMessage;
        ProtocolMeaning = string.IsNullOrWhiteSpace(item.ProtocolMeaning) ? item.Detail : item.ProtocolMeaning;
        OperatorAction = item.OperatorAction;
        RawHex = string.IsNullOrWhiteSpace(item.RawHex) ? "-" : item.RawHex;
        PollingReason = item.PollingReason;
        Category = item.Category;
        Acd = item.Acd.HasValue ? (item.Acd.Value ? "1" : "0") : "-";
        Dfc = item.Dfc.HasValue ? (item.Dfc.Value ? "1" : "0") : "-";
        AsduType = item.AsduType ?? "-";
        TypeId = item.TypeId.HasValue ? item.TypeId.Value.ToString(CultureInfo.InvariantCulture) : "-";
        TypeIdName = BuildTypeIdName(item);
        Vsq = item.VariableStructureQualifier.HasValue ? $"0x{item.VariableStructureQualifier.Value:X2}" : "-";
        CotCode = item.CauseOfTransmission.HasValue ? item.CauseOfTransmission.Value.ToString(CultureInfo.InvariantCulture) : "-";
        Cot = item.Cot ?? "-";
        CotDisplay = CotCode == "-" ? Cot : $"{CotCode} · {Cot}";
        LinkAddress = item.LinkAddress.HasValue ? item.LinkAddress.Value.ToString(CultureInfo.InvariantCulture) : "-";
        CommonAddress = item.CommonAddressNumber.HasValue ? item.CommonAddressNumber.Value.ToString(CultureInfo.InvariantCulture) : (item.Frame?.Asdu?.CommonAddress.ToString(CultureInfo.InvariantCulture) ?? "-");
        IoAddress = item.InformationObjectAddress.HasValue ? item.InformationObjectAddress.Value.ToString(CultureInfo.InvariantCulture) : "-";
        ApciFormat = string.IsNullOrWhiteSpace(item.ApciFormat) ? "-" : item.ApciFormat;
        SendSequence = item.SendSequence.HasValue ? item.SendSequence.Value.ToString(CultureInfo.InvariantCulture) : "-";
        ReceiveSequence = item.ReceiveSequence.HasValue ? item.ReceiveSequence.Value.ToString(CultureInfo.InvariantCulture) : "-";
        UFormatName = string.IsNullOrWhiteSpace(item.UFormatName) ? "-" : item.UFormatName;
        ObjectCount = item.ObjectCount.HasValue ? item.ObjectCount.Value.ToString(CultureInfo.InvariantCulture) : "-";
        SequenceMode = item.IsSequenceAsdu.HasValue ? (item.IsSequenceAsdu.Value ? "SQ=1" : "SQ=0") : "-";
        Quality = BuildQualityText(item);
        Fun = item.FunctionType.HasValue ? item.FunctionType.Value.ToString(CultureInfo.InvariantCulture) : "-";
        Inf = item.InformationNumber.HasValue ? item.InformationNumber.Value.ToString(CultureInfo.InvariantCulture) : "-";
        FunInf = Fun == "-" && Inf == "-" ? "-" : $"{Fun}/{Inf}";
        SemanticLabel = ioaPoint?.Name ?? item.SignalName;
        SemanticCategory = ioaPoint?.Group ?? item.SignalGroup;
        SemanticState = ioaPoint is not null && !string.IsNullOrWhiteSpace(item.SignalDisplayValue)
            ? ioaPoint.ResolveDisplayValue(ExtractSimpleStateToken(item.SignalDisplayValue))
            : item.SignalDisplayValue;
        ProfileName = ioaPoint is not null && string.IsNullOrWhiteSpace(item.MappingProfileName) ? "IOA profile" : item.MappingProfileName;
        RelayTime = string.IsNullOrWhiteSpace(item.RelayTimestampText) ? "-" : item.RelayTimestampText;
        Edge = item.IsRelayEdgeEvent ? item.EdgeReason : "-";
        Mapped = item.IsMappedSignal ? "Yes" : "No";
        SignalOrAddress = BuildSignalOrAddress();
        ProtocolAddress = BuildProtocolAddress();
        ProtocolService = BuildProtocolService();
        ReadableMeaning = BuildReadableMeaning(item);
        ProtocolTraceTitle = BuildProtocolTraceTitle();
        ProtocolTraceMeaning = BuildProtocolTraceMeaning();
        ProtocolTraceRaw = BuildProtocolTraceRaw();
        ProtocolTraceMeta = $"#{Sequence}  {Time}  {ProtocolName}";
        TrafficTone = ResolveTrafficTone();
    }

    public Iec103MasterEvidenceEvent Source { get; }
    public long Sequence { get; }
    public string Time { get; }
    public string Direction { get; }
    public string TrafficTone { get; }
    public string State { get; }
    public string ProtocolMode { get; }
    public string ProtocolName { get; }
    public string DataClass { get; }
    public string ResponseTime { get; }
    public string Summary { get; }
    public string Detail { get; }
    public string OperatorMessage { get; }
    public string ProtocolMeaning { get; }
    public string OperatorAction { get; }
    public string RawHex { get; }
    public string PollingReason { get; }
    public string Category { get; }
    public string Acd { get; }
    public string Dfc { get; }
    public string AsduType { get; }
    public string TypeId { get; }
    public string TypeIdName { get; }
    public string Vsq { get; }
    public string CotCode { get; }
    public string Cot { get; }
    public string CotDisplay { get; }
    public string LinkAddress { get; }
    public string CommonAddress { get; }
    public string IoAddress { get; }
    public string ApciFormat { get; }
    public string SendSequence { get; }
    public string ReceiveSequence { get; }
    public string UFormatName { get; }
    public string ObjectCount { get; }
    public string SequenceMode { get; }
    public string Quality { get; }
    public string Fun { get; }
    public string Inf { get; }
    public string FunInf { get; }
    public string SemanticLabel { get; }
    public string SemanticCategory { get; }
    public string SemanticState { get; }
    public string ProfileName { get; }
    public string RelayTime { get; }
    public string Edge { get; }
    public string Mapped { get; }
    public string SignalOrAddress { get; }
    public string ProtocolAddress { get; }
    public string ProtocolService { get; }
    public string ReadableMeaning { get; }
    public string ProtocolTraceTitle { get; }
    public string ProtocolTraceMeaning { get; }
    public string ProtocolTraceRaw { get; }
    public string ProtocolTraceMeta { get; }



    public EvidenceRow(CaptureFrameSnapshot capture)
    {
        Source = new Iec103MasterEvidenceEvent
        {
            SequenceNumber = capture.Sequence,
            TimestampUtc = DateTime.UtcNow,
            Category = string.IsNullOrWhiteSpace(capture.Category) ? "Capture" : capture.Category,
            DataClass = capture.DataClass,
            Summary = capture.ProtocolTraceTitle,
            Detail = capture.Detail,
            OperatorMessage = capture.ProtocolTraceMeaning,
            ProtocolMeaning = capture.Meaning,
            RawHex = capture.RawHex,
            ProtocolMode = capture.ProtocolMode switch
            {
                "101" or "IEC-101" => Iec60870ProtocolMode.Iec101,
                "104" or "IEC-104" => Iec60870ProtocolMode.Iec104,
                _ => Iec60870ProtocolMode.Iec103
            }
        };

        Sequence = capture.Sequence;
        Time = string.IsNullOrWhiteSpace(capture.Time) ? "-" : capture.Time;
        Direction = string.IsNullOrWhiteSpace(capture.Direction) ? "STATE" : capture.Direction;
        TrafficTone = string.Equals(Direction, "TX", StringComparison.OrdinalIgnoreCase) ? "Tx" : string.Equals(Direction, "RX", StringComparison.OrdinalIgnoreCase) ? "Rx" : "Status";
        State = string.IsNullOrWhiteSpace(capture.State) ? "OfflineCapture" : capture.State;
        ProtocolMode = string.IsNullOrWhiteSpace(capture.ProtocolMode) ? "-" : capture.ProtocolMode;
        ProtocolName = string.IsNullOrWhiteSpace(capture.ProtocolName) ? "ARIEC capture" : capture.ProtocolName;
        DataClass = string.IsNullOrWhiteSpace(capture.DataClass) ? "-" : capture.DataClass;
        ResponseTime = string.IsNullOrWhiteSpace(capture.ResponseTime) ? "-" : capture.ResponseTime;
        Summary = string.IsNullOrWhiteSpace(capture.ProtocolTraceTitle) ? "Offline capture frame" : capture.ProtocolTraceTitle;
        Detail = capture.Detail;
        OperatorMessage = string.IsNullOrWhiteSpace(capture.ProtocolTraceMeaning) ? capture.Meaning : capture.ProtocolTraceMeaning;
        ProtocolMeaning = string.IsNullOrWhiteSpace(capture.Meaning) ? capture.ProtocolTraceMeaning : capture.Meaning;
        OperatorAction = "Offline capture review";
        RawHex = string.IsNullOrWhiteSpace(capture.RawHex) ? "-" : capture.RawHex;
        PollingReason = "offline-capture";
        Category = string.IsNullOrWhiteSpace(capture.Category) ? "Capture" : capture.Category;
        Acd = string.IsNullOrWhiteSpace(capture.Acd) ? "-" : capture.Acd;
        Dfc = string.IsNullOrWhiteSpace(capture.Dfc) ? "-" : capture.Dfc;
        AsduType = string.IsNullOrWhiteSpace(capture.AsduType) ? "-" : capture.AsduType;
        TypeId = string.IsNullOrWhiteSpace(capture.TypeId) ? "-" : capture.TypeId;
        TypeIdName = TypeId == "-" ? AsduType : $"{TypeId} · {AsduType}";
        Vsq = "-";
        CotCode = string.IsNullOrWhiteSpace(capture.CotCode) ? "-" : capture.CotCode;
        Cot = string.IsNullOrWhiteSpace(capture.Cot) ? "-" : capture.Cot;
        CotDisplay = CotCode == "-" ? Cot : $"{CotCode} · {Cot}";
        LinkAddress = string.IsNullOrWhiteSpace(capture.LinkAddress) ? "-" : capture.LinkAddress;
        CommonAddress = string.IsNullOrWhiteSpace(capture.CommonAddress) ? "-" : capture.CommonAddress;
        IoAddress = string.IsNullOrWhiteSpace(capture.Ioa) ? "-" : capture.Ioa;
        ApciFormat = "-";
        SendSequence = "-";
        ReceiveSequence = "-";
        UFormatName = "-";
        ObjectCount = "-";
        SequenceMode = "-";
        Quality = string.IsNullOrWhiteSpace(capture.Quality) ? "-" : capture.Quality;
        Fun = "-";
        Inf = "-";
        FunInf = "-";
        SemanticLabel = string.IsNullOrWhiteSpace(capture.SignalOrAddress) ? "-" : capture.SignalOrAddress;
        SemanticCategory = "Offline Capture";
        SemanticState = string.IsNullOrWhiteSpace(capture.Value) ? "-" : capture.Value;
        ProfileName = "ARIEC capture";
        RelayTime = string.IsNullOrWhiteSpace(capture.RelayTime) ? "-" : capture.RelayTime;
        Edge = "-";
        Mapped = "-";
        SignalOrAddress = string.IsNullOrWhiteSpace(capture.SignalOrAddress) ? "-" : capture.SignalOrAddress;
        ProtocolAddress = string.IsNullOrWhiteSpace(capture.Address) ? "-" : capture.Address;
        ProtocolService = string.IsNullOrWhiteSpace(capture.Service) ? "-" : capture.Service;
        ReadableMeaning = string.IsNullOrWhiteSpace(capture.Meaning) ? capture.ProtocolTraceMeaning : capture.Meaning;
        ProtocolTraceTitle = string.IsNullOrWhiteSpace(capture.ProtocolTraceTitle) ? $"{Direction} {ProtocolService} | {ProtocolAddress}" : capture.ProtocolTraceTitle;
        ProtocolTraceMeaning = string.IsNullOrWhiteSpace(capture.ProtocolTraceMeaning) ? ReadableMeaning : capture.ProtocolTraceMeaning;
        ProtocolTraceRaw = string.IsNullOrWhiteSpace(capture.ProtocolTraceRaw) ? (RawHex == "-" ? "RAW -" : "RAW " + RawHex) : capture.ProtocolTraceRaw;
        ProtocolTraceMeta = string.IsNullOrWhiteSpace(capture.ProtocolTraceMeta) ? $"#{Sequence}  {Time}  {ProtocolName}" : capture.ProtocolTraceMeta;
    }

    private string BuildProtocolTraceTitle()
    {
        var direction = string.IsNullOrWhiteSpace(Direction) ? "STATE" : Direction;
        var service = CleanTracePart(ProtocolService);
        var address = CleanTracePart(ProtocolAddress);
        var signal = CleanTracePart(SignalOrAddress);
        var value = CleanTracePart(SemanticState);
        var quality = CleanTracePart(Quality);
        var time = CleanTracePart(RelayTime);

        var tail = signal == "-" ? string.Empty : $"  |  {signal}";
        if (value != "-")
        {
            tail += $" = {value}";
        }

        if (quality != "-" && !quality.Equals("Good", StringComparison.OrdinalIgnoreCase))
        {
            tail += $"  [{quality}]";
        }

        if (time != "-")
        {
            tail += $"  @{time}";
        }

        return $"{direction}  {service}  |  {address}{tail}";
    }

    private string BuildProtocolTraceMeaning()
    {
        var meaning = CleanTracePart(ReadableMeaning);
        if (meaning == "-")
        {
            meaning = CleanTracePart(ProtocolMeaning);
        }

        if (meaning == "-")
        {
            meaning = CleanTracePart(Summary);
        }

        return meaning;
    }

    private string BuildProtocolTraceRaw()
    {
        var raw = CleanTracePart(RawHex);
        return raw == "-" ? "RAW -" : "RAW " + raw;
    }

    private static string CleanTracePart(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.ReplaceLineEndings(" ").Trim();
    }


    private string ResolveTrafficTone()
    {
        var combined = string.Join(" ", State, Category, Summary, Detail, OperatorMessage, ProtocolMeaning, ReadableMeaning);
        if (combined.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("no outstation response", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("error", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("nack", StringComparison.OrdinalIgnoreCase))
        {
            return "Error";
        }

        if (string.Equals(Direction, "TX", StringComparison.OrdinalIgnoreCase))
        {
            return "Tx";
        }

        if (string.Equals(Direction, "RX", StringComparison.OrdinalIgnoreCase))
        {
            return "Rx";
        }

        return "Status";
    }

    private static string BuildTypeIdName(Iec103MasterEvidenceEvent item)
    {
        if (!item.TypeId.HasValue)
        {
            return item.AsduType ?? "-";
        }

        var name = string.IsNullOrWhiteSpace(item.TypeName) ? item.AsduType : item.TypeName;
        return string.IsNullOrWhiteSpace(name)
            ? item.TypeId.Value.ToString(CultureInfo.InvariantCulture)
            : $"{item.TypeId.Value} · {name}";
    }

    private string BuildSignalOrAddress()
    {
        if (!string.IsNullOrWhiteSpace(SemanticLabel))
        {
            return SemanticLabel;
        }

        return ProtocolMode switch
        {
            "101" or "104" => IoAddress == "-" ? "-" : "IOA " + IoAddress,
            _ => FunInf == "-" ? "-" : "FUN/INF " + FunInf
        };
    }

    private string BuildProtocolAddress()
    {
        return ProtocolMode switch
        {
            "101" => $"L={LinkAddress}, CA={CommonAddress}, IOA={IoAddress}",
            "104" => $"CA={CommonAddress}, IOA={IoAddress}",
            _ => FunInf == "-" ? $"L={LinkAddress}" : $"L={LinkAddress}, FUN/INF={FunInf}"
        };
    }

    private string BuildProtocolService()
    {
        return ProtocolMode switch
        {
            "104" => ApciFormat == "U" ? UFormatName : $"{ApciFormat} NS={SendSequence} NR={ReceiveSequence}",
            "101" => DataClass == "-" ? TypeIdName : $"{DataClass} · {TypeIdName}",
            _ => DataClass == "-" ? AsduType : $"{DataClass} · {AsduType}"
        };
    }

    private static string BuildQualityText(Iec103MasterEvidenceEvent item)
    {
        if (!string.IsNullOrWhiteSpace(item.QualityText))
        {
            return item.QualityText;
        }

        var source = string.Join(" ", item.SignalDisplayValue, item.SignalRawValue, item.ObjectSummary);
        var match = QdsRegex().Match(source);
        return match.Success ? match.Value : "-";
    }

    private static string ExtractSimpleStateToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var equals = trimmed.IndexOf('=');
        if (equals >= 0 && equals < trimmed.Length - 1)
        {
            var right = trimmed[(equals + 1)..].Trim();
            var comma = right.IndexOf(',');
            return comma > 0 ? right[..comma].Trim() : right;
        }

        var comma2 = trimmed.IndexOf(',');
        return comma2 > 0 ? trimmed[..comma2].Trim() : trimmed;
    }

    private string BuildReadableMeaning(Iec103MasterEvidenceEvent item)
    {
        if (item.IsRelayValue)
        {
            var signal = string.IsNullOrWhiteSpace(SemanticLabel) ? BuildSignalOrAddress() : SemanticLabel;
            var value = string.IsNullOrWhiteSpace(SemanticState) ? item.SignalRawValue : SemanticState;
            var source = string.IsNullOrWhiteSpace(Cot) || Cot == "-" ? DataClass : Cot;
            return string.IsNullOrWhiteSpace(value)
                ? $"{ProtocolName} value received: {signal} ({source})."
                : $"{ProtocolName} value received: {signal} = {value} ({source}).";
        }

        if (!string.IsNullOrWhiteSpace(OperatorMessage))
        {
            return OperatorMessage;
        }

        if (!string.IsNullOrWhiteSpace(ProtocolMeaning))
        {
            return ProtocolMeaning;
        }

        return string.IsNullOrWhiteSpace(Summary) ? Detail : Summary;
    }

    [GeneratedRegex("QDS=0x[0-9A-Fa-f]{2}")]
    private static partial Regex QdsRegex();
}
