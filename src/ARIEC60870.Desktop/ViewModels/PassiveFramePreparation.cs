// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Monitor;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Pure, ordered, WPF-independent preparation of a bounded batch of received
/// IEC-101 frames. Intended for one ThreadPool task per active nonempty batch,
/// never one task per frame. This is a UI preview optimization only: the raw
/// journal remains owned by the RX/session writer.
/// </summary>
public static class PassiveFramePreparation
{
    public sealed record Prepared(PassiveObservedFrame Frame, byte[] AsduRaw,
        Iec10xAsduDecode? Decode, string RawHex);

    public static IReadOnlyList<Prepared> PrepareBatch(
        IReadOnlyList<PassiveObservedFrame> frames,
        int linkAddressWidth, int cotWidth, int caWidth, int ioaWidth)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var decoder = new Iec10xAsduDecoder(cotWidth, caWidth, ioaWidth);
        var result = new Prepared[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var raw = frame.RawBytes;
            var linkWidth = frame.DetectedLinkAddressWidth ?? linkAddressWidth;
            var asduOffset = 5 + linkWidth;
            var advertised = frame.Format == "Variable" && raw.Length > 1
                ? Math.Max(0, raw[1] - 1 - linkWidth) : 0;
            // A malformed/truncated frame must not cause a range exception.
            // The UI still receives its original bytes and checksum/length warning.
            var validSpan = frame.Format == "Variable" &&
                advertised > 0 && asduOffset >= 0 &&
                asduOffset <= raw.Length &&
                advertised <= raw.Length - asduOffset;
            var asdu = validSpan
                ? raw.AsSpan(asduOffset, advertised).ToArray() : Array.Empty<byte>();
            var decoded = frame.ChecksumValid && asdu.Length > 0
                ? decoder.Decode(asdu) : null;
            result[i] = new Prepared(frame, asdu, decoded, Convert.ToHexString(raw));
        }
        return result;
    }
}
