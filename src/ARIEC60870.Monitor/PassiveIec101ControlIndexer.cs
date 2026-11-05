// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Monitor;

/// <summary>
/// Builds source-qualified command evidence on the lossless capture-writer path,
/// BEFORE the bounded UI preview. Does not transmit or create another ASDU parser.
/// Derived JSONL remains subordinate to the verified original raw journal.
/// </summary>
public sealed class PassiveIec101ControlIndexer
{
    private readonly int _linkWidth;
    private readonly Iec10xAsduDecoder _decoder;
    private readonly Iec10xObservedControlTimeline _timeline = new();

    public PassiveIec101ControlIndexer(int linkWidth, int cotWidth, int caWidth, int ioaWidth)
    {
        if (linkWidth is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(linkWidth));
        _linkWidth = linkWidth;
        _decoder = new Iec10xAsduDecoder(cotWidth, caWidth, ioaWidth);
    }

    public int PendingCount => _timeline.PendingCount;

    public bool TryObserve(PassiveObservedFrame frame, out PassiveControlEvidence? evidence)
    {
        evidence = null;
        var raw = frame.RawBytes;
        if (!frame.ChecksumValid || frame.Format != "Variable" ||
            raw.Length < 8 || raw[0] != 0x68 || raw[3] != 0x68 ||
            raw[1] != raw[2] || raw.Length != raw[1] + 6 ||
            raw[^1] != 0x16)
            return false;

        var width = frame.DetectedLinkAddressWidth ?? _linkWidth;
        var asduStart = 5 + width;
        var asduLength = raw[1] - 1 - width;
        if (asduLength < 3 || asduStart + asduLength > raw.Length - 2)
            return false;

        var typeId = raw[asduStart];
        if (typeId is not (>= 45 and <= 51 or >= 58 and <= 64))
            return false;

        var prm = (raw[4] & 0x40) != 0;
        // The validated FT1.2 PRM bit determines command/response direction.
        // Port labels are operator hints: swapped RX taps are valid evidence,
        // not a reason to drop a command or negative confirmation.

        var address = 0;
        for (var i = 0; i < width; i++)
            address |= raw[5 + i] << (8 * i);

        var typed = _decoder.Decode(raw.AsSpan(asduStart, asduLength).ToArray());
        var note = _timeline.Observe(prm ? FrameDirection.MasterToSlave : FrameDirection.SlaveToMaster,
            typed, frame.StreamId < 2 ? "Link A" : "Link B",
            frame.StreamOffset, trusted: true, linkAddress: width == 0 ? null : address);
        if (string.IsNullOrWhiteSpace(note)) return false;

        evidence = new PassiveControlEvidence(frame.StreamId, frame.StreamOffset, frame.EstimatedUtc,
            address, typed.TypeId, typed.CommonAddress, typed.FirstObject?.InformationObjectAddress,
            typed.CauseDetails?.Code, typed.IsNegativeConfirm, note)
        {
            DirectionCotMismatch = !prm && typed.CauseDetails?.Code == 6
        };
        return true;
    }
}

/// <summary>Immutable derived observation with original stream offset.</summary>
public sealed record PassiveControlEvidence(byte StreamId, long StreamOffset, DateTime EstimatedUtc,
    int LinkAddress, int TypeId, int CommonAddress, int? Ioa,
    int? CotCode, bool Negative, string Explanation)
{
    public bool DirectionCotMismatch { get; init; }
    public bool NeedsAttention => Negative || DirectionCotMismatch || CotCode is >= 44 and <= 47;
}
