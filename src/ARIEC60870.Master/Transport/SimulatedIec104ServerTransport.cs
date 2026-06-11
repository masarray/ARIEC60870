// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Channels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Master.Transport;

public sealed class SimulatedIec104ServerTransport : IByteTransport
{
    private readonly Iec103MasterSettings _settings;
    private readonly Iec104ApduParser _parser;
    private readonly Channel<byte> _rxBytes = Channel.CreateUnbounded<byte>();
    private bool _isOpen;
    private int _serverSendSeq;
    private int _serverRecvSeq;
    private int _polls;

    public SimulatedIec104ServerTransport(Iec103MasterSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _parser = new Iec104ApduParser(settings.CauseOfTransmissionSize, settings.CommonAddressSize, settings.InformationObjectAddressSize);
    }

    public bool IsOpen => _isOpen;

    public ValueTask OpenAsync(CancellationToken cancellationToken)
    {
        _isOpen = true;
        _serverSendSeq = 0;
        _serverRecvSeq = 0;
        _polls = 0;
        return ValueTask.CompletedTask;
    }

    public ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        _isOpen = false;
        return ValueTask.CompletedTask;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        EnsureOpen();
        var frame = buffer.ToArray();
        var decoded = _parser.Decode(frame);
        await Task.Delay(5, cancellationToken).ConfigureAwait(false);

        if (decoded.Format == "U")
        {
            if (decoded.UFormatName.Contains("STARTDT act", StringComparison.OrdinalIgnoreCase))
            {
                await EnqueueAsync(Iec104FrameBuilder.StartDtConfirmation(), cancellationToken).ConfigureAwait(false);
                return;
            }
            if (decoded.UFormatName.Contains("TESTFR act", StringComparison.OrdinalIgnoreCase))
            {
                await EnqueueAsync(Iec104FrameBuilder.TestFrConfirmation(), cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        if (decoded.Format == "S")
        {
            _polls++;
            if (_polls % 3 == 0)
            {
                await SendIAsync(Iec10xAsduBuilder.SinglePoint(_settings, ioa: 201, value: (_polls / 3) % 2 == 0, cause: 3), cancellationToken).ConfigureAwait(false);
                await SendIAsync(Iec10xAsduBuilder.DoublePoint(_settings, ioa: 202, dpi: ((_polls / 3) % 2 == 0) ? 2 : 1, cause: 3), cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        if (decoded.Format == "I")
        {
            _serverRecvSeq = (decoded.SendSequence ?? _serverRecvSeq) + 1;
            var type = decoded.Asdu?.TypeId ?? 0;
            if (type == 100)
            {
                await SendIAsync(Iec10xAsduBuilder.ActivationConfirmation(_settings, 100), cancellationToken).ConfigureAwait(false);
                await SendIAsync(Iec10xAsduBuilder.SinglePoint(_settings, ioa: 101, value: true, cause: 20), cancellationToken).ConfigureAwait(false);
                await SendIAsync(Iec10xAsduBuilder.DoublePoint(_settings, ioa: 102, dpi: 2, cause: 20), cancellationToken).ConfigureAwait(false);
                await SendIAsync(Iec10xAsduBuilder.FloatMeasurement(_settings, ioa: 1001, value: 20.6f, cause: 20), cancellationToken).ConfigureAwait(false);
                await SendIAsync(Iec10xAsduBuilder.ActivationTermination(_settings), cancellationToken).ConfigureAwait(false);
                return;
            }
            if (type == 103)
            {
                await SendIAsync(Iec10xAsduBuilder.ActivationConfirmation(_settings, 103), cancellationToken).ConfigureAwait(false);
                return;
            }

            _polls++;
            await SendIAsync(Iec10xAsduBuilder.FloatMeasurement(_settings, ioa: 1001, value: 20.0f + _polls, cause: 2), cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        EnsureOpen();
        if (buffer.Length == 0) return 0;
        var first = await _rxBytes.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        buffer.Span[0] = first;
        var count = 1;
        while (count < buffer.Length && _rxBytes.Reader.TryRead(out var next))
        {
            buffer.Span[count++] = next;
        }
        return count;
    }

    public void Dispose() => _isOpen = false;
    public ValueTask DisposeAsync() { _isOpen = false; return ValueTask.CompletedTask; }

    private async Task SendIAsync(byte[] asdu, CancellationToken cancellationToken)
    {
        var frame = Iec104FrameBuilder.I(_serverSendSeq++, _serverRecvSeq, asdu);
        await EnqueueAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnqueueAsync(byte[] frame, CancellationToken cancellationToken)
    {
        foreach (var b in frame)
        {
            await _rxBytes.Writer.WriteAsync(b, cancellationToken).ConfigureAwait(false);
        }
    }

    private void EnsureOpen()
    {
        if (!_isOpen) throw new InvalidOperationException("Simulated IEC-104 server is not open.");
    }
}
