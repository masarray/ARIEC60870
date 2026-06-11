// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;
using ARIEC60870.Core.Model;

namespace ARIEC60870.Core.Parsing;

public sealed class HexTraceExtractor
{
    private static readonly Regex ComFrameRegex = new(
        @"^(?<ts>\d{2}:\d{2}:\d{2}\.\d{3}).*?(?<port>COM\d+)\s+(?<dir><-|->)\s+(?<label>[^\[]*?)\s*\[(?<hex>[0-9A-Fa-f|\s]+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BracketHexRegex = new(
        @"\[(?<hex>[0-9A-Fa-f|\s]+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyList<TraceLogEntry> Extract(string text)
    {
        var entries = new List<TraceLogEntry>();
        using var reader = new StringReader(text);
        string? line;
        var lineNumber = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            var entry = TryParseLine(line, lineNumber);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    public TraceLogEntry? TryParseLine(string line, int lineNumber)
    {
        var match = ComFrameRegex.Match(line);
        if (match.Success)
        {
            var raw = ParseHex(match.Groups["hex"].Value);
            if (raw.Count == 0) return null;

            var tsText = match.Groups["ts"].Value;
            TimeSpan? timestamp = TimeSpan.TryParseExact(tsText, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out var ts)
                ? ts
                : null;

            return new TraceLogEntry
            {
                LineNumber = lineNumber,
                SourceLine = line,
                Timestamp = timestamp,
                TimestampText = tsText,
                PortName = match.Groups["port"].Value,
                Direction = match.Groups["dir"].Value == "->" ? FrameDirection.MasterToSlave : FrameDirection.SlaveToMaster,
                Label = match.Groups["label"].Value.Trim(),
                RawBytes = raw
            };
        }

        // Fallback: useful for raw hex files that only contain [10|09|01|0A|16]
        var bracket = BracketHexRegex.Match(line);
        if (bracket.Success)
        {
            var raw = ParseHex(bracket.Groups["hex"].Value);
            if (raw.Count == 0) return null;

            return new TraceLogEntry
            {
                LineNumber = lineNumber,
                SourceLine = line,
                Label = "RAW",
                RawBytes = raw
            };
        }

        return null;
    }

    private static IReadOnlyList<byte> ParseHex(string rawHex)
    {
        var normalized = rawHex.Replace('|', ' ');
        var tokens = normalized.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new List<byte>(tokens.Length);

        foreach (var token in tokens)
        {
            var clean = token.Trim();
            if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) clean = clean[2..];
            if (!byte.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                return Array.Empty<byte>();
            }
            bytes.Add(value);
        }

        return bytes;
    }
}
