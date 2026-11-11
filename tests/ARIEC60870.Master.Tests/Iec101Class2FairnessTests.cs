// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Master;
using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>
/// IEC101-R08 / S02: a permanently asserted ACD cannot starve Class 2
/// on either physical owner. This is a deterministic fake peer, not
/// a claim about 1200-bps physical FIFO capacity or NUC certification.
/// </summary>
public sealed class Iec101Class2FairnessTests
{
    private static Iec103MasterSettings Settings() => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec101,
        PortName = "FAKE-A",
        UseSimulatedSlave = true,
        LinkAddress = 105,
        LinkAddressSize = 1,
        CommonAddress = 105,
        CauseOfTransmissionSize = 2,
        CommonAddressSize = 2,
        InformationObjectAddressSize = 3,
        ResponseTimeoutMs = 140,
        StartupDelayMs = 0,
        ResetRemoteLinkOnConnect = false,
        ResetFcbOnConnect = false,
        SendGeneralInterrogationOnConnect = false,
        SendClockSyncOnConnect = false,
        RequestClass2ImmediatelyAfterStartup = false,
        Class2PollIntervalMs = 1,
        Class1DrainDelayMs = 0,
        MaxClass1DrainFrames = 24,
        MaxConsecutiveClass1BeforeClass2 = 3,
        MaxRetainedEvidenceEvents = 400
    };

    [Fact]
    public async Task SingleLinkKeepsClass2AliveDuringPermanentAcdBurst()
    {
        var settings = Settings();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var active = new PermanentAcdResponder(settings, stopOnClass2: stop);
        var result = await new Iec101MasterSession(settings, active).RunAsync(stop.Token);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.Equal(3, active.Class1RequestsBeforeFirstClass2);
        Assert.Equal(1, active.Class2Requests);
        Assert.True(result.Counters.Class1Requests >= 3);
        Assert.True(result.Counters.Class2Requests >= 1);
        Assert.Equal(0, active.GiOrControlRequests);
    }

    [Fact]
    public async Task DualLinkKeepsClass2OnActiveOwnerAndStandbyStatusOnly()
    {
        var settings = Settings();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var active = new PermanentAcdResponder(settings, stopOnClass2: stop);
        await using var standby = new PermanentAcdResponder(settings, standbyOnly: true);
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint
                { Name = "A", PortName = "FAKE-A", LinkAddress = 105 },
            LinkB = new Iec101DualLinkEndpoint
                { Name = "B", PortName = "FAKE-B", LinkAddress = 105 },
            StandbySupervisionInterval = TimeSpan.FromSeconds(1),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled
        };
        var result = await new Iec101DualLinkRedundancySession(options, active, standby)
            .RunAsync(stop.Token);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.Equal(3, active.Class1RequestsBeforeFirstClass2);
        Assert.Equal(1, active.Class2Requests);
        Assert.True(result.Counters.Class2Requests >= 1);
        Assert.Equal(0, standby.Class1Requests);
        Assert.Equal(0, standby.Class2Requests);
        Assert.True(standby.LinkStatusRequests > 0);
        Assert.Equal(0, active.GiOrControlRequests + standby.GiOrControlRequests);
    }


    [Fact]
    public async Task PermanentAcdAlsoPreservesPeriodicStandbyFc9Supervision()
    {
        var settings = Settings();
        settings.Class1DrainDelayMs = 110; // Three Class 1 replies cross the 250-ms standby deadline.
        settings.ResponseTimeoutMs = 250;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var active = new PermanentAcdResponder(settings, stopOnClass2: stop);
        await using var standby = new PermanentAcdResponder(settings, standbyOnly: true);
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint
                { Name = "A", PortName = "FAKE-A", LinkAddress = 105 },
            LinkB = new Iec101DualLinkEndpoint
                { Name = "B", PortName = "FAKE-B", LinkAddress = 105 },
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(250),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled
        };
        var result = await new Iec101DualLinkRedundancySession(options, active, standby)
            .RunAsync(stop.Token);
        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.Equal(3, active.Class1RequestsBeforeFirstClass2);
        Assert.Equal(1, active.Class2Requests);
        Assert.True(standby.LinkStatusRequests >= 2,
            "Standby FC9 must be refreshed even with a permanently asserted active ACD.");
        Assert.Equal(0, standby.Class1Requests);
        Assert.Equal(0, standby.Class2Requests);
        Assert.Equal(0, standby.GiOrControlRequests);
    }

    private sealed class PermanentAcdResponder : IByteTransport
    {
        private readonly Iec103MasterSettings _settings;
        private readonly CancellationTokenSource? _stopOnClass2;
        private readonly bool _standbyOnly;
        private readonly Queue<byte> _bytes = new();
        private readonly Ft12Parser _parser;
        public PermanentAcdResponder(Iec103MasterSettings settings,
            CancellationTokenSource? stopOnClass2 = null, bool standbyOnly = false)
        {
            _settings = settings;
            _stopOnClass2 = stopOnClass2;
            _standbyOnly = standbyOnly;
            _parser = Ft12Parser.ForIec101(settings.LinkAddressSize);
        }

        public bool IsOpen { get; private set; }
        public int Class1Requests { get; private set; }
        public int Class2Requests { get; private set; }
        public int Class1RequestsBeforeFirstClass2 { get; private set; } = -1;
        public int LinkStatusRequests { get; private set; }
        public int GiOrControlRequests { get; private set; }

        public ValueTask OpenAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            IsOpen = true;
            return ValueTask.CompletedTask;
        }
        public ValueTask CloseAsync(CancellationToken ct)
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsOpen) throw new InvalidOperationException("Scripted COM is closed.");
            var f = _parser.Decode(data.ToArray());
            var fc = f.LinkControl?.FunctionCode ?? -1;
            byte[] response;
            if (fc == 9)
            {
                LinkStatusRequests++;
                response = Ft12FrameBuilder.Fixed(
                    (byte)(_standbyOnly ? 0x0B : 0x2B), _settings.LinkAddress,
                    _settings.LinkAddressSize);
            }
            else if (fc == 10 && !_standbyOnly)
            {
                Class1Requests++;
                response = Ft12FrameBuilder.Variable(0x28, _settings.LinkAddress,
                    Iec10xAsduBuilder.SinglePoint(_settings, ioa: 201, value: true, cause: 3),
                    _settings.LinkAddressSize);
            }
            else if (fc == 11 && !_standbyOnly)
            {
                Class2Requests++;
                if (Class1RequestsBeforeFirstClass2 < 0)
                    Class1RequestsBeforeFirstClass2 = Class1Requests;
                response = Ft12FrameBuilder.Fixed(0x29, _settings.LinkAddress,
                    _settings.LinkAddressSize);
            }
            else
            {
                GiOrControlRequests++;
                throw new InvalidOperationException(
                    "Unexpected command or Class poll on standby; FC=" + fc);
            }
            foreach (var octet in response) _bytes.Enqueue(octet);
            if (fc == 11) _stopOnClass2?.Cancel();
            return ValueTask.CompletedTask;
        }
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_bytes.Count == 0) return ValueTask.FromResult(0);
            buffer.Span[0] = _bytes.Dequeue();
            return ValueTask.FromResult(1);
        }
        public void Dispose() { IsOpen = false; _bytes.Clear(); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
