// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>Decoder result for an archived on-wire frame and its recorded profile.</summary>
public sealed record Iec10xArchivedDecode(
    Ft12FrameDecode? SerialFrame, Iec104ApduDecode? TcpFrame,
    Iec10xAsduDecode? Asdu, bool IsIntegrityValid);

/// <summary>
/// Replay archive bytes exactly once using recorded field widths. No guessed profile:
/// callers with missing/invalid profile retain raw evidence but receive no typed decode.
/// </summary>
public static class Iec10xArchivedFrameDecoder
{
    public static Iec10xArchivedDecode Decode(Iec60870ProtocolMode protocol, IReadOnlyList<byte> raw,
        int? linkWidth, int? cotWidth, int? caWidth, int? ioaWidth)
    {
        if (raw.Count == 0)
            return new Iec10xArchivedDecode(null, null, null, false);

        if (protocol == Iec60870ProtocolMode.Iec101 && linkWidth is >= 0 and <= 2)
        {
            var serial = new Ft12Parser(linkWidth.Value).Decode(raw);
            if (!serial.IsChecksumValid || !serial.IsLengthValid)
                return new Iec10xArchivedDecode(serial, null, null, false);

            if (!ValidAsduProfile(cotWidth, caWidth, ioaWidth) || serial.AsduBytes.Count == 0)
                return new Iec10xArchivedDecode(serial, null, null, true);

            var asdu = new Iec10xAsduDecoder(cotWidth!.Value, caWidth!.Value, ioaWidth!.Value)
                .Decode(serial.AsduBytes);
            return new Iec10xArchivedDecode(serial, null, asdu, asdu.Issues.Count == 0);
        }

        if (protocol == Iec60870ProtocolMode.Iec104 && ValidAsduProfile(cotWidth, caWidth, ioaWidth))
        {
            var frame = new Iec104ApduParser(cotWidth!.Value, caWidth!.Value, ioaWidth!.Value)
                .Decode(raw);
            return new Iec10xArchivedDecode(null, frame,
                frame.IsValid ? frame.Asdu : null,
                frame.IsValid && (frame.Asdu is null || frame.Asdu.Issues.Count == 0));
        }

        return new Iec10xArchivedDecode(null, null, null, false);
    }

    private static bool ValidAsduProfile(int? cot, int? ca, int? ioa)
        => cot is >= 1 and <= 2 && ca is >= 1 and <= 2 && ioa is >= 1 and <= 3;
}
