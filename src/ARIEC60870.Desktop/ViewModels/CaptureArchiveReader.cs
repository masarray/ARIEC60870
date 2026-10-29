// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Single-pass, bounded reader for selected .ariec ZIP evidence. Hashes the
/// uncompressed original bytes before exposing any rows to the UI.
/// Keeps only the latest preview rows; the original archive is untouched.
/// </summary>
public static class CaptureArchiveReader
{
    public const int DefaultPreviewLimit = 600;
    private const long MaxFramesBytes = 1024L * 1024 * 1024;
    private const int MaxLineBytes = 128 * 1024;
    private const int MaxManifestBytes = 128 * 1024;
    private const long MaxFrameCount = 4_000_000;

    public sealed record Result(IReadOnlyList<CaptureFrameSnapshot> Rows,
        long TotalRows, bool IntegrityVerified, string TrustDescription);

    public static Result Read(string fileName, int previewLimit = DefaultPreviewLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (previewLimit is < 1 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(previewLimit));

        using var archive = ZipFile.OpenRead(fileName);
        var frames = UniqueEntry(archive, "frames.jsonl")
            ?? throw new InvalidDataException("No frames.jsonl in this capture.");
        ValidateSize(frames, MaxFramesBytes);
        var manifest = UniqueEntry(archive, "manifest.json");
        string? requiredHash = null;
        int? manifestCount = null;
        if (manifest is not null)
        {
            ValidateSize(manifest, MaxManifestBytes);
            using var source = manifest.Open();
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var n = source.Read(buffer);
                if (n == 0) break;
                if (memory.Length + n > MaxManifestBytes)
                    throw new InvalidDataException("Capture manifest exceeds safety limit.");
                memory.Write(buffer, 0, n);
            }

            using var json = JsonDocument.Parse(memory.ToArray());
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid capture manifest.");
            if (root.TryGetProperty("FramesSha256", out var hashElement))
                requiredHash = hashElement.GetString();
            if (root.TryGetProperty("RowCount", out var countElement)
                && countElement.TryGetInt32(out var declared) && declared >= 0)
                manifestCount = declared;
            if (!string.IsNullOrWhiteSpace(requiredHash) &&
                (requiredHash.Length != 64 || !requiredHash.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Invalid capture SHA-256 manifest value.");
        }

        var retained = new Queue<CaptureFrameSnapshot>(previewLimit);
        var pending = new ArrayBufferWriter<byte>(4096);
        var rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long bytesSeen = 0;
        long rowsSeen = 0;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            using var source = frames.Open();
            int read;
            while ((read = source.Read(rented)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bytesSeen += read;
                if (bytesSeen > MaxFramesBytes)
                    throw new InvalidDataException("Capture frames exceed the 1 GiB import safety limit.");
                sha.AppendData(rented, 0, read);
                var remaining = rented.AsSpan(0, read);
                while (!remaining.IsEmpty)
                {
                    var newline = remaining.IndexOf((byte)'\n');
                    var piece = newline < 0 ? remaining : remaining[..newline];
                    if (pending.WrittenCount + piece.Length > MaxLineBytes)
                        throw new InvalidDataException("Capture contains an oversized JSONL frame row.");
                    pending.Write(piece);
                    if (newline < 0) break;
                    AcceptLine(pending.WrittenSpan);
                    pending.Clear();
                    remaining = remaining[(newline + 1)..];
                }
            }
            if (pending.WrittenCount > 0) AcceptLine(pending.WrittenSpan);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        var computed = Convert.ToHexString(sha.GetHashAndReset());
        if (!string.IsNullOrWhiteSpace(requiredHash) &&
            !string.Equals(requiredHash, computed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Capture SHA-256 mismatch. Original frames were altered or corrupted.");
        if (manifestCount.HasValue && !string.IsNullOrWhiteSpace(requiredHash)
            && manifestCount.Value != rowsSeen)
            throw new InvalidDataException("Capture row count does not match the signed manifest metadata.");
        return new Result(retained.OrderBy(x => x.Sequence).ToArray(), rowsSeen,
            !string.IsNullOrWhiteSpace(requiredHash),
            string.IsNullOrWhiteSpace(requiredHash) ? "LEGACY · hash not available" : "SHA-256 VERIFIED");

        void AcceptLine(ReadOnlySpan<byte> raw)
        {
            if (rowsSeen >= MaxFrameCount)
                throw new InvalidDataException("Capture contains too many JSONL rows.");
            // Old ZIP writers may add UTF-8 BOM on their first record.
            if (rowsSeen == 0 && raw.Length >= 3 &&
                raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
                raw = raw[3..];
            if (raw.IsEmpty || (raw.Length == 1 && raw[0] == '\r')) return;
            CaptureFrameSnapshot snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<CaptureFrameSnapshot>(raw)
                    ?? throw new JsonException("Frame object is null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Malformed JSONL frame at record " +
                    (rowsSeen + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".", ex);
            }
            rowsSeen++;
            if (retained.Count == previewLimit) retained.Dequeue();
            retained.Enqueue(snapshot);
        }
    }

    private static ZipArchiveEntry? UniqueEntry(ZipArchive zip, string name)
    {
        var matches = zip.Entries.Where(e =>
            string.Equals(e.FullName, name, StringComparison.Ordinal)).Take(2).ToArray();
        if (matches.Length > 1)
            throw new InvalidDataException("Duplicate ZIP evidence entry: " + name);
        return matches.FirstOrDefault();
    }

    private static void ValidateSize(ZipArchiveEntry entry, long limit)
    {
        if (entry.Length < 0 || entry.Length > limit)
            throw new InvalidDataException("ZIP evidence entry exceeds safety limit: " + entry.Name);
        if (entry.Length > 0 && (entry.CompressedLength <= 0 ||
            entry.Length / (double)entry.CompressedLength > 1500d))
            throw new InvalidDataException("ZIP entry compression ratio is unsafe: " + entry.Name);
    }
}
