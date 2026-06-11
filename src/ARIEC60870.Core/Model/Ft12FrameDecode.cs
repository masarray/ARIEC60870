// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Model;

public sealed class Ft12FrameDecode
{
    public Ft12FrameFormat Format { get; init; }
    public IReadOnlyList<byte> RawBytes { get; init; } = Array.Empty<byte>();
    public byte? Control { get; init; }
    public int? LinkAddress { get; init; }
    public int LinkAddressSize { get; init; } = 1;
    public byte? Checksum { get; init; }
    public byte? CalculatedChecksum { get; init; }
    public bool IsChecksumValid { get; init; }
    public bool IsLengthValid { get; init; }
    public int? DeclaredLength { get; init; }

    /// <summary>
    /// IEC-101/103 FT1.2 one-character response meaning. E5 is positive ACK;
    /// A2 is a negative single-character response used by some RTU profiles.
    /// </summary>
    public string SingleCharacterName => Format == Ft12FrameFormat.SingleCharacter && RawBytes.Count == 1
        ? RawBytes[0] == 0xE5 ? "ACK"
        : RawBytes[0] == 0xA2 ? "NACK"
        : $"0x{RawBytes[0]:X2}"
        : string.Empty;
    public bool IsSingleCharacterAck => Format == Ft12FrameFormat.SingleCharacter && RawBytes.Count == 1 && RawBytes[0] == 0xE5;
    public bool IsSingleCharacterNack => Format == Ft12FrameFormat.SingleCharacter && RawBytes.Count == 1 && RawBytes[0] == 0xA2;
    public LinkControlInfo? LinkControl { get; init; }
    public AsduDecode? Asdu { get; init; }
    public IReadOnlyList<byte> AsduBytes { get; init; } = Array.Empty<byte>();
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    public string Hex => string.Join(" ", RawBytes.Select(x => x.ToString("X2")));

    public string ShortMeaning
    {
        get
        {
            if (Format == Ft12FrameFormat.SingleCharacter) return IsSingleCharacterNack ? "Single character NACK" : "Single character ACK";
            if (LinkControl is null) return "Malformed or unsupported frame";
            if (Asdu is not null)
            {
                var semantic = string.IsNullOrWhiteSpace(Asdu.SemanticLabel) ? string.Empty : " - " + Asdu.SemanticLabel;
                return $"{LinkControl.FunctionName}; {Asdu.TypeName}{semantic}";
            }
            return LinkControl.FunctionName;
        }
    }
}
