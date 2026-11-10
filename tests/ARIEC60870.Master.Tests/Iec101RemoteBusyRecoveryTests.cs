// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Master;
using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101RemoteBusyRecoveryTests
{
    private static Iec103MasterSettings Settings() => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec101,
        PortName = "FAKE-COM-A",
        BaudRate = 1200,
        LinkAddress = 105,
        LinkAddressSize = 2,
        CommonAddress = 105,
        ResponseTimeoutMs = 90,
        BusyBackoffMs = 120,
        StartupDelayMs = 0,
        Class2PollIntervalMs = 40,
        ResetRemoteLinkOnConnect = false,
        ResetFcbOnConnect = false,
        SendGeneralInterrogationOnConnect = false,
        SendClockSyncOnConnect = false
    };

    [Fact]
    public async Task SingleLinkBusyStatusDefersQueuedCommandUntilFc11DfcClears()
    {
        await using var transport = new RemoteFlowTransport(busyOnStartup: true);
        var session = new Iec101MasterSession(Settings(), transport);
        session.QueueControlCommand(new Iec60870ControlCommandRequest
        {
            Kind = Iec60870ControlCommandKind.Read,
            InformationObjectAddress = 7
        });

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(850), CancellationToken.None);

        Assert.True(transport.StatusProbes >= 3);
        Assert.True(transport.DataRequests > 0, "The pending command or polling did not resume after DFC cleared.");
        Assert.Equal(0, transport.DataSentWhileBusy);
        Assert.Contains(result.Events, e => e.Summary.Contains("DFC=1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Events, e => e.Summary.Contains("DFC cleared", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Findings, f => f.Id == "IEC101-LINK-LOST");
    }

    [Fact]
    public async Task UnaddressedStartupResetAckCannotSkipFc9OrDispatchGi()
    {
        var settings = Settings();
        settings.ResetRemoteLinkOnConnect = true;
        settings.SendGeneralInterrogationOnConnect = true;
        await using var transport = new RemoteFlowTransport(busyOnStartup: false, unaddressedStatusOnly: true);
        var result = await new Iec101MasterSession(settings, transport)
            .RunForAsync(TimeSpan.FromMilliseconds(370), CancellationToken.None);

        Assert.True(transport.StatusProbes > 0, "An E5 reset ACK must not suppress FC9 status verification.");
        Assert.Equal(0, result.Counters.GiCommands);
        Assert.Equal(0, transport.DataRequests);
    }

    [Fact]
    public async Task DualUnaddressedResetAckMustNotEnableUnverifiedActiveGi()
    {
        var settings = Settings();
        settings.ResetRemoteLinkOnConnect = true;
        settings.SendGeneralInterrogationOnConnect = true;
        await using var unverified = new RemoteFlowTransport(busyOnStartup: false, unaddressedStatusOnly: true);
        await using var healthy = new RemoteFlowTransport(busyOnStartup: false);
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "A", PortName = "FAKE-COM-A", LinkAddress = 105 },
            LinkB = new Iec101DualLinkEndpoint { Name = "B", PortName = "FAKE-COM-B", LinkAddress = 105 },
            PreferredActiveLink = "A"
        };
        var result = await new Iec101DualLinkRedundancySession(options, unverified, healthy)
            .RunForAsync(TimeSpan.FromMilliseconds(370), CancellationToken.None);

        Assert.True(unverified.StatusProbes > 0);
        Assert.Equal(0, unverified.DataRequests);
        Assert.DoesNotContain(result.Events, e => e.Summary.Contains("GI", StringComparison.OrdinalIgnoreCase)
            && e.Direction == ARIEC60870.Core.Model.FrameDirection.MasterToSlave
            && e.DataClass.Contains("A/Active", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PreStartupControlCannotExecuteOnLaterFailoverOwnerWithoutFreshAuthorization()
    {
        var settings = Settings();
        await using var unverified = new RemoteFlowTransport(busyOnStartup: false, unaddressedStatusOnly: true);
        await using var standby = new RemoteFlowTransport(busyOnStartup: false);
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "A", PortName = "FAKE-COM-A", LinkAddress = 105 },
            LinkB = new Iec101DualLinkEndpoint { Name = "B", PortName = "FAKE-COM-B", LinkAddress = 105 },
            PreferredActiveLink = "A",
            ActiveFailureThreshold = 1,
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled,
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(250)
        };
        var session = new Iec101DualLinkRedundancySession(options, unverified, standby);
        session.QueueControlCommand(new Iec60870ControlCommandRequest
        {
            Kind = Iec60870ControlCommandKind.SingleCommand, InformationObjectAddress = 7, Value = 1
        });
        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(1000), CancellationToken.None);

        Assert.True(unverified.StatusProbes > 0);
        Assert.Equal(0, unverified.DataRequests);
        Assert.Equal(0, standby.ControlRequests);
        Assert.Contains(result.Findings, f => f.Id == "IEC101-DUAL-COMMAND-STALE-OWNER");
    }

    [Fact]
    public async Task SilentStandbyCannotBePromotedFromOldAddressedStatusProof()
    {
        var settings = Settings();
        await using var unverified = new RemoteFlowTransport(busyOnStartup: false, unaddressedStatusOnly: true);
        await using var standby = new RemoteFlowTransport(busyOnStartup: false, stopRespondingToStatusAfter: 1);
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "A", PortName = "FAKE-COM-A", LinkAddress = 105 },
            LinkB = new Iec101DualLinkEndpoint { Name = "B", PortName = "FAKE-COM-B", LinkAddress = 105 },
            PreferredActiveLink = "A",
            ActiveFailureThreshold = 1,
            StandbyFailureThreshold = 2,
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled
        };
        var session = new Iec101DualLinkRedundancySession(options, unverified, standby);
        await session.RunForAsync(TimeSpan.FromMilliseconds(850), CancellationToken.None);

        Assert.True(standby.StatusProbes >= 2);
        Assert.Equal(0, unverified.DataRequests);
        Assert.Equal(0, standby.DataRequests);
        Assert.DoesNotContain(session.FailoverJournal, x => x.Completed);
    }

    [Fact]
    public async Task DualLinkBusyActiveUsesOnlyStatusProbesAndKeepsHealthyStandby()
    {
        var settings = Settings();
        await using var active = new RemoteFlowTransport(busyOnStartup: false, busyAfterFirstClass2: true);
        await using var standby = new RemoteFlowTransport(busyOnStartup: false);
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "A", PortName = "FAKE-COM-A", LinkAddress = 105 },
            LinkB = new Iec101DualLinkEndpoint { Name = "B", PortName = "FAKE-COM-B", LinkAddress = 105 },
            PreferredActiveLink = "A",
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(250)
        };
        var session = new Iec101DualLinkRedundancySession(options, active, standby);

        await session.RunForAsync(TimeSpan.FromMilliseconds(1000), CancellationToken.None);
        // After RunForAsync the COM transports have closed, so a final
        // IsHealthy snapshot must be false. Prove live recovery by resumed
        // Class 2 traffic and independent standby status evidence instead.
        Assert.True(active.Class2Requests >= 2, "Class 2 must resume after a valid FC11 DFC=0.");
        Assert.True(active.StatusProbes >= 3, "Busy active link must recover using addressed status probes.");
        Assert.Equal(0, active.DataSentWhileBusy);
        Assert.True(standby.StatusProbes > 0, "Standby status supervision must remain independent.");
        Assert.Equal(0, standby.Class2Requests);
    }

    private sealed class RemoteFlowTransport : IByteTransport
    {
        private readonly bool _busyOnStartup;
        private readonly bool _busyAfterFirstClass2;
        private readonly bool _unaddressedStatusOnly;
        private readonly int _stopRespondingToStatusAfter;
        private readonly Queue<byte> _rx = new();
        private readonly Ft12Parser _parser = Ft12Parser.ForIec101(2);
        private bool _remoteBusy;
        private int _busyStatusReplies;

        public RemoteFlowTransport(bool busyOnStartup, bool busyAfterFirstClass2 = false,
            bool unaddressedStatusOnly = false, int stopRespondingToStatusAfter = int.MaxValue)
        {
            _busyOnStartup = busyOnStartup;
            _busyAfterFirstClass2 = busyAfterFirstClass2;
            _unaddressedStatusOnly = unaddressedStatusOnly;
            _stopRespondingToStatusAfter = stopRespondingToStatusAfter;
            _remoteBusy = busyOnStartup;
        }

        public bool IsOpen { get; private set; }
        public int StatusProbes { get; private set; }
        public int ControlRequests { get; private set; }
        public int Class2Requests { get; private set; }
        public int DataRequests { get; private set; }
        public int DataSentWhileBusy { get; private set; }

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsOpen = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            IsOpen = false;
            _rx.Clear();
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = _parser.Decode(data.ToArray());
            var fc = frame.LinkControl?.FunctionCode;
            if (fc == 9)
            {
                StatusProbes++;
                if (StatusProbes > _stopRespondingToStatusAfter)
                    return ValueTask.CompletedTask; // Silent standby after one genuine FC11.
                if (_unaddressedStatusOnly)
                {
                    Enqueue(new byte[] { 0xE5 });
                }
                else
                {
                    if (_remoteBusy && ++_busyStatusReplies >= 3)
                        _remoteBusy = false;
                    Enqueue(Ft12FrameBuilder.Fixed(_remoteBusy ? (byte)0x1B : (byte)0x0B, 105, 2));
                }
            }
            else if (fc == 11)
            {
                Class2Requests++;
                DataRequests++;
                if (_remoteBusy) DataSentWhileBusy++;
                if (_busyAfterFirstClass2 && Class2Requests == 1)
                    _remoteBusy = true;
                Enqueue(Ft12FrameBuilder.Fixed(_remoteBusy ? (byte)0x19 : (byte)0x09, 105, 2));
            }
            else if (fc is 10 or 3)
            {
                if (fc == 3) ControlRequests++;
                DataRequests++;
                if (_remoteBusy) DataSentWhileBusy++;
                Enqueue(fc == 3 ? new byte[] { 0xE5 } : Ft12FrameBuilder.Fixed(0x09, 105, 2));
            }
            else if (fc == 0 || fc == 7)
            {
                Enqueue([0xE5]);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> ReadAsync(Memory<byte> data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_rx.Count == 0) return ValueTask.FromResult(0);
            data.Span[0] = _rx.Dequeue();
            return ValueTask.FromResult(1);
        }

        private void Enqueue(byte[] bytes)
        {
            foreach (var b in bytes) _rx.Enqueue(b);
        }

        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
    }
}
