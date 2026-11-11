// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Monitor;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// One deterministic operator interpretation of existing, source-owned wire
/// evidence. Framing precedes ASDU interpretation; neither stage can erase a
/// received frame. This is a pure presentation decision, NOT another decoder.
/// </summary>
public static class PassiveWirePresentation
{
    public enum Kind
    {
        Decoded,
        LinkOnly,
        UnknownAsdu,
        VendorSpecific,
        InvalidFrame,
        Partial,
        Noise,
        LinkWidthMismatch
    }

    public sealed record Verdict(Kind Status, string Message, bool TrustedWire,
        bool TrustedProcess)
    {
        public bool NeedsAttention => Status is not (Kind.Decoded or Kind.LinkOnly or Kind.VendorSpecific);
    }

    public static Verdict Classify(PassiveObservedFrame frame,
        int configuredLinkWidth, Iec10xAsduDecode? asdu)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.RawBytes.Length == 0)
            return new(Kind.Noise, "Unknown serial data · no frame bytes supplied", false, false);

        if (frame.Format == "Noise")
            return new(Kind.Noise,
                "Unrecognized serial bytes (Unknown) · original HEX preserved; no IEC-101 meaning inferred",
                false, false);

        if (frame.Format == "Partial")
            return new(Kind.Partial,
                "Partial FT1.2 candidate · received bytes preserved; telegram incomplete",
                false, false);

        if (frame.Format == "LinkWidthMismatch")
            return new(Kind.LinkWidthMismatch,
                $"IEC-101 Link Address width mismatch · observed {frame.DetectedLinkAddressWidth?.ToString() ?? "unknown"} octet(s), setup {configuredLinkWidth}; HEX retained",
                false, false);

        if (frame.Format is not ("Fixed" or "Variable" or "Single") ||
            !frame.ChecksumValid)
            return new(Kind.InvalidFrame,
                "Invalid FT1.2 candidate · bad framing, checksum or end delimiter; HEX retained",
                false, false);

        if (frame.Format == "Single" && frame.RawBytes[0] == 0xA2)
            return new(Kind.InvalidFrame,
                "Unsupported IEC-101 A2 single-character response · original HEX retained",
                false, false);

        var width = frame.DetectedLinkAddressWidth ?? configuredLinkWidth;
        if (!Ft12WireLayout.TryParse(frame.RawBytes, width, out var layout))
            return new(Kind.InvalidFrame,
                "Invalid FT1.2 wire layout · cannot trust a byte-derived process value",
                false, false);

        if (layout.Kind != Ft12WireKind.Variable)
            return new(Kind.LinkOnly,
                layout.Kind == Ft12WireKind.Single
                    ? "Recognized IEC-101 single-character link response"
                    : "Recognized IEC-101 fixed link frame",
                true, false);

        if (asdu is null)
            return new(Kind.UnknownAsdu,
                "Unknown IEC-101 ASDU · verified link frame, application payload not decoded; HEX retained",
                true, false);

        if (asdu.IsOpaqueVendorSpecific)
            return new(Kind.VendorSpecific,
                $"Vendor-specific IEC-101 TI {asdu.TypeId} · raw ASDU CA 0x{asdu.CommonAddress:X4}; " +
                "payload opaque, no standard process IOA decoded; original HEX retained",
                true, false);

        if (asdu.Issues.Count != 0 || asdu.ObjectCount == 0 ||
            asdu.Objects.Count != asdu.ObjectCount ||
            asdu.Objects.Any(x => x.Issues.Count != 0))
        {
            var first = asdu.Issues.FirstOrDefault() ??
                asdu.Objects.SelectMany(x => x.Issues).FirstOrDefault() ??
                "ASDU object cardinality or structure incomplete";
            return new(Kind.UnknownAsdu,
                $"Unknown/invalid IEC-101 ASDU (Type {asdu.TypeId}) · {first}; HEX retained",
                true, false);
        }

        return new(Kind.Decoded, asdu.ShortMeaning, true, true);
    }
}
