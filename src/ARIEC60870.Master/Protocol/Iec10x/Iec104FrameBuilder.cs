// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol.Iec10x;

public static class Iec104FrameBuilder
{
    public static byte[] StartDtActivation() => U(0x07);
    public static byte[] StartDtConfirmation() => U(0x0B);
    public static byte[] StopDtActivation() => U(0x13);
    public static byte[] StopDtConfirmation() => U(0x23);
    public static byte[] TestFrActivation() => U(0x43);
    public static byte[] TestFrConfirmation() => U(0x83);

    public static byte[] S(int receiveSequence)
    {
        var r = receiveSequence << 1;
        return new[] { (byte)0x68, (byte)0x04, (byte)0x01, (byte)0x00, (byte)(r & 0xFF), (byte)((r >> 8) & 0xFF) };
    }

    public static byte[] I(int sendSequence, int receiveSequence, IReadOnlyList<byte> asdu)
    {
        asdu ??= Array.Empty<byte>();
        var length = checked((byte)(4 + asdu.Count));
        var s = sendSequence << 1;
        var r = receiveSequence << 1;
        var frame = new byte[2 + length];
        frame[0] = 0x68;
        frame[1] = length;
        frame[2] = (byte)(s & 0xFF);
        frame[3] = (byte)((s >> 8) & 0xFF);
        frame[4] = (byte)(r & 0xFF);
        frame[5] = (byte)((r >> 8) & 0xFF);
        for (var i = 0; i < asdu.Count; i++) frame[6 + i] = asdu[i];
        return frame;
    }

    private static byte[] U(byte control) => new[] { (byte)0x68, (byte)0x04, control, (byte)0x00, (byte)0x00, (byte)0x00 };
}
