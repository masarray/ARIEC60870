// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;
using ARIEC60870.Core.Mapping;
using ARIEC60870.Core.Model;
using ARIEC60870.Master.Protocol.Iec10x;
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
        Acd = ResolveLinkFlagText(item.Acd, item, "ACD");
        Dfc = ResolveLinkFlagText(item.Dfc, item, "DFC");
        LinkFlags = BuildLinkFlags(item.Frame?.LinkControl?.Prm, Acd, Dfc);
        AsduType = item.AsduType ?? "-";
        TypeId = item.TypeId.HasValue ? item.TypeId.Value.ToString(CultureInfo.InvariantCulture) : "-";
        TypeIdName = BuildTypeIdName(item);
        TypeBadge = item.AsduDetails is { Issues.Count: 0 } validAsdu
            ? IecSignalTypeBadge.FromDecoded(ProtocolMode, validAsdu.TypeId) : null;
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
        // Render the already-decoded IEC-101 frame once, reusing its compact
        // narration for both line title and explanation (no duplicate formatting).
        (string Title, string Meaning)? narration =
            item.ProtocolMode == Iec60870ProtocolMode.Iec101 && item.Frame is not null
                ? Iec101TraceNarrator.Describe(item)
                : null;
        ProtocolTraceTitle = narration?.Title ?? BuildProtocolTraceTitle();
        ProtocolTraceMeaning = narration?.Meaning ?? BuildProtocolTraceMeaning();
        ProtocolTraceRaw = BuildProtocolTraceRaw();
        ProtocolTraceMeta = narration is null ? $"#{Sequence}  {Time}  {ProtocolName}" : string.Empty;
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
    public string LinkFlags { get; }
    public string AsduType { get; }
    public string TypeId { get; }
    public string TypeIdName { get; }
    public IecSignalTypeBadge? TypeBadge { get; }
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
    // Trace has a dedicated millisecond timestamp and direction icon; retain the
    // original verbose title for exports and evidence ledgers.
    public string ProtocolTraceHeader =>
        ProtocolMode == "101" && ProtocolTraceTitle.Length > 2 &&
        (ProtocolTraceTitle[0] is '⇧' or '⇩' or '•') &&
        ProtocolTraceTitle.IndexOf("] ", StringComparison.Ordinal) is var end && end > 0
            ? ProtocolTraceTitle[(end + 2)..] : ProtocolTraceTitle;
    public string ProtocolTraceMeaning { get; }
    // Compact high-volume list projection only. Full typed explanation is
    // retained in ProtocolTraceMeaning, Inspector, copy and report exports.
    private string? _compactTraceMeaning;
    public string ProtocolTraceCompactMeaning => _compactTraceMeaning ??= CompactTraceMeaning();
    private string? _operatorLine;
    /// <summary>One fast meaning-first ASDU line. Exact raw/offset details remain in inspector.</summary>
    public string ProtocolTraceOperatorLine => _operatorLine ??= BuildProtocolTraceOperatorLine();

    private string BuildProtocolTraceOperatorLine()
    {
        var asdu = Source.AsduDetails;
        if (asdu is null) return ProtocolTraceCompactMeaning;
        var status = (asdu.IsNegativeConfirm ? "NEG · " : "") +
            (asdu.IsTest ? "TEST · " : "");
        var label = $"TI {asdu.TypeId} · {status}COT {asdu.CauseOfTransmission} ({asdu.CauseName}) · CA {asdu.CommonAddress}";
        if (asdu.Objects.Count == 0) return label + " · no decoded points";
        var examples = string.Join(", ", asdu.Objects.Take(2).Select(o =>
            $"IOA {o.InformationObjectAddress} {o.ShortValue}" +
            (o.QualityDetails is { DisplayText: not "Good" } q ? " [" + q.DisplayText + "]" : "")));
        return label + $" · {asdu.Objects.Count}/{asdu.ObjectCount} points · " +
            examples + (asdu.Objects.Count > 2 ? " …" : string.Empty);
    }
    private string CompactTraceMeaning()
    {
        if (ProtocolMode != "101" || Source.CommandDetails is not { } cmd ||
            cmd.TypeId is not (>= 45 and <= 51 or >= 58 and <= 64))
            return ProtocolTraceMeaning;

        var cause = Source.CauseDetails ?? cmd.Cause;
        var stage = cause.Test ? "TEST" :
            cause.Negative ? "NEG" :
            cause.Code == 7 ? "ACTCON +" :
            cause.Code == 10 ? "ACTTERM" :
            cause.Code is >= 44 and <= 47 ? "COT ERROR" :
            cause.Code == 9 ? "DEACTCON" : "COMMAND";
        return stage + "  ·  " + cmd.Operation + "  ·  COT " +
            cause.Code.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "  ·  CA " + CommonAddress + " / IOA " + IoAddress;
    }
    public string ProtocolTraceRaw { get; }
    public string ProtocolTraceMeta { get; }



    public EvidenceRow(CaptureFrameSnapshot capture)
    {
        Source = RestoreCaptureSource(capture);

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
        LinkFlags = BuildLinkFlags(null, Acd, Dfc);
        AsduType = string.IsNullOrWhiteSpace(capture.AsduType) ? "-" : capture.AsduType;
        TypeId = string.IsNullOrWhiteSpace(capture.TypeId) ? "-" : capture.TypeId;
        TypeIdName = TypeId == "-" ? AsduType : $"{TypeId} · {AsduType}";
        // Capture display metadata cannot forge a badge: use restored typed
        // original ASDU, never saved TypeId text from the JSONL row.
        TypeBadge = Source.AsduDetails is { Issues.Count: 0 } validAsdu
            ? IecSignalTypeBadge.FromDecoded(ProtocolMode, validAsdu.TypeId) : null;
        Vsq = Source.AsduDetails is { } asdu ? $"0x{asdu.VariableStructureQualifier:X2}" : "-";
        CotCode = Source.CauseDetails is { } cause ? cause.Code.ToString(CultureInfo.InvariantCulture) :
            string.IsNullOrWhiteSpace(capture.CotCode) ? "-" : capture.CotCode;
        Cot = string.IsNullOrWhiteSpace(capture.Cot) ? "-" : capture.Cot;
        CotDisplay = CotCode == "-" ? Cot : $"{CotCode} · {Cot}";
        LinkAddress = string.IsNullOrWhiteSpace(capture.LinkAddress) ? "-" : capture.LinkAddress;
        CommonAddress = string.IsNullOrWhiteSpace(capture.CommonAddress) ? "-" : capture.CommonAddress;
        IoAddress = string.IsNullOrWhiteSpace(capture.Ioa) ? "-" : capture.Ioa;
        ApciFormat = string.IsNullOrWhiteSpace(Source.ApciFormat) ? "-" : Source.ApciFormat;
        SendSequence = "-";
        ReceiveSequence = "-";
        UFormatName = "-";
        ObjectCount = Source.AsduDetails?.ObjectCount.ToString(CultureInfo.InvariantCulture) ?? "-";
        SequenceMode = Source.AsduDetails is { } decoded ? (decoded.IsSequence ? "SQ=1" : "SQ=0") : "-";
        Quality = Source.QualityDetails?.DisplayText ?? (Source.Frame is { IsChecksumValid: false } ? "Invalid frame" :
            string.IsNullOrWhiteSpace(capture.Quality) ? "-" : capture.Quality);
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

    private static Iec103MasterEvidenceEvent RestoreCaptureSource(CaptureFrameSnapshot capture)
    {
        var mode = capture.ProtocolMode switch
        {
            "101" or "IEC-101" => Iec60870ProtocolMode.Iec101,
            "104" or "IEC-104" => Iec60870ProtocolMode.Iec104,
            _ => Iec60870ProtocolMode.Iec103
        };

        // Older captures have raw bytes but no authoritative decoder profile.
        // Never guess COT/CA/IOA field widths to make an inspector look populated.
        Iec10xArchivedDecode? decoded = null;
        if (capture.DecoderSchema == "IEC10X-OBJECT-V1" &&
            capture.CotWidth is >= 1 and <= 2 &&
            capture.CaWidth is >= 1 and <= 2 &&
            capture.IoaWidth is >= 1 and <= 3 &&
            (mode == Iec60870ProtocolMode.Iec104 || capture.LinkWidth is >= 0 and <= 2) &&
            !string.IsNullOrWhiteSpace(capture.RawHex))
        {
            try
            {
                var hex = string.Concat(capture.RawHex.Where(Uri.IsHexDigit));
                var raw = Convert.FromHexString(hex);
                decoded = Iec10xArchivedFrameDecoder.Decode(mode, raw, capture.LinkWidth,
                    capture.CotWidth, capture.CaWidth, capture.IoaWidth);
            }
            catch (FormatException)
            {
                // Malformed capture remains viewable as raw historical evidence.
            }
        }

        var asdu = decoded?.Asdu;
        var first = capture.SelectedObjectIndex.HasValue
            ? asdu?.Objects.FirstOrDefault(o => o.Index == capture.SelectedObjectIndex.Value)
            : asdu?.FirstObject;
        return new Iec103MasterEvidenceEvent
        {
            SequenceNumber = capture.Sequence,
            TimestampUtc = capture.CaptureArrivalUtc ?? DateTime.UnixEpoch, // Historical V1: unknown UTC; never invent reload time.
            Category = string.IsNullOrWhiteSpace(capture.Category) ? "Capture" : capture.Category,
            DataClass = capture.DataClass,
            Summary = capture.ProtocolTraceTitle,
            Detail = capture.Detail,
            OperatorMessage = capture.ProtocolTraceMeaning,
            ProtocolMeaning = capture.Meaning,
            RawHex = capture.RawHex,
            PollingReason = "offline-capture",
            PhysicalSource = capture.PhysicalSource,
            PhysicalStreamId = capture.PhysicalStreamId,
            RawStreamOffset = capture.RawStreamOffset,
            ProtocolMode = mode,
            Direction = capture.Direction == "RX" ? FrameDirection.SlaveToMaster :
                capture.Direction == "TX" ? FrameDirection.MasterToSlave : FrameDirection.Unknown,
            Frame = decoded?.SerialFrame,
            // Never substitute Common Address or a displayed text field for the
            // real FT1.2 link address. Only trusted byte-decoded provenance qualifies.
            LinkAddress = decoded?.SerialFrame?.LinkAddress,
            AsduDetails = asdu,
            InformationObjectDetails = first,
            ApciFormat = decoded?.TcpFrame?.Format ?? string.Empty,
            SendSequence = decoded?.TcpFrame?.SendSequence,
            ReceiveSequence = decoded?.TcpFrame?.ReceiveSequence,
            UFormatName = decoded?.TcpFrame?.UFormatName ?? string.Empty,
            TypeId = asdu?.TypeId,
            TypeName = asdu?.TypeName ?? capture.AsduType,
            VariableStructureQualifier = asdu?.VariableStructureQualifier,
            IsSequenceAsdu = asdu?.IsSequence,
            ObjectCount = asdu?.ObjectCount,
            CauseOfTransmission = asdu?.CauseOfTransmission,
            CauseName = asdu?.CotNameWithFlags ?? capture.Cot,
            CauseDetails = asdu?.CauseDetails,
            CommandDetails = asdu?.CommandDetails,
            CommonAddressNumber = asdu?.CommonAddress,
            OriginatorAddress = asdu?.OriginatorAddress,
            InformationObjectAddress = first?.InformationObjectAddress,
            QualityDetails = first?.QualityDetails,
            QualityText = first?.QualityText ?? capture.Quality,
            RelayTimestampText = first?.TimestampText ?? capture.RelayTime,
            IsRelayValue = asdu?.TypeId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37,
            SignalDisplayValue = first?.ShortValue ?? capture.Value
        };
    }

    private string BuildProtocolTraceTitle()
    {
        var direction = Direction == "TX" ? "➤ TX" : Direction == "RX" ? "◀ RX" :
            string.IsNullOrWhiteSpace(Direction) ? "STATE" : Direction;
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

        var linkFlags = CleanTracePart(LinkFlags);
        if (linkFlags != "-")
        {
            tail += $"  |  {linkFlags}";
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
        if (ProtocolMode == "101")
        {
            // Raw bytes always come from captured frames. Passive replay keeps a
            // compact hexadecimal string; normalize it once for readable byte groups.
            if (raw != "-" && !raw.Contains(' ') && Source.Frame?.RawBytes is { Count: > 0 } bytes)
            {
                raw = string.Create(bytes.Count * 3 - 1, bytes, static (chars, source) =>
                {
                    const string digits = "0123456789ABCDEF";
                    for (var i = 0; i < source.Count; i++)
                    {
                        var value = source[i];
                        chars[i * 3] = digits[value >> 4];
                        chars[i * 3 + 1] = digits[value & 15];
                        if (i + 1 < source.Count) chars[i * 3 + 2] = ' ';
                    }
                });
            }
            return "Hex Frame: " + raw;
        }

        var prefix = CleanTracePart(LinkFlags);
        var rawLine = raw == "-" ? "RAW -" : "RAW " + raw;
        return prefix == "-" ? rawLine : $"LINK {prefix}  ·  {rawLine}";
    }

    private static string ResolveLinkFlagText(bool? decoded, Iec103MasterEvidenceEvent item, string name)
    {
        if (decoded.HasValue)
        {
            return decoded.Value ? "1" : "0";
        }

        var source = string.Join(" ", item.Detail, item.OperatorMessage, item.ProtocolMeaning, item.OperatorAction, item.Summary);
        var match = Regex.Match(source, name + @"\s*=\s*([01])", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "-";
    }

    private static string BuildLinkFlags(bool? prm, string acd, string dfc)
    {
        if (acd == "-" && dfc == "-")
        {
            return "-";
        }

        // ACD/DFC are secondary/outstation response bits. For primary master requests, the same bit positions are FCB/FCV.
        // Keep the label explicit so a beginner does not mistake master FCB/FCV for outstation ACD/DFC.
        if (prm == true)
        {
            return "PRM=1, FCB/FCV only";
        }

        return $"ACD={acd}, DFC={dfc}";
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
