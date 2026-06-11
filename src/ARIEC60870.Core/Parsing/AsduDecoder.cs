// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using ARIEC60870.Core.Model;

namespace ARIEC60870.Core.Parsing;

public sealed class AsduDecoder
{

    public AsduDecode Decode(IReadOnlyList<byte> asduBytes)
    {
        var notes = new List<string>();

        if (asduBytes.Count < 4)
        {
            return new AsduDecode
            {
                Status = DecodeStatus.Error,
                TypeName = "Incomplete ASDU",
                Notes = new[] { "ASDU is shorter than the IEC-103 common header." },
                DataBytes = asduBytes.ToArray()
            };
        }

        var typeId = asduBytes[0];
        var vsq = asduBytes[1];
        var cot = asduBytes[2];
        var ca = asduBytes[3];
        int? fun = asduBytes.Count >= 5 ? asduBytes[4] : null;
        int? inf = asduBytes.Count >= 6 ? asduBytes[5] : null;
        var data = asduBytes.Skip(6).ToArray();

        int? dpi = null;
        string? dpiText = null;
        double? numericValue = null;
        string? numericValueText = null;
        Cp32Time2a? time = null;
        string? identification = null;
        string? meaning = null;
        var status = DecodeStatus.Ok;

        switch (typeId)
        {
            case 1:
                meaning = "Time-tagged protection/status message returned by the relay.";
                if (data.Length >= 1)
                {
                    dpi = data[0];
                    dpiText = DpiText(dpi.Value);
                }
                if (data.Length >= 5)
                {
                    var timeStart = data.Length >= 6 ? 1 : 0;
                    time = Cp32Time2aDecoder.TryDecode(data.Skip(timeStart).Take(5).ToArray());
                }
                break;

            case 2:
                meaning = "Protection/status message with relative-time context. Often seen in event or disturbance-related sequences.";
                if (data.Length >= 1)
                {
                    dpi = data[0];
                    dpiText = DpiText(dpi.Value);
                }
                if (data.Length >= 5)
                {
                    time = Cp32Time2aDecoder.TryDecode(data.Skip(Math.Max(0, data.Length - 5)).Take(5).ToArray());
                }
                break;

            case 5:
                meaning = "Identification response from the relay/protection equipment.";
                identification = ExtractPrintableAscii(data);
                if (string.IsNullOrWhiteSpace(identification))
                {
                    identification = ExtractPrintableAscii(asduBytes.Skip(6).ToArray());
                }
                break;

            case 6:
                meaning = "Clock synchronization message.";
                if (data.Length >= 4)
                {
                    time = Cp32Time2aDecoder.TryDecode(data.Take(Math.Min(5, data.Length)).ToArray());
                }
                break;

            case 7:
                meaning = "General Interrogation command/request.";
                if (data.Length > 0) notes.Add($"Scan/qualifier byte: 0x{data[0]:X2}");
                break;

            case 8:
                meaning = "End of General Interrogation.";
                if (data.Length > 0) notes.Add($"Scan/qualifier byte: 0x{data[0]:X2}");
                break;

            case 9:
                meaning = "Measurand or monitored data ASDU. ARIEC60870 decodes the first signed 16-bit value as a universal numeric hint; final scaling/name comes from the user mapping profile.";
                if (data.Length >= 2)
                {
                    var raw = (short)(data[0] | (data[1] << 8));
                    numericValue = raw;
                    numericValueText = raw.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    notes.Add($"First signed 16-bit measurand value: {raw}. Apply user mapping scale/unit for engineering display.");
                }
                break;

            case 20:
                meaning = "General command ASDU. Command semantics are project/relay-specific and must be validated against the user mapping/profile.";
                if (data.Length > 0) notes.Add($"Command qualifier/value byte: 0x{data[0]:X2}");
                break;

            case 23:
                meaning = "Disturbance-related ASDU. Detailed interpretation depends on the relay profile and disturbance object layout.";
                break;

            default:
                status = DecodeStatus.Unknown;
                meaning = "Unknown or vendor/private ASDU. Keep raw payload and map through device profile if available.";
                notes.Add("No ASDU-specific decoder is registered for this type yet; keep raw payload and use a user mapping profile if needed.");
                break;
        }

        string? semanticLabel = null;
        string? semanticCategory = null;
        string? semanticState = null;
        string? semanticConfidence = null;
        string? semanticNote = null;

        return new AsduDecode
        {
            TypeId = typeId,
            TypeName = TypeName(typeId),
            Vsq = vsq,
            ObjectCount = vsq & 0x7F,
            Sequence = (vsq & 0x80) != 0,
            CauseOfTransmission = cot,
            CauseName = CauseName(cot),
            CommonAddress = ca,
            FunctionType = fun,
            InformationNumber = inf,
            Dpi = dpi,
            DpiText = dpiText,
            NumericValue = numericValue,
            NumericValueText = numericValueText,
            Time = time,
            IdentificationText = identification,
            EngineeringMeaning = meaning,
            ProfileName = null,
            SemanticLabel = semanticLabel,
            SemanticCategory = semanticCategory,
            SemanticState = semanticState,
            SemanticConfidence = semanticConfidence,
            SemanticNote = semanticNote,
            Status = status,
            DataBytes = data,
            Notes = notes
        };
    }

    public static string TypeName(int typeId) => typeId switch
    {
        1 => "DPI(TM) - time-tagged message",
        2 => "DPI(RT) - message with relative time",
        5 => "Identification",
        6 => "Clock synchronization",
        7 => "General Interrogation",
        8 => "General Interrogation End",
        9 => "Measurands",
        20 => "General command",
        23 => "Disturbance-related ASDU",
        205 => "Private/vendor-specific ASDU",
        _ => $"Unknown ASDU Type {typeId}"
    };

    public static string CauseName(int cot) => cot switch
    {
        1 => "Spontaneous",
        2 => "Cyclic",
        3 => "Reset FCB / initialization related",
        4 => "Reset CU / startup related",
        5 => "Request or requested",
        6 => "Activation",
        7 => "Activation confirmation",
        8 => "Clock sync / activation",
        9 => "General Interrogation",
        10 => "General Interrogation termination",
        20 => "Command",
        _ => $"COT {cot}"
    };

    public static string DpiText(int dpi) => (dpi & 0x03) switch
    {
        0 => "Indeterminate / intermediate",
        1 => "State 1 / OFF-like state",
        2 => "State 2 / ON-like state",
        3 => "Invalid / bad state",
        _ => "Unknown DPI"
    };

    private static string? ExtractPrintableAscii(IReadOnlyList<byte> bytes)
    {
        var runs = new List<string>();
        var builder = new StringBuilder();

        foreach (var b in bytes)
        {
            if (b >= 0x20 && b <= 0x7E)
            {
                builder.Append((char)b);
                continue;
            }

            if (builder.Length >= 3) runs.Add(builder.ToString().TrimEnd('\0', ' '));
            builder.Clear();
        }

        if (builder.Length >= 3) runs.Add(builder.ToString().TrimEnd('\0', ' '));
        var result = string.Join(" ", runs.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
