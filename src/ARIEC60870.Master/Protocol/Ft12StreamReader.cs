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
    private readonly int _fixedFrameCompletionBudgetMs;
    private readonly int _variableFrameCompletionBudgetMs;
    // A damaged FT1.2 prefix can contain 0xE5 inside its payload. Its
    // resynchronized single byte is NOT a proved reply to the Master request.
    private bool _uncertainFrameBoundary;
    public long AmbiguousShortResponsesSuppressed { get; private set; }
    /// <summary>
    /// Total physical octets consumed by this single connection's RX owner.
    /// This is NOT a validated telegram count. Compare around a read exchange
    /// to prohibit retransmission when even partial serial response bytes arrived.
    /// </summary>
    public long ReceivedByteCount => _receivedBytes;
    /// <summary>Physical fragments abandoned at a response deadline or cancellation.
    /// Counts are per reader/connection, never inferred as a valid RTU response.</summary>
    public long ExpiredPartialFrames { get; private set; }
    public long ExpiredPartialBytes { get; private set; }

    public Ft12StreamReader(IByteTransport transport, int linkAddressSize = 1, int baudRate = 0)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        // One maximum FT1.2 telegram takes up to 262 octets. Reserve the full
        // wire time at the selected baud (12 bits/octet covers 8E2) plus a
        // bounded converter/Windows scheduling margin. Silence still uses the
        // configured response timeout; only a physical frame start extends it.
        if (baudRate > 0)
        {
            _fixedFrameCompletionBudgetMs = Math.Clamp(
                (int)Math.Ceiling((linkAddressSize + 4d) * 12 * 1000 / baudRate) + 200, 250, 30000);
            _variableFrameCompletionBudgetMs = Math.Clamp(
                (int)Math.Ceiling(262d * 12 * 1000 / baudRate) + 300, 500, 30000);
        }
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
        var frameStartObserved = false;
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
                // Do not time out midway through a legitimate slow serial frame.
                // A single bounded extension is granted only for a FT1.2 start
                // byte; noise cannot perpetually prolong the request deadline.
                if (!frameStartObserved && _byte[0] is 0x10 or 0x68)
                {
                    frameStartObserved = true;
                    var completionBudgetMs = _byte[0] == 0x10
                        ? _fixedFrameCompletionBudgetMs
                        : _variableFrameCompletionBudgetMs;
                    if (completionBudgetMs > timeoutMs)
                        deadline.CancelAfter(completionBudgetMs);
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
        catch (OperationCanceledException)
        {
            // Both response timeout and operator Stop invalidate a partial
            // FT1.2 prefix. A later E5 has no address and cannot clear this
            // ambiguity; only a verified addressed frame can re-establish it.
            ExpirePartialFrame();
            if (cancellationToken.IsCancellationRequested)
                throw;
            return null;
        }
        catch (TimeoutException)
        {
            ExpirePartialFrame();
            return null;
        }
    }

    private void ExpirePartialFrame()
    {
        var partial = _framer.ExpireIncomplete();
        if (partial is not null)
        {
            ExpiredPartialFrames++;
            ExpiredPartialBytes += partial.RawBytes.Length;
            _uncertainFrameBoundary = true;
        }
        _frame = null;
    }
}
