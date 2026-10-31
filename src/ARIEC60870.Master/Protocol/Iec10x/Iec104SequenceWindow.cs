// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>
/// Pure IEC-104 15-bit APCI sequence arithmetic. The 32767 -> 0 rollover
/// MUST be treated modulo 32768, not compared using ordinary integers.
/// This does not infer application acceptance from an APCI ACK.
/// </summary>
public static class Iec104SequenceWindow
{
    public const int Modulus = 32768;
    public static int Next(int current) => (current + 1) & 0x7FFF;
    public static int Distance(int start, int end) => (end - start + Modulus) & 0x7FFF;

    /// <summary>
    /// Accept a cumulative N(R) only within [oldestUnacked, nextToSend],
    /// with a bounded outstanding window smaller than half the sequence space.
    /// </summary>
    public static bool IsAcknowledgementWithinWindow(int oldestUnacked, int nextToSend, int nr)
    {
        if ((uint)oldestUnacked >= Modulus || (uint)nextToSend >= Modulus ||
            (uint)nr >= Modulus) return false;
        var outstanding = Distance(oldestUnacked, nextToSend);
        return outstanding < Modulus / 2 && Distance(oldestUnacked, nr) <= outstanding;
    }
}
