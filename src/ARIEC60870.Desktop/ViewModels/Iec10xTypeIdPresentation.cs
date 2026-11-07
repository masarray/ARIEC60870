// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>Shared read-only IEC-101/104 type identity. Raw numeric TI is authoritative.</summary>
public static class Iec10xTypeIdPresentation
{
    public static string Tooltip(int? typeId)
    {
        if (typeId is null) return "Type ID unavailable";
        var name = typeId.Value switch
        {
            1 => "Single-Point Information (M_SP_NA_1)",
            3 => "Double-Point Information (M_DP_NA_1)",
            5 => "Step-Position Information (M_ST_NA_1)",
            7 => "Bitstring of 32 Bits (M_BO_NA_1)",
            9 => "Normalized Measured Value (M_ME_NA_1)",
            11 => "Scaled Measured Value (M_ME_NB_1)",
            13 => "Short Floating-Point Measured Value (M_ME_NC_1)",
            15 => "Integrated Totals (M_IT_NA_1)",
            30 => "Single-Point Information with CP56Time2a (M_SP_TB_1)",
            31 => "Double-Point Information with CP56Time2a (M_DP_TB_1)",
            32 => "Step-Position Information with CP56Time2a (M_ST_TB_1)",
            33 => "Bitstring with CP56Time2a (M_BO_TB_1)",
            34 => "Normalized Measured Value with CP56Time2a (M_ME_TD_1)",
            35 => "Scaled Measured Value with CP56Time2a (M_ME_TE_1)",
            36 => "Short Floating-Point Measured Value with CP56Time2a (M_ME_TF_1)",
            37 => "Integrated Totals with CP56Time2a (M_IT_TB_1)",
            45 => "Single Command (C_SC_NA_1)",
            46 => "Double Command (C_DC_NA_1)",
            47 => "Regulating Step Command (C_RC_NA_1)",
            100 => "General Interrogation (C_IC_NA_1)",
            101 => "Counter Interrogation (C_CI_NA_1)",
            _ => "Unknown or reserved Type ID"
        };
        return $"TI{typeId.Value} — {name}";
    }
}
