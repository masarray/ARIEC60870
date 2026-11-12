// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>L14/P05 independent physical A/B recovery, not hardware interoperability proof.</summary>
public sealed class Iec101DualLinkPhysicalRecoveryTests
{
    private static Iec103MasterSettings Settings() => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec101,
        LinkAddress = 1,
        LinkAddressSize = 1,
        CommonAddress = 1,
        BaudRate = 9600,
        ResponseTimeoutMs = 100,
        Class2PollIntervalMs = 30,
        StartupDelayMs = 0,
        TimeoutRecoveryBackoffMs = 10,
        ResetRemoteLinkOnConnect = false,
        ResetFcbOnConnect = false,
        SendGeneralInterrogationOnConnect = false,
        SendClockSyncOnConnect = false
    };

    private static Iec101DualLinkRedundancyOptions Options(Iec103MasterSettings settings) => new()
    {
        BaseSettings = settings,
        LinkA = new Iec101DualLinkEndpoint { Name = "A", PortName = "TEST-A", LinkAddress = 1 },
        LinkB = new Iec101DualLinkEndpoint { Name = "B", PortName = "TEST-B", LinkAddress = 1 },
        StandbySupervisionInterval = TimeSpan.FromMilliseconds(70),
        RecoveryBackoff = TimeSpan.FromMilliseconds(100),
        AntiPingPongWindow = TimeSpan.FromMilliseconds(200),
        PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled
    };

    [Fact]
    public async Task ActiveAUnplugDuringClass2PreservesHealthyBAndReopensA()
    {
        var settings = Settings();
        var options = Options(settings);
        await using var a = new FlakySimulatedPort(options.LinkA.ApplyTo(settings), unplugOnFirstClass2: true);
        await using var b = new FlakySimulatedPort(options.LinkB.ApplyTo(settings));
        var session = new Iec101DualLinkRedundancySession(options, a, b);

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(1600), CancellationToken.None);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.True(a.WasUnplugged);
        Assert.True(a.OpenCount >= 2, "Failed A port must be reopened without another user Start.");
        Assert.True(b.Class2Writes > 0, "Validated healthy B should continue application polling.");
        Assert.Contains(result.Findings, f => f.Id == "IEC101-DUAL-COM-PHYSICAL-RECOVERY");
        Assert.Contains(result.Events, e => e.Summary.Contains("COM reopened", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, result.Counters.GiCommands);
    }

    [Fact]
    public async Task MissingStandbyOnStartupDoesNotKillHealthyActiveMaster()
    {
        var settings = Settings();
        var options = Options(settings);
        await using var a = new FlakySimulatedPort(options.LinkA.ApplyTo(settings));
        await using var b = new FlakySimulatedPort(options.LinkB.ApplyTo(settings), failFirstOpen: true);
        var session = new Iec101DualLinkRedundancySession(options, a, b);

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(1200), CancellationToken.None);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.True(a.Class2Writes > 0, "Healthy A must keep polling without B.");
        Assert.True(b.OpenCount >= 2);
        Assert.True(b.StatusWrites > 0, "Reopened B requires addressed FC9 supervision.");
        Assert.Contains(result.Findings, f => f.Id == "IEC101-DUAL-COM-PHYSICAL-RECOVERY");
        Assert.Equal(0, result.Counters.GiCommands);
    }

    [Fact]
    public async Task BothComPortsAbsentRemainsCancellableWithoutTransmitting()
    {
        var settings = Settings();
        var options = Options(settings);
        await using var a = new FlakySimulatedPort(options.LinkA.ApplyTo(settings), alwaysFailOpen: true);
        await using var b = new FlakySimulatedPort(options.LinkB.ApplyTo(settings), alwaysFailOpen: true);
        var session = new Iec101DualLinkRedundancySession(options, a, b);

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(370), CancellationToken.None);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.Equal(0, result.Counters.TxFrames);
        Assert.InRange(a.OpenCount, 2, 4);
        Assert.InRange(b.OpenCount, 2, 4);
        Assert.Contains(result.Findings, f => f.Id == "IEC101-DUAL-COM-PHYSICAL-RECOVERY");
    }

    private sealed class FlakySimulatedPort : IByteTransport
    {
        private readonly SimulatedIec101Transport _peer;
        private readonly bool _unplugOnFirstClass2;
        private readonly bool _failFirstOpen;
        private readonly bool _alwaysFailOpen;

        public FlakySimulatedPort(Iec103MasterSettings settings, bool unplugOnFirstClass2 = false,
            bool failFirstOpen = false, bool alwaysFailOpen = false)
        {
            _peer = new SimulatedIec101Transport(settings);
            _unplugOnFirstClass2 = unplugOnFirstClass2;
            _failFirstOpen = failFirstOpen;
            _alwaysFailOpen = alwaysFailOpen;
        }

        public bool IsOpen => _peer.IsOpen;
        public int OpenCount { get; private set; }
        public int StatusWrites { get; private set; }
        public int Class2Writes { get; private set; }
        public bool WasUnplugged { get; private set; }

        public ValueTask OpenAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            OpenCount++;
            if (_alwaysFailOpen || (_failFirstOpen && OpenCount == 1))
                throw new IOException("Synthetic USB COM absent");
            return _peer.OpenAsync(token);
        }

        public ValueTask CloseAsync(CancellationToken token) => _peer.CloseAsync(token);

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            var message = bytes.ToArray();
            var fc = message.Length > 1 && message[0] == 0x10
                ? message[1] & 0x0f : -1;
            if (fc == 9) StatusWrites++;
            if (fc == 11)
            {
                Class2Writes++;
                if (_unplugOnFirstClass2 && !WasUnplugged)
                {
                    WasUnplugged = true;
                    await _peer.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    throw new IOException("Synthetic physical COM unplug during class 2 write.");
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
