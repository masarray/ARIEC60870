// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Transport;

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>
/// Session-owned IEC-104 stream reader. An incomplete APDU survives a bounded
/// read timeout; the following receive continues the same byte stream, not
/// a fresh 0x68 search. Never prefetch bytes from a later APDU.
/// </summary>
public sealed class Iec104StreamReader
{
    private readonly IByteTransport _transport;
    private readonly byte[] _pending = new byte[255];
    private int _pendingCount;

    public Iec104StreamReader(IByteTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<byte[]?> ReadFrameAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(1, timeoutMs));
        try
        {
            while (true)
            {
                if (_pendingCount == 0)
                {
                    var count = await _transport.ReadAsync(_pending.AsMemory(0, 1), timeout.Token)
                        .ConfigureAwait(false);
                    if (count <= 0) return null;
                    if (_pending[0] != 0x68) continue;
                    _pendingCount = 1;
                }

                if (_pendingCount == 1)
                {
                    var count = await _transport.ReadAsync(_pending.AsMemory(1, 1), timeout.Token)
                        .ConfigureAwait(false);
                    if (count <= 0) return null;

                    var declared = _pending[1];
                    if (declared is < 4 or > 253)
                    {
                        var invalidHeader = new[] { (byte)0x68, declared };
                        // An invalid length which is itself 0x68 may be the next
                        // APDU start. Keep it without promoting any invalid data.
                        _pendingCount = declared == 0x68 ? 1 : 0;
                        return invalidHeader;
                    }

                    _pendingCount = 2;
                }

                var completeLength = _pending[1] + 2;
                while (_pendingCount < completeLength)
                {
                    var count = await _transport.ReadAsync(
                        _pending.AsMemory(_pendingCount, completeLength - _pendingCount),
                        timeout.Token).ConfigureAwait(false);
                    if (count <= 0) return null;
                    _pendingCount += count;
                }

                var completed = _pending.AsSpan(0, completeLength).ToArray();
                _pendingCount = 0;
                return completed;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Preserve the partial APDU for the next bounded receive on this
            // same TCP association; no bytes have been reinterpreted or lost.
            return null;
        }
    }
}
