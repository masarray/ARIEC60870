// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>
/// Exact decoded control-element identity shared by on-wire IEC-101 SBO admission
/// and the IEC-101/104 forensic casebook. The S/E bit is the only byte ignored:
/// value, QU/QL, CP56 time (if present), TI, CA, OA and IOA must all agree.
/// No opaque/vendor/invalid control can yield an affirmative correlation.
/// </summary>
public static class Iec10xCommandWireIdentity
{
    public static bool TryCreate(Iec10xAsduDecode? asdu, bool select, out string identity)
    {
        identity = string.Empty;
        if (asdu is not { IsHeaderValid: true, IsTest: false, ObjectCount: 1, Issues.Count: 0 } ||
            asdu.Objects.Count != 1 || asdu.Objects[0].Issues.Count != 0 ||
            asdu.CommandDetails?.Select != select || asdu.InformationObjectAddress is null)
            return false;

        var qualifierIndex = asdu.TypeId switch
        {
            45 or 46 or 47 or 58 or 59 or 60 => 0,
            48 or 49 or 61 or 62 => 2,
            50 or 63 => 4,
            _ => -1
        };
        var bytes = asdu.Objects[0].RawBytes;
        if (qualifierIndex < 0 || bytes.Count <= qualifierIndex) return false;

        var normalized = bytes.ToArray();
        normalized[qualifierIndex] &= 0x7F;
        identity = $"{asdu.TypeId}|{asdu.CommonAddress}|{asdu.OriginatorAddress}|" +
                   $"{asdu.InformationObjectAddress.Value}|{Convert.ToHexString(normalized)}";
        return true;
    }
}
