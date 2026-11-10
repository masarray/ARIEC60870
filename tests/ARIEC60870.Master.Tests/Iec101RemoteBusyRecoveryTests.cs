// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Master;
using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Model;
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
        var snapshot = session.CreateSnapshot();

        Assert.True(active.Class2Requests > 0, "Fixture must reach busy state through a Class 2 response.");
        Assert.True(active.StatusProbes >= 3, "Busy active link must recover using addressed status probes.");
        Assert.Equal(0, active.DataSentWhileBusy);
        Assert.Equal(0, standby.Class2Requests);
        Assert.True(snapshot.LinkA.IsHealthy, "Active link should recover after confirmed DFC=0.");
        Assert.True(snapshot.LinkB.IsHealthy, "Independent healthy standby should remain supervised.");
    }

    private sealed class RemoteFlowTransport : IByteTransport
    {
        private readonly bool _busyOnStartup;
        private readonly bool _busyAfterFirstClass2;
        private readonly Queue<byte> _rx = new();
        private readonly Ft12Parser _parser = Ft12Parser.ForIec101(2);
        private bool _remoteBusy;
        private int _busyStatusReplies;

        public RemoteFlowTransport(bool busyOnStartup, bool busyAfterFirstClass2 = false)
        {
            _busyOnStartup = busyOnStartup;
            _busyAfterFirstClass2 = busyAfterFirstClass2;
            _remoteBusy = busyOnStartup;
        }

        public bool IsOpen { get; private set; }
        public int StatusProbes { get; private set; }
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
                if (_remoteBusy && ++_busyStatusReplies >= 3)
                    _remoteBusy = false;
                Enqueue(Ft12FrameBuilder.Fixed(_remoteBusy ? (byte)0x1B : (byte)0x0B, 105, 2));
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
                DataRequests++;
                if (_remoteBusy) DataSentWhileBusy++;
                Enqueue(fc == 3 ? [0xE5] : Ft12FrameBuilder.Fixed(0x09, 105, 2));
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
