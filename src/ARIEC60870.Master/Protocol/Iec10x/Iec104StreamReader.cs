// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Transport;

namespace ARIEC60870.Master.Protocol.Iec10x;

public sealed class Iec104StreamReader
{
    private readonly IByteTransport _transport;

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
            while (!timeout.IsCancellationRequested)
            {
                var start = await ReadByteAsync(timeout.Token).ConfigureAwait(false);
                if (start is null) return null;
                if (start.Value != 0x68) continue;
                var lengthByte = await ReadByteAsync(timeout.Token).ConfigureAwait(false);
                if (lengthByte is null) return new[] { (byte)0x68 };
                var length = lengthByte.Value;
                var rest = await ReadExactAsync(length, timeout.Token).ConfigureAwait(false);
                return rest is null ? new[] { (byte)0x68, length } : new[] { (byte)0x68, length }.Concat(rest).ToArray();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return null;
    }

    private async Task<byte?> ReadByteAsync(CancellationToken cancellationToken)
    {
        var b = new byte[1];
        var read = await _transport.ReadAsync(b, cancellationToken).ConfigureAwait(false);
        return read <= 0 ? null : b[0];
    }

    private async Task<byte[]?> ReadExactAsync(int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await _transport.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read <= 0) return null;
            offset += read;
        }
        return buffer;
    }
}
