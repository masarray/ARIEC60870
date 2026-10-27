// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;

namespace ARIEC60870.Monitor;

/// <summary>
/// Fixed 40-byte header + payload. LE fields:
/// 0 magic APM1; 4 version; 5 stream; 6 flags; 8 seq(i64); 16 ticks(i64);
/// 24 source byte offset(i64); 32 length(i32); 36 CRC32 over header[0..36)+payload.
/// </summary>
public static class PassiveJournalFormat
{
    public const int HeaderSize = 40;
    public const int MaxPayload = 16384;

    public static void Encode(Span<byte> record, byte stream, long sequence, long ticks, long byteOffset, int payloadLength)
    {
        if (payloadLength <= 0 || payloadLength > MaxPayload || record.Length < HeaderSize + payloadLength)
            throw new ArgumentOutOfRangeException(nameof(payloadLength));
        record[0] = (byte)'A'; record[1] = (byte)'P'; record[2] = (byte)'M'; record[3] = (byte)'1';
        record[4] = 1; record[5] = stream;
        record[6] = record[7] = 0;
        BinaryPrimitives.WriteInt64LittleEndian(record.Slice(8, 8), sequence);
        BinaryPrimitives.WriteInt64LittleEndian(record.Slice(16, 8), ticks);
        BinaryPrimitives.WriteInt64LittleEndian(record.Slice(24, 8), byteOffset);
        BinaryPrimitives.WriteInt32LittleEndian(record.Slice(32, 4), payloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(36, 4),
            Crc32.Compute(record[..36], record.Slice(HeaderSize, payloadLength)));
    }

    public static bool TryDecode(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, byte stream,
        long expectedSequence, long expectedOffset, out string error)
    {
        error = "";
        if (header.Length != HeaderSize || header[0] != 'A' || header[1] != 'P' ||
            header[2] != 'M' || header[3] != '1' || header[4] != 1 || header[5] != stream)
            { error = "Record header/version/source mismatch"; return false; }
        if (header[6] != 0 || header[7] != 0 ||
            BinaryPrimitives.ReadInt64LittleEndian(header.Slice(8, 8)) != expectedSequence ||
            BinaryPrimitives.ReadInt64LittleEndian(header.Slice(16, 8)) <= 0 ||
            BinaryPrimitives.ReadInt64LittleEndian(header.Slice(24, 8)) != expectedOffset)
            { error = "Header flags, timestamp, sequence or offset mismatch"; return false; }
        var n = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(32, 4));
        if (n <= 0 || n > MaxPayload || n != payload.Length)
            { error = "Invalid record payload length"; return false; }
        if (BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(36, 4)) != Crc32.Compute(header[..36], payload))
            { error = "CRC32 mismatch"; return false; }
        return true;
    }

    private static class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        public static uint Compute(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload)
        {
            var value = 0xffffffffu;
            foreach (var b in header) value = Table[(int)((value ^ b) & 0xff)] ^ (value >> 8);
            foreach (var b in payload) value = Table[(int)((value ^ b) & 0xff)] ^ (value >> 8);
            return ~value;
        }

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                var v = i;
                for (var bit = 0; bit < 8; bit++)
                    v = (v & 1) != 0 ? (v >> 1) ^ 0xedb88320u : v >> 1;
                table[(int)i] = v;
            }
            return table;
        }
    }
}
