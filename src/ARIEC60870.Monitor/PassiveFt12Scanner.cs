// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Monitor;

/// <summary>
/// A passive, direction-preserving FT1.2 frame boundary detector. It does not
/// decode ASDUs, transmit, own FCB state, or discard bytes from the raw archive.
/// One instance per physical RX direction; pending bytes survive UART read splits.
/// </summary>
public sealed class PassiveFt12Scanner
{
    private const int MaxFrameBytes = 261; // 0x68 + L + L + 0x68 + 255 + CS + 0x16
    private readonly byte[] _pending = new byte[MaxFrameBytes + 1];
    private readonly int _addressWidth;
    private int _count;
    private long _startOffset;
    private long _noiseBytes;
    private readonly byte[] _noise = new byte[128];
    private int _noiseCount;
    private long _noiseStart;

    public PassiveFt12Scanner(int linkAddressWidth = 1)
    {
        if (linkAddressWidth is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(linkAddressWidth));
        _addressWidth = linkAddressWidth;
    }

    /// <summary>
    /// A communication idle gap/EOF can terminate an uncompleted candidate.
    /// Bytes remain present in the raw journal; emit a partial diagnostic instead
    /// of silently losing a later ACK behind a stale 0x10/0x68 prefix.
    /// </summary>
    public PassiveFt12Frame? ExpireIncomplete()
    {
        if (_count == 0) return null;
        var raw = new byte[_count];
        Array.Copy(_pending, raw, _count);
        var partial = new PassiveFt12Frame(_startOffset, "Partial", false, raw);
        _noiseBytes += _count;
        _count = 0;
        return partial;
    }

    public long NoiseBytes => _noiseBytes;
    public long IncompleteBytes => _count;

    /// <summary>Appends all bytes in order. Complete candidates are reported even on bad checksum.</summary>
    public void Feed(ReadOnlySpan<byte> incoming, long offset, Action<PassiveFt12Frame> emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        for (var i = 0; i < incoming.Length; i++)
        {
            if (_count == 0) _startOffset = offset + i;
            _pending[_count++] = incoming[i];
            Process(emit);
        }
        // Surface discarded material in bounded chunks, including when there
        // is no recognizable frame at all. Archive bytes remain authoritative.
        FlushNoise(emit);
    }

    private void Process(Action<PassiveFt12Frame> emit)
    {
        while (_count > 0)
        {
            var first = _pending[0];
            if (first is 0xE5 or 0xA2)
            {
                FlushNoise(emit);
                // A2 is a recognized single byte on the wire, but invalid
                // under IEC-101; the protocol interpreter flags it.
                Emit(1, true, "Single", emit);
                continue;
            }
            if (first != 0x10 && first != 0x68)
            {
                ShiftOne(emit);
                continue;
            }

            if (first == 0x10)
            {
                var expected = 4 + _addressWidth;
                // The selected profile is authoritative for interpretation.
                // Still retain a structurally valid 0/1/2-octet *alternative*
                // as a profile-mismatch candidate instead of discarding a
                // real Link Status/ACK as random serial noise.
                if (_count >= expected && _pending[expected - 1] == 0x16)
                {
                    var valid = FixedChecksum(_addressWidth);
                    FlushNoise(emit);
                    Emit(expected, valid, "Fixed", emit);
                    continue;
                }
                var mismatchWidth = -1;
                for (var width = 0; width <= 2; width++)
                {
                    if (width == _addressWidth) continue;
                    var length = 4 + width;
                    if (_count >= length && _pending[length - 1] == 0x16 &&
                        FixedChecksum(width))
                    {
                        mismatchWidth = width;
                        break;
                    }
                }
                if (mismatchWidth >= 0)
                {
                    FlushNoise(emit);
                    Emit(4 + mismatchWidth, true, "LinkWidthMismatch", emit,
                        mismatchWidth);
                    continue;
                }
                // Wait for a possible 2-octet frame before rejecting a 0/1
                // octet interpretation. A short read is not a malformed frame.
                if (_count < 6) return;
                ShiftOne(emit);
                continue;
            }

            if (_count < 4) return;
            if (_pending[1] != _pending[2] || _pending[3] != 0x68 ||
                _pending[1] < 1 + _addressWidth)
            {
                ShiftOne(emit);
                continue;
            }
            var size = 6 + _pending[1];
            if (size > MaxFrameBytes) { ShiftOne(emit); continue; }
            if (_count < size) return;
            if (_pending[size - 1] != 0x16) { ShiftOne(emit); continue; }
            var calc = 0;
            for (var j = 4; j < 4 + _pending[1]; j++) calc += _pending[j];
            FlushNoise(emit);
            Emit(size, _pending[size - 2] == (byte)calc, "Variable", emit);
        }
    }

    private bool FixedChecksum(int width)
    {
        var sum = _pending[1];
        for (var j = 0; j < width; j++) sum += _pending[2 + j];
        return _pending[2 + width] == (byte)sum;
    }

    private void Emit(int length, bool valid, string format, Action<PassiveFt12Frame> emit,
        int? detectedWidth = null)
    {
        var raw = new byte[length];
        Array.Copy(_pending, raw, length);
        emit(new PassiveFt12Frame(_startOffset, format, valid, raw, detectedWidth));
        if (_count > length)
            Array.Copy(_pending, length, _pending, 0, _count - length);
        _startOffset += length;
        _count -= length;
    }

    private void ShiftOne(Action<PassiveFt12Frame> emit)
    {
        _noiseBytes++;
        if (_noiseCount == 0) _noiseStart = _startOffset;
        _noise[_noiseCount++] = _pending[0];
        if (_noiseCount == _noise.Length) FlushNoise(emit);
        if (--_count > 0) Array.Copy(_pending, 1, _pending, 0, _count);
        _startOffset++;
    }

    private void FlushNoise(Action<PassiveFt12Frame> emit)
    {
        if (_noiseCount == 0) return;
        var raw = new byte[_noiseCount];
        Array.Copy(_noise, raw, _noiseCount);
        emit(new PassiveFt12Frame(_noiseStart, "Noise", false, raw));
        _noiseCount = 0;
    }

}

/// <summary>A frame candidate, not necessarily checksum-valid; serial direction is supplied by its source.</summary>
public sealed record PassiveFt12Frame(long ByteOffset, string Format, bool ChecksumValid,
    byte[] RawBytes, int? DetectedLinkAddressWidth = null);

public sealed record PassiveObservedFrame(
    byte StreamId, long StreamOffset, DateTime EstimatedUtc, string Format,
    bool ChecksumValid, byte[] RawBytes, int? DetectedLinkAddressWidth = null);
