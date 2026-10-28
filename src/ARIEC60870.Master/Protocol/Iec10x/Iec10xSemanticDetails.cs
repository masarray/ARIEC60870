// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>On-wire IEC-101/104 cause flags, never inferred from the English COT label.</summary>
public sealed record Iec10xCauseDetails(byte Raw, int Code, bool Negative, bool Test, int? OriginatorAddress)
{
    public string Phase => Code switch
    {
        5 => "Requested",
        6 => "Activation request",
        7 => "Activation confirmation",
        8 => "Deactivation request",
        9 => "Deactivation confirmation",
        10 => "Activation termination",
        11 => "Return information: remote command",
        12 => "Return information: local command",
        44 => "Unknown Type ID",
        45 => "Unknown COT",
        46 => "Unknown common address",
        47 => "Unknown information object address",
        _ => Iec10xAsduDecoder.CauseName(Code)
    };

    public string Outcome => Negative
        ? "NEGATIVE (P/N=1); request rejected/negatively confirmed"
        : Code is 44 or 45 or 46 or 47
            ? "Protocol error reported; inspect Type/COT/CA/IOA"
            : Code == 7 ? "Activation confirmed at application level; physical operation NOT proven"
            : Code == 10 ? "Activation terminated; verify returned process state separately"
            : "No application success/failure conclusion from this COT alone";

    public string Detail =>
        $"COT={Code} ({Phase}); raw=0x{Raw:X2}; P/N={(Negative ? 1 : 0)}; TEST={(Test ? 1 : 0)}; " +
        $"OA={(OriginatorAddress.HasValue ? OriginatorAddress.Value.ToString(CultureInfo.InvariantCulture) : "N/A (1-octet COT)")}. {Outcome}.";
}

public enum Iec10xQualityKind { Siq, Diq, Qds, Bcr }

/// <summary>Typed information-object quality; an inapplicable bit is null, not false.</summary>
public sealed record Iec10xQualityDetails(Iec10xQualityKind Kind, byte Raw)
{
    public bool Invalid => (Raw & 0x80) != 0;
    public bool? Blocked => Kind == Iec10xQualityKind.Bcr ? null : (Raw & 0x10) != 0;
    public bool? Substituted => Kind == Iec10xQualityKind.Bcr ? null : (Raw & 0x20) != 0;
    public bool? NotTopical => Kind == Iec10xQualityKind.Bcr ? null : (Raw & 0x40) != 0;
    public bool? Overflow => Kind == Iec10xQualityKind.Qds ? (Raw & 0x01) != 0 : null;
    public bool? Carry => Kind == Iec10xQualityKind.Bcr ? (Raw & 0x20) != 0 : null;
    public bool? Adjusted => Kind == Iec10xQualityKind.Bcr ? (Raw & 0x40) != 0 : null;
    public int? CounterSequence => Kind == Iec10xQualityKind.Bcr ? Raw & 0x1F : null;

    public IReadOnlyList<string> ActiveBadges
    {
        get
        {
            var badges = new List<string>(5);
            if (Invalid) badges.Add("INVALID");
            if (Overflow == true) badges.Add("OVF");
            if (Carry == true) badges.Add("CARRY");
            if (NotTopical == true) badges.Add("NT");
            if (Blocked == true) badges.Add("BLK");
            if (Substituted == true) badges.Add("SUB");
            if (Adjusted == true) badges.Add("ADJ");
            return badges.Count == 0 ? new[] { "Good" } : badges;
        }
    }

    public string DisplayText => string.Join(", ", ActiveBadges.Select(b => b switch
    {
        "OVF" => "Overflow", "CARRY" => "Carry", "BLK" => "Blocked",
        "SUB" => "Substituted", "NT" => "Not topical",
        "ADJ" => "Adjusted", "INVALID" => "Invalid", _ => b
    }));

    public string BitDetails =>
        $"{Kind} raw=0x{Raw:X2}; IV={(Invalid ? 1 : 0)}; " +
        $"BL={Bit(Blocked)}; SB={Bit(Substituted)}; NT={Bit(NotTopical)}; " +
        $"OV={Bit(Overflow)}; CY={Bit(Carry)}; CA={Bit(Adjusted)}" +
        (CounterSequence.HasValue ? $"; SQ={CounterSequence.Value}" : string.Empty);

    private static string Bit(bool? value) => value.HasValue ? (value.Value ? "1" : "0") : "N/A";

    public static Iec10xQualityDetails? Decode(byte type, IReadOnlyList<byte> data)
    {
        var (kind, index) = type switch
        {
            1 or 2 or 30 => (Iec10xQualityKind.Siq, 0),
            3 or 4 or 31 => (Iec10xQualityKind.Diq, 0),
            5 or 6 or 32 => (Iec10xQualityKind.Qds, 1),
            7 or 8 or 33 => (Iec10xQualityKind.Qds, 4),
            9 or 10 or 11 or 12 or 34 or 35 => (Iec10xQualityKind.Qds, 2),
            13 or 14 or 36 => (Iec10xQualityKind.Qds, 4),
            15 or 16 or 37 => (Iec10xQualityKind.Bcr, 4),
            _ => (Iec10xQualityKind.Qds, -1)
        };
        return index >= 0 && data.Count > index ? new Iec10xQualityDetails(kind, data[index]) : null;
    }
}

/// <summary>
/// Exact command qualifier and application response semantics from the ASDU.
/// The value indicates a PROTOCOL confirmation only, never verified switchgear motion.
/// Byte offsets are relative to the ASDU, not FT1.2/APDU framing.
/// </summary>
public sealed record Iec10xCommandDetails(
    byte TypeId, int InformationObjectAddress, string CommandKind,
    string ValueMeaning, string QualifierName, byte? QualifierRaw,
    int? QualifierAsduOffset, bool? Select, int? QualifierValue,
    Iec10xCauseDetails Cause)
{
    public string Operation => Select is null ? "N/A" : Select.Value ? "SELECT" : "EXECUTE";

    public string Detail =>
        $"{CommandKind}; IOA={InformationObjectAddress}; {ValueMeaning}; " +
        $"operation={Operation}; {QualifierName}=" +
        (QualifierRaw.HasValue ? $"0x{QualifierRaw.Value:X2}" : "N/A") +
        (QualifierValue.HasValue ? $" (qualifier={QualifierValue.Value})" : "") +
        (QualifierAsduOffset.HasValue ? $"; ASDU byte offset={QualifierAsduOffset.Value}" : "") +
        $". {Cause.Detail}";

    public static Iec10xCommandDetails? Decode(byte type, Iec10xInformationObject? obj, Iec10xCauseDetails cause)
    {
        if (obj is null) return null;
        var data = obj.RawBytes;
        var kind = type switch
        {
            45 or 58 => "C_SC single command (SCO)",
            46 or 59 => "C_DC double command (DCO)",
            47 or 60 => "C_RC regulating step (RCO)",
            48 or 61 => "C_SE normalized setpoint (QOS)",
            49 or 62 => "C_SE scaled setpoint (QOS)",
            50 or 63 => "C_SE float setpoint (QOS)",
            51 or 64 => "C_BO 32-bit bitstring command"
            100 => "C_IC general interrogation (QOI)",
            101 => "C_CI counter interrogation (QCC)",
            102 => "C_RD read command",
            103 => "C_CS clock synchronization",
            104 or 107 => "C_TS test command",
            105 => "C_RP reset process",
            _ => null
        };
        if (kind is null) return null;

        var index = type switch
        {
            45 or 46 or 47 or 58 or 59 or 60 or 100 or 101 or 105 => 0,
            48 or 49 or 61 or 62 => 2,
            50 or 63 => 4,
            _ => -1
        };
        byte? q = index >= 0 && data.Count > index ? data[index] : null;
        var isOperation = type is 45 or 46 or 47 or 48 or 49 or 50 or 58 or 59 or 60 or 61 or 62 or 63;
        bool? select = isOperation && q.HasValue ? (q.Value & 0x80) != 0 : null;
        int? qualifier = q.HasValue ? type switch
        {
            45 or 46 or 47 or 58 or 59 or 60 => (q.Value >> 2) & 0x1F,
            48 or 49 or 50 or 61 or 62 or 63 => q.Value & 0x7F,
            101 => q.Value & 0x3F,
            100 or 105 => q.Value,
            _ => null
        } : null;
        var qualifierName = type switch
        {
            45 or 46 or 47 or 58 or 59 or 60 => "QU (bits 2..6), S/E (bit 7)",
            48 or 49 or 50 or 61 or 62 or 63 => "QOS (QL bits 0..6, S/E bit 7)",
            100 => "QOI (interrogation group)",
            101 => "QCC (RQT bits 0..5, FRZ bits 6..7)",
            105 => "QRP (reset qualifier)",
            _ => "no qualifier byte"
        };
        var value = type switch
        {
            45 or 58 when q.HasValue => $"SCS={(q.Value & 1)} ({((q.Value & 1) == 1 ? "ON" : "OFF")})",
            46 or 59 when q.HasValue => $"DCS={(q.Value & 3)} ({DoubleState(q.Value & 3)})",
            47 or 60 when q.HasValue => $"RCS={(q.Value & 3)} ({StepState(q.Value & 3)})",
            100 when q.HasValue => q.Value == 20 ? "Station GI" : q.Value is >= 21 and <= 36 ? $"Group GI #{q.Value - 20}" : $"QOI={q.Value}",
            101 when q.HasValue => $"RQT={q.Value & 0x3F}, FRZ={(q.Value >> 6) & 3}",
            102 => "Read addressed IOA",
            103 => "Clock synchronization; inspect CP56 timestamp and validity",
            104 or 107 => "Test command; inspect test sequence counter and timestamp where present",
            105 when q.HasValue => $"Reset process qualifier={q.Value}",
            _ => obj.ValueText
        };
        return new Iec10xCommandDetails(
            type, obj.InformationObjectAddress, kind, value, qualifierName, q,
            q.HasValue ? obj.InformationElementOffset + index : null,
            select, qualifier, cause);
    }

    private static string DoubleState(int code) => code switch
    {
        1 => "OFF", 2 => "ON", 0 => "Not permitted / intermediate", _ => "Not permitted / indeterminate"
    };
    private static string StepState(int code) => code switch
    {
        1 => "LOWER", 2 => "HIGHER", 0 => "Not permitted", _ => "Not permitted"
    };
}
