// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>
/// A verified IEC-101 single link must obey secondary DFC remote flow control.
/// This byte-exact fake cannot certify physical RTU/UART behavior.
/// </summary>
public sealed class Iec101SingleLinkDfcBusyRecoveryTests
{
    [Fact]
    public async Task SingleLinkDfcBusyStopsClassPollingUntilNewAddressedFc11ClearsIt()
    {
        var settings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PortName = "DFC-TEST",
            BaudRate = 1200,
            LinkAddress = 105,
            LinkAddressSize = 2,
            CommonAddress = 105,
            CommonAddressSize = 2,
            CauseOfTransmissionSize = 2,
            InformationObjectAddressSize = 3,
            ResponseTimeoutMs = 90,
            Class2PollIntervalMs = 40,
            BusyBackoffMs = 120,
            StartupDelayMs = 0,
            ResetRemoteLinkOnConnect = false,
            ResetFcbOnConnect = false,
            SendGeneralInterrogationOnConnect = false,
            SendClockSyncOnConnect = false
        };
        await using var port = new BusyThenRecoveredSecondary();
        var result = await new Iec101MasterSession(settings, port)
            .RunForAsync(TimeSpan.FromMilliseconds(950), CancellationToken.None);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.True(port.Class2Requests >= 2, "Class 2 should resume after addressed FC11 clears DFC.");
        Assert.True(port.StatusProbes >= 3, "DFC must trigger bounded FC9 status probes until it clears.");
        Assert.Equal(0, port.DataSentWhileBusy);
        Assert.True(result.Counters.BusyResponses > 0);
        Assert.DoesNotContain(result.Findings, x => x.Id == "IEC101-SESSION-FAULT");
    }

    [Fact]
    public async Task FirstConnectGiWaitsForDfcClearAndTransmitsExactlyOnce()
    {
        var settings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PortName = "DFC-FIRST-GI",
            BaudRate = 1200,
            LinkAddress = 105, LinkAddressSize = 2,
            CommonAddress = 105, CommonAddressSize = 2,
            CauseOfTransmissionSize = 2, InformationObjectAddressSize = 3,
            ResponseTimeoutMs = 90,
            BusyBackoffMs = 70,
            Class2PollIntervalMs = 50,
            StartupDelayMs = 0,
            MaxClass1DrainFrames = 2,
            ResetRemoteLinkOnConnect = false,
            ResetFcbOnConnect = false,
            SendGeneralInterrogationOnConnect = true,
            SendClockSyncOnConnect = false,
            RequestClass2ImmediatelyAfterStartup = false
        };
        // The first addressed FC11 proves the secondary but sets DFC=1;
        // the second FC9 clears it. A confirmed link is NOT GI permission
        // while that exact source is still remote-busy.
        await using var port = new BusyThenRecoveredSecondary(initialBusy: true);
        var result = await new Iec101MasterSession(settings, port)
            .RunForAsync(TimeSpan.FromMilliseconds(850), CancellationToken.None);

        Assert.True(result.CompletedNormally, result.CompletionReason);
        Assert.True(port.StatusProbes >= 2);
        Assert.Equal(0, port.DataSentWhileBusy);
        Assert.Equal(1, port.GiRequests);
        Assert.Equal(1, result.Counters.GiCommands);
        Assert.Single(result.Events, e =>
            e.Direction == ARIEC60870.Core.Model.FrameDirection.MasterToSlave &&
            e.TypeId == 100 && e.CauseOfTransmission == 6);
    }

    private sealed class BusyThenRecoveredSecondary : IByteTransport
    {
        private readonly Ft12Parser _parser = Ft12Parser.ForIec101(2);
        private readonly Queue<byte> _incoming = new();
        private bool _busy;
        private int _busyProbes;

        public BusyThenRecoveredSecondary(bool initialBusy = false) =>
            _busy = initialBusy;

        public bool IsOpen { get; private set; }
        public int Class2Requests { get; private set; }
        public int StatusProbes { get; private set; }
        public int GiRequests { get; private set; }
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
            _incoming.Clear();
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = _parser.Decode(data.ToArray());
            switch (frame.LinkControl?.FunctionCode)
            {
                case 9: // Link status: the sole authority for clearing remote DFC.
                    StatusProbes++;
                    if (_busy && ++_busyProbes >= 2) _busy = false;
                    Enqueue(Ft12FrameBuilder.Fixed(_busy ? (byte)0x1B : (byte)0x0B, 105, 2));
                    break;
                case 11: // Class 2: first response asserts remote busy.
                    Class2Requests++;
                    if (_busy) DataSentWhileBusy++;
                    if (Class2Requests == 1) _busy = true;
                    Enqueue(Ft12FrameBuilder.Fixed(_busy ? (byte)0x19 : (byte)0x09, 105, 2));
                    break;
                case 10:
                case 3:
                    if (_busy) DataSentWhileBusy++;
                    if (frame.AsduBytes.Count > 0 && frame.AsduBytes[0] == 100)
                        GiRequests++;
                    Enqueue([0xE5]);
                    break;
                case 0:
                case 7:
                    Enqueue([0xE5]);
                    break;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> ReadAsync(Memory<byte> target, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_incoming.Count == 0) return ValueTask.FromResult(0);
            target.Span[0] = _incoming.Dequeue();
            return ValueTask.FromResult(1);
        }

        private void Enqueue(byte[] bytes)
        {
            foreach (var b in bytes) _incoming.Enqueue(b);
        }

        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
    }
}
