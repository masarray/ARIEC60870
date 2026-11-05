// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Parsing;

/// <summary>
/// A passive, direction-preserving FT1.2 frame boundary detector. It does not
/// decode ASDUs, transmit, own FCB state, or discard bytes from the raw archive.
/// One instance per physical RX direction; pending bytes survive UART read splits.
/// </summary>
public sealed class Ft12IncrementalFramer
{
    private const int MaxFrameBytes = 261; // 0x68 + L + L + 0x68 + 255 + CS + 0x16
    private readonly byte[] _pending = new byte[MaxFrameBytes + 1];
    private readonly int _addressWidth;
    private readonly bool _permitAlternateLinkWidths;
    private readonly bool _returnMalformedEnd;
    // Fixed FT1.2 frames have an unambiguous checksum/length for 0/1/2-byte
    // link addresses. Require two consistent physical observations before
    // using an alternate width for variable ASDUs. No extra ASDU parser.
    private int _confirmedWidth;
    private int _candidateWidth = -1;
    private int _candidateAddress = -1;
    private int _candidateHits;
    private int _count;
    private long _startOffset;
    private long _noiseBytes;
    private readonly byte[] _noise = new byte[128];
    private int _noiseCount;
    private long _noiseStart;

    public Ft12IncrementalFramer(int linkAddressWidth = 1,
        bool permitAlternateLinkWidths = true, bool returnMalformedEnd = false)
    {
        if (linkAddressWidth is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(linkAddressWidth));
        _addressWidth = linkAddressWidth;
        _permitAlternateLinkWidths = permitAlternateLinkWidths;
        _returnMalformedEnd = returnMalformedEnd;
        _confirmedWidth = linkAddressWidth;
    }

    /// <summary>
    /// A communication idle gap/EOF can terminate an uncompleted candidate.
    /// Bytes remain present in the raw journal; emit a partial diagnostic instead
    /// of silently losing a later ACK behind a stale 0x10/0x68 prefix.
    /// </summary>
    public Ft12StreamCandidate? ExpireIncomplete()
    {
        if (_count == 0) return null;
        var raw = new byte[_count];
        Array.Copy(_pending, raw, _count);
        var partial = new Ft12StreamCandidate(_startOffset, "Partial", false, raw);
        _noiseBytes += _count;
        _count = 0;
        return partial;
    }

    public long NoiseBytes => _noiseBytes;
    public long IncompleteBytes => _count;

    /// <summary>Appends all bytes in order. Complete candidates are reported even on bad checksum.</summary>
    public void Feed(ReadOnlySpan<byte> incoming, long offset, Action<Ft12StreamCandidate> emit,
        bool flushNoiseAtEnd = true)
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
        if (flushNoiseAtEnd) FlushNoise(emit);
    }

    private void Process(Action<Ft12StreamCandidate> emit)
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
                var expected = 4 + _confirmedWidth;
                // The selected profile is authoritative for interpretation.
                // Still retain a structurally valid 0/1/2-octet *alternative*
                // as a profile-mismatch candidate instead of discarding a
                // real Link Status/ACK as random serial noise.
                if (_count >= expected && _pending[expected - 1] == 0x16)
                {
                    var valid = FixedChecksum(_confirmedWidth);
                    if (valid) ObserveVerifiedFixedWidth(_confirmedWidth);
                    FlushNoise(emit);
                    Emit(expected, valid,
                        _confirmedWidth == _addressWidth ? "Fixed" : "LinkWidthMismatch",
                        emit, _confirmedWidth == _addressWidth ? null : _confirmedWidth);
                    continue;
                }
                var mismatchWidth = -1;
                for (var width = 0; _permitAlternateLinkWidths && width <= 2; width++)
                {
                    if (width == _confirmedWidth) continue;
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
                    ObserveVerifiedFixedWidth(mismatchWidth);
                    FlushNoise(emit);
                    Emit(4 + mismatchWidth, true,
                        mismatchWidth == _addressWidth ? "Fixed" : "LinkWidthMismatch",
                        emit, mismatchWidth == _addressWidth ? null : mismatchWidth);
                    continue;
                }
                // Wait for a possible 2-octet frame before rejecting a 0/1
                // octet interpretation. A short read is not a malformed frame.
                if (_count < 6 && !_returnMalformedEnd) return;
                if (_count < expected) return;
                if (_returnMalformedEnd)
                {
                    FlushNoise(emit);
                    Emit(expected, false, "Malformed", emit);
                }
                else ShiftOne(emit);
                continue;
            }

            if (_count < 4) return;
            if (_pending[1] != _pending[2] || _pending[3] != 0x68 ||
                _pending[1] < 1 + _confirmedWidth)
            {
                ShiftOne(emit);
                continue;
            }
            var size = 6 + _pending[1];
            if (size > MaxFrameBytes) { ShiftOne(emit); continue; }
            if (_count < size) return;
            if (_pending[size - 1] != 0x16)
            {
                if (_returnMalformedEnd)
                {
                    FlushNoise(emit);
                    Emit(size, false, "Malformed", emit,
                        _confirmedWidth == _addressWidth ? null : _confirmedWidth);
                }
                else ShiftOne(emit);
                continue;
            }
            var calc = 0;
            for (var j = 4; j < 4 + _pending[1]; j++) calc += _pending[j];
            FlushNoise(emit);
            Emit(size, _pending[size - 2] == (byte)calc, "Variable", emit,
                _confirmedWidth == _addressWidth ? null : _confirmedWidth);
        }
    }

    private void ObserveVerifiedFixedWidth(int width)
    {
        if (width == _confirmedWidth)
        {
            _candidateWidth = -1;
            _candidateHits = 0;
            return;
        }

        var address = 0;
        for (var i = 0; i < width; i++) address |= _pending[2 + i] << (8 * i);
        if (width == _candidateWidth && address == _candidateAddress)
            _candidateHits++;
        else
        {
            _candidateWidth = width;
            _candidateAddress = address;
            _candidateHits = 1;
        }
        if (_candidateHits < 2) return;
        _confirmedWidth = width;
        _candidateWidth = -1;
        _candidateHits = 0;
    }

    private bool FixedChecksum(int width)
    {
        var sum = _pending[1];
        for (var j = 0; j < width; j++) sum += _pending[2 + j];
        return _pending[2 + width] == (byte)sum;
    }

    private void Emit(int length, bool valid, string format, Action<Ft12StreamCandidate> emit,
        int? detectedWidth = null)
    {
        var raw = new byte[length];
        Array.Copy(_pending, raw, length);
        emit(new Ft12StreamCandidate(_startOffset, format, valid, raw, detectedWidth));
        if (_count > length)
            Array.Copy(_pending, length, _pending, 0, _count - length);
        _startOffset += length;
        _count -= length;
    }

    private void ShiftOne(Action<Ft12StreamCandidate> emit)
    {
        _noiseBytes++;
        if (_noiseCount == 0) _noiseStart = _startOffset;
        _noise[_noiseCount++] = _pending[0];
        if (_noiseCount == _noise.Length) FlushNoise(emit);
        if (--_count > 0) Array.Copy(_pending, 1, _pending, 0, _count);
        _startOffset++;
    }

    private void FlushNoise(Action<Ft12StreamCandidate> emit)
    {
        if (_noiseCount == 0) return;
        var raw = new byte[_noiseCount];
        Array.Copy(_noise, raw, _noiseCount);
        emit(new Ft12StreamCandidate(_noiseStart, "Noise", false, raw));
        _noiseCount = 0;
    }

}

/// <summary>A frame candidate, not necessarily checksum-valid; serial direction is supplied by its source.</summary>

/// <summary>Bounded source-qualified candidate; failed checksum never implies trusted application data.</summary>
public sealed record Ft12StreamCandidate(long ByteOffset, string Format,
    bool ChecksumValid, byte[] RawBytes, int? DetectedLinkAddressWidth = null);
