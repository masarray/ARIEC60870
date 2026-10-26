// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;

namespace ARIEC60870.Master.Protocol;

/// <summary>
/// Validates the direction and address of one IEC-101 unbalanced secondary reply.
/// A valid checksum alone is not proof that a received frame came from the outstation.
/// </summary>
public static class Iec101SecondaryResponseValidator
{
    public const int MaxRejectedFramesPerExchange = 16;

    public static Iec101SecondaryResponseValidation Validate(
        Ft12FrameDecode frame, int expectedLinkAddress, ReadOnlySpan<byte> lastTransmit)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Format == Ft12FrameFormat.Malformed || !frame.IsChecksumValid || !frame.IsLengthValid)
        {
            return Rejected("IEC101-RX-INTEGRITY",
                $"Malformed FT1.2 envelope or invalid length/checksum. Received={frame.Hex}.");
        }

        if (frame.Format == Ft12FrameFormat.SingleCharacter)
        {
            if (frame.IsSingleCharacterAck) return Accepted(advanceFcb: true);
            if (frame.IsSingleCharacterNack) return Accepted(advanceFcb: false);
            return Rejected("IEC101-RX-UNSUPPORTED", $"Unexpected single-character reply: {frame.Hex}.");
        }

        var control = frame.LinkControl;
        if (control is null)
        {
            return Rejected("IEC101-RX-NO-CONTROL", $"FT1.2 frame lacks decoded link control. Received={frame.Hex}.");
        }

        // Primary requests arriving at an unbalanced master are not secondary replies,
        // including delayed echoes of an earlier request. Do not advance FCB or link health.
        if (control.Prm)
        {
            var isExactEcho = frame.RawBytes is byte[] bytes
                              && bytes.AsSpan().SequenceEqual(lastTransmit)
                              && lastTransmit.Length > 0;
            var code = isExactEcho ? "IEC101-RX-TX-ECHO"
                : frame.LinkAddress != expectedLinkAddress ? "IEC101-RX-FOREIGN-PRIMARY"
                : "IEC101-RX-UNEXPECTED-PRIMARY";
            return Rejected(code,
                $"PRM=1 (primary/master frame, expected secondary PRM=0); received Link={frame.LinkAddress?.ToString() ?? "-"}, expected Link={expectedLinkAddress}; exact last-TX echo={isExactEcho}; FC={control.FunctionCode}. Could be echo, another master, crossover, or wrong link mode. The trace alone cannot identify the physical cause.");
        }

        if (frame.LinkAddress != expectedLinkAddress)
        {
            return Rejected("IEC101-RX-LINK-ADDRESS-MISMATCH",
                $"Secondary frame addressed to Link={frame.LinkAddress?.ToString() ?? "-"}, but this channel expects Link={expectedLinkAddress}. Check RTU link addresses, channel wiring and converter routing.");
        }

        if (control.FunctionCode is not (0 or 1 or 8 or 9 or 11))
        {
            return Rejected("IEC101-RX-SECONDARY-FUNCTION",
                $"Unexpected secondary function FC={control.FunctionCode}; expected one of 0/1/8/9/11.");
        }

        // NACK and DFC=1 are genuine secondary evidence, but not a positive
        // acknowledgment of the confirmed request and must never advance FCB.
        return Accepted(advanceFcb: control.FunctionCode != 1 && control.Dfc != true);
    }

    public static bool CanAdvanceFcb(Ft12FrameDecode? frame)
        => frame is not null
           && (frame.IsSingleCharacterAck
               || (frame.LinkControl is { Prm: false } link
                   && link.Dfc != true
                   && link.FunctionCode is 0 or 8 or 9 or 11));

    private static Iec101SecondaryResponseValidation Accepted(bool advanceFcb)
        => new(true, advanceFcb, "IEC101-RX-ACCEPTED", "Valid IEC-101 secondary response.");

    private static Iec101SecondaryResponseValidation Rejected(string code, string explanation)
        => new(false, false, code, explanation);
}

public readonly record struct Iec101SecondaryResponseValidation(
    bool IsValid,
    bool AdvancesFcb,
    string Code,
    string Explanation);
