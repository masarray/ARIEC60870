// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Transport;

namespace ARIEC60870.Master.Protocol;

/// <summary>
/// Active serial byte-transport adapter for the canonical Core FT1.2 framer.
/// The framing state is per reader/connection, never shared across link A/B.
/// No TX, polling, FCB or protocol-level application logic is owned here.
/// </summary>
public sealed class Ft12StreamReader
{
    private readonly IByteTransport _transport;
    private readonly Ft12IncrementalFramer _framer;
    private readonly byte[] _byte = new byte[1];
    private readonly Action<Ft12StreamCandidate> _onCandidate;
    private byte[]? _frame;
    private long _receivedBytes;
    // A damaged FT1.2 prefix can contain 0xE5 inside its payload. Its
    // resynchronized single byte is NOT a proved reply to the Master request.
    private bool _uncertainFrameBoundary;
    public long AmbiguousShortResponsesSuppressed { get; private set; }

    public Ft12StreamReader(IByteTransport transport, int linkAddressSize = 1)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        // Master never adopts a different link-address profile implicitly.
        // A complete malformed-end frame is retained for the existing
        // secondary response validator and frame-quality diagnostics.
        _framer = new Ft12IncrementalFramer(linkAddressSize,
            permitAlternateLinkWidths: false, returnMalformedEnd: true);
        _onCandidate = candidate =>
        {
            // Source noise/invalid headers are not application responses.
            // Core preserves verified boundary candidates (including checksum
            // failures); the existing Master validator decides acceptance.
            if (candidate.Format == "Noise")
            {
                // Ordinary converter noise can precede a legitimate E5.
                // Only an abandoned 0x10/0x68 frame prefix creates ambiguity.
                if (candidate.RawBytes.Contains((byte)0x10) ||
                    candidate.RawBytes.Contains((byte)0x68))
                    _uncertainFrameBoundary = true;
                return;
            }
            if (candidate.Format == "Single" && candidate.RawBytes[0] == 0xE5 &&
                _uncertainFrameBoundary)
            {
                AmbiguousShortResponsesSuppressed++;
                return;
            }
            if (candidate.Format is "Fixed" or "Variable" && candidate.ChecksumValid)
                _uncertainFrameBoundary = false;
            _frame = candidate.RawBytes;
        };
    }

    public async Task<byte[]?> ReadFrameAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Math.Max(1, timeoutMs));
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await _transport.ReadAsync(_byte, deadline.Token).ConfigureAwait(false);
                if (read > _byte.Length || read < 0)
                    throw new IOException("Transport returned invalid serial read length.");
                if (read == 0)
                {
                    // Preserve the previous transport contract for transient
                    // empty reads without claiming that a response arrived.
                    await Task.Delay(1, deadline.Token).ConfigureAwait(false);
                    continue;
                }
                _framer.Feed(_byte.AsSpan(0, read), _receivedBytes,
                    _onCandidate, flushNoiseAtEnd: false);
                _receivedBytes += read;
                if (_frame is { } result)
                {
                    _frame = null;
                    return result;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // An incomplete frame cannot silently become a response during
            // the next independent Master request on a persistent reader.
            _framer.ExpireIncomplete();
            _frame = null;
            return null;
        }
        catch (TimeoutException)
        {
            _framer.ExpireIncomplete();
            _frame = null;
            return null;
        }
    }
}
