// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Globalization;

namespace ARIEC60870.Master.Protocol.Iec10x;

public sealed class Iec10xAsduDecoder
{
    private readonly int _cotSize;
    private readonly int _caSize;
    private readonly int _ioaSize;

    public Iec10xAsduDecoder(int cotSize = 2, int caSize = 2, int ioaSize = 3)
    {
        _cotSize = Math.Clamp(cotSize, 1, 2);
        _caSize = Math.Clamp(caSize, 1, 2);
        _ioaSize = Math.Clamp(ioaSize, 1, 3);
    }

    public Iec10xAsduDecode Decode(IReadOnlyList<byte> bytes)
    {
        var raw = bytes.ToArray();
        var issues = new List<string>();
        if (raw.Length < 2 + _cotSize + _caSize)
        {
            return new Iec10xAsduDecode
            {
                RawBytes = raw,
                TypeName = "Malformed ASDU",
                CauseName = "Unknown",
                Issues = new[] { $"ASDU too short for configured header. Length={raw.Length}, COT={_cotSize}, CA={_caSize}." }
            };
        }

        var offset = 0;
        var type = raw[offset++];
        var vsq = raw[offset++];
        var cotRaw = raw[offset++];
        var cot = cotRaw & 0x3F;
        var negative = (cotRaw & 0x40) != 0;
        var test = (cotRaw & 0x80) != 0;
        var oa = 0;
        if (_cotSize == 2)
        {
            oa = raw[offset++];
        }

        var ca = ReadLe(raw, ref offset, _caSize);
        var objectCount = vsq & 0x7F;
        var sq = (vsq & 0x80) != 0;
        if (objectCount == 0)
        {
            issues.Add("VSQ object count is zero; ASDU contains no information object.");
        }

        var objects = DecodeObjects(type, raw, offset, objectCount, sq, issues);
        var first = objects.FirstOrDefault();

        return new Iec10xAsduDecode
        {
            RawBytes = raw,
            TypeId = type,
            TypeName = TypeName(type),
            VariableStructureQualifier = vsq,
            IsSequence = sq,
            ObjectCount = objectCount,
            CauseOfTransmission = cot,
            CauseName = CauseName(cot),
            OriginatorAddress = oa,
            IsTest = test,
            IsNegativeConfirm = negative,
            CommonAddress = ca,
            InformationObjectAddress = first?.InformationObjectAddress,
            ObjectSummary = objects.Count == 0 ? "No information objects decoded" : string.Join(" | ", objects.Select(x => x.ReadableSummary)),
            ValueText = first?.ShortValue ?? string.Empty,
            QualityText = first?.QualityText ?? string.Empty,
            TimestampText = first?.TimestampText ?? string.Empty,
            Objects = objects,
            IsControlCommand = type is >= 45 and <= 51 or 58 or 59 or 60 or 61 or 62 or 63 or 64 or 100 or 101 or 102 or 103 or 104 or 105 or 107,
            Issues = issues.Concat(objects.SelectMany(x => x.Issues.Select(issue => $"Object {x.Index}: {issue}"))).ToArray()
        };
    }

    private IReadOnlyList<Iec10xInformationObject> DecodeObjects(byte type, byte[] raw, int offset, int objectCount, bool sq, List<string> issues)
    {
        var objects = new List<Iec10xInformationObject>();
        if (objectCount <= 0)
        {
            return objects;
        }

        var dataLength = InformationElementLength(type);
        if (dataLength < 0)
        {
            issues.Add($"Type ID {type} does not have a built-in information-element decoder yet; raw object bytes will be shown.");
        }

        int? sequenceBaseIoa = null;
        for (var i = 0; i < objectCount; i++)
        {
            if (sq && i > 0)
            {
                if (!sequenceBaseIoa.HasValue)
                {
                    issues.Add("SQ=1 object stream has no base IOA.");
                    break;
                }
            }
            else
            {
                if (raw.Length < offset + _ioaSize)
                {
                    issues.Add($"Payload ended before IOA #{i + 1} could be decoded. Offset={offset}, IOA size={_ioaSize}.");
                    break;
                }

                sequenceBaseIoa = ReadLe(raw, ref offset, _ioaSize);
            }

            var ioa = sq ? sequenceBaseIoa!.Value + i : sequenceBaseIoa!.Value;
            var remaining = raw.Length - offset;
            var take = dataLength >= 0 ? Math.Min(dataLength, Math.Max(0, remaining)) : Math.Max(0, remaining);
            var elementRaw = raw.Skip(offset).Take(take).ToArray();
            if (dataLength >= 0)
            {
                if (remaining < dataLength)
                {
                    var objectIssues = new[] { $"Information element too short. Expected={dataLength}, actual={remaining}." };
                    objects.Add(BuildObject(type, i + 1, ioa, elementRaw, objectIssues));
                    break;
                }

                offset += dataLength;
            }
            else
            {
                offset += take;
            }

            objects.Add(BuildObject(type, i + 1, ioa, elementRaw, Array.Empty<string>()));
        }

        if (offset < raw.Length && dataLength >= 0)
        {
            issues.Add($"Trailing ASDU bytes after decoded objects: {ToHex(raw.Skip(offset))}.");
        }

        return objects;
    }

    private static Iec10xInformationObject BuildObject(byte type, int index, int ioa, byte[] data, IReadOnlyList<string> existingIssues)
    {
        var issues = existingIssues.ToList();
        var value = DecodeValueText(type, data, issues);
        var quality = DecodeQualityText(type, data);
        var time = DecodeTimestampText(type, data);

        return new Iec10xInformationObject
        {
            Index = index,
            InformationObjectAddress = ioa,
            ValueText = value,
            EngineeringValue = value,
            QualityText = quality,
            TimestampText = time,
            ElementSummary = BuildElementSummary(value, quality, time, data),
            RawBytes = data,
            Issues = issues
        };
    }

    private static string BuildElementSummary(string value, string quality, string time, IReadOnlyList<byte> raw)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(value)) parts.Add(value);
        if (!string.IsNullOrWhiteSpace(quality)) parts.Add("Quality=" + quality);
        if (!string.IsNullOrWhiteSpace(time)) parts.Add("Time=" + time);
        if (parts.Count == 0 && raw.Count > 0) parts.Add("Payload=" + ToHex(raw));
        return string.Join(", ", parts);
    }

    private static int InformationElementLength(byte type) => type switch
    {
        1 or 3 => 1,
        2 or 4 => 4,
        5 => 2,
        6 => 5,
        7 => 5,
        8 => 8,
        9 or 11 => 3,
        10 or 12 => 6,
        13 or 15 => 5,
        14 or 16 => 8,
        30 or 31 => 8,
        32 => 9,
        33 => 12,
        34 or 35 => 10,
        36 or 37 => 12,
        45 or 46 or 47 => 1,
        48 or 49 => 3,
        50 => 5,
        58 or 59 or 60 => 8,
        61 or 62 => 10,
        63 => 12,
        100 or 101 or 105 => 1,
        102 => 0,
        103 => 7,
        104 => 2,
        107 => 9,
        _ => -1
    };

    private static int ReadLe(byte[] raw, ref int offset, int count)
    {
        var value = 0;
        for (var i = 0; i < count && offset < raw.Length; i++)
        {
            value |= raw[offset++] << (8 * i);
        }

        return value;
    }

    private static string DecodeValueText(byte type, byte[] data, List<string> issues)
    {
        try
        {
            return type switch
            {
                1 or 2 or 30 => data.Length >= 1 ? $"SP={DecodeSinglePoint(data[0])}" : string.Empty,
                3 or 4 or 31 => data.Length >= 1 ? $"DP={DecodeDoublePoint(data[0])}" : string.Empty,
                5 or 6 or 32 => data.Length >= 1 ? $"ST={unchecked((sbyte)data[0])}" : string.Empty,
                7 or 8 or 33 => data.Length >= 4 ? $"Bitstring=0x{BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4)):X8}" : string.Empty,
                9 or 10 or 34 => data.Length >= 2 ? $"Normalized={ReadNormalized(data).ToString("0.#####", CultureInfo.InvariantCulture)}" : string.Empty,
                11 or 12 or 35 => data.Length >= 2 ? $"Scaled={BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(0, 2))}" : string.Empty,
                13 or 14 or 36 => data.Length >= 4 ? $"Float={BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(0, 4)).ToString("0.###", CultureInfo.InvariantCulture)}" : string.Empty,
                15 or 16 or 37 => data.Length >= 4 ? DecodeBcrValue(data) : string.Empty,
                45 or 58 => data.Length >= 1 ? DecodeSingleCommand(data[0]) : string.Empty,
                46 or 59 => data.Length >= 1 ? DecodeDoubleCommand(data[0]) : string.Empty,
                47 or 60 => data.Length >= 1 ? $"Regulating step RCO=0x{data[0]:X2}" : string.Empty,
                48 or 61 => data.Length >= 2 ? $"Setpoint normalized={ReadNormalized(data).ToString("0.#####", CultureInfo.InvariantCulture)}" : string.Empty,
                49 or 62 => data.Length >= 2 ? $"Setpoint scaled={BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(0, 2))}" : string.Empty,
                50 or 63 => data.Length >= 4 ? $"Setpoint float={BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(0, 4)).ToString("0.###", CultureInfo.InvariantCulture)}" : string.Empty,
                100 => data.Length >= 1 ? $"Interrogation qualifier QOI={data[0]}" : string.Empty,
                101 => data.Length >= 1 ? $"Counter interrogation qualifier QCC=0x{data[0]:X2}" : string.Empty,
                102 => "Read command",
                103 => data.Length >= 7 ? "Clock sync" : string.Empty,
                _ => data.Length > 0 ? $"Payload={ToHex(data)}" : string.Empty
            };
        }
        catch (Exception ex)
        {
            issues.Add("Value decode warning: " + ex.Message);
            return data.Length > 0 ? $"Payload={ToHex(data)}" : string.Empty;
        }
    }

    private static string DecodeQualityText(byte type, byte[] data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        return type switch
        {
            1 or 2 or 30 => DecodeSiq(data[0]),
            3 or 4 or 31 => DecodeDiq(data[0]),
            5 or 32 => data.Length >= 2 ? DecodeQds(data[1]) : string.Empty,
            6 => data.Length >= 2 ? DecodeQds(data[1]) : string.Empty,
            7 or 33 => data.Length >= 5 ? DecodeQds(data[4]) : string.Empty,
            8 => data.Length >= 5 ? DecodeQds(data[4]) : string.Empty,
            9 or 11 or 34 or 35 => data.Length >= 3 ? DecodeQds(data[2]) : string.Empty,
            10 or 12 => data.Length >= 3 ? DecodeQds(data[2]) : string.Empty,
            13 or 36 => data.Length >= 5 ? DecodeQds(data[4]) : string.Empty,
            14 => data.Length >= 5 ? DecodeQds(data[4]) : string.Empty,
            15 or 37 => data.Length >= 5 ? DecodeBcrQuality(data[4]) : string.Empty,
            16 => data.Length >= 5 ? DecodeBcrQuality(data[4]) : string.Empty,
            _ => string.Empty
        };
    }

    private static string DecodeTimestampText(byte type, byte[] data)
    {
        return type switch
        {
            2 or 4 => data.Length >= 4 ? DecodeCp24Time2a(data.AsSpan(1, 3)) : string.Empty,
            6 => data.Length >= 5 ? DecodeCp24Time2a(data.AsSpan(2, 3)) : string.Empty,
            8 => data.Length >= 8 ? DecodeCp24Time2a(data.AsSpan(5, 3)) : string.Empty,
            10 or 12 => data.Length >= 6 ? DecodeCp24Time2a(data.AsSpan(3, 3)) : string.Empty,
            14 or 16 => data.Length >= 8 ? DecodeCp24Time2a(data.AsSpan(5, 3)) : string.Empty,
            30 or 31 => data.Length >= 8 ? DecodeCp56Time2a(data.AsSpan(1, 7)) : string.Empty,
            32 => data.Length >= 9 ? DecodeCp56Time2a(data.AsSpan(2, 7)) : string.Empty,
            33 => data.Length >= 12 ? DecodeCp56Time2a(data.AsSpan(5, 7)) : string.Empty,
            34 or 35 => data.Length >= 10 ? DecodeCp56Time2a(data.AsSpan(3, 7)) : string.Empty,
            36 or 37 => data.Length >= 12 ? DecodeCp56Time2a(data.AsSpan(5, 7)) : string.Empty,
            58 or 59 or 60 => data.Length >= 8 ? DecodeCp56Time2a(data.AsSpan(1, 7)) : string.Empty,
            61 or 62 => data.Length >= 10 ? DecodeCp56Time2a(data.AsSpan(3, 7)) : string.Empty,
            63 => data.Length >= 12 ? DecodeCp56Time2a(data.AsSpan(5, 7)) : string.Empty,
            103 => data.Length >= 7 ? DecodeCp56Time2a(data.AsSpan(0, 7)) : string.Empty,
            _ => string.Empty
        };
    }

    private static string DecodeSinglePoint(byte siq) => (siq & 0x01) != 0 ? "ON" : "OFF";

    private static string DecodeDoublePoint(byte diq) => (diq & 0x03) switch
    {
        0 => "Intermediate/Indeterminate",
        1 => "OFF",
        2 => "ON",
        3 => "Indeterminate/Fault",
        _ => "Unknown"
    };

    private static string DecodeSingleCommand(byte sco)
    {
        var state = (sco & 0x01) != 0 ? "ON" : "OFF";
        var select = (sco & 0x80) != 0 ? "select" : "execute";
        var qualifier = (sco >> 2) & 0x1F;
        return $"Single command={state}, {select}, QU={qualifier}";
    }

    private static string DecodeDoubleCommand(byte dco)
    {
        var state = DecodeDoublePoint(dco);
        var select = (dco & 0x80) != 0 ? "select" : "execute";
        var qualifier = (dco >> 2) & 0x1F;
        return $"Double command={state}, {select}, QU={qualifier}";
    }

    private static string DecodeSiq(byte q) => DecodeQualityFlags(q, hasOverflow: false);

    private static string DecodeDiq(byte q) => DecodeQualityFlags(q, hasOverflow: false);

    private static string DecodeQds(byte q) => DecodeQualityFlags(q, hasOverflow: true);

    private static string DecodeQualityFlags(byte q, bool hasOverflow)
    {
        var flags = new List<string>();
        if (hasOverflow && (q & 0x01) != 0) flags.Add("Overflow");
        if ((q & 0x10) != 0) flags.Add("Blocked");
        if ((q & 0x20) != 0) flags.Add("Substituted");
        if ((q & 0x40) != 0) flags.Add("Not topical");
        if ((q & 0x80) != 0) flags.Add("Invalid");
        return flags.Count == 0 ? "Good" : string.Join(", ", flags);
    }

    private static string DecodeBcrValue(byte[] data)
    {
        if (data.Length < 5)
        {
            return string.Empty;
        }

        var counter = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, 4));
        var flags = data[4];
        var sequence = flags & 0x1F;
        return $"Counter={counter}, sequence={sequence}";
    }

    private static string DecodeBcrQuality(byte q)
    {
        var flags = new List<string>();
        if ((q & 0x20) != 0) flags.Add("Carry");
        if ((q & 0x40) != 0) flags.Add("Adjusted");
        if ((q & 0x80) != 0) flags.Add("Invalid");
        return flags.Count == 0 ? "Good" : string.Join(", ", flags);
    }

    private static double ReadNormalized(byte[] data)
    {
        var raw = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(0, 2));
        return raw / 32768.0;
    }

    public static string DecodeCp24Time2a(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3) return "invalid";
        var ms = data[0] | (data[1] << 8);
        var second = ms / 1000;
        var millisecond = ms % 1000;
        var minuteInvalid = (data[2] & 0x80) != 0;
        var minute = data[2] & 0x3F;
        var invalid = minuteInvalid ? " invalid-time" : string.Empty;
        return $"CP24 minute={minute:00}, second={second:00}.{millisecond:000}{invalid}";
    }

    public static string DecodeCp56Time2a(byte[] data) => data.Length < 7 ? "invalid" : DecodeCp56Time2a(data.AsSpan(0, 7));

    public static string DecodeCp56Time2a(ReadOnlySpan<byte> data)
    {
        if (data.Length < 7) return "invalid";
        var ms = data[0] | (data[1] << 8);
        var second = ms / 1000;
        var millisecond = ms % 1000;
        var minuteInvalid = (data[2] & 0x80) != 0;
        var minute = data[2] & 0x3F;
        var hour = data[3] & 0x1F;
        var dayInvalid = (data[4] & 0x80) != 0;
        var day = data[4] & 0x1F;
        var month = data[5] & 0x0F;
        var year = 2000 + (data[6] & 0x7F);
        var invalid = minuteInvalid || dayInvalid ? " invalid-time" : string.Empty;
        return $"{year:0000}-{month:00}-{day:00} {hour:00}:{minute:00}:{second:00}.{millisecond:000}{invalid}";
    }

    public static string TypeName(byte type) => type switch
    {
        1 => "M_SP_NA_1 single-point",
        2 => "M_SP_TA_1 single-point with CP24Time2a",
        3 => "M_DP_NA_1 double-point",
        4 => "M_DP_TA_1 double-point with CP24Time2a",
        5 => "M_ST_NA_1 step position",
        6 => "M_ST_TA_1 step position with CP24Time2a",
        7 => "M_BO_NA_1 bitstring",
        8 => "M_BO_TA_1 bitstring with CP24Time2a",
        9 => "M_ME_NA_1 normalized measured value",
        10 => "M_ME_TA_1 normalized measured value with CP24Time2a",
        11 => "M_ME_NB_1 scaled measured value",
        12 => "M_ME_TB_1 scaled measured value with CP24Time2a",
        13 => "M_ME_NC_1 short floating measured value",
        14 => "M_ME_TC_1 short floating measured value with CP24Time2a",
        15 => "M_IT_NA_1 integrated totals",
        16 => "M_IT_TA_1 integrated totals with CP24Time2a",
        30 => "M_SP_TB_1 single-point with CP56Time2a",
        31 => "M_DP_TB_1 double-point with CP56Time2a",
        32 => "M_ST_TB_1 step position with CP56Time2a",
        34 => "M_ME_TD_1 normalized measured value with CP56Time2a",
        35 => "M_ME_TE_1 scaled measured value with CP56Time2a",
        36 => "M_ME_TF_1 short floating measured value with CP56Time2a",
        37 => "M_IT_TB_1 integrated totals with CP56Time2a",
        45 => "C_SC_NA_1 single command",
        46 => "C_DC_NA_1 double command",
        47 => "C_RC_NA_1 regulating step command",
        48 => "C_SE_NA_1 setpoint normalized",
        49 => "C_SE_NB_1 setpoint scaled",
        50 => "C_SE_NC_1 setpoint short float",
        58 => "C_SC_TA_1 single command with CP56Time2a",
        59 => "C_DC_TA_1 double command with CP56Time2a",
        60 => "C_RC_TA_1 regulating step with CP56Time2a",
        61 => "C_SE_TA_1 setpoint normalized with CP56Time2a",
        62 => "C_SE_TB_1 setpoint scaled with CP56Time2a",
        63 => "C_SE_TC_1 setpoint short float",
        100 => "C_IC_NA_1 interrogation command",
        101 => "C_CI_NA_1 counter interrogation",
        102 => "C_RD_NA_1 read command",
        103 => "C_CS_NA_1 clock synchronization",
        104 => "C_TS_NA_1 test command",
        105 => "C_RP_NA_1 reset process command",
        107 => "C_TS_TA_1 test command with CP56Time2a",
        _ => $"Type {type}"
    };

    public static string CauseName(int cot) => cot switch
    {
        1 => "periodic/cyclic",
        2 => "background scan",
        3 => "spontaneous",
        4 => "initialized",
        5 => "request/requested",
        6 => "activation",
        7 => "activation confirmation",
        8 => "deactivation",
        9 => "deactivation confirmation",
        10 => "activation termination",
        11 => "return information caused by remote command",
        12 => "return information caused by local command",
        13 => "file transfer",
        20 => "interrogated by station interrogation",
        21 => "interrogated by group 1",
        22 => "interrogated by group 2",
        23 => "interrogated by group 3",
        24 => "interrogated by group 4",
        25 => "interrogated by group 5",
        26 => "interrogated by group 6",
        27 => "interrogated by group 7",
        28 => "interrogated by group 8",
        29 => "interrogated by group 9",
        30 => "interrogated by group 10",
        31 => "interrogated by group 11",
        32 => "interrogated by group 12",
        33 => "interrogated by group 13",
        34 => "interrogated by group 14",
        35 => "interrogated by group 15",
        36 => "interrogated by group 16",
        44 => "unknown type identification",
        45 => "unknown cause of transmission",
        46 => "unknown common address",
        47 => "unknown information object address",
        _ => cot.ToString(CultureInfo.InvariantCulture)
    };

    private static string ToHex(IEnumerable<byte> bytes) => string.Join(" ", bytes.Select(x => x.ToString("X2")));
}
