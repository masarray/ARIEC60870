// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Master;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101SicamNoDataRecoveryTests
{
    private static Iec103MasterSettings Settings() => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec101,
        PortName = "SICAM-TEST",
        LinkAddress = 105,
        LinkAddressSize = 2,
        CommonAddress = 105,
        CommonAddressSize = 2,
        CauseOfTransmissionSize = 2,
        InformationObjectAddressSize = 3,
        ResponseTimeoutMs = 40,
        Class2PollIntervalMs = 30,
        StartupDelayMs = 0,
        SendGeneralInterrogationOnConnect = true,
        SendClockSyncOnConnect = false,
        MaxClass1DrainFrames = 64
    };

    [Fact]
    public async Task OpenComWithoutAnySecondaryReplyCannotClaimConnectedOrSendGi()
    {
        var settings = Settings();
        await using var transport = new SelectiveSecondaryTransport(settings, respondToLinkStatus: false);
        var result = await new Iec101MasterSession(settings, transport)
            .RunForAsync(TimeSpan.FromMilliseconds(260), CancellationToken.None);

        Assert.Equal(0, result.Counters.TimedResponses);
        Assert.Equal(0, result.Counters.GiCommands);
        Assert.Equal(0, result.Counters.Class1Requests);
        Assert.Equal(0, result.Counters.Class2Requests);
        Assert.Contains(result.Findings, f => f.Id == "IEC101-LINK-UNVERIFIED");
        Assert.DoesNotContain(result.Events, e => e.Summary == "IEC-101 secondary link verified");
        Assert.Contains(result.Events, e => e.Summary.Contains("COM open", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RealSecondaryLinkButSilentGiDoesNotBlindlyDrainSixtyFourClassOnePolls()
    {
        var settings = Settings();
        await using var transport = new SelectiveSecondaryTransport(settings, respondToLinkStatus: true);
        var result = await new Iec101MasterSession(settings, transport)
            .RunForAsync(TimeSpan.FromMilliseconds(330), CancellationToken.None);

        Assert.True(result.Counters.TimedResponses >= 1);
        Assert.Equal(1, result.Counters.GiCommands);
        Assert.Equal(0, result.Counters.Class1Requests);
        Assert.Contains(result.Findings, f => f.Id == "IEC101-GI-UNCONFIRMED");
        Assert.Contains(result.Events, e => e.Summary == "IEC-101 secondary link verified");
        Assert.DoesNotContain(result.Events, e => e.Summary.Contains("GI follow-up drain started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifiedLinkRetainsCompleteGiAndRealProcessData()
    {
        var settings = Settings();
        settings.ResponseTimeoutMs = 200;
        await using var transport = new SimulatedIec101Transport(settings);
        var result = await new Iec101MasterSession(settings, transport)
            .RunForAsync(TimeSpan.FromMilliseconds(850), CancellationToken.None);

        Assert.True(result.Counters.GiCommands >= 1);
        Assert.True(result.Counters.GiEndResponses >= 1);
        Assert.True(result.Counters.UserDataResponses >= 1);
        Assert.Contains(result.Events, e => e.Summary == "IEC-101 secondary link verified");
        Assert.Contains(result.Events, e => e.IsRelayValue && e.InformationObjectAddress == 101);
        Assert.Contains(result.Events, e => e.IsRelayValue && e.InformationObjectAddress == 1001);
        Assert.DoesNotContain(result.Findings, f => f.Id == "IEC101-GI-UNCONFIRMED");
    }

    private sealed class SelectiveSecondaryTransport : IByteTransport
    {
        private readonly Ft12Parser _parser;
        private readonly int _link;
        private readonly bool _respondToLinkStatus;
        private readonly Queue<byte> _rx = new();

        public SelectiveSecondaryTransport(Iec103MasterSettings settings, bool respondToLinkStatus)
        {
            _link = settings.LinkAddress;
            _parser = new Ft12Parser(settings.LinkAddressSize);
            _respondToLinkStatus = respondToLinkStatus;
        }

        public bool IsOpen { get; private set; }
        public ValueTask OpenAsync(CancellationToken token) { IsOpen = true; return ValueTask.CompletedTask; }
        public ValueTask CloseAsync(CancellationToken token) { IsOpen = false; return ValueTask.CompletedTask; }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken token)
        {
            var frame = _parser.Decode(data.ToArray());
            if (_respondToLinkStatus && frame.LinkControl?.FunctionCode == 9
                && frame.Format == ARIEC60870.Core.Model.Ft12FrameFormat.FixedLength)
            {
                foreach (var b in Ft12FrameBuilder.Fixed(control: 0x0B, linkAddress: _link, linkAddressSize: 2))
                    _rx.Enqueue(b);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> ReadAsync(Memory<byte> target, CancellationToken token)
        {
            if (_rx.Count == 0) return ValueTask.FromResult(0);
            target.Span[0] = _rx.Dequeue();
            return ValueTask.FromResult(1);
        }

        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync() { IsOpen = false; return ValueTask.CompletedTask; }
    }
}
