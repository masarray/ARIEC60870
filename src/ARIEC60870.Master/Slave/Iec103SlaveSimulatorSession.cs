// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Transport;

namespace ARIEC60870.Master.Slave;

/// <summary>
/// Deterministic IEC 60870-5-103 slave simulator for testing ARIEC60870 master behavior.
///
/// The simulator behaves like a simple protection relay slave:
/// - ACK startup commands.
/// - Accept GI and expose Class 1 data through ACD=1.
/// - Use Class 2 as background/measurement polling when no Class 1 event is pending.
/// - Generate latched protection pickup/trip events with relay timestamps.
/// - Reset latched protection states by IEC-103 reset command or by auto reset.
///
/// It is intentionally a test simulator, not a vendor relay model.
/// </summary>
public sealed class Iec103SlaveSimulatorSession
{
    private const int MaxRetainedEvents = 2000;

    private readonly Iec103SlaveSimulatorSettings _settings;
    private readonly IByteTransport _transport;
    private readonly Ft12Parser _parser = new();
    private readonly Queue<byte[]> _class1Queue = new();
    private readonly List<Iec103SlaveSimulatorEvent> _events = new();
    private readonly Iec103SlaveSimulatorCounters _counters = new();
    private readonly Random _random;

    private int _class2Polls;
    private DateTime _nextFaultUtc;
    private DateTime? _tripDueUtc;
    private DateTime? _autoResetDueUtc;
    private ProtectionPhase? _activePhase;
    private bool _pickupLatched;
    private bool _tripLatched;
    private double _currentAngle;

    public Iec103SlaveSimulatorSession(Iec103SlaveSimulatorSettings settings, IByteTransport transport)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _random = new Random(_settings.RandomSeed);
        _nextFaultUtc = DateTime.UtcNow.AddSeconds(Math.Max(1, _settings.InitialFaultDelaySeconds));
    }

    public event EventHandler<Iec103SlaveSimulatorEvent>? EventRaised;

    public async Task<Iec103SlaveSimulatorRunResult> RunForAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(duration);
        return await RunAsync(timeout.Token).ConfigureAwait(false);
    }

    public async Task<Iec103SlaveSimulatorRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var completion = "Stopped by cancellation or requested duration.";
        var completedNormally = true;

        try
        {
            AddEvent("STATE", "-", "Opening IEC-103 slave simulator", _settings.SerialSummary);
            await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);
            AddEvent("STATE", "-", "Slave simulator ready", "Waiting for IEC-103 master frames.");
            AddEvent("STATE", "Protection", "Protection behavior armed", $"First pickup after {_settings.InitialFaultDelaySeconds}s, trip delay={_settings.TripDelayMs}ms, auto reset={_settings.AutoResetSeconds}s, repeat={_settings.FaultRepeatDelaySeconds}s.");

            var reader = new Ft12StreamReader(_transport);
            while (!cancellationToken.IsCancellationRequested)
            {
                var raw = await reader.ReadFrameAsync(_settings.ResponseTimeoutMs, cancellationToken).ConfigureAwait(false);
                if (raw is null || raw.Length == 0)
                {
                    continue;
                }

                await HandleMasterFrameAsync(raw, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            completion = "Stopped by cancellation or requested duration.";
        }
        catch (Exception ex)
        {
            completedNormally = false;
            completion = "Fault: " + ex.Message;
            AddEvent("DIAG", "-", "Slave simulator fault", ex.ToString());
        }
        finally
        {
            try
            {
                await _transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                completedNormally = false;
                AddEvent("DIAG", "-", "Transport close warning", ex.ToString());
            }

            AddEvent("STATE", "-", "Slave simulator stopped", completion);
        }

        return new Iec103SlaveSimulatorRunResult
        {
            Settings = _settings,
            Counters = _counters,
            Events = _events.ToArray(),
            StartedUtc = started,
            FinishedUtc = DateTime.UtcNow,
            CompletedNormally = completedNormally,
            CompletionReason = completion
        };
    }

    private async Task HandleMasterFrameAsync(byte[] raw, CancellationToken cancellationToken)
    {
        _counters.RxFrames++;
        var decoded = _parser.Decode(raw);
        AddEvent("RX", ClassFromRequest(decoded), decoded.ShortMeaning, ToHex(raw));

        if (decoded.LinkAddress.HasValue && decoded.LinkAddress.Value != _settings.LinkAddress)
        {
            AddEvent("STATE", "-", "Frame ignored", $"Link address mismatch. Expected {_settings.LinkAddress}, received {decoded.LinkAddress.Value}.");
            return;
        }

        if (decoded.Format == Ft12FrameFormat.Malformed || decoded.LinkControl is null)
        {
            _counters.MalformedRxFrames++;
            await SendFixedAsync(functionCode: 1, acd: HasClass1Pending, dfc: false, "NACK malformed request", "Link", cancellationToken).ConfigureAwait(false);
            return;
        }

        var link = decoded.LinkControl;
        if (!link.Prm)
        {
            await SendFixedAsync(functionCode: 1, acd: HasClass1Pending, dfc: false, "NACK non-primary request", "Link", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_settings.SilentMode)
        {
            AddEvent("STATE", ClassFromRequest(decoded), "Silent mode", "Configured not to respond. Useful for master timeout/recovery tests.");
            return;
        }

        if (decoded.Format == Ft12FrameFormat.VariableLength && decoded.Asdu is not null)
        {
            await RespondToVariableCommandAsync(decoded.Asdu, cancellationToken).ConfigureAwait(false);
            return;
        }

        await RespondToFixedCommandAsync(link.FunctionCode, cancellationToken).ConfigureAwait(false);
    }

    private bool HasClass1Pending => _class1Queue.Count > 0;

    private async Task RespondToFixedCommandAsync(int functionCode, CancellationToken cancellationToken)
    {
        UpdateProtectionModel(DateTime.UtcNow);

        switch (functionCode)
        {
            case 0: // reset remote link
                _counters.ResetLinkRequests++;
                await SendFixedAsync(0, acd: HasClass1Pending, dfc: false, "ACK reset remote link", "Link", cancellationToken).ConfigureAwait(false);
                return;

            case 7: // reset FCB
                _counters.ResetFcbRequests++;
                await SendFixedAsync(0, acd: HasClass1Pending, dfc: false, "ACK reset FCB", "Link", cancellationToken).ConfigureAwait(false);
                return;

            case 9: // request link status
                await SendFixedAsync(0, acd: HasClass1Pending, dfc: _settings.DfcBusyMode, "ACK link status", "Link", cancellationToken).ConfigureAwait(false);
                return;

            case 10: // request class 1
                _counters.Class1Requests++;
                if (_settings.DfcBusyMode)
                {
                    await SendFixedAsync(9, acd: HasClass1Pending, dfc: true, "DFC busy during Class 1 request", "Class 1", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (_class1Queue.Count > 0)
                {
                    var asdu = _class1Queue.Dequeue();
                    await SendUserDataAsync(asdu, acd: HasClass1Pending, "Class 1 data response", "Class 1", cancellationToken).ConfigureAwait(false);
                    return;
                }

                await SendFixedAsync(9, acd: false, dfc: false, "NO DATA - Class 1 queue empty", "Class 1", cancellationToken).ConfigureAwait(false);
                return;

            case 11: // request class 2
                _counters.Class2Requests++;
                _class2Polls++;
                UpdateProtectionModel(DateTime.UtcNow);
                if (_settings.DfcBusyMode)
                {
                    await SendFixedAsync(9, acd: HasClass1Pending, dfc: true, "DFC busy during Class 2 request", "Class 2", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (HasClass1Pending)
                {
                    await SendFixedAsync(9, acd: true, dfc: false, "Class 2 response / ACD indicates pending protection event", "Class 2", cancellationToken).ConfigureAwait(false);
                    return;
                }

                await SendUserDataAsync(BuildAnimatedCurrentAsdu(), acd: false, "Class 2 animated current measurand", "Class 2", cancellationToken).ConfigureAwait(false);
                return;

            default:
                _counters.UnknownRequests++;
                await SendFixedAsync(1, acd: HasClass1Pending, dfc: false, $"NACK unsupported primary function {functionCode}", "Link", cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async Task RespondToVariableCommandAsync(AsduDecode asdu, CancellationToken cancellationToken)
    {
        UpdateProtectionModel(DateTime.UtcNow);

        switch (asdu.TypeId)
        {
            case 7: // General Interrogation
                _counters.GiRequests++;
                SeedGeneralInterrogationBurst();
                await SendFixedAsync(0, acd: HasClass1Pending, dfc: false, "ACK General Interrogation; ACD raised", "Class 2", cancellationToken).ConfigureAwait(false);
                return;

            case 6: // Clock Sync
                _counters.ClockSyncRequests++;
                await SendFixedAsync(0, acd: HasClass1Pending, dfc: false, "ACK Clock Synchronization", "Class 2", cancellationToken).ConfigureAwait(false);
                return;

            case 20: // General command / user command. Used by simulator as protection reset command.
                if (asdu.FunctionType == _settings.ResetCommandFun && asdu.InformationNumber == _settings.ResetCommandInf)
                {
                    ResetProtectionLatch("master reset command", DateTime.UtcNow);
                    _counters.CommandResets++;
                    await SendFixedAsync(0, acd: HasClass1Pending, dfc: false, "ACK protection reset command", "Command", cancellationToken).ConfigureAwait(false);
                    return;
                }

                _counters.UnknownRequests++;
                await SendFixedAsync(0, acd: HasClass1Pending, dfc: false, $"ACK unsupported command FUN={asdu.FunctionType}, INF={asdu.InformationNumber}", "Command", cancellationToken).ConfigureAwait(false);
                return;

            default:
                _counters.UnknownRequests++;
                await SendFixedAsync(0, acd: HasClass1Pending, dfc: false, $"ACK variable ASDU Type {asdu.TypeId}", "Class 2", cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private void SeedGeneralInterrogationBurst()
    {
        EnqueueProtectionSnapshot(cot: 9);
        EnqueueCurrentSnapshot(cot: 9);
        _class1Queue.Enqueue(Identification(fun: 100, inf: 2, text: "ARIEC60870 IEC-103 SLAVE SIM"));
        if (_settings.SeedGiEnd)
        {
            _class1Queue.Enqueue(GiEnd());
        }

        MarkQueueDepth();
        AddEvent("STATE", "Class 1", "GI data queue seeded", $"Queue depth={_class1Queue.Count}; GI END={(_settings.SeedGiEnd ? "enabled" : "disabled")}.");
    }

    private void UpdateProtectionModel(DateTime utcNow)
    {
        if (!_settings.EnableProtectionBehavior)
        {
            return;
        }

        if (_pickupLatched && !_tripLatched && _tripDueUtc.HasValue && utcNow >= _tripDueUtc.Value)
        {
            _tripLatched = true;
            var phase = _activePhase ?? ProtectionPhase.A;
            _class1Queue.Enqueue(DpiTimeTagged(FunTrip, InfForPhase(phase), DpiOn, cot: 1));
            _counters.TripEvents++;
            _tripDueUtc = null;
            AddEvent("STATE", "Protection", $"Trip {PhaseText(phase)} ON queued", $"Trip followed pickup after {_settings.TripDelayMs} ms. Signals stay latched until reset.");
            MarkQueueDepth();
        }

        if ((_pickupLatched || _tripLatched) && _autoResetDueUtc.HasValue && utcNow >= _autoResetDueUtc.Value)
        {
            ResetProtectionLatch("auto reset timeout", utcNow);
            _counters.AutoResets++;
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
        _tripDueUtc = utcNow.AddMilliseconds(Math.Max(1, _settings.TripDelayMs));
        _autoResetDueUtc = utcNow.AddSeconds(Math.Max(1, _settings.AutoResetSeconds));
        _nextFaultUtc = DateTime.MaxValue;

        _class1Queue.Enqueue(DpiTimeTagged(FunPickup, InfForPhase(phase), DpiOn, cot: 1));
        _counters.ProtectionCycles++;
        _counters.PickupEvents++;
        AddEvent("STATE", "Protection", $"Pickup {PhaseText(phase)} ON queued", $"Trip will be queued after {_settings.TripDelayMs} ms. Auto reset in {_settings.AutoResetSeconds} s unless master sends reset command FUN={_settings.ResetCommandFun}, INF={_settings.ResetCommandInf}.");
        MarkQueueDepth();
    }

    private void ResetProtectionLatch(string reason, DateTime utcNow)
    {
        if (!_pickupLatched && !_tripLatched)
        {
            _nextFaultUtc = utcNow.AddSeconds(Math.Max(1, _settings.FaultRepeatDelaySeconds));
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
        _nextFaultUtc = utcNow.AddSeconds(Math.Max(1, _settings.FaultRepeatDelaySeconds));

        AddEvent("STATE", "Protection", "Protection latch reset", $"Reason={reason}. Next pickup cycle after {_settings.FaultRepeatDelaySeconds} s.");
        MarkQueueDepth();
    }

    private void EnqueueProtectionSnapshot(byte cot)
    {
        foreach (var phase in new[] { ProtectionPhase.A, ProtectionPhase.B, ProtectionPhase.V })
        {
            var pickupOn = _pickupLatched && _activePhase == phase;
            var tripOn = _tripLatched && _activePhase == phase;
            _class1Queue.Enqueue(DpiTimeTagged(FunPickup, InfForPhase(phase), pickupOn ? DpiOn : DpiOff, cot));
            _class1Queue.Enqueue(DpiTimeTagged(FunTrip, InfForPhase(phase), tripOn ? DpiOn : DpiOff, cot));
        }
    }

    private void EnqueueCurrentSnapshot(byte cot)
    {
        foreach (var asdu in BuildCurrentAsdus(cot))
        {
            _class1Queue.Enqueue(asdu);
        }
    }

    private byte[] BuildAnimatedCurrentAsdu()
    {
        var phase = NextCurrentPhase();
        _counters.CurrentFrames++;
        return Measurand(fun: FunCurrent, inf: InfForPhase(phase), value: CurrentValueForPhase(phase), cot: 2);
    }

    private IEnumerable<byte[]> BuildCurrentAsdus(byte cot)
    {
        yield return Measurand(fun: FunCurrent, inf: 1, value: CurrentValueForPhase(ProtectionPhase.A), cot: cot);
        yield return Measurand(fun: FunCurrent, inf: 2, value: CurrentValueForPhase(ProtectionPhase.B), cot: cot);
        yield return Measurand(fun: FunCurrent, inf: 3, value: CurrentValueForPhase(ProtectionPhase.V), cot: cot);
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

    private async Task SendFixedAsync(int functionCode, bool acd, bool dfc, string summary, string dataClass, CancellationToken cancellationToken)
    {
        var control = (byte)(functionCode & 0x0F);
        if (dfc) control |= 0x10;
        if (acd) control |= 0x20;
        var frame = Ft12FrameBuilder.Fixed(control, _settings.LinkAddress);
        await SendFrameAsync(frame, summary, dataClass, cancellationToken).ConfigureAwait(false);

        if (functionCode == 0) _counters.AckResponses++;
        if (functionCode == 9) _counters.NoDataResponses++;
        if (dfc) _counters.DfcBusyResponses++;
    }

    private async Task SendUserDataAsync(IReadOnlyList<byte> asdu, bool acd, string summary, string dataClass, CancellationToken cancellationToken)
    {
        var control = (byte)0x08;
        if (acd) control |= 0x20;
        var frame = Ft12FrameBuilder.Variable(control, _settings.LinkAddress, asdu);
        await SendFrameAsync(frame, summary, dataClass, cancellationToken).ConfigureAwait(false);
        _counters.UserDataResponses++;
    }

    private async Task SendFrameAsync(byte[] frame, string summary, string dataClass, CancellationToken cancellationToken)
    {
        if (_settings.BadChecksumMode && frame.Length >= 5)
        {
            frame[^2] = unchecked((byte)(frame[^2] + 1));
            _counters.BadChecksumResponses++;
        }

        if (_settings.TurnaroundDelayMs > 0)
        {
            await Task.Delay(_settings.TurnaroundDelayMs, cancellationToken).ConfigureAwait(false);
        }

        await _transport.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        _counters.TxFrames++;
        AddEvent("TX", dataClass, summary, ToHex(frame));
    }

    private byte[] DpiTimeTagged(byte fun, byte inf, byte dpi, byte cot)
    {
        var time = Iec103AsduBuilder.EncodeCp32Time2a(DateTime.Now);
        return new[]
        {
            (byte)0x01, (byte)0x81, cot, _settings.CommonAddress, fun, inf, dpi,
            time[0], time[1], time[2], time[3], time[4]
        };
    }

    private byte[] Measurand(byte fun, byte inf, short value, byte cot)
    {
        return new[]
        {
            (byte)0x09, (byte)0x81, cot, _settings.CommonAddress, fun, inf,
            (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)0x00
        };
    }

    private byte[] Identification(byte fun, byte inf, string text)
    {
        var ascii = System.Text.Encoding.ASCII.GetBytes(text);
        return new[] { (byte)0x05, (byte)0x81, (byte)0x03, _settings.CommonAddress, fun, inf }
            .Concat(ascii.Take(24))
            .ToArray();
    }

    private byte[] GiEnd() => new[]
    {
        (byte)0x08, (byte)0x81, (byte)0x0A, _settings.CommonAddress, (byte)0xFF, (byte)0x00, (byte)0x00
    };

    private void MarkQueueDepth() => _counters.Class1QueueMaxDepth = Math.Max(_counters.Class1QueueMaxDepth, _class1Queue.Count);

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

    private static string PhaseText(ProtectionPhase phase) => phase switch
    {
        ProtectionPhase.A => "phase A",
        ProtectionPhase.B => "phase B",
        ProtectionPhase.V => "phase V/C",
        _ => "phase ?"
    };

    private static string ClassFromRequest(Ft12FrameDecode frame)
    {
        if (frame.LinkControl?.IsPrimaryRequestClass1 == true) return "Class 1";
        if (frame.LinkControl?.IsPrimaryRequestClass2 == true) return "Class 2";
        if (frame.Asdu?.TypeId == 7) return "Class 2";
        if (frame.Asdu?.TypeId == 6) return "Class 2";
        if (frame.Asdu?.TypeId == 20) return "Command";
        return "Link";
    }

    private void AddEvent(string direction, string dataClass, string summary, string detail)
    {
        var item = new Iec103SlaveSimulatorEvent
        {
            TimestampUtc = DateTime.UtcNow,
            Direction = direction,
            DataClass = dataClass,
            Summary = summary,
            Detail = detail,
            RawHex = LooksLikeHex(detail) ? detail : string.Empty
        };

        _events.Add(item);
        if (_events.Count > MaxRetainedEvents)
        {
            _events.RemoveAt(0);
        }

        EventRaised?.Invoke(this, item);
    }

    private static bool LooksLikeHex(string value) => value.Length >= 2 && value.All(c => char.IsWhiteSpace(c) || Uri.IsHexDigit(c));

    private static string ToHex(IReadOnlyList<byte> bytes) => string.Join(" ", bytes.Select(x => x.ToString("X2")));

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
}
