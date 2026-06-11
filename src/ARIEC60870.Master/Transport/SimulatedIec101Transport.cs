// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Channels;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Master.Transport;

public sealed class SimulatedIec101Transport : IByteTransport
{
    private readonly Iec103MasterSettings _settings;
    private readonly Ft12Parser _ft12;
    private readonly Iec10xAsduDecoder _asduDecoder;
    private readonly Channel<byte> _rxBytes = Channel.CreateUnbounded<byte>();
    private readonly Queue<byte[]> _class1Queue = new();
    private bool _isOpen;
    private int _class2Polls;

    public SimulatedIec101Transport(Iec103MasterSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _ft12 = new Ft12Parser(settings.LinkAddressSize);
        _asduDecoder = new Iec10xAsduDecoder(settings.CauseOfTransmissionSize, settings.CommonAddressSize, settings.InformationObjectAddressSize);
    }

    public bool IsOpen => _isOpen;

    public ValueTask OpenAsync(CancellationToken cancellationToken)
    {
        _isOpen = true;
        _class1Queue.Clear();
        _class2Polls = 0;
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
        var decoded = _ft12.Decode(buffer.ToArray());
        await Task.Delay(6, cancellationToken).ConfigureAwait(false);
        if (decoded.LinkControl is null || decoded.LinkControl.Prm != true)
        {
            await EnqueueAsync(FixedSecondary(functionCode: 1, acd: HasClass1Pending), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (decoded.Format == Ft12FrameFormat.VariableLength && decoded.AsduBytes.Count > 0)
        {
            var asdu = _asduDecoder.Decode(decoded.AsduBytes);
            if (asdu.TypeId == 100)
            {
                SeedGiData();
                await EnqueueAsync(FixedSecondary(functionCode: 0, acd: true), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (asdu.TypeId == 103)
            {
                await EnqueueAsync(FixedSecondary(functionCode: 0, acd: HasClass1Pending), cancellationToken).ConfigureAwait(false);
                return;
            }

            await EnqueueAsync(FixedSecondary(functionCode: 0, acd: HasClass1Pending), cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (decoded.LinkControl.FunctionCode)
        {
            case 0:
            case 7:
            case 9:
                await EnqueueAsync(FixedSecondary(functionCode: 0, acd: HasClass1Pending), cancellationToken).ConfigureAwait(false);
                break;
            case 10:
                if (_class1Queue.Count > 0)
                {
                    await EnqueueAsync(UserData(_class1Queue.Dequeue(), acd: HasClass1Pending), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await EnqueueAsync(FixedSecondary(functionCode: 9, acd: false), cancellationToken).ConfigureAwait(false);
                }
                break;
            case 11:
                _class2Polls++;
                if (_class2Polls % 4 == 0 && !HasClass1Pending)
                {
                    SeedSpontaneousData(_class2Polls);
                }

                if (HasClass1Pending)
                {
                    await EnqueueAsync(FixedSecondary(functionCode: 9, acd: true), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await EnqueueAsync(UserData(Iec10xAsduBuilder.FloatMeasurement(_settings, ioa: 1001, value: 20.0f + _class2Polls, cause: 2), acd: false), cancellationToken).ConfigureAwait(false);
                }
                break;
            default:
                await EnqueueAsync(FixedSecondary(functionCode: 1, acd: HasClass1Pending), cancellationToken).ConfigureAwait(false);
                break;
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

    private bool HasClass1Pending => _class1Queue.Count > 0;

    private void SeedGiData()
    {
        _class1Queue.Enqueue(Iec10xAsduBuilder.ActivationConfirmation(_settings, 100));
        _class1Queue.Enqueue(Iec10xAsduBuilder.SinglePoint(_settings, ioa: 101, value: true, cause: 20));
        _class1Queue.Enqueue(Iec10xAsduBuilder.DoublePoint(_settings, ioa: 102, dpi: 2, cause: 20));
        _class1Queue.Enqueue(Iec10xAsduBuilder.FloatMeasurement(_settings, ioa: 1001, value: 21.7f, cause: 20));
        _class1Queue.Enqueue(Iec10xAsduBuilder.FloatMeasurement(_settings, ioa: 1002, value: 150.3f, cause: 20));
        _class1Queue.Enqueue(Iec10xAsduBuilder.ActivationTermination(_settings));
    }


    private void SeedSpontaneousData(int tick)
    {
        var sp = (tick / 4) % 2 == 0;
        var dp = sp ? 2 : 1;
        _class1Queue.Enqueue(Iec10xAsduBuilder.SinglePoint(_settings, ioa: 201, value: sp, cause: 3));
        _class1Queue.Enqueue(Iec10xAsduBuilder.DoublePoint(_settings, ioa: 202, dpi: dp, cause: 3));
    }

    private byte[] FixedSecondary(int functionCode, bool acd)
    {
        var control = (byte)(functionCode & 0x0F);
        if (acd) control |= 0x20;
        return Ft12FrameBuilder.Fixed(control, _settings.LinkAddress, _settings.LinkAddressSize);
    }

    private byte[] UserData(byte[] asdu, bool acd)
    {
        var control = (byte)0x08;
        if (acd) control |= 0x20;
        return Ft12FrameBuilder.Variable(control, _settings.LinkAddress, asdu, _settings.LinkAddressSize);
    }

    private async Task EnqueueAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        foreach (var b in bytes)
        {
            await _rxBytes.Writer.WriteAsync(b, cancellationToken).ConfigureAwait(false);
        }
    }

    private void EnsureOpen()
    {
        if (!_isOpen) throw new InvalidOperationException("Simulated IEC-101 transport is not open.");
    }
}
