// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master;
using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101StartupRemoteLinkResetDefaultsTests
{
    [Theory]
    [InlineData(Iec60870ProtocolMode.Iec101, true)]
    [InlineData(Iec60870ProtocolMode.Iec103, false)]
    [InlineData(Iec60870ProtocolMode.Iec104, false)]
    public void DefaultResetPolicyIsProtocolScoped(Iec60870ProtocolMode mode, bool expected)
    {
        var settings = Iec103MasterSettings.CreateDefault();
        settings.ProtocolMode = mode;
        Assert.Equal(expected, settings.ResetRemoteLinkOnConnect);
        Assert.False(settings.ResetFcbOnConnect);
        Assert.False(settings.SendClockSyncOnConnect);
    }

    [Fact]
    public void ExplicitOperatorOptOutSurvivesModeChangeAndReportSnapshot()
    {
        var settings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            ResetRemoteLinkOnConnect = false
        };
        Assert.False(settings.ResetRemoteLinkOnConnect);
        settings.ProtocolMode = Iec60870ProtocolMode.Iec103;
        settings.ProtocolMode = Iec60870ProtocolMode.Iec101;
        Assert.False(settings.ResetRemoteLinkOnConnect);
        Assert.False(settings.CreateReportSnapshot().ResetRemoteLinkOnConnect);

        settings.ResetRemoteLinkOnConnect = true;
        Assert.True(settings.CreateReportSnapshot().ResetRemoteLinkOnConnect);
    }

    [Fact]
    public async Task NewIec101SingleLinkTransmitsFc0AtStartupAndReachesClass2()
    {
        var settings = NewSettings();
        await using var port = new ObservingPort(new SimulatedIec101Transport(settings));
        var result = await new Iec101MasterSession(settings, port)
            .RunForAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.True(settings.ResetRemoteLinkOnConnect);
        Assert.NotEmpty(port.Functions);
        Assert.Equal(0, port.Functions[0]);
        Assert.Contains(11, port.Functions);
        Assert.True(result.Counters.RxFrames > 0);
        Assert.Equal(0, result.Counters.GiCommands);
    }

    [Fact]
    public async Task ExplicitOffUsesStatusProbeWithoutImplicitRemoteReset()
    {
        var settings = NewSettings();
        settings.ResetRemoteLinkOnConnect = false;
        await using var port = new ObservingPort(new SimulatedIec101Transport(settings));
        var result = await new Iec101MasterSession(settings, port)
            .RunForAsync(TimeSpan.FromMilliseconds(400), CancellationToken.None);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.NotEmpty(port.Functions);
        Assert.Equal(9, port.Functions[0]);
        Assert.DoesNotContain(0, port.Functions);
    }

    [Fact]
    public async Task NewIec101DualLinkResetsActiveOnlyAndLeavesStandbyStatusOnly()
    {
        var settings = NewSettings();
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "A", PortName = "SIM-A", LinkAddress = 105 },
            LinkB = new Iec101DualLinkEndpoint { Name = "B", PortName = "SIM-B", LinkAddress = 105 },
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(100),
            RecoveryBackoff = TimeSpan.FromMilliseconds(100),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled
        };
        await using var a = new ObservingPort(new SimulatedIec101Transport(options.LinkA.ApplyTo(settings)));
        await using var b = new ObservingPort(new SimulatedIec101Transport(options.LinkB.ApplyTo(settings)));
        var result = await new Iec101DualLinkRedundancySession(options, a, b)
            .RunForAsync(TimeSpan.FromMilliseconds(700), CancellationToken.None);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.Contains(0, a.Functions);
        Assert.Contains(11, a.Functions);
        Assert.Contains(9, b.Functions);
        Assert.DoesNotContain(0, b.Functions);
        Assert.DoesNotContain(10, b.Functions);
        Assert.DoesNotContain(11, b.Functions);
        Assert.Equal(0, result.Counters.GiCommands);
    }

    private static Iec103MasterSettings NewSettings() => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec101,
        PortName = "SIM-A",
        BaudRate = 9600,
        LinkAddress = 105,
        LinkAddressSize = 2,
        CommonAddress = 105,
        ResponseTimeoutMs = 120,
        StartupDelayMs = 0,
        Class2PollIntervalMs = 40,
        SendGeneralInterrogationOnConnect = false,
        SendClockSyncOnConnect = false
    };

    private sealed class ObservingPort(IByteTransport inner) : IByteTransport
    {
        public List<int> Functions { get; } = new();
        public bool IsOpen => inner.IsOpen;

        public ValueTask OpenAsync(CancellationToken token) => inner.OpenAsync(token);
        public ValueTask CloseAsync(CancellationToken token) => inner.CloseAsync(token);
        public ValueTask<int> ReadAsync(Memory<byte> data, CancellationToken token)
            => inner.ReadAsync(data, token);

        public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken token)
        {
            if (data.Length > 1 && data.Span[0] is 0x10 or 0x68)
                Functions.Add(data.Span[1] & 0x0f);
            return inner.WriteAsync(data, token);
        }

        public void Dispose() => inner.Dispose();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
