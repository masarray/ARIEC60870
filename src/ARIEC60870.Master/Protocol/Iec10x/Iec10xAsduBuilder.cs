// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Master.Protocol.Iec10x;

public static class Iec10xAsduBuilder
{
    public static byte[] GeneralInterrogation(Iec103MasterSettings settings, byte qualifier = 20, int cause = 6)
    {
        var bytes = Header(typeId: 100, vsq: 1, cause: cause, settings: settings, ioa: 0);
        bytes.Add(qualifier);
        return bytes.ToArray();
    }

    public static byte[] ClockSynchronization(Iec103MasterSettings settings, DateTime localTime, int cause = 6)
    {
        var bytes = Header(typeId: 103, vsq: 1, cause: cause, settings: settings, ioa: 0);
        bytes.AddRange(EncodeCp56Time2a(localTime));
        return bytes.ToArray();
    }

    public static byte[] ReadCommand(Iec103MasterSettings settings, int ioa, int cause = 5)
    {
        return Header(typeId: 102, vsq: 1, cause: cause, settings: settings, ioa: ioa).ToArray();
    }

    public static byte[] SinglePoint(Iec103MasterSettings settings, int ioa, bool value, int cause = 20)
    {
        var bytes = Header(typeId: 1, vsq: 1, cause: cause, settings: settings, ioa: ioa);
        bytes.Add(value ? (byte)1 : (byte)0);
        return bytes.ToArray();
    }

    public static byte[] DoublePoint(Iec103MasterSettings settings, int ioa, int dpi, int cause = 20)
    {
        var bytes = Header(typeId: 3, vsq: 1, cause: cause, settings: settings, ioa: ioa);
        bytes.Add((byte)(dpi & 0x03));
        return bytes.ToArray();
    }

    public static byte[] FloatMeasurement(Iec103MasterSettings settings, int ioa, float value, int cause = 20)
    {
        var bytes = Header(typeId: 13, vsq: 1, cause: cause, settings: settings, ioa: ioa);
        var data = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(data, value);
        bytes.AddRange(data);
        bytes.Add(0); // QDS
        return bytes.ToArray();
    }


    public static byte[] SingleCommand(Iec103MasterSettings settings, int ioa, bool value, bool select, int qualifier = 0, int cause = 6)
    {
        var bytes = Header(typeId: 45, vsq: 1, cause: cause, settings: settings, ioa: ioa);
        var sco = (value ? 0x01 : 0x00) | ((qualifier & 0x1F) << 2);
        if (select) sco |= 0x80;
        bytes.Add((byte)(sco & 0xFF));
        return bytes.ToArray();
    }

    public static byte[] DoubleCommand(Iec103MasterSettings settings, int ioa, int dcs, bool select, int qualifier = 0, int cause = 6)
    {
        var bytes = Header(typeId: 46, vsq: 1, cause: cause, settings: settings, ioa: ioa);
        var normalizedDcs = Math.Clamp(dcs, 0, 3) & 0x03;
        var dco = normalizedDcs | ((qualifier & 0x1F) << 2);
        if (select) dco |= 0x80;
        bytes.Add((byte)(dco & 0xFF));
        return bytes.ToArray();
    }



    public static byte[] RegulatingStepCommand(Iec103MasterSettings settings, int ioa, int rcs, bool select, int qualifier = 0, int cause = 6)
    {
        var bytes = Header(typeId: 47, vsq: 1, cause: cause, settings: settings, ioa: ioa);
        var normalizedRcs = Math.Clamp(rcs, 0, 3) & 0x03;
        var rco = normalizedRcs | ((qualifier & 0x1F) << 2);
        if (select) rco |= 0x80;
        bytes.Add((byte)(rco & 0xFF));
        return bytes.ToArray();
    }

    public static byte[] SetpointNormalizedCommand(Iec103MasterSettings settings, int ioa, double normalizedValue, bool select, int qualifier = 0, int cause = 6)
    {
        var bytes = Header(typeId: 48, vsq: 1, cause: cause, settings: settings, ioa: ioa);
        var bounded = Math.Clamp(normalizedValue, -1.0, 1.0);
        var raw = (short)Math.Round(bounded * 32767.0, MidpointRounding.AwayFromZero);
        var data = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(data, raw);
        bytes.AddRange(data);
        var qos = qualifier & 0x7F;
        if (select) qos |= 0x80;
        bytes.Add((byte)(qos & 0xFF));
        return bytes.ToArray();
    }

    public static byte[] ActivationConfirmation(Iec103MasterSettings settings, byte commandType, int ioa = 0)
    {
        var bytes = Header(typeId: commandType, vsq: 1, cause: 7, settings: settings, ioa: ioa);
        if (commandType == 100)
        {
            bytes.Add(20);
        }
        else if (commandType == 103)
        {
            bytes.AddRange(EncodeCp56Time2a(DateTime.Now));
        }
        return bytes.ToArray();
    }

    public static byte[] ActivationTermination(Iec103MasterSettings settings)
    {
        var bytes = Header(typeId: 100, vsq: 1, cause: 10, settings: settings, ioa: 0);
        bytes.Add(20);
        return bytes.ToArray();
    }

    public static List<byte> Header(byte typeId, byte vsq, int cause, Iec103MasterSettings settings, int ioa)
    {
        var bytes = new List<byte> { typeId, vsq };
        WriteLe(bytes, cause & 0x3F, Math.Clamp(settings.CauseOfTransmissionSize, 1, 2));
        if (settings.CauseOfTransmissionSize >= 2)
        {
            bytes[3] = 0; // originator address
        }
        WriteLe(bytes, settings.CommonAddress, Math.Clamp(settings.CommonAddressSize, 1, 2));
        WriteLe(bytes, ioa, Math.Clamp(settings.InformationObjectAddressSize, 1, 3));
        return bytes;
    }

    public static byte[] EncodeCp56Time2a(DateTime localTime)
    {
        var t = localTime.Kind == DateTimeKind.Utc ? localTime.ToLocalTime() : localTime;
        var milliseconds = checked((ushort)((t.Second * 1000) + t.Millisecond));
        return new[]
        {
            (byte)(milliseconds & 0xFF),
            (byte)((milliseconds >> 8) & 0xFF),
            (byte)(t.Minute & 0x3F),
            (byte)(t.Hour & 0x1F),
            (byte)(t.Day & 0x1F),
            (byte)(t.Month & 0x0F),
            (byte)((t.Year - 2000) & 0x7F)
        };
    }

    private static void WriteLe(List<byte> bytes, int value, int count)
    {
        for (var i = 0; i < count; i++)
        {
            bytes.Add((byte)((value >> (8 * i)) & 0xFF));
        }
    }
}
