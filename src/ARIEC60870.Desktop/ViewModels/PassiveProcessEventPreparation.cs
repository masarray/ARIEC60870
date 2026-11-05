// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Monitor;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Stateless, off-Dispatcher page preparation from writer-owned event facts.
/// Never interprets a frame from WPF display text or guesses field widths.
/// Reuses the canonical codec and caches repeated objects from one ASDU page.
/// </summary>
public static class PassiveProcessEventPreparation
{
    public sealed record Prepared(
        PassiveProcessEventFact Fact,
        Ft12FrameDecode? Frame,
        Iec10xAsduDecode? Asdu,
        Iec10xInformationObject? Object,
        string RawHex)
    {
        public bool Trusted => Frame is { IsChecksumValid: true, IsLengthValid: true } &&
            Asdu is { Issues.Count: 0 } && Object is { Issues.Count: 0 };
    }

    public static IReadOnlyList<Prepared> PreparePage(
        IReadOnlyList<PassiveProcessEventFact> facts,
        int linkWidth, int cotWidth, int caWidth, int ioaWidth)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var decoder = new Iec10xAsduDecoder(cotWidth, caWidth, ioaWidth);
        var output = new Prepared[facts.Count];
        Ft12FrameDecode? cachedFrame = null;
        Iec10xAsduDecode? cachedAsdu = null;
        string cachedHex = "";
        byte cachedStream = byte.MaxValue;
        long cachedOffset = -1;
        for (var i = 0; i < facts.Count; i++)
        {
            var item = facts[i];
            var seen = item.Frame;
            if (cachedFrame is null || cachedStream != seen.StreamId ||
                cachedOffset != seen.StreamOffset)
            {
                var actualWidth = seen.DetectedLinkAddressWidth ?? linkWidth;
                cachedFrame = Ft12Parser.ForIec101(actualWidth).Decode(seen.RawBytes);
                cachedAsdu = cachedFrame.IsChecksumValid && cachedFrame.IsLengthValid &&
                    cachedFrame.AsduBytes.Count > 0
                    ? decoder.Decode(cachedFrame.AsduBytes) : null;
                cachedHex = Convert.ToHexString(seen.RawBytes);
                cachedStream = seen.StreamId;
                cachedOffset = seen.StreamOffset;
            }
            var obj = cachedAsdu is { Issues.Count: 0 } &&
                      cachedAsdu.ObjectCount == cachedAsdu.Objects.Count
                ? cachedAsdu.Objects.FirstOrDefault(x => x.Index == item.ObjectIndex)
                : null;
            // Never promote a stale/edited index to a process observation:
            // canonical wire and ASDU structure must validate again.
            output[i] = new Prepared(item, cachedFrame, cachedAsdu, obj, cachedHex);
        }
        return output;
    }
}
