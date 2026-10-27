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
    public static PassiveJournalAudit Verify(string sessionDirectory)
    {
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
        if (manifest?.Streams.Length != 2)
            problems.Add("Missing or invalid two-direction stream declaration.");

        var audits = new PassiveStreamAudit[2];
        for (byte id = 0; id < 2; id++)
        {
            var file = Path.Combine(sessionDirectory, $"stream-{id:00}.bin");
            audits[id] = VerifyStream(file, id);
            if (!audits[id].Valid) problems.Add($"Stream {id}: {audits[id].Problem}");
            var declared = manifest?.Streams.FirstOrDefault(x => x.StreamId == id);
            if (declared is null) { problems.Add($"Stream {id}: missing manifest entry"); continue; }
            if (declared.Bytes != audits[id].RawBytes || declared.Chunks != audits[id].Records)
                problems.Add($"Stream {id}: count differs from manifest");
            if (!string.IsNullOrEmpty(declared.RawSha256) &&
                !string.Equals(declared.RawSha256, audits[id].Sha256, StringComparison.OrdinalIgnoreCase))
                problems.Add($"Stream {id}: SHA256 differs from manifest");
            if (declared.DriverErrors != 0)
                problems.Add($"Stream {id}: UART driver errors detected");
        }

        return new PassiveJournalAudit(problems.Count == 0, manifest?.Status ?? "Unknown",
            audits, problems.ToArray());
    }

    public static PassiveStreamAudit VerifyStream(string filePath, byte streamId)
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
