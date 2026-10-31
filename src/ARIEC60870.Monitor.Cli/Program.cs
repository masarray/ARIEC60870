// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.IO.Ports;
using System.Text.Json;
using ARIEC60870.Monitor;

namespace ARIEC60870.Monitor.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"] || args is ["help"])
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var command = args[0].ToLowerInvariant();
            var values = ParsePairs(args);
            switch (command)
            {
                case "list-ports":
                    RequireKeys(values);
                    ShowPorts();
                    return 0;
                case "verify":
                    RequireKeys(values, "folder");
                    if (!values.TryGetValue("folder", out var directory) || string.IsNullOrWhiteSpace(directory))
                        throw new ArgumentException("verify requires --folder with a finalized capture directory.");
                    var verified = PassiveJournalVerifier.Verify(directory);
                    PrintAudit(verified);
                    return verified.Complete ? 0 : 2;
                case "preflight":
                case "capture":
                    var plan = PassiveFieldCapturePlan.Parse(values);
                    var result = PrintPreflight(plan);
                    if (command == "preflight") return result;
                    if (result != 0) return result;
                    return await CaptureAsync(plan).ConfigureAwait(false);
                default:
                    throw new ArgumentException("Command must be list-ports, preflight, capture or verify.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FIELD TOOL ERROR: " + ex.Message);
            return 1;
        }
    }

    private static Dictionary<string, string> ParsePairs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                throw new ArgumentException("Expected --name value pairs; use --help.");
            if (!result.TryAdd(args[i][2..], args[i + 1]))
                throw new ArgumentException("Duplicate argument: " + args[i]);
        }
        return result;
    }

    private static void RequireKeys(IReadOnlyDictionary<string, string> values, params string[] allowed)
    {
        foreach (var key in values.Keys)
            if (!allowed.Contains(key, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Unexpected option --" + key + ".");
    }

    private static void ShowPorts()
    {
        var ports = SerialPort.GetPortNames()
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        Console.WriteLine("Windows COM inventory (ports NOT opened): " +
            (ports.Length == 0 ? "none" : string.Join(", ", ports)));
        Console.WriteLine("Presence is not proof of an electrically safe tap or valid RTU response.");
    }

    private static int PrintPreflight(PassiveFieldCapturePlan plan)
    {
        var opts = plan.Options;
        Console.WriteLine($"Validated IEC-101 RX-only configuration · {opts.StreamCount} independent COM inputs · {plan.DurationSeconds}s");
        for (var i = 0; i < opts.Sources.Length; i++)
        {
            var source = opts.Sources[i];
            Console.WriteLine($"  RX{i}: {source.PortName} · {source.PhysicalTransmitter} · " +
                $"{source.BaudRate} {source.DataBits}{source.Parity}/{source.StopBits}");
        }
        Console.WriteLine($"  IEC field widths: LA={opts.LinkAddressWidth} COT={opts.CotWidth} CA={opts.CommonAddressWidth} IOA={opts.InformationAddressWidth}; " +
            $"link={opts.LinkMode}; gap={opts.FrameGapMilliseconds}ms");
        Console.WriteLine("  Recording: FULL RAW JOURNAL (explicit CLI capture) · " + opts.OutputDirectory);
        Console.WriteLine("SAFETY: monitor adapter TXD/RTS/DTR MUST be electrically isolated from plant.");
        Console.WriteLine("DO NOT CONNECT CLI directly to a production serial bus without an approved receive-only tap.");
        Console.WriteLine("Software cannot prove the tap is non-intrusive; no COM opens during preflight.");
        var ports = SerialPort.GetPortNames();
        var missing = plan.MissingPorts(ports);
        Console.WriteLine("  Windows COM inventory: " +
            (ports.Length == 0 ? "none" : string.Join(", ", ports.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))));
        if (missing.Length == 0)
        {
            Console.WriteLine("PREFLIGHT CONFIG/PRESENCE PASS. This does NOT prove cabling, waveform or RTU interoperability.");
            return 0;
        }
        Console.Error.WriteLine("PREFLIGHT BLOCKED: configured adapters not enumerated by Windows: " +
            string.Join(", ", missing) + ". Check adapters/driver/COM assignment.");
        return 3;
    }

    private static async Task<int> CaptureAsync(PassiveFieldCapturePlan plan)
    {
        var opts = plan.Options;
        Console.WriteLine("Starting bounded RX-only capture. Ctrl+C requests graceful Stop and final journal verification.");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(plan.DurationSeconds));
        ConsoleCancelEventHandler cancel = (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            var session = new PassiveMonitorSession(opts,
                opts.Sources.Select(x => (IReadOnlySerialTap)new WindowsReadOnlySerialTap(x)).ToArray());
            var result = await session.RunAsync(stop.Token).ConfigureAwait(false);
            Console.WriteLine("Capture directory: " + result.Directory);
            Console.WriteLine("Session status: " + result.Manifest.Status);
            foreach (var x in result.Manifest.Streams)
                Console.WriteLine($"  RX{x.StreamId} ({x.PortName}, {x.PhysicalTransmitter}): " +
                    $"{x.Bytes} bytes, {x.Chunks} chunks, {x.DriverErrors} driver errors");
            foreach (var warning in result.Manifest.Warnings) Console.Error.WriteLine("WARNING: " + warning);
            var audit = PassiveJournalVerifier.Verify(result.Directory);
            PrintAudit(audit);
            var anyData = audit.Streams.Any(x => x.RawBytes > 0);
            var missingDirections = audit.Streams.Where(x => x.RawBytes == 0)
                .Select(x => "RX" + x.StreamId.ToString(CultureInfo.InvariantCulture)).ToArray();
            if (missingDirections.Length > 0)
                Console.Error.WriteLine("NO TRAFFIC OBSERVED on: " + string.Join(", ", missingDirections) +
                    ". This is not proof of cable failure; check traffic window, tap and UART configuration.");
            var outcome = !audit.Complete ? "IntegrityNotVerified" :
                !anyData ? "NoTrafficObserved" : "TrafficObservedNotProtocolCertified";
            var summary = new
            {
                format = "ARIEC_FIELD_CAPTURE_SUMMARY_V1",
                generatedUtc = DateTime.UtcNow,
                sessionId = result.Manifest.SessionId,
                observationOutcome = outcome,
                durationSeconds = plan.DurationSeconds,
                streamCount = opts.StreamCount,
                sourceMetadata = result.Manifest.Streams.Select(x => new
                {
                    x.StreamId, x.PortName, x.PhysicalTransmitter, x.BaudRate, x.Parity,
                    x.DataBits, x.StopBits, x.Bytes, x.Chunks, x.DriverErrors
                }),
                audit.Complete, audit.Status, audit.Problems,
                streamAudits = audit.Streams,
                physicalValidationPerformed = false,
                disclaimer = "No proof of physical non-interference, UART wire integrity, field timing or RTU conformance."
            };
            File.WriteAllText(Path.Join(result.Directory, "field-summary.json"),
                JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Field result: " + Path.Join(result.Directory, "field-summary.json"));
            return !audit.Complete ? 2 : !anyData ? 3 : 0;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static void PrintAudit(PassiveJournalAudit audit)
    {
        Console.WriteLine($"Journal SHA256/CRC verification: {(audit.Complete ? "PASS" : "INCOMPLETE")} · {audit.Status}");
        foreach (var x in audit.Streams)
            Console.WriteLine($"  RX{x.StreamId}: {x.Records} records, {x.RawBytes} bytes, SHA256={x.Sha256}, valid={x.Valid}");
        foreach (var problem in audit.Problems) Console.Error.WriteLine("WARNING: " + problem);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("ARIEC60870 IEC-101 RX-ONLY Field Tools · software does not transmit");
        Console.WriteLine("  list-ports                              Windows COM inventory; no port open");
        Console.WriteLine("  preflight --scada-com COM7 --rtu-com COM8 --output C:\\Capture --baud 1200");
        Console.WriteLine("  capture   --scada-com COM7 --rtu-com COM8 --output C:\\Capture --baud 1200 --seconds 180");
        Console.WriteLine("  verify    --folder C:\\Capture\\ARIEC-PASSIVE-...");
        Console.WriteLine("  Optional Link B: --link-b-scada-com COM9 --link-b-rtu-com COM10 (both mandatory)");
        Console.WriteLine("  UART: --parity Even|None|Odd --data-bits 8 --stop-bits One|Two");
        Console.WriteLine("  Decode: --link-width 0|1|2 --cot-width 1|2 --ca-width 1|2 --ioa-width 1|2|3");
        Console.WriteLine("  Capture: --frame-gap-ms 900 --link-mode Auto|Balanced|Unbalanced --seconds 1..86400");
        Console.WriteLine("  Exit: 0 verified capture with traffic / valid preflight; 2 integrity failure;");
        Console.WriteLine("        3 no traffic or COM inventory mismatch; 1 invalid configuration/error.");
        Console.WriteLine("  CAUTION: approved isolated/high-impedance tap required. Do not connect TXD/RTS/DTR.");
    }
}
