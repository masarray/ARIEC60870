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

    public PassiveFt12Scanner(int linkAddressWidth = 1)
    {
        if (linkAddressWidth is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(linkAddressWidth));
        _addressWidth = linkAddressWidth;
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
    }

    private void Process(Action<PassiveFt12Frame> emit)
    {
        while (_count > 0)
        {
            var first = _pending[0];
            if (first is 0xE5 or 0xA2)
            {
                Emit(1, first == 0xE5, "Single", emit);
                continue;
            }
            if (first != 0x10 && first != 0x68)
            {
                ShiftOne();
                continue;
            }
            if (first == 0x10)
            {
                var count = 4 + _addressWidth;
                if (_count < count) return;
                var endOk = _pending[count - 1] == 0x16;
                if (!endOk) { ShiftOne(); continue; }
                var checksum = _pending[2 + _addressWidth];
                var sum = _pending[1];
                for (var j = 0; j < _addressWidth; j++) sum += _pending[2 + j];
                Emit(count, checksum == (byte)sum, "Fixed", emit);
                continue;
            }

            if (_count < 4) return;
            if (_pending[1] != _pending[2] || _pending[3] != 0x68 ||
                _pending[1] < 1 + _addressWidth)
            {
                ShiftOne();
                continue;
            }
            var size = 6 + _pending[1];
            if (size > MaxFrameBytes) { ShiftOne(); continue; }
            if (_count < size) return;
            if (_pending[size - 1] != 0x16) { ShiftOne(); continue; }
            var calc = 0;
            for (var j = 4; j < 4 + _pending[1]; j++) calc += _pending[j];
            Emit(size, _pending[size - 2] == (byte)calc, "Variable", emit);
        }
    }

    private void Emit(int length, bool valid, string format, Action<PassiveFt12Frame> emit)
    {
        var raw = new byte[length];
        Array.Copy(_pending, raw, length);
        emit(new PassiveFt12Frame(_startOffset, format, valid, raw));
        if (_count > length)
            Array.Copy(_pending, length, _pending, 0, _count - length);
        _startOffset += length;
        _count -= length;
    }

    private void ShiftOne()
    {
        _noiseBytes++;
        if (--_count > 0) Array.Copy(_pending, 1, _pending, 0, _count);
        _startOffset++;
    }
}

/// <summary>A frame candidate, not necessarily checksum-valid; serial direction is supplied by its source.</summary>
public sealed record PassiveFt12Frame(long ByteOffset, string Format, bool ChecksumValid, byte[] RawBytes);

public sealed record PassiveObservedFrame(
    byte StreamId, long StreamOffset, DateTime EstimatedUtc, string Format,
    bool ChecksumValid, byte[] RawBytes);
