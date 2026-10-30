// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Immutable, strict query over verified portable capture frames. Never infer
/// unknown field values from a display string or treat a missing byte profile
/// as a valid match. The same typed archive decoder handles multi-object IOAs.
/// </summary>
public sealed record CaptureArchiveQuery(
    int? Ioa = null, int? Ca = null, int? TypeId = null, int? Cot = null,
    string? Source = null, string? Direction = null, string? Hex = null,
    string? Time = null, bool? Negative = null, bool? Test = null)
{
    public static CaptureArchiveQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new CaptureArchiveQuery();
        if (text.Length > 512) throw new FormatException("Capture search is limited to 512 characters.");
        int? ioa = null, ca = null, ti = null, cot = null;
        string? source = null, direction = null, hex = null, time = null;
        bool? negative = null, test = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = token.IndexOf(':');
            if (colon <= 0 || colon == token.Length - 1)
                throw new FormatException("Use key:value filters, e.g. ioa:123 cot:7 source:A.");
            var key = token[..colon].ToLowerInvariant();
            var value = token[(colon + 1)..];
            if (!seen.Add(key)) throw new FormatException("Repeated capture filter: " + key);
            static int Number(string value, int max)
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                    parsed < 0 || parsed > max)
                    throw new FormatException("Invalid numeric capture filter: " + value);
                return parsed;
            }
            static bool Flag(string value) => value.ToLowerInvariant() switch
            {
                "1" or "true" or "yes" => true,
                "0" or "false" or "no" => false,
                _ => throw new FormatException("Flag must be 0/1 or true/false.")
            };
            switch (key)
            {
                case "ioa": ioa = Number(value, 0xFFFFFF); break;
                case "ca": ca = Number(value, 0xFFFF); break;
                case "ti": case "type": ti = Number(value, 255); break;
                case "cot": cot = Number(value, 63); break;
                case "source":
                    if (value.Length > 32) throw new FormatException("Source filter is too long.");
                    source = value; break;
                case "direction":
                    if (value is not ("RX" or "TX" or "rx" or "tx"))
                        throw new FormatException("Direction must be TX or RX.");
                    direction = value; break;
                case "hex":
                    hex = value.Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
                    if (hex.Length > 128 || hex.Any(c => !Uri.IsHexDigit(c)))
                        throw new FormatException("HEX filter must contain only hexadecimal characters (max 128).");
                    break;
                case "time":
                    if (value.Length > 40) throw new FormatException("Time filter is too long.");
                    time = value; break;
                case "neg": negative = Flag(value); break;
                case "test": test = Flag(value); break;
                default: throw new FormatException("Unknown capture filter: " + key);
            }
        }
        return new CaptureArchiveQuery(ioa, ca, ti, cot, source, direction, hex, time, negative, test);
    }

    /// <summary>
    /// Numeric IEC protocol filters are evaluated only against the original
    /// checksum-checked bytes and recorded ASDU field widths. Snapshot strings
    /// are display metadata, never authoritative protocol evidence.
    /// </summary>
    public bool Matches(CaptureFrameSnapshot frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (Source is not null &&
            !string.Equals(frame.PhysicalSource, Source, StringComparison.OrdinalIgnoreCase) &&
            !frame.PhysicalSource.EndsWith(" " + Source, StringComparison.OrdinalIgnoreCase))
            return false;
        if (Direction is not null &&
            !string.Equals(frame.Direction, Direction, StringComparison.OrdinalIgnoreCase)) return false;
        if (Time is not null && !frame.Time.Contains(Time, StringComparison.OrdinalIgnoreCase)) return false;
        if (Hex is not null &&
            !frame.RawHex.Replace(" ", "", StringComparison.Ordinal)
                .Contains(Hex, StringComparison.OrdinalIgnoreCase)) return false;

        // Text-only source/hex/time searches preserve malformed/legacy raw
        // evidence. They never infer a valid ASDU from a stored display label.
        if (!Ioa.HasValue && !Ca.HasValue && !TypeId.HasValue && !Cot.HasValue &&
            !Negative.HasValue && !Test.HasValue) return true;

        if (!TryDecode(frame, out var decoded) || decoded?.Asdu is not { } asdu ||
            !decoded.IsIntegrityValid) return false;
        if (TypeId is int type && asdu.TypeId != type) return false;
        if (Cot is int cause && asdu.CauseDetails?.Code != cause) return false;
        if (Ca is int common && asdu.CommonAddress != common) return false;
        if (Ioa is int address &&
            !asdu.Objects.Any(o => o.InformationObjectAddress == address)) return false;
        if (Negative is bool negative && asdu.CauseDetails?.Negative != negative) return false;
        if (Test is bool test && asdu.CauseDetails?.Test != test) return false;
        return true;
    }

    private static bool TryDecode(CaptureFrameSnapshot frame, out Iec10xArchivedDecode? decoded)
    {
        decoded = null;
        Iec60870ProtocolMode? protocol = frame.ProtocolMode switch
        {
            "101" => Iec60870ProtocolMode.Iec101,
            "104" => Iec60870ProtocolMode.Iec104,
            _ => null
        };
        if (protocol is null || string.IsNullOrWhiteSpace(frame.RawHex)) return false;
        try
        {
            var raw = Convert.FromHexString(frame.RawHex.Replace(" ", "", StringComparison.Ordinal));
            decoded = Iec10xArchivedFrameDecoder.Decode(protocol.Value, raw,
                frame.LinkWidth, frame.CotWidth, frame.CaWidth, frame.IoaWidth);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
