// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol;

/// <summary>Bounded per-COM TX record for identifying delayed local request echoes.</summary>
internal sealed class Iec101TransmitHistory
{
    private const int Capacity = 8;
    private readonly byte[][] _frames = new byte[Capacity][];
    private int _next;
    private int _count;

    public void Record(byte[] ownedFrame)
    {
        ArgumentNullException.ThrowIfNull(ownedFrame);
        _frames[_next] = ownedFrame;
        _next = (_next + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    public bool MatchesEarlier(IReadOnlyList<byte> received)
    {
        // Do not classify the most recently sent frame as delayed; the validator
        // already reports exact last-TX echo separately.
        var lastIndex = (_next + Capacity - 1) % Capacity;
        for (var i = 0; i < _count; i++)
        {
            if (i == lastIndex) continue;
            var candidate = _frames[i];
            if (candidate is null || candidate.Length != received.Count) continue;
            var matches = true;
            for (var j = 0; j < candidate.Length; j++)
            {
                if (candidate[j] == received[j]) continue;
                matches = false;
                break;
            }
            if (matches) return true;
        }
        return false;
    }
}
