// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Channels;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Master.Transport;

namespace ARIEC60870.Protocol.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("FT1.2 parser accepts valid fixed secondary NO DATA frame", TestParserAcceptsValidFixedNoData),
            ("FT1.2 parser detects checksum mismatch", TestParserDetectsChecksumMismatch),
            ("FT1.2 reader resynchronizes after noise bytes", TestReaderResynchronizesAfterNoise),
            ("FT1.2 parser and reader preserve single-character NACK A2", TestSingleCharacterNackA2),
            ("Sanitized fixed-frame test vector decodes", TestVectorFixedNoData),
            ("Sanitized Type 1 event test vector decodes", TestVectorType1Event),
            ("Sanitized Type 5 identification test vector decodes", TestVectorType5Identification),
            ("Sanitized Type 8 GI-end test vector decodes", TestVectorType8GiEnd),
            ("Sanitized Type 9 measurand test vector decodes", TestVectorType9Measurand),
            ("Sanitized private ASDU test vector stays transparent", TestVectorPrivateAsdu),
            ("Master FCB is held after timeout and advances only after valid response", TestFcbHeldAfterTimeout),
            ("Master FCB is held after invalid checksum response", TestFcbHeldAfterInvalidChecksum),
            ("IEC-101 GI ASDU builder and decoder round-trip", TestIec101GiAsduRoundTrip),
            ("IEC-101 two-octet link address decodes", TestIec101TwoOctetLinkAddress),
            ("FT1.2 zero-octet link-address frame decodes internally", TestFt12ZeroOctetLinkAddress),
            ("IEC-101/104 multi-object ASDU separates value and quality", TestIec10xMultiObjectQualityDecode),
            ("IEC-101 CP24 time-tagged ASDU decodes timestamp", TestIec101Cp24TimestampDecode),
            ("IEC-101 integrated totals BCR decodes quality flags", TestIec101BcrDecode),
            ("IEC-104 STARTDT and I-format parser round-trip", TestIec104ParserRoundTrip)
        };

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run().ConfigureAwait(false);
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {test.Name}");
                Console.Error.WriteLine("     " + ex.Message);
            }
        }

        Console.WriteLine(failed == 0
            ? $"Protocol smoke tests passed: {tests.Length}/{tests.Length}"
            : $"Protocol smoke tests failed: {failed}/{tests.Length}");

        return failed == 0 ? 0 : 1;
    }

    private static Task TestParserAcceptsValidFixedNoData()
    {
        var parser = new Ft12Parser();
        var frame = parser.Decode(SecondaryFixed(functionCode: 9, acd: false));

        AssertEqual(Ft12FrameFormat.FixedLength, frame.Format, "frame format");
        AssertTrue(frame.IsChecksumValid, "checksum must be valid");
        AssertNotNull(frame.LinkControl, "link control must decode");
        AssertFalse(frame.LinkControl!.Prm, "secondary response must have PRM=0");
        AssertEqual(9, frame.LinkControl.FunctionCode, "secondary function code");
        return Task.CompletedTask;
    }

    private static Task TestParserDetectsChecksumMismatch()
    {
        var parser = new Ft12Parser();
        var frame = SecondaryFixed(functionCode: 9, acd: false);
        frame[3] ^= 0x7F;
        var decoded = parser.Decode(frame);

        AssertFalse(decoded.IsChecksumValid, "checksum must be invalid");
        AssertTrue(decoded.Issues.Any(x => x.Contains("Checksum", StringComparison.OrdinalIgnoreCase)), "checksum issue must be recorded");
        return Task.CompletedTask;
    }

    private static async Task TestReaderResynchronizesAfterNoise()
    {
        await using var transport = new ScriptedTransport();
        transport.EnqueueIncoming(new byte[] { 0x00, 0xFF, 0x33 });
        transport.EnqueueIncoming(SecondaryFixed(functionCode: 9, acd: true));

        var reader = new Ft12StreamReader(transport);
        var raw = await reader.ReadFrameAsync(timeoutMs: 200, CancellationToken.None).ConfigureAwait(false);

        AssertNotNull(raw, "reader must return a frame after noise");
        AssertEqual(5, raw!.Length, "fixed frame length");
        AssertEqual((byte)0x10, raw[0], "first returned byte must be FT1.2 fixed-frame start");
    }

    private static async Task TestSingleCharacterNackA2()
    {
        var parser = new Ft12Parser();
        var nack = parser.Decode(new byte[] { 0xA2 });
        AssertEqual(Ft12FrameFormat.SingleCharacter, nack.Format, "NACK frame format");
        AssertTrue(nack.IsSingleCharacterNack, "A2 must be classified as single-character NACK");
        AssertEqual("Single character NACK", nack.ShortMeaning, "NACK short meaning");

        await using var transport = new ScriptedTransport();
        transport.EnqueueIncoming(new byte[] { 0x55, 0xA2 });
        var reader = new Ft12StreamReader(transport);
        var raw = await reader.ReadFrameAsync(timeoutMs: 200, CancellationToken.None).ConfigureAwait(false);
        AssertNotNull(raw, "reader must return single-character NACK after noise");
        AssertEqual((byte)0xA2, raw![0], "reader must preserve A2");
    }

    private static Task TestVectorFixedNoData()
    {
        var parser = new Ft12Parser();
        var frame = parser.Decode(ReadTestVector("fixed-no-data.hex"));

        AssertEqual(Ft12FrameFormat.FixedLength, frame.Format, "test vector frame format");
        AssertTrue(frame.IsChecksumValid, "test vector checksum must be valid");
        AssertNotNull(frame.LinkControl, "link control must decode");
        AssertEqual(9, frame.LinkControl!.FunctionCode, "NO DATA secondary function code");
        return Task.CompletedTask;
    }

    private static Task TestVectorType1Event()
    {
        var frame = DecodeVector("class1-event-type1.hex");

        AssertEqual(Ft12FrameFormat.VariableLength, frame.Format, "Type 1 frame format");
        AssertTrue(frame.IsChecksumValid, "Type 1 checksum must be valid");
        AssertNotNull(frame.Asdu, "Type 1 ASDU must decode");
        AssertEqual(1, frame.Asdu!.TypeId, "Type 1 id");
        AssertEqual(192, frame.Asdu.FunctionType, "Type 1 FUN");
        AssertEqual(36, frame.Asdu.InformationNumber, "Type 1 INF");
        AssertEqual(2, frame.Asdu.Dpi, "Type 1 DPI");
        AssertNotNull(frame.Asdu.Time, "Type 1 relay time must decode");
        AssertEqual("14:34:12.345", frame.Asdu.Time!.DisplayTime, "Type 1 relay timestamp");
        return Task.CompletedTask;
    }

    private static Task TestVectorType5Identification()
    {
        var frame = DecodeVector("identification-type5.hex");

        AssertNotNull(frame.Asdu, "Type 5 ASDU must decode");
        AssertEqual(5, frame.Asdu!.TypeId, "Type 5 id");
        AssertTrue(frame.Asdu.IdentificationText?.Contains("ARIEC60870 Relay Simulator", StringComparison.Ordinal) == true, "identification text must be extracted");
        return Task.CompletedTask;
    }

    private static Task TestVectorType8GiEnd()
    {
        var frame = DecodeVector("gi-end-type8.hex");

        AssertNotNull(frame.Asdu, "Type 8 ASDU must decode");
        AssertEqual(8, frame.Asdu!.TypeId, "Type 8 id");
        AssertEqual(10, frame.Asdu.CauseOfTransmission, "GI end COT");
        return Task.CompletedTask;
    }

    private static Task TestVectorType9Measurand()
    {
        var frame = DecodeVector("class2-measurand-type9.hex");

        AssertNotNull(frame.Asdu, "Type 9 ASDU must decode");
        AssertEqual(9, frame.Asdu!.TypeId, "Type 9 id");
        AssertEqual(1234d, frame.Asdu.NumericValue, "Type 9 first signed 16-bit value");
        return Task.CompletedTask;
    }

    private static Task TestVectorPrivateAsdu()
    {
        var frame = DecodeVector("unknown-private-type205.hex");

        AssertNotNull(frame.Asdu, "private ASDU must decode as transparent payload");
        AssertEqual(205, frame.Asdu!.TypeId, "private ASDU type id");
        AssertEqual(DecodeStatus.Unknown, frame.Asdu.Status, "private ASDU status");
        AssertTrue(frame.Asdu.DataBytes.Count >= 2, "private ASDU raw payload must be retained");
        return Task.CompletedTask;
    }

    private static Ft12FrameDecode DecodeVector(string fileName)
    {
        var parser = new Ft12Parser();
        var frame = parser.Decode(ReadTestVector(fileName));
        AssertTrue(frame.IsLengthValid, $"{fileName} length must be valid");
        AssertTrue(frame.IsChecksumValid, $"{fileName} checksum must be valid");
        return frame;
    }

    private static async Task TestFcbHeldAfterTimeout()
    {
        await using var transport = new ScriptedTransport();
        transport.ScriptWriteResponse(null); // first Class 2 request times out
        transport.ScriptWriteResponse(SecondaryFixed(functionCode: 9, acd: false)); // second request succeeds
        transport.ScriptWriteResponse(SecondaryFixed(functionCode: 9, acd: false)); // third request proves FCB advanced

        var session = CreateShortTimeoutSession(transport);
        await session.RequestClass2Async("timeout test - first", CancellationToken.None).ConfigureAwait(false);
        await session.RequestClass2Async("timeout test - second", CancellationToken.None).ConfigureAwait(false);
        await session.RequestClass2Async("timeout test - third", CancellationToken.None).ConfigureAwait(false);

        AssertEqual(3, transport.Writes.Count, "write count");
        AssertEqual((byte)0x5B, transport.Writes[0][1], "first Class 2 control must use FCB=0");
        AssertEqual((byte)0x5B, transport.Writes[1][1], "second Class 2 control must still use FCB=0 after timeout");
        AssertEqual((byte)0x7B, transport.Writes[2][1], "third Class 2 control must use FCB=1 after one valid response");
    }

    private static async Task TestFcbHeldAfterInvalidChecksum()
    {
        var badResponse = SecondaryFixed(functionCode: 9, acd: false);
        badResponse[3] ^= 0x11;

        await using var transport = new ScriptedTransport();
        transport.ScriptWriteResponse(badResponse);
        transport.ScriptWriteResponse(SecondaryFixed(functionCode: 9, acd: false));
        transport.ScriptWriteResponse(SecondaryFixed(functionCode: 9, acd: false));

        var session = CreateShortTimeoutSession(transport);
        await session.RequestClass2Async("invalid checksum test - first", CancellationToken.None).ConfigureAwait(false);
        await session.RequestClass2Async("invalid checksum test - second", CancellationToken.None).ConfigureAwait(false);
        await session.RequestClass2Async("invalid checksum test - third", CancellationToken.None).ConfigureAwait(false);

        AssertEqual(3, transport.Writes.Count, "write count");
        AssertEqual((byte)0x5B, transport.Writes[0][1], "first Class 2 control must use FCB=0");
        AssertEqual((byte)0x5B, transport.Writes[1][1], "second Class 2 control must still use FCB=0 after invalid response");
        AssertEqual((byte)0x7B, transport.Writes[2][1], "third Class 2 control must use FCB=1 after valid response");
    }


    private static Task TestIec101GiAsduRoundTrip()
    {
        var settings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            CommonAddress = 1,
            CauseOfTransmissionSize = 2,
            CommonAddressSize = 2,
            InformationObjectAddressSize = 3
        };

        var asdu = Iec10xAsduBuilder.GeneralInterrogation(settings);
        var decoded = new Iec10xAsduDecoder(2, 2, 3).Decode(asdu);

        AssertEqual((byte)100, decoded.TypeId, "IEC-101 GI type id");
        AssertEqual(6, decoded.CauseOfTransmission, "IEC-101 GI COT activation");
        AssertEqual(1, decoded.CommonAddress, "IEC-101 common address");
        AssertEqual(0, decoded.InformationObjectAddress ?? -1, "IEC-101 GI IOA");
        AssertTrue(decoded.ValueText.Contains("QOI=20", StringComparison.Ordinal), "IEC-101 GI QOI must decode");
        return Task.CompletedTask;
    }


    private static Task TestIec101TwoOctetLinkAddress()
    {
        var control = Ft12FrameBuilder.BuildPrimaryControl(11, fcv: true, fcb: false);
        var frame = Ft12FrameBuilder.Fixed(control, 300, linkAddressSize: 2);
        var decoded = new Ft12Parser(linkAddressSize: 2).Decode(frame);

        AssertEqual(Ft12FrameFormat.FixedLength, decoded.Format, "two-octet fixed frame format");
        AssertEqual(300, decoded.LinkAddress ?? -1, "two-octet link address");
        AssertTrue(decoded.IsChecksumValid, "two-octet link address checksum must be valid");
        return Task.CompletedTask;
    }

    private static Task TestFt12ZeroOctetLinkAddress()
    {
        var control = Ft12FrameBuilder.BuildPrimaryControl(11, fcv: true, fcb: false);
        var frame = Ft12FrameBuilder.Fixed(control, 0, linkAddressSize: 0);
        var decoded = new Ft12Parser(linkAddressSize: 0).Decode(frame);

        AssertEqual(Ft12FrameFormat.FixedLength, decoded.Format, "zero-octet fixed frame format");
        AssertEqual(0, decoded.LinkAddress ?? -1, "zero-octet link address is represented as 0");
        AssertTrue(decoded.IsChecksumValid, "zero-octet link address checksum must be valid");
        return Task.CompletedTask;
    }

    private static Task TestIec10xMultiObjectQualityDecode()
    {
        var bytes = new List<byte>
        {
            13, 2, 20, 0, 1, 0,
            0xE9, 0x03, 0x00
        };
        bytes.AddRange(BitConverter.GetBytes(12.5f));
        bytes.Add(0x00);
        bytes.AddRange(new byte[] { 0xEA, 0x03, 0x00 });
        bytes.AddRange(BitConverter.GetBytes(13.5f));
        bytes.Add(0x80);

        var decoded = new Iec10xAsduDecoder(2, 2, 3).Decode(bytes);

        AssertEqual(2, decoded.Objects.Count, "multi-object count");
        AssertTrue(decoded.Objects[0].ShortValue.Contains("Float=12.5", StringComparison.Ordinal), "first object value");
        AssertEqual("Good", decoded.Objects[0].QualityText, "first object quality");
        AssertTrue(decoded.Objects[1].ShortValue.Contains("Float=13.5", StringComparison.Ordinal), "second object value");
        AssertTrue(decoded.Objects[1].QualityText.Contains("Invalid", StringComparison.OrdinalIgnoreCase), "second object invalid quality");
        AssertFalse(decoded.Objects[0].ShortValue.Contains("QDS", StringComparison.OrdinalIgnoreCase), "value text must not mix QDS marker");
        return Task.CompletedTask;
    }

    private static Task TestIec101Cp24TimestampDecode()
    {
        var bytes = new List<byte>
        {
            2, 1, 3, 0, 1, 0,
            0xE9, 0x03, 0x00,
            0x01,
            0x39, 0x30,
            0x22
        };
        var decoded = new Iec10xAsduDecoder(2, 2, 3).Decode(bytes);
        AssertEqual((byte)2, decoded.TypeId, "CP24 type id");
        AssertTrue(decoded.TimestampText.Contains("CP24", StringComparison.Ordinal), "CP24 timestamp must be exposed");
        AssertTrue(decoded.FirstObject?.TimestampText.Contains("second=12.345", StringComparison.Ordinal) == true, "CP24 seconds must decode");
        return Task.CompletedTask;
    }

    private static Task TestIec101BcrDecode()
    {
        var bytes = new List<byte>
        {
            15, 1, 20, 0, 1, 0,
            0xE9, 0x03, 0x00,
            0x39, 0x30, 0x00, 0x00,
            0xE3
        };
        var decoded = new Iec10xAsduDecoder(2, 2, 3).Decode(bytes);
        AssertEqual((byte)15, decoded.TypeId, "BCR type id");
        AssertTrue(decoded.ValueText.Contains("Counter=12345", StringComparison.Ordinal), "BCR counter value");
        AssertTrue(decoded.QualityText.Contains("Carry", StringComparison.Ordinal), "BCR carry flag");
        AssertTrue(decoded.QualityText.Contains("Adjusted", StringComparison.Ordinal), "BCR adjusted flag");
        AssertTrue(decoded.QualityText.Contains("Invalid", StringComparison.Ordinal), "BCR invalid flag");
        return Task.CompletedTask;
    }

    private static Task TestIec104ParserRoundTrip()
    {
        var settings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            CommonAddress = 1,
            CauseOfTransmissionSize = 2,
            CommonAddressSize = 2,
            InformationObjectAddressSize = 3
        };

        var parser = new Iec104ApduParser(2, 2, 3);
        var start = parser.Decode(Iec104FrameBuilder.StartDtActivation());
        AssertEqual("U", start.Format, "IEC-104 STARTDT format");
        AssertTrue(start.UFormatName.Contains("STARTDT", StringComparison.Ordinal), "IEC-104 STARTDT name");

        var iFrame = Iec104FrameBuilder.I(3, 7, Iec10xAsduBuilder.FloatMeasurement(settings, 1001, 12.5f, cause: 3));
        var decoded = parser.Decode(iFrame);
        AssertEqual("I", decoded.Format, "IEC-104 I-format");
        AssertEqual(3, decoded.SendSequence, "IEC-104 send sequence");
        AssertEqual(7, decoded.ReceiveSequence, "IEC-104 receive sequence");
        AssertNotNull(decoded.Asdu, "IEC-104 I-format ASDU");
        AssertEqual((byte)13, decoded.Asdu!.TypeId, "IEC-104 float measurement type");
        AssertEqual(1001, decoded.Asdu.InformationObjectAddress ?? -1, "IEC-104 IOA");
        return Task.CompletedTask;
    }

    private static Iec103MasterSession CreateShortTimeoutSession(ScriptedTransport transport)
    {
        return new Iec103MasterSession(new Iec103MasterSettings
        {
            UseSimulatedSlave = true,
            ResponseTimeoutMs = 25,
            TimeoutRecoveryBackoffMs = 0,
            MaxConsecutiveTimeoutsBeforeResetFcb = 99,
            ResetFcbAfterTimeoutBurst = false,
            Class2PollIntervalMs = 100,
            LinkAddress = 1,
            CommonAddress = 1
        }, transport);
    }

    private static byte[] ReadTestVector(string fileName)
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "samples", "test-vectors", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Test vector not found: {path}", path);
        }

        var bytes = new List<byte>();
        foreach (var line in File.ReadAllLines(path))
        {
            var clean = line.Split('#')[0].Trim();
            if (string.IsNullOrWhiteSpace(clean))
            {
                continue;
            }

            foreach (var token in clean.Split(new[] { ' ', '\t', ',', ';', ':' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bytes.Add(Convert.ToByte(token, 16));
            }
        }

        if (bytes.Count == 0)
        {
            throw new InvalidOperationException($"Test vector is empty: {path}");
        }

        return bytes.ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ARIEC60870.sln")) &&
                Directory.Exists(Path.Combine(current.FullName, "samples", "test-vectors")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root containing ARIEC60870.sln and samples/test-vectors.");
    }

    private static byte[] SecondaryFixed(int functionCode, bool acd)
    {
        var control = (byte)(functionCode & 0x0F);
        if (acd)
        {
            control |= 0x20;
        }

        return Ft12FrameBuilder.Fixed(control, linkAddress: 1);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

    private static void AssertNotNull(object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected={expected}, actual={actual}");
        }
    }

    private sealed class ScriptedTransport : IByteTransport
    {
        private readonly Queue<byte[]?> _scriptedResponses = new();
        private readonly Channel<byte> _rx = Channel.CreateUnbounded<byte>();
        private bool _isOpen = true;

        public bool IsOpen => _isOpen;
        public List<byte[]> Writes { get; } = new();

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            _isOpen = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            _isOpen = false;
            return ValueTask.CompletedTask;
        }

        public void Dispose() => _isOpen = false;

        public ValueTask DisposeAsync()
        {
            _isOpen = false;
            return ValueTask.CompletedTask;
        }

        public void ScriptWriteResponse(byte[]? response) => _scriptedResponses.Enqueue(response);

        public void EnqueueIncoming(IReadOnlyList<byte> bytes)
        {
            foreach (var b in bytes)
            {
                _rx.Writer.TryWrite(b);
            }
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            Writes.Add(buffer.ToArray());
            if (_scriptedResponses.Count > 0)
            {
                var response = _scriptedResponses.Dequeue();
                if (response is not null)
                {
                    EnqueueIncoming(response);
                }
            }

            return ValueTask.CompletedTask;
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (buffer.Length == 0)
            {
                return 0;
            }

            var first = await _rx.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            buffer.Span[0] = first;
            var count = 1;
            while (count < buffer.Length && _rx.Reader.TryRead(out var next))
            {
                buffer.Span[count++] = next;
            }

            return count;
        }
    }
}
