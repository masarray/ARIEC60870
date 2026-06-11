// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol;

public static class Iec103AsduBuilder
{
    public static byte[] GeneralInterrogation(int commonAddress, byte scanQualifier = 0)
    {
        // Type 7, VSQ 1, COT 9, CA, FUN=255, INF=0, scan qualifier.
        return new[] { (byte)0x07, (byte)0x81, (byte)0x09, CaByte(commonAddress), (byte)0xFF, (byte)0x00, scanQualifier };
    }

    public static byte[] ClockSynchronization(int commonAddress, DateTime localTime)
    {
        var time = EncodeCp32Time2a(localTime);
        return new[]
        {
            (byte)0x06, (byte)0x81, (byte)0x08, CaByte(commonAddress), (byte)0xFF, (byte)0x00,
            time[0], time[1], time[2], time[3], time[4]
        };
    }


    public static byte[] GeneralCommand(int commonAddress, byte functionType, byte informationNumber, byte commandQualifier = 1)
    {
        // Type 20 is used by ARIEC60870 as a generic IEC-103 command envelope.
        // Project/relay-specific command semantics must be validated by the user mapping/profile.
        return new[] { (byte)0x14, (byte)0x81, (byte)0x14, CaByte(commonAddress), functionType, informationNumber, commandQualifier };
    }

    public static byte[] ProtectionResetCommand(int commonAddress, byte functionType = 255, byte informationNumber = 19)
    {
        return GeneralCommand(commonAddress, functionType, informationNumber, commandQualifier: 1);
    }

    private static byte CaByte(int commonAddress)
    {
        if (commonAddress < 0 || commonAddress > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(commonAddress), "IEC-103 common address must fit in one octet.");
        }

        return (byte)commonAddress;
    }

    public static byte[] EncodeCp32Time2a(DateTime localTime)
    {
        var t = localTime.Kind == DateTimeKind.Utc ? localTime.ToLocalTime() : localTime;
        var milliseconds = checked((ushort)((t.Second * 1000) + t.Millisecond));

        return new[]
        {
            (byte)(milliseconds & 0xFF),
            (byte)((milliseconds >> 8) & 0xFF),
            (byte)(t.Minute & 0x3F),
            (byte)(t.Hour & 0x1F),
            (byte)0x00
        };
    }
}
