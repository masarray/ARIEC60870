// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol.Iec10x;

public sealed class Iec104ApduDecode
{
    public IReadOnlyList<byte> RawBytes { get; init; } = Array.Empty<byte>();
    public bool IsValid { get; init; }
    public string Format { get; init; } = "Malformed";
    public int? SendSequence { get; init; }
    public int? ReceiveSequence { get; init; }
    public string UFormatName { get; init; } = string.Empty;
    public Iec10xAsduDecode? Asdu { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
    public string Hex => string.Join(" ", RawBytes.Select(x => x.ToString("X2")));
    public string ShortMeaning => Format switch
    {
        "I" => Asdu is null ? $"I-format APDU N(S)={SendSequence}, N(R)={ReceiveSequence}" : $"I-format APDU N(S)={SendSequence}, N(R)={ReceiveSequence}; {Asdu.ShortMeaning}",
        "S" => $"S-format acknowledgement N(R)={ReceiveSequence}",
        "U" => "U-format " + UFormatName,
        _ => "Malformed IEC-104 APDU"
    };
}
