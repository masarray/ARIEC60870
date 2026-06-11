// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Model;

public sealed class LinkControlInfo
{
    public byte Raw { get; init; }
    public bool Prm { get; init; }
    public int FunctionCode { get; init; }
    public string FunctionName { get; init; } = "Unknown";

    // Primary/master side bits
    public bool? Fcb { get; init; }
    public bool? Fcv { get; init; }

    // Secondary/slave side bits
    public bool? Acd { get; init; }
    public bool? Dfc { get; init; }

    public bool IsPrimaryRequestClass1 => Prm && FunctionCode == 10;
    public bool IsPrimaryRequestClass2 => Prm && FunctionCode == 11;
    public bool IsPrimaryResetFcb => Prm && FunctionCode == 7;
    public bool IsSecondaryNoData => !Prm && FunctionCode == 9;
    public bool IsSecondaryUserData => !Prm && FunctionCode == 8;
    public bool IsSecondaryAck => !Prm && FunctionCode == 0;
    public bool IsSecondaryNack => !Prm && FunctionCode == 1;

    public string BitSummary
    {
        get
        {
            if (Prm)
            {
                return $"PRM=1, FCB={(Fcb == true ? 1 : 0)}, FCV={(Fcv == true ? 1 : 0)}, FC={FunctionCode}";
            }

            return $"PRM=0, ACD={(Acd == true ? 1 : 0)}, DFC={(Dfc == true ? 1 : 0)}, FC={FunctionCode}";
        }
    }
}
