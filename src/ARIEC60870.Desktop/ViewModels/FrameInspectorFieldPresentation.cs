// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>Selection-only children from decoded protocol facts; no inferred wire quality.</summary>
public static class FrameInspectorFieldPresentation
{
    public sealed record DetailRow(string Field, string Value);

    public static IReadOnlyList<DetailRow> Vsq(byte raw, bool sequence, int objects)
        => [new("Sequence (SQ)", sequence ? "True" : "False"),
            new("Number of objects", objects.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Raw VSQ", $"0x{raw:X2}")];

    public static IReadOnlyList<DetailRow> Ioa(int address)
    {
        if (address is < 0 or > 0xFFFFFF) return [];
        return [
            Byte("IOA1 (low)", address & 0xFF),
            Byte("IOA2 (mid)", (address >> 8) & 0xFF),
            Byte("IOA3 (high)", (address >> 16) & 0xFF)
        ];
    }

    private static DetailRow Byte(string name, int value) => new(name, $"{value} (0x{value:X2})");

    public static IReadOnlyList<DetailRow> Quality(Iec10xQualityDetails? quality)
    {
        if (quality is null) return []; // Unknown must NOT be displayed as False.
        var children = new List<DetailRow>(9) { new("Invalid (IV)", quality.Invalid.ToString()) };
        static void Add(List<DetailRow> target, string name, bool? bit)
        {
            if (bit.HasValue) target.Add(new(name, bit.Value.ToString()));
        }
        Add(children, "Overflow (OV)", quality.Overflow);
        Add(children, "Not topical (NT)", quality.NotTopical);
        Add(children, "Blocked (BL)", quality.Blocked);
        Add(children, "Substituted (SB)", quality.Substituted);
        Add(children, "Carry (CY)", quality.Carry);
        Add(children, "Adjusted (CA)", quality.Adjusted);
        if (quality.CounterSequence.HasValue)
            children.Add(new("Counter sequence", quality.CounterSequence.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));
        children.Add(new("Raw quality", $"0x{quality.Raw:X2}"));
        return children;
    }
}
