// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>
/// A truncated physical reply must not turn the next unaddressed E5 into
/// valid RTU link proof. This regression crosses two actual Master requests,
/// not merely two direct calls on an isolated FT1.2 reader.
/// </summary>
public sealed class Iec101SingleLinkFramingEpochTests
{
    [Fact]
    public async Task AbortedLinkStatusThenUnaddressedE5NeverCertifiesSingleLink()
    {
        var settings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PortName = "SCRIPTED-IEC101",
            BaudRate = 1200,
            LinkAddress = 105,
            LinkAddressSize = 2,
            CommonAddress = 105,
            ResponseTimeoutMs = 40,
            Class2PollIntervalMs = 250,
            StartupDelayMs = 0,
            ResetRemoteLinkOnConnect = false,
            ResetFcbOnConnect = false,
            SendGeneralInterrogationOnConnect = false,
            SendClockSyncOnConnect = false,
            RequestClass2ImmediatelyAfterStartup = false
        };
        await using var transport = new PartialThenE5Transport();
        var session = new ARIEC60870.Master.Iec101MasterSession(settings, transport);

        var result = await session.RunForAsync(
            TimeSpan.FromMilliseconds(1250), CancellationToken.None);

        Assert.True(transport.LinkStatusWrites >= 2,
            "Fixture must issue two independent FC9 exchanges on one COM epoch.");
        Assert.Equal(0, result.Counters.TimedResponses);
        Assert.Equal(0, result.Counters.AckResponses);
        Assert.Equal(0, result.Counters.GiCommands);
        Assert.True(result.CompletedNormally);
        Assert.DoesNotContain(result.Events, e =>
            e.Summary.Contains("secondary link verified", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class PartialThenE5Transport : IByteTransport
    {
        private readonly Queue<byte> _rx = new();
        public int LinkStatusWrites { get; private set; }
        public bool IsOpen { get; private set; }

        public ValueTask OpenAsync(CancellationToken token)
        {
            IsOpen = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken token)
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!IsOpen) throw new InvalidOperationException("Scripted COM is closed.");
            if (frame.Length > 1 && frame.Span[0] == 0x10 &&
                (frame.Span[1] & 0x0F) == 9)
            {
                LinkStatusWrites++;
                if (LinkStatusWrites == 1)
                    _rx.Enqueue(0x10); // Incomplete fixed-length secondary frame.
                else if (LinkStatusWrites == 2)
                    _rx.Enqueue(0xE5); // No address; could be damaged old frame.
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_rx.Count == 0) return ValueTask.FromResult(0);
            buffer.Span[0] = _rx.Dequeue();
            return ValueTask.FromResult(1);
        }

        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
