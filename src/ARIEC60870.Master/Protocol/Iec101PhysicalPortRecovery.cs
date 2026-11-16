// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using ARIEC60870.Master.Transport;

namespace ARIEC60870.Master.Protocol;

/// <summary>
/// Driver/port faults only. An IEC-101 link timeout, DFC=1 or remote NO DATA
/// must never trigger a physical COM reset or a hidden command retransmission.
/// </summary>
internal static class Iec101PhysicalPortRecovery
{
    public static bool IsRecoverable(Exception fault, IByteTransport transport, CancellationToken stop)
    {
        if (stop.IsCancellationRequested || fault is OperationCanceledException)
            return false;

        return fault is IOException or UnauthorizedAccessException or TimeoutException
            || (fault is InvalidOperationException && !transport.IsOpen);
    }

    public static int BackoffMs(int configuredBackoffMs, int attempt)
    {
        var seed = Math.Clamp(configuredBackoffMs, 100, 1000);
        var shift = Math.Clamp(attempt - 1, 0, 4);
        return Math.Min(5000, seed * (1 << shift));
    }

    public static bool ShouldReport(int attempt)
        => attempt <= 2 || (attempt & (attempt - 1)) == 0;
}
