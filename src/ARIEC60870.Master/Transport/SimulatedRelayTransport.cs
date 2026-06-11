// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Channels;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;

namespace ARIEC60870.Master.Transport;

/// <summary>
/// In-process IEC-103 relay simulation used by the WPF demo target mode.
/// It mirrors the standalone slave simulator behavior: Class 2 measurements,
/// ACD-driven Class 1 protection pickup/trip events, latch reset, and GI snapshots.
/// </summary>
public sealed class SimulatedRelayTransport : IByteTransport
{
    private readonly Iec103MasterSettings _settings;
    private readonly Ft12Parser _parser = new();
    private readonly Channel<byte> _rxBytes = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = true
    });
    private readonly Queue<byte[]> _class1Queue = new();
    private readonly Random _random = new(103);
    private bool _isOpen;
    private int _class2Polls;
    private DateTime _nextFaultUtc;
    private DateTime? _tripDueUtc;
    private DateTime? _autoResetDueUtc;
    private ProtectionPhase? _activePhase;
    private bool _pickupLatched;
    private bool _tripLatched;
    private double _currentAngle;

    public SimulatedRelayTransport(Iec103MasterSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _nextFaultUtc = DateTime.UtcNow.AddSeconds(3);
    }

    public bool IsOpen => _isOpen;

    public ValueTask OpenAsync(CancellationToken cancellationToken)
    {
        _isOpen = true;
        SeedStartupEvents();
        return ValueTask.CompletedTask;
    }

    public ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        _isOpen = false;
        return ValueTask.CompletedTask;
    }

    public void Dispose() => _isOpen = false;

    public ValueTask DisposeAsync()
    {
        _isOpen = false;
        return ValueTask.CompletedTask;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        EnsureOpen();
        var written = buffer.ToArray();
        var decoded = _parser.Decode(written);

        await Task.Delay(8, cancellationToken).ConfigureAwait(false);
        UpdateProtectionModel(DateTime.UtcNow);

        if (decoded.LinkControl is null || decoded.LinkControl.Prm != true)
        {
            await EnqueueAsync(FixedSecondary(functionCode: 1, acd: HasClass1Pending), cancellationToken).ConfigureAwait(false);
            return;
        }

        var link = decoded.LinkControl;
        if (decoded.Format == Ft12FrameFormat.VariableLength && decoded.Asdu is not null)
        {
            await RespondToVariableCommandAsync(decoded.Asdu, cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (link.FunctionCode)
        {
            case 0: // reset remote link
            case 7: // reset FCB
            case 9: // link status
                await EnqueueAsync(FixedSecondary(functionCode: 0, acd: HasClass1Pending), cancellationToken).ConfigureAwait(false);
                break;

            case 10: // class 1
                UpdateProtectionModel(DateTime.UtcNow);
                if (_class1Queue.Count > 0)
                {
                    var asdu = _class1Queue.Dequeue();
                    await EnqueueAsync(UserData(asdu, acd: HasClass1Pending), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await EnqueueAsync(FixedSecondary(functionCode: 9, acd: false), cancellationToken).ConfigureAwait(false);
                }
                break;

            case 11: // class 2/background
                _class2Polls++;
                UpdateProtectionModel(DateTime.UtcNow);
                if (HasClass1Pending)
                {
                    await EnqueueAsync(FixedSecondary(functionCode: 9, acd: true), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await EnqueueAsync(UserData(BuildAnimatedCurrentAsdu(), acd: false), cancellationToken).ConfigureAwait(false);
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
        if (buffer.Length == 0)
        {
            return 0;
        }

        var first = await _rxBytes.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        buffer.Span[0] = first;
        var count = 1;

        while (count < buffer.Length && _rxBytes.Reader.TryRead(out var next))
        {
            buffer.Span[count++] = next;
        }

        return count;
    }

    private bool HasClass1Pending => _class1Queue.Count > 0;

    private async Task RespondToVariableCommandAsync(AsduDecode asdu, CancellationToken cancellationToken)
    {
        if (asdu.TypeId == 7) // GI activation/request
        {
            SeedGeneralInterrogationBurst();
            await EnqueueAsync(FixedSecondary(functionCode: 0, acd: true), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (asdu.TypeId == 6) // clock sync
        {
            await EnqueueAsync(FixedSecondary(functionCode: 0, acd: HasClass1Pending), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (asdu.TypeId == 20 && asdu.FunctionType == 255 && asdu.InformationNumber == 19)
        {
            ResetProtectionLatch(DateTime.UtcNow);
            await EnqueueAsync(FixedSecondary(functionCode: 0, acd: HasClass1Pending), cancellationToken).ConfigureAwait(false);
            return;
        }

        await EnqueueAsync(FixedSecondary(functionCode: 0, acd: HasClass1Pending), cancellationToken).ConfigureAwait(false);
    }

    private void SeedStartupEvents()
    {
        _class1Queue.Clear();
        _class2Polls = 0;
        _nextFaultUtc = DateTime.UtcNow.AddSeconds(3);
        _tripDueUtc = null;
        _autoResetDueUtc = null;
        _activePhase = null;
        _pickupLatched = false;
        _tripLatched = false;
    }

    private void SeedGeneralInterrogationBurst()
    {
        foreach (var phase in new[] { ProtectionPhase.A, ProtectionPhase.B, ProtectionPhase.V })
        {
            _class1Queue.Enqueue(DpiTimeTagged(FunPickup, InfForPhase(phase), _pickupLatched && _activePhase == phase ? DpiOn : DpiOff, cot: 9));
            _class1Queue.Enqueue(DpiTimeTagged(FunTrip, InfForPhase(phase), _tripLatched && _activePhase == phase ? DpiOn : DpiOff, cot: 9));
        }

        _class1Queue.Enqueue(Measurand(FunCurrent, 1, CurrentValueForPhase(ProtectionPhase.A), cot: 9));
        _class1Queue.Enqueue(Measurand(FunCurrent, 2, CurrentValueForPhase(ProtectionPhase.B), cot: 9));
        _class1Queue.Enqueue(Measurand(FunCurrent, 3, CurrentValueForPhase(ProtectionPhase.V), cot: 9));
        _class1Queue.Enqueue(Identification(fun: 100, inf: 2, text: "GENERIC IEC103 RELAY DEMO"));
        _class1Queue.Enqueue(GiEnd());
    }

    private void UpdateProtectionModel(DateTime utcNow)
    {
        if (_pickupLatched && !_tripLatched && _tripDueUtc.HasValue && utcNow >= _tripDueUtc.Value)
        {
            _tripLatched = true;
            var phase = _activePhase ?? ProtectionPhase.A;
            _class1Queue.Enqueue(DpiTimeTagged(FunTrip, InfForPhase(phase), DpiOn, cot: 1));
            _tripDueUtc = null;
        }

        if ((_pickupLatched || _tripLatched) && _autoResetDueUtc.HasValue && utcNow >= _autoResetDueUtc.Value)
        {
            ResetProtectionLatch(utcNow);
            return;
        }

        if (!_pickupLatched && !_tripLatched && utcNow >= _nextFaultUtc)
        {
            StartProtectionFault(utcNow);
        }
    }

    private void StartProtectionFault(DateTime utcNow)
    {
        var phase = PickRandomPhase();
        _activePhase = phase;
        _pickupLatched = true;
        _tripLatched = false;
        _tripDueUtc = utcNow.AddMilliseconds(200);
        _autoResetDueUtc = utcNow.AddSeconds(20);
        _nextFaultUtc = DateTime.MaxValue;
        _class1Queue.Enqueue(DpiTimeTagged(FunPickup, InfForPhase(phase), DpiOn, cot: 1));
    }

    private void ResetProtectionLatch(DateTime utcNow)
    {
        if (!_pickupLatched && !_tripLatched)
        {
            _nextFaultUtc = utcNow.AddSeconds(10);
            return;
        }

        var phase = _activePhase ?? ProtectionPhase.A;
        if (_tripLatched)
        {
            _class1Queue.Enqueue(DpiTimeTagged(FunTrip, InfForPhase(phase), DpiOff, cot: 1));
        }

        if (_pickupLatched)
        {
            _class1Queue.Enqueue(DpiTimeTagged(FunPickup, InfForPhase(phase), DpiOff, cot: 1));
        }

        _pickupLatched = false;
        _tripLatched = false;
        _tripDueUtc = null;
        _autoResetDueUtc = null;
        _activePhase = null;
        _nextFaultUtc = utcNow.AddSeconds(10);
    }

    private byte[] BuildAnimatedCurrentAsdu()
    {
        var phase = NextCurrentPhase();
        return Measurand(FunCurrent, InfForPhase(phase), CurrentValueForPhase(phase), cot: 2);
    }

    private ProtectionPhase NextCurrentPhase()
    {
        var index = _class2Polls % 3;
        return index == 1 ? ProtectionPhase.A : index == 2 ? ProtectionPhase.B : ProtectionPhase.V;
    }

    private short CurrentValueForPhase(ProtectionPhase phase)
    {
        _currentAngle += 0.29;
        var baseAmp = 110.0 + 12.0 * Math.Sin(_currentAngle + (int)phase);
        if ((_pickupLatched || _tripLatched) && _activePhase == phase)
        {
            baseAmp = _tripLatched ? 680.0 : 420.0;
            baseAmp += 35.0 * Math.Sin(_currentAngle * 3.0);
        }

        return checked((short)Math.Clamp((int)Math.Round(baseAmp * 100.0), 0, short.MaxValue));
    }

    private byte[] DpiTimeTagged(byte fun, byte inf, byte dpi, byte cot)
    {
        var time = Iec103AsduBuilder.EncodeCp32Time2a(DateTime.Now);
        return new[]
        {
            (byte)0x01, (byte)0x81, cot, CaByte(_settings.CommonAddress), fun, inf, dpi,
            time[0], time[1], time[2], time[3], time[4]
        };
    }

    private byte[] Measurand(byte fun, byte inf, short value, byte cot)
    {
        return new[]
        {
            (byte)0x09, (byte)0x81, cot, CaByte(_settings.CommonAddress), fun, inf,
            (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)0x00
        };
    }

    private byte[] Identification(byte fun, byte inf, string text)
    {
        var ascii = System.Text.Encoding.ASCII.GetBytes(text);
        return new[] { (byte)0x05, (byte)0x81, (byte)0x03, CaByte(_settings.CommonAddress), fun, inf }
            .Concat(ascii.Take(24))
            .ToArray();
    }

    private byte[] GiEnd() => new[]
    {
        (byte)0x08, (byte)0x81, (byte)0x0A, CaByte(_settings.CommonAddress), (byte)0xFF, (byte)0x00, (byte)0x00
    };

    private byte[] FixedSecondary(int functionCode, bool acd = false, bool dfc = false)
    {
        var control = (byte)(functionCode & 0x0F);
        if (acd) control |= 0x20;
        if (dfc) control |= 0x10;
        return Ft12FrameBuilder.Fixed(control, _settings.LinkAddress);
    }

    private byte[] UserData(IReadOnlyList<byte> asdu, bool acd)
    {
        var control = (byte)0x08;
        if (acd) control |= 0x20;
        return Ft12FrameBuilder.Variable(control, _settings.LinkAddress, asdu);
    }

    private async Task EnqueueAsync(IReadOnlyList<byte> frame, CancellationToken cancellationToken)
    {
        foreach (var b in frame)
        {
            await _rxBytes.Writer.WriteAsync(b, cancellationToken).ConfigureAwait(false);
        }
    }

    private ProtectionPhase PickRandomPhase()
    {
        var value = _random.Next(0, 3);
        return value == 0 ? ProtectionPhase.A : value == 1 ? ProtectionPhase.B : ProtectionPhase.V;
    }

    private static byte InfForPhase(ProtectionPhase phase) => phase switch
    {
        ProtectionPhase.A => 1,
        ProtectionPhase.B => 2,
        ProtectionPhase.V => 3,
        _ => 1
    };

    private void EnsureOpen()
    {
        if (!_isOpen)
        {
            throw new InvalidOperationException("Simulated generic relay transport is not open.");
        }
    }

    private const byte FunPickup = 160;
    private const byte FunTrip = 161;
    private const byte FunCurrent = 144;
    private const byte DpiOff = 1;
    private const byte DpiOn = 2;

    private enum ProtectionPhase
    {
        A = 1,
        B = 2,
        V = 3
    }
    private static byte CaByte(int commonAddress)
    {
        if (commonAddress < 0 || commonAddress > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(commonAddress), "IEC-103 common address must fit in one octet.");
        }

        return (byte)commonAddress;
    }

}
