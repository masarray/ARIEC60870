// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
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
            // Reuse the same verified wire envelope as the active Master
            // and archived decoder; do not duplicate offsets in the Desktop.
            Ft12WireLayout layout = default;
            var verified = frame.ChecksumValid && frame.Format == "Variable" &&
                Ft12WireLayout.TryParse(raw,
                    frame.DetectedLinkAddressWidth ?? linkAddressWidth, out layout) &&
                layout.Kind == Ft12WireKind.Variable;
            var asdu = verified && layout.AsduLength > 0
                ? raw.AsSpan(layout.AsduOffset, layout.AsduLength).ToArray()
                : Array.Empty<byte>();
            var decoded = verified && asdu.Length > 0 ? decoder.Decode(asdu) : null;
            result[i] = new Prepared(frame, asdu, decoded, Convert.ToHexString(raw));
        }
        return result;
    }
}
