// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Monitor;

/// <summary>
/// Unbalanced IEC-101 logical sender role, independent of the physical COM
/// connector on which a wire frame was actually received.
/// </summary>
public enum PassiveWireRole { Unknown, Primary, Secondary }

public static class PassiveIec101FrameRoles
{
    /// <summary>
    /// Only well-formed, checksum-confirmed FT1.2 frames can establish roles.
    /// PRM=1 means primary (Master), PRM=0 means secondary (RTU);
    /// E5 is the addressless secondary short response.
    /// Does not fabricate direction for garbage, A2, partial frames or
    /// an unsupported link width.
    /// </summary>
    public static PassiveWireRole Classify(PassiveObservedFrame frame, int configuredLinkWidth)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!frame.ChecksumValid) return PassiveWireRole.Unknown;
        var raw = frame.RawBytes;
        if (frame.Format == "Single")
            return raw.Length == 1 && raw[0] == 0xE5
                ? PassiveWireRole.Secondary : PassiveWireRole.Unknown;

        var width = frame.DetectedLinkAddressWidth ?? configuredLinkWidth;
        if (width is < 0 or > 2) return PassiveWireRole.Unknown;
        int control;
        int checksum;
        if (frame.Format is "Fixed" or "LinkWidthMismatch")
        {
            if (raw.Length != 4 + width || raw[0] != 0x10 || raw[^1] != 0x16)
                return PassiveWireRole.Unknown;
            control = raw[1];
            checksum = 0;
            for (var i = 1; i < 2 + width; i++) checksum += raw[i];
            if ((checksum & 0xff) != raw[2 + width]) return PassiveWireRole.Unknown;
        }
        else if (frame.Format == "Variable")
        {
            if (raw.Length < 7 || raw[0] != 0x68 || raw[1] != raw[2] ||
                raw[3] != 0x68 || raw[^1] != 0x16 ||
                raw.Length != raw[1] + 6 || raw[1] < 1 + width)
                return PassiveWireRole.Unknown;
            control = raw[4];
            checksum = 0;
            for (var i = 4; i < raw.Length - 2; i++) checksum += raw[i];
            if ((checksum & 0xff) != raw[^2]) return PassiveWireRole.Unknown;
        }
        else return PassiveWireRole.Unknown;
        return (control & 0x40) != 0 ? PassiveWireRole.Primary : PassiveWireRole.Secondary;
    }
}

/// <summary>
/// Tiny, single-consumer status tracker. It never swaps physical COM handles,
/// rewrites source IDs or infers direction from a malformed byte stream.
/// An unbalanced port pair is considered reversed only after complementary
/// protocol evidence from both connectors. If one connector is absent, valid
/// frame direction still works but overall wiring status remains provisional.
/// </summary>
public sealed class PassiveIec101PortRoles
{
    private readonly int[,] _counts = new int[4, 2];

    public void Reset() => Array.Clear(_counts);

    public PassiveWireRole Observe(PassiveObservedFrame frame, int width)
    {
        var role = PassiveIec101FrameRoles.Classify(frame, width);
        if (frame.StreamId > 3 || role == PassiveWireRole.Unknown) return role;
        var column = role == PassiveWireRole.Primary ? 0 : 1;
        if (_counts[frame.StreamId, column] < 1000)
            _counts[frame.StreamId, column]++;
        return role;
    }

    public PassiveWireRole KnownRole(byte stream)
    {
        if (stream > 3) return PassiveWireRole.Unknown;
        var primary = _counts[stream, 0];
        var secondary = _counts[stream, 1];
        if (primary > 0 && secondary == 0) return PassiveWireRole.Primary;
        if (secondary > 0 && primary == 0) return PassiveWireRole.Secondary;
        return PassiveWireRole.Unknown;
    }

    public string KnownDirectionLabel(byte stream) => KnownRole(stream) switch
    {
        PassiveWireRole.Primary => "SCADA → RTU · auto",
        PassiveWireRole.Secondary => "RTU → SCADA · auto",
        _ => "physical RX · direction pending"
    };

    public string DescribeAll()
    {
        var a = DescribePair(0, "A");
        var b = DescribePair(2, "B");
        return b.Length == 0 ? a : a + " · " + b;
    }

    private string DescribePair(int start, string label)
    {
        var first = KnownRole((byte)start);
        var second = KnownRole((byte)(start + 1));
        if (first == PassiveWireRole.Unknown && second == PassiveWireRole.Unknown)
        {
            var seen = _counts[start, 0] + _counts[start, 1] +
                       _counts[start + 1, 0] + _counts[start + 1, 1];
            return seen > 0 ? $"{label} mixed/echo · inspect wiring" : "";
        }
        if (first == PassiveWireRole.Secondary && second == PassiveWireRole.Primary)
            return $"{label} COM roles auto-reversed";
        if (first == PassiveWireRole.Primary && second == PassiveWireRole.Secondary)
            return $"{label} COM roles confirmed";
        if (first != PassiveWireRole.Unknown && second == first)
            return $"{label} duplicate direction · inspect taps";
        return $"{label} auto direction (partial capture)";
    }
}
