// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Small, presentational IEC-101/104 digital-state chip. No protocol decoding,
/// device semantics or quality inference belongs in this visual projection.
/// Unrecognized, analog and IEC-103 values retain their exact visible text.
/// </summary>
public sealed record DigitalStateBadge(string Type, string State)
{
    public static DigitalStateBadge? FromDecoded(string protocol, string? typeId, string? value)
    {
        if (protocol is not ("101" or "104") || string.IsNullOrWhiteSpace(value))
            return null;

        var type = typeId switch
        {
            "1" or "2" or "30" => "SP",
            "3" or "4" or "31" => "DP",
            _ => null
        };
        if (type is null) return null;

        var state = value.Trim().ToUpperInvariant();
        if (state.StartsWith("SINGLE POINT ", StringComparison.Ordinal))
        {
            if (type != "SP") return null;
            state = state["SINGLE POINT ".Length..].Trim();
        }
        else if (state.StartsWith("DOUBLE POINT ", StringComparison.Ordinal))
        {
            if (type != "DP") return null;
            state = state["DOUBLE POINT ".Length..].Trim();
        }
        else if (state.StartsWith("SP=", StringComparison.Ordinal))
        {
            if (type != "SP") return null;
            state = state[3..].Trim();
        }
        else if (state.StartsWith("DP=", StringComparison.Ordinal))
        {
            if (type != "DP") return null;
            state = state[3..].Trim();
        }

        state = state switch
        {
            "ON" or "CLOSED" => "ON",
            "OFF" or "OPEN" => "OFF",
            "0" when type == "SP" => "OFF",
            "1" when type == "SP" => "ON",
            // IEC double-point information: DPI=0 indeterminate/intermediate,
            // DPI=1 OFF, DPI=2 ON, DPI=3 indeterminate. Never treat DP=1 as ON.
            "0" when type == "DP" => "INTERMEDIATE",
            "1" when type == "DP" => "OFF",
            "2" when type == "DP" => "ON",
            "3" when type == "DP" => "INDETERMINATE",
            "TRANSIENT" or "INTERMEDIATE" => "INTERMEDIATE",
            "UNKNOWN" or "INDETERMINATE" => "INDETERMINATE",
            _ => ""
        };
        return state.Length == 0 ? null : new DigitalStateBadge(type, state);
    }
}
