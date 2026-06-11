// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Transport;

namespace ARIEC60870.Master.Protocol;

public sealed class Ft12StreamReader
{
    private readonly IByteTransport _transport;
    private readonly int _linkAddressSize;

    public Ft12StreamReader(IByteTransport transport, int linkAddressSize = 1)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _linkAddressSize = Math.Clamp(linkAddressSize, 0, 2);
    }

    public async Task<byte[]?> ReadFrameAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(1, timeoutMs));

        try
        {
            var first = await ReadStartByteAsync(timeout.Token).ConfigureAwait(false);
            if (first is null)
            {
                return null;
            }

            if (first.Value is 0xE5 or 0xA2)
            {
                return new[] { first.Value };
            }

            if (first.Value == 0x10)
            {
                var tail = await ReadExactAsync(3 + _linkAddressSize, timeout.Token).ConfigureAwait(false);
                return tail is null ? new[] { first.Value } : new[] { first.Value }.Concat(tail).ToArray();
            }

            if (first.Value == 0x68)
            {
                var header = await ReadExactAsync(3, timeout.Token).ConfigureAwait(false);
                if (header is null)
                {
                    return new[] { first.Value };
                }

                var length = header[0];
                var rest = await ReadExactAsync(length + 2, timeout.Token).ConfigureAwait(false);
                if (rest is null)
                {
                    return new[] { first.Value }.Concat(header).ToArray();
                }

                return new[] { first.Value }.Concat(header).Concat(rest).ToArray();
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private async Task<byte?> ReadStartByteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var value = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            if (value is null)
            {
                return null;
            }

            if (value.Value is 0xE5 or 0xA2 or 0x10 or 0x68)
            {
                return value;
            }

            // Serial links can contain leftover/noise bytes after converter turnaround,
            // cable disturbance, or aborted reads. Resync to the next FT1.2 start byte
            // instead of returning one malformed frame per noise byte.
        }

        return null;
    }

    private async Task<byte?> ReadByteAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var read = await _transport.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        return read <= 0 ? null : buffer[0];
    }

    private async Task<byte[]?> ReadExactAsync(int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = await _transport.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                return null;
            }

            offset += read;
        }

        return buffer;
    }
}
