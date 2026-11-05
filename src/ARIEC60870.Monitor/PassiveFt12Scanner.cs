// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;

namespace ARIEC60870.Monitor;

/// <summary>
/// Compatibility adapter retaining the exact historical Monitor public API.
/// The single canonical incremental FT1.2 framing algorithm now lives in Core.
/// Physical RX direction, archive and interpretation remain Monitor-owned.
/// </summary>
public sealed class PassiveFt12Scanner
{
    private readonly Ft12IncrementalFramer _framer;

    public PassiveFt12Scanner(int linkAddressWidth = 1)
        => _framer = new Ft12IncrementalFramer(linkAddressWidth);

    public long NoiseBytes => _framer.NoiseBytes;
    public long IncompleteBytes => _framer.IncompleteBytes;

    public PassiveFt12Frame? ExpireIncomplete()
    {
        var candidate = _framer.ExpireIncomplete();
        return candidate is null ? null : Adapt(candidate);
    }

    public void Feed(ReadOnlySpan<byte> incoming, long offset, Action<PassiveFt12Frame> emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        _framer.Feed(incoming, offset, item => emit(Adapt(item)));
    }

    private static PassiveFt12Frame Adapt(Ft12StreamCandidate frame)
        => new(frame.ByteOffset, frame.Format, frame.ChecksumValid,
            frame.RawBytes, frame.DetectedLinkAddressWidth);
}

public sealed record PassiveFt12Frame(long ByteOffset, string Format, bool ChecksumValid,
    byte[] RawBytes, int? DetectedLinkAddressWidth = null);

public sealed record PassiveObservedFrame(
    byte StreamId, long StreamOffset, DateTime EstimatedUtc, string Format,
    bool ChecksumValid, byte[] RawBytes, int? DetectedLinkAddressWidth = null);
