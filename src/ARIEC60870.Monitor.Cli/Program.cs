// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO.Ports;
using ARIEC60870.Monitor;

namespace ARIEC60870.Monitor.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help"))
        {
            Console.WriteLine("IEC-101 PASSIVE P0 — raw RX-only logger (no protocol decode or GUI yet)");
            Console.WriteLine("  capture --scada-com COM7 --rtu-com COM8 --output C:\\Capture --baud 9600 --parity Even --seconds 180");
            Console.WriteLine("  verify --folder C:\\Capture\\ARIEC-PASSIVE-...");
            Console.WriteLine("Physical TXD/RTS/DTR of monitor adapters MUST be disconnected; use approved buffered/isolated tap.");
            return args.Length == 0 ? 1 : 0;
        }
        try
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < args.Length; i += 2)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                    throw new ArgumentException("Expected --name value pairs.");
                if (!values.TryAdd(args[i][2..], args[i + 1]))
                    throw new ArgumentException("Duplicate argument: " + args[i]);
            }
            string Get(string key, string fallback = "") => values.TryGetValue(key, out var v) ? v : fallback;
            if (args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
            {
                var audit = PassiveJournalVerifier.Verify(Get("folder"));
                Console.WriteLine($"Journal status: {audit.Status}; complete: {audit.Complete}");
                foreach (var x in audit.Streams)
                    Console.WriteLine($"RX{x.StreamId}: chunks={x.Records}, bytes={x.RawBytes}, SHA256={x.Sha256}, valid={x.Valid}");
                foreach (var problem in audit.Problems) Console.Error.WriteLine("WARNING: " + problem);
                return audit.Complete ? 0 : 2;
            }
            if (!args[0].Equals("capture", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Command must be capture or verify.");

            var baud = int.Parse(Get("baud", "9600"), System.Globalization.CultureInfo.InvariantCulture);
            var parity = Enum.Parse<Parity>(Get("parity", "Even"), ignoreCase: true);
            var bits = int.Parse(Get("data-bits", "8"), System.Globalization.CultureInfo.InvariantCulture);
            var stopBits = Enum.Parse<StopBits>(Get("stop-bits", "One"), ignoreCase: true);
            var duration = int.Parse(Get("seconds", "180"), System.Globalization.CultureInfo.InvariantCulture);
            if (duration is < 1 or > 86400) throw new ArgumentOutOfRangeException(nameof(duration));
            var scada = new PassiveSerialSource(Get("scada-com"), "SCADA TX -> RTU", baud, parity, bits, stopBits);
            var rtu = new PassiveSerialSource(Get("rtu-com"), "RTU TX -> SCADA", baud, parity, bits, stopBits);
            var opts = new PassiveCaptureOptions(Get("output"), scada, rtu);
            opts.Validate();

            Console.WriteLine("RECEIVE-ONLY monitor. This software does not make an unsafe serial cable safe.");
            Console.WriteLine("Check isolated tap, GND and intact production handshake wiring BEFORE capture.");
            Console.WriteLine($"Observing SCADA TX on {scada.PortName} and RTU TX on {rtu.PortName} for {duration}s.");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(duration));
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            var session = new PassiveMonitorSession(opts,
                new WindowsReadOnlySerialTap(scada), new WindowsReadOnlySerialTap(rtu));
            var result = await session.RunAsync(cts.Token).ConfigureAwait(false);
            Console.WriteLine("Capture directory: " + result.Directory);
            Console.WriteLine("Status: " + result.Manifest.Status);
            foreach (var stream in result.Manifest.Streams)
                Console.WriteLine($"{stream.PhysicalTransmitter}: {stream.Bytes} bytes / {stream.Chunks} chunks, UART errors {stream.DriverErrors}");
            foreach (var w in result.Manifest.Warnings) Console.Error.WriteLine("WARNING: " + w);
            var verify = PassiveJournalVerifier.Verify(result.Directory);
            Console.WriteLine("Reopen/CRC/SHA256 audit: " + (verify.Complete ? "PASS" : "INCOMPLETE"));
            return verify.Complete ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Passive capture failed: " + ex.Message);
            return 1;
        }
    }
}
