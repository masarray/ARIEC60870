// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;

namespace ARIEC60870.Master.Protocol;

/// <summary>
/// IEC-101 unbalanced remote DFC flow control, separate from serial RTS/CTS.
/// An E5 without an addressed secondary control field never clears busy.
/// </summary>
internal sealed class Iec101UnbalancedBusyGate
{
    private readonly int _backoffMs;

    public Iec101UnbalancedBusyGate(int busyBackoffMs)
    {
        _backoffMs = Math.Clamp(busyBackoffMs, 100, 30000);
    }

    public bool IsBusy { get; private set; }
    public DateTime NextProbeUtc { get; private set; }

    public bool Observe(Ft12FrameDecode response, DateTime utcNow)
    {
        if (!response.IsChecksumValid || !response.IsLengthValid ||
            response.LinkControl is not { Prm: false } secondary)
            return false;

        var wasBusy = IsBusy;
        IsBusy = secondary.Dfc == true;
        if (IsBusy)
            NextProbeUtc = utcNow.AddMilliseconds(_backoffMs);
        return wasBusy != IsBusy;
    }

    public bool ShouldProbe(DateTime utcNow) => IsBusy && utcNow >= NextProbeUtc;

    public int DelayMs(DateTime utcNow) => !IsBusy ? 0
        : Math.Clamp((int)Math.Ceiling((NextProbeUtc - utcNow).TotalMilliseconds), 1, 100);
}
