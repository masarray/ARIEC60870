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
        Ft12FrameDecode frame, int expectedLinkAddress, ReadOnlySpan<byte> lastTransmit,
        bool matchesEarlierTransmit = false, int? pendingPrimaryFunctionCode = null)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Format == Ft12FrameFormat.Malformed || !frame.IsChecksumValid || !frame.IsLengthValid)
        {
            return Rejected("IEC101-RX-INTEGRITY",
                $"Malformed FT1.2 envelope or invalid length/checksum. Received={frame.Hex}.");
        }

        if (frame.Format == Ft12FrameFormat.SingleCharacter)
        {
            // IEC 60870-5-101 Amendment 2, 6.2.1.1 / Table 10:
            // E5 substitutes FC0 confirmation or FC9 class-poll no-data only.
            // It carries neither an address nor ACD/DFC and cannot verify FC8/9 status.
            // A2 is recognizable as raw wire evidence but prohibited as an IEC101 reply.
            if (frame.IsSingleCharacterNack)
                return Rejected("IEC101-RX-FORBIDDEN-A2",
                    "A2 is not a permitted IEC-101 unbalanced secondary reply; retain its raw byte without interpreting it as a NACK.");
            if (frame.IsSingleCharacterAck)
            {
                if (pendingPrimaryFunctionCode is 8 or 9)
                    return Rejected("IEC101-RX-UNEXPECTED-E5-STATUS",
                        "E5 cannot answer an access-demand/link-status request; await an addressed FC11 secondary status reply.");
                if (pendingPrimaryFunctionCode is int request && request is not (0 or 1 or 3 or 7 or 10 or 11))
                    return Rejected("IEC101-RX-UNEXPECTED-E5",
                        $"E5 is not a permitted reply to primary FC={request} in IEC-101 unbalanced mode.");
                return Accepted(advanceFcb: true);
            }
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
                : matchesEarlierTransmit ? "IEC101-RX-DELAYED-TX-ECHO"
                : frame.LinkAddress != expectedLinkAddress ? "IEC101-RX-FOREIGN-PRIMARY"
                : "IEC101-RX-UNEXPECTED-PRIMARY";
            return Rejected(code,
                $"PRM=1 (primary/master frame, expected secondary PRM=0); received Link={frame.LinkAddress?.ToString() ?? "-"}, expected Link={expectedLinkAddress}; exact last-TX echo={isExactEcho}; earlier-TX match={matchesEarlierTransmit}; FC={control.FunctionCode}. Could be echo, another master, crossover, or wrong link mode. The trace alone cannot identify the physical cause.");
        }

        if (frame.LinkAddress != expectedLinkAddress)
        {
            return Rejected("IEC101-RX-LINK-ADDRESS-MISMATCH",
                $"Secondary frame addressed to Link={frame.LinkAddress?.ToString() ?? "-"}, but this channel expects Link={expectedLinkAddress}. Check RTU link addresses, channel wiring and converter routing.");
        }

        var functionCode = control.FunctionCode;
        if (functionCode is 14 or 15)
            return Rejected("IEC101-RX-LINK-SERVICE-ERROR",
                $"Secondary FC={functionCode} reports link service unavailable/not implemented; it must not verify a healthy link or advance FCB.");
        if (functionCode is not (0 or 1 or 8 or 9 or 11))
            return Rejected("IEC101-RX-SECONDARY-FUNCTION",
                $"Unexpected secondary FC={functionCode}; expected a defined IEC-101 unbalanced secondary service.");

        // A checksum-valid, addressed reply still must match the outstanding
        // primary service. In particular FC0/FC8/FC9 must NEVER certify FC9 status;
        // FC11 status cannot acknowledge a confirmed command or class poll.
        if (pendingPrimaryFunctionCode is int pending)
        {
            var compatible = pending switch
            {
                0 or 1 or 3 or 7 => functionCode is 0 or 1,
                8 or 9 => functionCode == 11,
                10 or 11 => functionCode is 8 or 9,
                _ => false
            };
            if (!compatible)
                return Rejected("IEC101-RX-UNEXPECTED-FUNCTION",
                    $"Secondary FC={functionCode} cannot answer outstanding primary FC={pending}; retain the original response as out-of-context evidence.");

            // Status with DFC=1 is real secondary evidence but NOT a working
            // station/link-status confirmation; active/standby health must not turn green.
            if (pending is 8 or 9 && control.Dfc == true)
                return new Iec101SecondaryResponseValidation(true, false, "IEC101-RX-STATUS-BUSY",
                    $"Secondary FC11 DFC=1 is an observed busy status during FC={pending}; not healthy readiness, silence, or FCB progress.");
        }

        // A matching secondary NACK or DFC=1 is evidence, not a positive
        // confirmation or permission to toggle the primary FCB.
        return Accepted(advanceFcb: functionCode != 1 && control.Dfc != true);
    }

    public static bool CanAdvanceFcb(Ft12FrameDecode? frame)
        => frame is { IsChecksumValid: true, IsLengthValid: true }
           && frame.Format != Ft12FrameFormat.Malformed
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
