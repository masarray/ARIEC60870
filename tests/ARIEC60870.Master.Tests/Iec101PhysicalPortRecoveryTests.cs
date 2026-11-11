// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>L14/P05 source-to-session fault fixtures; physical adapter FAT remains separate.</summary>
public sealed class Iec101PhysicalPortRecoveryTests
{
    private static Iec103MasterSettings Settings() => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec101,
        PortName = "TEST-RECOVER-COM",
        LinkAddress = 105,
        LinkAddressSize = 2,
        CommonAddress = 105,
        BaudRate = 1200,
        StartupDelayMs = 0,
        ResponseTimeoutMs = 60,
        TimeoutRecoveryBackoffMs = 10,
        Class2PollIntervalMs = 10,
        ResetRemoteLinkOnConnect = false,
        ResetFcbOnConnect = false,
        SendClockSyncOnConnect = false,
        SendGeneralInterrogationOnConnect = false
    };

    [Fact]
    public async Task UnplugDuringClassPollAutomaticallyReopensAndVerifiesFc9BeforeData()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var port = new RecoverablePort(onRecoveredClass2: () => stop.Cancel());
        var session = new Iec101MasterSession(Settings(), port);

        var result = await session.RunAsync(stop.Token);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.Equal(2, port.OpenCount);
        Assert.True(port.CloseCount >= 2);
        Assert.Contains(result.Findings, f => f.Id == "IEC101-COM-PHYSICAL-RECOVERY");
        var epoch2 = port.Requests.Where(x => x.Epoch == 2).ToArray();
        Assert.True(epoch2.Length >= 2, "Recovered COM should verify FC9 before a Class 2 poll.");
        Assert.Equal(9, epoch2[0].Function);
        Assert.Contains(epoch2, x => x.Function == 11);
        Assert.DoesNotContain(epoch2, x => x.Function is 0 or 3 or 7);
    }

    [Fact]
    public async Task InitialPortUnavailableAutomaticallyRetriesWithoutManualSecondStart()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var port = new RecoverablePort(onRecoveredClass2: () => stop.Cancel(), failFirstOpen: true);
        var result = await new Iec101MasterSession(Settings(), port).RunAsync(stop.Token);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.Equal(2, port.OpenCount);
        Assert.Contains(result.Findings, x => x.Id == "IEC101-COM-PHYSICAL-RECOVERY");
        Assert.Equal(9, port.Requests.First().Function);
        Assert.DoesNotContain(port.Requests, x => x.Function is 0 or 3 or 7);
    }

    [Fact]
    public async Task CancelDuringRepeatedPortOpenFailuresIsBoundedAndReleasesSession()
    {
        var settings = Settings();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(320));
        await using var port = new RecoverablePort(alwaysFailOpen: true);
        var result = await new Iec101MasterSession(settings, port).RunAsync(stop.Token);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.InRange(port.OpenCount, 1, 5);
        Assert.Equal(0, result.Counters.TxFrames);
        Assert.Contains(result.Findings, x => x.Id == "IEC101-COM-PHYSICAL-RECOVERY");
    }

    [Fact]
    public async Task ReadIoFailureRecoversEvenIfAdapterStillReportsIsOpen()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var port = new RecoverablePort(
            onRecoveredClass2: () => stop.Cancel(), readFaultWhileOpen: true);
        var result = await new Iec101MasterSession(Settings(), port).RunAsync(stop.Token);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.Equal(2, port.OpenCount);
        Assert.Contains(result.Findings, f => f.Id == "IEC101-COM-PHYSICAL-RECOVERY");
        Assert.Equal(9, port.Requests.First(x => x.Epoch == 2).Function);
    }

    [Fact]
    public async Task ProgrammingInvariantExceptionIsNotMisclassifiedAsPhysicalComUnplug()
    {
        await using var port = new RecoverablePort(failWithProtocolBug: true);
        var result = await new Iec101MasterSession(Settings(), port)
            .RunForAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        Assert.False(result.CompletedNormally);
        Assert.Equal(1, port.OpenCount);
        Assert.Contains(result.Findings, f => f.Id == "IEC101-SESSION-FAULT");
        Assert.DoesNotContain(result.Findings, f => f.Id == "IEC101-COM-PHYSICAL-RECOVERY");
    }

    [Fact]
    public async Task StartupGiAlreadyTransmittedIsNeverReplayedAfterPhysicalComRecovery()
    {
        var settings = Settings();
        settings.SendGeneralInterrogationOnConnect = true;
        settings.Class1DrainDelayMs = 0;
        settings.MaxClass1DrainFrames = 10;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        await using var port = new GiThenUnplugPort(settings, () => stop.Cancel());

        var result = await new Iec101MasterSession(settings, port).RunAsync(stop.Token);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.Equal(2, port.OpenCount);
        Assert.Equal(1, port.GiWrites);
        Assert.Contains(result.Findings, f => f.Id == "IEC101-COM-PHYSICAL-RECOVERY");
        Assert.True(port.RecoveredClass2Writes > 0);
        Assert.True(result.Counters.GiCommands <= 1, "A GI attempted before COM loss must not replay on another physical epoch.");
    }

    private sealed class RecoverablePort : IByteTransport
    {
        private readonly Queue<byte> _rx = new();
        private readonly Action? _onRecoveredClass2;
        private readonly bool _failFirstOpen;
        private readonly bool _alwaysFailOpen;
        private readonly bool _readFaultWhileOpen;
        private readonly bool _failWithProtocolBug;
        private bool _pendingRxIoFault;
        private bool _unplugged;

        public RecoverablePort(Action? onRecoveredClass2 = null,
            bool failFirstOpen = false, bool alwaysFailOpen = false,
            bool readFaultWhileOpen = false, bool failWithProtocolBug = false)
        {
            _onRecoveredClass2 = onRecoveredClass2;
            _failFirstOpen = failFirstOpen;
            _alwaysFailOpen = alwaysFailOpen;
            _readFaultWhileOpen = readFaultWhileOpen;
            _failWithProtocolBug = failWithProtocolBug;
        }

        public int OpenCount { get; private set; }
        public int CloseCount { get; private set; }
        public bool IsOpen { get; private set; }
        public List<(int Epoch, int Function)> Requests { get; } = new();

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            if (_alwaysFailOpen || (_failFirstOpen && OpenCount == 1))
                throw new IOException("Synthetic USB adapter is disconnected.");
            _rx.Clear();
            IsOpen = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            CloseCount++;
            _rx.Clear();
            IsOpen = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsOpen) throw new IOException("Synthetic COM disconnected.");
            var frame = buffer.Span;
            if (frame.Length < 2 || frame[0] != 0x10)
                throw new InvalidOperationException("Fixture expects fixed FT1.2 polling.");
            var fc = frame[1] & 0x0F;
            Requests.Add((OpenCount, fc));
            if (fc == 9)
            {
                foreach (var b in Convert.FromHexString("100B69007416")) _rx.Enqueue(b);
            }
            else if (fc == 11 && OpenCount == 1 && !_unplugged)
            {
                if (_failWithProtocolBug)
                    throw new InvalidOperationException("Synthetic scheduler invariant failure while COM remains open.");
                _unplugged = true;
                if (_readFaultWhileOpen)
                {
                    _pendingRxIoFault = true;
                    return ValueTask.CompletedTask;
                }
                IsOpen = false;
                throw new IOException("Synthetic USB unplug during Class 2 transmission.");
            }
            else if (fc == 11)
            {
                _rx.Enqueue(0xE5);
                _onRecoveredClass2?.Invoke();
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_pendingRxIoFault)
            {
                _pendingRxIoFault = false;
                throw new IOException("Synthetic USB driver I/O loss while SerialPort.IsOpen is still true.");
            }
            if (!IsOpen) throw new IOException("COM unavailable during RX.");
            if (_rx.Count == 0) return ValueTask.FromResult(0);
            buffer.Span[0] = _rx.Dequeue();
            return ValueTask.FromResult(1);
        }

        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    // Independent IEC-101 simulation with a physical disconnect after the
    // first startup GI and before the following Class2 confirmation.
    private sealed class GiThenUnplugPort : IByteTransport
    {
        private readonly SimulatedIec101Transport _peer;
        private readonly Ft12Parser _parser;
        private readonly Action _onRecoveredClass2;
        private bool _unplugged;

        public GiThenUnplugPort(Iec103MasterSettings settings, Action onRecoveredClass2)
        {
            _peer = new SimulatedIec101Transport(settings);
            _parser = Ft12Parser.ForIec101(settings.LinkAddressSize);
            _onRecoveredClass2 = onRecoveredClass2;
        }

        public bool IsOpen => _peer.IsOpen;
        public int OpenCount { get; private set; }
        public int GiWrites { get; private set; }
        public int RecoveredClass2Writes { get; private set; }

        public ValueTask OpenAsync(CancellationToken token)
        {
            OpenCount++;
            return _peer.OpenAsync(token);
        }

        public ValueTask CloseAsync(CancellationToken token) => _peer.CloseAsync(token);

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            var decoded = _parser.Decode(bytes.ToArray());
            if (decoded.AsduBytes.Count > 0 && decoded.AsduBytes[0] == 100)
                GiWrites++;
            var function = decoded.LinkControl?.FunctionCode;
            if (decoded.Format == Ft12FrameFormat.FixedLength && function == 11)
            {
                if (OpenCount == 1 && GiWrites > 0 && !_unplugged)
                {
                    _unplugged = true;
                    await _peer.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    throw new IOException("Synthetic physical unplug after startup GI.");
                }
                if (OpenCount >= 2)
                {
                    RecoveredClass2Writes++;
                    _onRecoveredClass2();
                }
            }
            await _peer.WriteAsync(bytes, token).ConfigureAwait(false);
        }

        public ValueTask<int> ReadAsync(Memory<byte> bytes, CancellationToken token)
            => _peer.ReadAsync(bytes, token);

        public void Dispose() => _peer.Dispose();
        public ValueTask DisposeAsync() => _peer.DisposeAsync();
    }
}
