// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace ARIEC60870.Monitor;

public sealed record PassiveStreamAudit(
    byte StreamId, long Records, long RawBytes, string Sha256, bool Valid, string? Problem);

public sealed record PassiveJournalAudit(
    bool Complete, string Status, PassiveStreamAudit[] Streams, string[] Problems);

/// <summary>Offline verification is read-only, bounded and independent of WPF/active Master.</summary>
public static class PassiveJournalVerifier
{
    public static PassiveJournalAudit Verify(string sessionDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var problems = new List<string>();
        PassiveCaptureManifest? manifest = null;
        var manifestPath = Path.Combine(sessionDirectory, "manifest.json");
        if (File.Exists(manifestPath))
        {
            try { manifest = JsonSerializer.Deserialize<PassiveCaptureManifest>(File.ReadAllText(manifestPath)); }
            catch (Exception ex) when (ex is JsonException or IOException)
            { problems.Add("Unreadable manifest: " + ex.Message); }
        }
        else problems.Add("No finalized manifest; capture may have been interrupted.");

        if (manifest?.Status != "NoApplicationLossDetected")
            problems.Add("Capture is not finalized cleanly: " + (manifest?.Status ?? "Unknown"));
        var streamCount = manifest?.Streams.Length is 2 or 4
            ? manifest.Streams.Length : 2;
        if (manifest?.Streams.Length is not (2 or 4) ||
            manifest.Streams.Select(x => x.StreamId).Distinct().Count() != streamCount)
            problems.Add("Manifest requires exactly 2 or 4 unique, paired source declarations.");

        var audits = new PassiveStreamAudit[streamCount];
        for (byte id = 0; id < streamCount; id++)
        {
            var file = Path.Combine(sessionDirectory, $"stream-{id:00}.bin");
            cancellationToken.ThrowIfCancellationRequested();
            audits[id] = VerifyStream(file, id, cancellationToken);
            if (!audits[id].Valid) problems.Add($"Stream {id}: {audits[id].Problem}");
            var declared = manifest?.Streams.FirstOrDefault(x => x.StreamId == id);
            if (declared is null) { problems.Add($"Stream {id}: missing manifest entry"); continue; }
            if (declared.Bytes != audits[id].RawBytes || declared.Chunks != audits[id].Records)
                problems.Add($"Stream {id}: count differs from manifest");
            if (string.IsNullOrWhiteSpace(declared.RawSha256))
                problems.Add($"Stream {id}: SHA256 absent from finalized manifest");
            else if (!string.Equals(declared.RawSha256, audits[id].Sha256, StringComparison.OrdinalIgnoreCase))
                problems.Add($"Stream {id}: SHA256 differs from manifest");
            if (declared.BaudRate <= 0 || declared.DataBits == 0 ||
                string.IsNullOrWhiteSpace(declared.Parity) || string.IsNullOrWhiteSpace(declared.StopBits))
                problems.Add($"Stream {id}: serial configuration metadata missing");
            if (declared.DriverErrors != 0)
                problems.Add($"Stream {id}: UART driver errors detected");
        }

        return new PassiveJournalAudit(problems.Count == 0, manifest?.Status ?? "Unknown",
            audits, problems.ToArray());
    }

    public static PassiveStreamAudit VerifyStream(string filePath, byte streamId, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return new PassiveStreamAudit(streamId, 0, 0, "", false, "Journal file absent");
        long records = 0, rawBytes = 0;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 65536, options: FileOptions.SequentialScan);
        var header = new byte[PassiveJournalFormat.HeaderSize];
        var payload = ArrayPool<byte>.Shared.Rent(PassiveJournalFormat.MaxPayload);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var headRead = ReadUpTo(input, header.AsSpan());
                if (headRead == 0) break;
                if (headRead != header.Length)
                    return Error("Truncated record header");
                var n = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(32, 4));
                if (n <= 0 || n > PassiveJournalFormat.MaxPayload)
                    return Error("Out-of-range record length");
                if (ReadUpTo(input, payload.AsSpan(0, n)) != n)
                    return Error("Truncated record payload");
                if (!PassiveJournalFormat.TryDecode(header, payload.AsSpan(0, n), streamId,
                        records + 1, rawBytes, out var reason))
                    return Error(reason);
                sha.AppendData(payload, 0, n);
                records++; rawBytes += n;
            }
            return new PassiveStreamAudit(streamId, records, rawBytes,
                Convert.ToHexString(sha.GetHashAndReset()), true, null);
        }
        finally { ArrayPool<byte>.Shared.Return(payload); }

        PassiveStreamAudit Error(string reason) =>
            new(streamId, records, rawBytes, Convert.ToHexString(sha.GetHashAndReset()), false,
                $"{reason} after {records} valid records and {rawBytes} bytes");
    }

    private static int ReadUpTo(Stream stream, Span<byte> dst)
    {
        var count = 0;
        while (count < dst.Length)
        {
            var n = stream.Read(dst[count..]);
            if (n == 0) break;
            count += n;
        }
        return count;
    }
}
