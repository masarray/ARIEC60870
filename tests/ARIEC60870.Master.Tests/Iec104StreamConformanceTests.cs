// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec104StreamConformanceTests
{
    [Fact]
    public async Task PartialTcpApduSurvivesReadDeadlineAndDoesNotConsumeNextFrame()
    {
        using var transport = new BufferedScriptTransport();
        var reader = new Iec104StreamReader(transport);
        var first = Iec104FrameBuilder.StartDtConfirmation();
        var second = Iec104FrameBuilder.TestFrConfirmation();
        transport.Push(first.AsSpan(0, 3).ToArray());
        Assert.Null(await reader.ReadFrameAsync(20, CancellationToken.None));

        transport.Push(first.AsSpan(3).ToArray().Concat(second).ToArray());
        Assert.Equal(first, await reader.ReadFrameAsync(200, CancellationToken.None));
        Assert.Equal(second, await reader.ReadFrameAsync(200, CancellationToken.None));
        Assert.Equal(0, transport.Writes);
    }

    [Fact]
    public async Task InvalidLengthIsReportedAndSubsequentFrameIsNotSwallowed()
    {
        using var transport = new BufferedScriptTransport();
        var reader = new Iec104StreamReader(transport);
        var good = Iec104FrameBuilder.StartDtConfirmation();
        transport.Push(new byte[] { 0x68, 0x02, 0x00, 0x15 }.Concat(good).ToArray());
        Assert.Equal(new byte[] { 0x68, 0x02 }, await reader.ReadFrameAsync(200, CancellationToken.None));
        Assert.Equal(good, await reader.ReadFrameAsync(200, CancellationToken.None));
    }

    [Fact]
    public async Task InterleavedNoiseAndFiveThousandConcatenatedApdusAreByteExact()
    {
        using var transport = new BufferedScriptTransport();
        var reader = new Iec104StreamReader(transport);
        var first = Iec104FrameBuilder.StartDtConfirmation();
        var second = Iec104FrameBuilder.TestFrConfirmation();
        var all = new byte[2 + 5_000 * first.Length];
        all[0] = 0x15;
        all[1] = 0x00;
        for (var i = 0; i < 5_000; i++)
            (i % 2 == 0 ? first : second).CopyTo(all, 2 + i * first.Length);
        transport.Push(all);
        for (var i = 0; i < 5_000; i++)
            Assert.Equal(i % 2 == 0 ? first : second,
                await reader.ReadFrameAsync(500, CancellationToken.None));
        Assert.Equal(0, transport.Writes);
    }

    [Fact]
    public void CoreWireValidationAndArchivedEvidenceVerifierAgree()
    {
        var good = Iec104FrameBuilder.StartDtConfirmation();
        Assert.True(Iec104ApciWireLayout.TryValidate(good, out _));
        Assert.True(Iec104ApciEvidenceVerifier.Matches(good, "U", null, null, "STARTDT con"));
        var forbidden = (byte[])good.Clone();
        forbidden[3] = 0x01;
        Assert.False(Iec104ApciWireLayout.TryValidate(forbidden, out _));
        Assert.False(Iec104ApciEvidenceVerifier.Matches(forbidden, "U", null, null, "STARTDT con"));
    }

    [Fact]
    public async Task InvalidStartDtControlCannotAdvanceSessionToIFormatTransmission()
    {
        using var transport = new BufferedScriptTransport(malformedStartReply: true);
        var settings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            CauseOfTransmissionSize = 2,
            CommonAddressSize = 2,
            InformationObjectAddressSize = 3,
            ResponseTimeoutMs = 20,
            Iec104T1AckTimeoutMs = 90,
            SendClockSyncOnConnect = true,
            SendGeneralInterrogationOnConnect = true
        };
        var result = await new Iec104ClientSession(settings, transport)
            .RunForAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);
        Assert.False(result.CompletedNormally);
        Assert.Contains(result.Findings, x => x.Id == "IEC104-STARTDT-CON");
        Assert.Equal(1, transport.Writes);
    }

    private sealed class BufferedScriptTransport : IByteTransport
    {
        private readonly Queue<byte> _bytes = new();
        private readonly bool _malformedStartReply;

        public BufferedScriptTransport(bool malformedStartReply = false)
            => _malformedStartReply = malformedStartReply;

        public bool IsOpen { get; private set; }
        public int Writes { get; private set; }

        public void Push(IEnumerable<byte> data)
        {
            foreach (var value in data) _bytes.Enqueue(value);
        }

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
            Writes++;
            if (_malformedStartReply)
            {
                var malformed = Iec104FrameBuilder.StartDtConfirmation();
                malformed[3] = 1;
                Push(malformed);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_bytes.Count == 0)
                return new ValueTask<int>(WaitForDeadlineAsync(cancellationToken));

            var count = 0;
            while (count < buffer.Length && _bytes.Count > 0)
                buffer.Span[count++] = _bytes.Dequeue();
            return ValueTask.FromResult(count);
        }

        private static async Task<int> WaitForDeadlineAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public void Dispose() => IsOpen = false;
        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
    }
}
