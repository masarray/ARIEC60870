// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO.Ports;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101SicamFieldRegressionTests
{
    private static Iec103MasterSettings CreateSettings() => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec101,
        PortName = "SICAM-FAKE",
        LinkAddress = 105,
        LinkAddressSize = 2,
        CommonAddress = 105,
        ResponseTimeoutMs = 180,
        StartupDelayMs = 0,
        Class2PollIntervalMs = 50,
        Class1DrainDelayMs = 0,
        ResetRemoteLinkOnConnect = false,
        ResetFcbOnConnect = false,
        SendGeneralInterrogationOnConnect = false,
        SendClockSyncOnConnect = false,
        RequestClass2ImmediatelyAfterStartup = false
    };

    [Fact]
    public void ThreeWireDefaultIsPreservedAndRtsCtsOwnershipIsExplicit()
    {
        var settings = CreateSettings();
        Assert.Equal(Handshake.None, settings.SerialHandshake);
        Assert.True(settings.SerialRtsEnable);
        Assert.False(settings.SerialDtrEnable);
        Assert.Contains("RTS=ON", settings.SerialControlLineSummary);

        settings.SerialHandshake = Handshake.RequestToSend;
        settings.SerialDtrEnable = true;
        Assert.Contains("driver-controlled", settings.SerialControlLineSummary);
        Assert.Contains("DTR=ON", settings.SerialControlLineSummary);
    }

    [Fact]
    public void RejectedChecksummedFrameMustNeverAdvanceFcb()
    {
        var frame = new Ft12Parser(2).Decode(Convert.FromHexString("100B69007516"));
        Assert.False(frame.IsChecksumValid);
        Assert.False(Iec101SecondaryResponseValidator.CanAdvanceFcb(frame));
    }

    [Fact]
    public void OlderTxMatchIsClassifiedSeparatelyFromForeignPrimary()
    {
        var parser = new Ft12Parser(2);
        var previousRequest = Convert.FromHexString("10496900B216");
        var recentRequest = Convert.FromHexString("105B6900C416");
        var response = Iec101SecondaryResponseValidator.Validate(
            parser.Decode(previousRequest), 105, recentRequest, matchesEarlierTransmit: true);

        Assert.False(response.IsValid);
        Assert.False(response.AdvancesFcb);
        Assert.Equal("IEC101-RX-DELAYED-TX-ECHO", response.Code);
    }

    [Fact]
    public async Task Ft12ReaderWaitsAcrossTransientEmptyReadsWithinFrame()
    {
        var raw = Convert.FromHexString("100B69007416");
        await using var transport = new ScriptedSicamTransport(responseOnly: true);
        await transport.OpenAsync(CancellationToken.None);
        var reader = new Ft12StreamReader(transport, linkAddressSize: 2);
        await transport.WriteAsync(Convert.FromHexString("10496900B216"), CancellationToken.None);

        var received = await reader.ReadFrameAsync(180, CancellationToken.None);

        Assert.NotNull(received);
        Assert.Equal(raw, received);
    }

    [Fact]
    public async Task SicamEchoThenValidSecondaryCanRecoverWithoutAdditionalTransmit()
    {
        var settings = CreateSettings();
        await using var transport = new ScriptedSicamTransport(responseOnly: false);
        var session = new Iec101MasterSession(settings, transport);

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        Assert.True(result.Counters.TxFrames >= 2);
        Assert.True(result.Counters.RxFrames >= 2);
        Assert.True(result.Counters.TimedResponses >= 2);
        Assert.True(result.Counters.RejectedFrames >= 2);
        Assert.True(result.Counters.TxEchoFrames >= 2);
        Assert.Contains(result.Events, e => e.PollingReason == "IEC101-RX-TX-ECHO");
        Assert.Contains(result.Events, e => e.PollingReason == "IEC101-RX-DELAYED-TX-ECHO");
        Assert.Contains(result.Events, e => e.PollingReason == "IEC101-RX-DELAYED-TX-ECHO"
            && e.Detail.Contains("last TX=", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, result.Counters.ChecksumErrors);
    }

    private sealed class ScriptedSicamTransport : IByteTransport
    {
        private static readonly byte[] StatusResponse = Convert.FromHexString("100B69007416");
        private readonly Queue<byte?> _pending = new();
        private readonly bool _responseOnly;
        private byte[]? _previousRequest;

        public ScriptedSicamTransport(bool responseOnly) => _responseOnly = responseOnly;
        public bool IsOpen { get; private set; }

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            IsOpen = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            if (!IsOpen) throw new InvalidOperationException("Transport closed.");
            var request = buffer.ToArray();
            if (!_responseOnly)
            {
                // First request comes back as immediate echo. Subsequent
                // requests receive an older primary frame before the real reply.
                EnqueueFragmented(_previousRequest ?? request);
            }

            EnqueueFragmented(StatusResponse);
            _previousRequest = request;
            return ValueTask.CompletedTask;
        }

        private void EnqueueFragmented(byte[] frame)
        {
            foreach (var value in frame)
            {
                _pending.Enqueue(value);
                _pending.Enqueue(null); // 0-byte read after every byte, like a polling adapter.
            }
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (!IsOpen) throw new InvalidOperationException("Transport closed.");
            if (buffer.Length == 0 || _pending.Count == 0) return ValueTask.FromResult(0);
            var next = _pending.Dequeue();
            if (next is null) return ValueTask.FromResult(0);
            buffer.Span[0] = next.Value;
            return ValueTask.FromResult(1);
        }

        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
    }
}
