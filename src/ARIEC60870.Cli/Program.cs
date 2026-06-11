// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO.Ports;
using System.Text.Json;
using ARIEC60870.Core.Analysis;
using ARIEC60870.Core.Mapping;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Reporting;
using ARIEC60870.Master;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Reporting;
using ARIEC60870.Master.Slave;
using ARIEC60870.Master.Transport;

namespace ARIEC60870.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || HasHelp(args))
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var command = args[0].ToLowerInvariant();

            if (command == "master")
            {
                return await RunMasterAsync(args.Skip(1).ToArray()).ConfigureAwait(false);
            }

            if (command == "analyze")
            {
                return RunAnalyze(args.Skip(1).ToArray());
            }

            if (command == "slave")
            {
                return await RunSlaveSimulatorAsync(args.Skip(1).ToArray()).ConfigureAwait(false);
            }

            // Backward-compatible v0.2 style:
            // ARIEC60870.Cli <input-log> --report out/report.md
            return RunAnalyze(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ARIEC60870 failed: " + ex.Message);
            return 1;
        }
    }

    private static bool HasHelp(string[] args) =>
        args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
        args.Contains("-h", StringComparer.OrdinalIgnoreCase) ||
        args.Contains("/?", StringComparer.OrdinalIgnoreCase);

    private static int RunAnalyze(string[] args)
    {
        var options = AnalyzeOptions.Parse(args);
        if (string.IsNullOrWhiteSpace(options.InputFile))
        {
            Console.Error.WriteLine("Input file is required.");
            PrintHelp();
            return 1;
        }

        var analyzer = new Iec103TraceAnalyzer();
        var report = analyzer.AnalyzeFile(options.InputFile);

        PrintConsoleSummary(report);

        if (!string.IsNullOrWhiteSpace(options.MarkdownReportPath))
        {
            var writer = new MarkdownReportWriter();
            var markdown = writer.Write(report, options.MaxRows);
            EnsureDirectory(options.MarkdownReportPath);
            File.WriteAllText(options.MarkdownReportPath, markdown);
            Console.WriteLine($"Markdown report written: {options.MarkdownReportPath}");
        }

        if (!string.IsNullOrWhiteSpace(options.JsonReportPath))
        {
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            EnsureDirectory(options.JsonReportPath);
            File.WriteAllText(options.JsonReportPath, json);
            Console.WriteLine($"JSON report written: {options.JsonReportPath}");
        }

        return report.Summary.ChecksumErrors > 0 ? 2 : 0;
    }

    private static async Task<int> RunMasterAsync(string[] args)
    {
        var options = MasterOptions.Parse(args);
        var settings = options.ToSettings();

        Console.WriteLine("ARIEC60870 Protocol Lab - IEC 60870 CLI");
        Console.WriteLine("=======================================");
        Console.WriteLine("Mode      : Single connection active master");
        Console.WriteLine("Target    : " + (settings.UseSimulatedSlave ? "generic relay demo simulated slave" : "protection relay as IEC-103 slave"));
        Console.WriteLine("Endpoint  : " + settings.SerialSummary);
        Console.WriteLine("Duration  : " + options.DurationSeconds + " seconds");
        Console.WriteLine("Polling   : Class 2 normal cycle; Class 1 only when ACD=1 or bounded GI follow-up");
        Console.WriteLine();

        var mappingProfile = string.IsNullOrWhiteSpace(options.MappingProfilePath)
            ? Iec103SignalMappingProfile.Empty
            : Iec103SignalMappingProfile.LoadFromFile(options.MappingProfilePath);

        if (mappingProfile.HasSignals)
        {
            Console.WriteLine("Mapping   : " + mappingProfile.ProfileName + " (" + mappingProfile.Signals.Count + " signals)");
            Console.WriteLine();
        }

        await using IByteTransport transport = options.Simulate
            ? new SimulatedRelayTransport(settings)
            : new SerialByteTransport(settings);
        var session = new Iec103MasterSession(settings, transport, mappingProfile);
        session.EvidenceReceived += (_, item) =>
        {
            var time = item.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
            var raw = string.IsNullOrWhiteSpace(item.RawHex) ? string.Empty : " [" + item.RawHex + "]";
            var rt = item.ResponseTimeMs.HasValue ? $" {item.ResponseTimeMs.Value,4}ms" : "      ";
            var signal = string.IsNullOrWhiteSpace(item.SignalName) ? string.Empty : " | " + item.SignalName + (string.IsNullOrWhiteSpace(item.SignalDisplayValue) ? string.Empty : "=" + item.SignalDisplayValue);
            var edge = item.IsRelayEdgeEvent ? " | EDGE(" + item.EdgeReason + ")" : string.Empty;
            Console.WriteLine($"{time} #{item.SequenceNumber,-4} {item.DirectionText,-5} {item.DataClass,-7} {rt} {item.Summary}{signal}{edge} - {item.Detail}{raw}");
        };
        session.FindingRaised += (_, finding) =>
        {
            Console.WriteLine($"FINDING [{finding.Severity}] {finding.Id}: {finding.Title}");
        };

        var result = await session.RunForAsync(TimeSpan.FromSeconds(options.DurationSeconds), CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("Master summary");
        Console.WriteLine("--------------");
        Console.WriteLine($"TX/RX                 : {result.Counters.TxFrames} / {result.Counters.RxFrames}");
        Console.WriteLine($"Class 1 / Class 2     : {result.Counters.Class1Requests} / {result.Counters.Class2Requests}");
        Console.WriteLine($"NO DATA / user data   : {result.Counters.NoDataResponses} / {result.Counters.UserDataResponses}");
        Console.WriteLine($"ACK / NACK            : {result.Counters.AckResponses} / {result.Counters.NackResponses}");
        Console.WriteLine($"RST.Link / RST.FCB    : {result.Counters.ResetRemoteLinkCommands} / {result.Counters.ResetFcbCommands}");
        Console.WriteLine($"GI / GI END / CS      : {result.Counters.GiCommands} / {result.Counters.GiEndResponses} / {result.Counters.ClockSyncCommands}");
        Console.WriteLine($"DPI / unknown ASDU    : {result.Counters.DpiEvents} / {result.Counters.UnknownAsduResponses}");
        Console.WriteLine($"Timeouts / recovery   : {result.Counters.Timeouts} / {result.Counters.TimeoutRecoveries}");
        Console.WriteLine($"Busy / checksum err   : {result.Counters.BusyResponses} / {result.Counters.ChecksumErrors}");
        Console.WriteLine($"Avg / max response    : {result.Counters.AverageResponseTimeMs:F1} ms / {result.Counters.MaxResponseTimeMs} ms");
        Console.WriteLine($"Value points          : {result.ValuePoints.Count}");
        Console.WriteLine($"Relay event log       : {result.EventLog.Count}");
        Console.WriteLine($"Assessment            : {result.Assessment.OverallStatus} ({result.Assessment.Score}/100) - {result.Assessment.Summary}");
        Console.WriteLine($"Findings              : {result.Findings.Count}");
        Console.WriteLine($"Completion            : {result.CompletionReason}");
        Console.WriteLine();
        Console.WriteLine("AutoTest checklist");
        Console.WriteLine("------------------");
        foreach (var item in result.Assessment.Items)
        {
            Console.WriteLine($"[{item.Status}] {item.Area}: {item.Title}");
            Console.WriteLine($"  Evidence: {item.Evidence}");
            Console.WriteLine($"  Recommendation: {item.Recommendation}");
        }

        if (!string.IsNullOrWhiteSpace(options.MarkdownReportPath))
        {
            EnsureDirectory(options.MarkdownReportPath);
            var markdown = new MasterMarkdownReportWriter().Write(result, options.MaxEvents);
            File.WriteAllText(options.MarkdownReportPath, markdown);
            Console.WriteLine($"Markdown evidence written: {options.MarkdownReportPath}");
        }

        if (!string.IsNullOrWhiteSpace(options.JsonReportPath))
        {
            EnsureDirectory(options.JsonReportPath);
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(options.JsonReportPath, json);
            Console.WriteLine($"JSON evidence written: {options.JsonReportPath}");
        }

        return result.Assessment.OverallStatus == Iec103AssessmentStatus.Fail ? 3 : result.Counters.ChecksumErrors > 0 ? 2 : 0;
    }


    private static async Task<int> RunSlaveSimulatorAsync(string[] args)
    {
        var options = SlaveOptions.Parse(args);
        var settings = options.ToSettings();

        Console.WriteLine("ARIEC60870 - IEC 60870-5-103 Slave Simulator");
        Console.WriteLine("==========================================");
        Console.WriteLine("Mode      : Deterministic IEC-103 slave for active master testing");
        Console.WriteLine("Endpoint  : " + settings.SerialSummary);
        Console.WriteLine("Duration  : " + options.DurationSeconds + " seconds");
        Console.WriteLine("Behavior  : GI snapshot; Class 2 animated current; ACD-driven Class 1 pickup/trip/reset events");
        Console.WriteLine($"Protection: pickup random phase after {settings.InitialFaultDelaySeconds}s, trip after {settings.TripDelayMs}ms, latch reset by command FUN={settings.ResetCommandFun}/INF={settings.ResetCommandInf} or auto reset after {settings.AutoResetSeconds}s, repeat after {settings.FaultRepeatDelaySeconds}s");
        if (settings.DfcBusyMode) Console.WriteLine("Mode flag : DFC busy mode enabled");
        if (settings.SilentMode) Console.WriteLine("Mode flag : silent/no-response mode enabled");
        if (settings.BadChecksumMode) Console.WriteLine("Mode flag : bad checksum mode enabled");
        if (!settings.SeedGiEnd) Console.WriteLine("Mode flag : missing GI END mode enabled");
        Console.WriteLine();

        var serialSettings = new Iec103MasterSettings
        {
            PortName = settings.PortName,
            BaudRate = settings.BaudRate,
            DataBits = settings.DataBits,
            Parity = settings.Parity,
            StopBits = settings.StopBits,
            LinkAddress = settings.LinkAddress,
            CommonAddress = settings.CommonAddress,
            ResponseTimeoutMs = settings.ResponseTimeoutMs,
            TargetProfile = "IEC-103 slave simulator"
        };

        await using IByteTransport transport = new SerialByteTransport(serialSettings);
        var session = new Iec103SlaveSimulatorSession(settings, transport);
        session.EventRaised += (_, item) =>
        {
            var time = item.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
            var raw = string.IsNullOrWhiteSpace(item.RawHex) ? string.Empty : " [" + item.RawHex + "]";
            Console.WriteLine($"{time} {item.Direction,-5} {item.DataClass,-7} {item.Summary} - {item.Detail}{raw}");
        };

        var result = await session.RunForAsync(TimeSpan.FromSeconds(options.DurationSeconds), CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("Slave simulator summary");
        Console.WriteLine("-----------------------");
        Console.WriteLine($"RX/TX                 : {result.Counters.RxFrames} / {result.Counters.TxFrames}");
        Console.WriteLine($"Class 1 / Class 2 req : {result.Counters.Class1Requests} / {result.Counters.Class2Requests}");
        Console.WriteLine($"GI / Clock Sync req   : {result.Counters.GiRequests} / {result.Counters.ClockSyncRequests}");
        Console.WriteLine($"Reset link / FCB req  : {result.Counters.ResetLinkRequests} / {result.Counters.ResetFcbRequests}");
        Console.WriteLine($"ACK / NO DATA / data  : {result.Counters.AckResponses} / {result.Counters.NoDataResponses} / {result.Counters.UserDataResponses}");
        Console.WriteLine($"DFC / bad checksum    : {result.Counters.DfcBusyResponses} / {result.Counters.BadChecksumResponses}");
        Console.WriteLine($"Malformed / unknown   : {result.Counters.MalformedRxFrames} / {result.Counters.UnknownRequests}");
        Console.WriteLine($"Max Class 1 queue     : {result.Counters.Class1QueueMaxDepth}");
        Console.WriteLine($"Protection cycles     : {result.Counters.ProtectionCycles}");
        Console.WriteLine($"Pickup / Trip events  : {result.Counters.PickupEvents} / {result.Counters.TripEvents}");
        Console.WriteLine($"Auto / Cmd resets     : {result.Counters.AutoResets} / {result.Counters.CommandResets}");
        Console.WriteLine($"Current frames        : {result.Counters.CurrentFrames}");
        Console.WriteLine($"Completion            : {result.CompletionReason}");

        return result.CompletedNormally ? 0 : 1;
    }

    private static void PrintConsoleSummary(AnalysisReport report)
    {
        var s = report.Summary;
        Console.WriteLine("ARIEC60870 - IEC 60870-5-103 Field Forensic Analyzer");
        Console.WriteLine("====================================================");
        Console.WriteLine($"Source              : {report.SourceFile}");
        Console.WriteLine($"Frames              : {s.TotalFrames}");
        Console.WriteLine($"Fixed / Variable    : {s.FixedFrames} / {s.VariableFrames}");
        Console.WriteLine($"Class 1 / Class 2   : {s.Class1Requests} / {s.Class2Requests}");
        Console.WriteLine($"NO DATA responses   : {s.NoDataResponses} ({s.NoDataRatio:P1})");
        Console.WriteLine($"Reset FCB commands  : {s.ResetFcbCommands}");
        Console.WriteLine($"Checksum errors     : {s.ChecksumErrors}");
        Console.WriteLine();

        Console.WriteLine("Findings");
        Console.WriteLine("--------");
        if (report.Findings.Count == 0)
        {
            Console.WriteLine("No semantic finding was raised by the current rule set.");
        }
        else
        {
            foreach (var finding in report.Findings)
            {
                Console.WriteLine($"[{finding.Severity}] {finding.Id} - {finding.Title}");
                Console.WriteLine($"  {finding.Summary}");
                Console.WriteLine($"  Recommendation: {finding.Recommendation}");
                Console.WriteLine();
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("ARIEC60870");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Analyze existing log:");
        Console.WriteLine("    ARIEC60870.Cli analyze <input-log> [--report <report.md>] [--json <report.json>] [--max-rows 120]");
        Console.WriteLine();
        Console.WriteLine("  Active single-connection IEC-103 master:");
        Console.WriteLine("    ARIEC60870.Cli master --port COM1 [--baud 9600] [--link 1] [--ca 1] [--duration 30] [--mapping samples/mapping-profiles/example-user-mapping.profile.json] [--report out/master.md] [--json out/master.json]");
        Console.WriteLine("    ARIEC60870.Cli master --simulate --duration 10 --report out/demo-master.md");
        Console.WriteLine();
        Console.WriteLine("  IEC-103 slave simulator for master testing:");
        Console.WriteLine("    ARIEC60870.Cli slave --port COM2 [--baud 9600] [--link 1] [--ca 1] [--duration 300]");
        Console.WriteLine("    ARIEC60870.Cli slave --port COM2 --missing-gi-end | --dfc-busy | --silent | --bad-checksum");
        Console.WriteLine();
        Console.WriteLine("Important master options:");
        Console.WriteLine("    --simulate --mapping <profile.json> --timeout <ms> --class2-interval <ms> --max-class1-drain <n> --clock-sync --no-gi --reset-link --no-reset-fcb");
        Console.WriteLine();
        Console.WriteLine("Important slave simulator options:");
        Console.WriteLine("    --spontaneous-after <n> --no-spontaneous --missing-gi-end --dfc-busy --silent --bad-checksum --turnaround <ms>");
        Console.WriteLine();
        Console.WriteLine("Backward-compatible analyzer mode:");
        Console.WriteLine("    ARIEC60870.Cli <input-log> --report out/report.md");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run --project src/ARIEC60870.Cli -- analyze samples/sample_iec103_trace.log --report out/report.md");
        Console.WriteLine("  dotnet run --project src/ARIEC60870.Cli -- master --port COM3 --baud 9600 --link 1 --ca 1 --duration 60 --report out/master.md --json out/master.json");
        Console.WriteLine("  dotnet run --project src/ARIEC60870.Cli -- master --simulate --duration 10 --report out/demo-master.md");
        Console.WriteLine("  dotnet run --project src/ARIEC60870.Cli -- slave --port COM2 --baud 9600 --link 1 --ca 1 --duration 300");
    }

    private static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private sealed class AnalyzeOptions
    {
        public string InputFile { get; private init; } = string.Empty;
        public string? MarkdownReportPath { get; private init; }
        public string? JsonReportPath { get; private init; }
        public int MaxRows { get; private init; } = 120;

        public static AnalyzeOptions Parse(string[] args)
        {
            string? input = null;
            string? markdown = null;
            string? json = null;
            var maxRows = 120;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.Equals("--report", StringComparison.OrdinalIgnoreCase) || arg.Equals("-r", StringComparison.OrdinalIgnoreCase))
                {
                    markdown = RequireValue(args, ref i, arg);
                    continue;
                }

                if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    json = RequireValue(args, ref i, arg);
                    continue;
                }

                if (arg.Equals("--max-rows", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = RequireValue(args, ref i, arg);
                    if (!int.TryParse(raw, out maxRows) || maxRows < 1)
                    {
                        throw new ArgumentException("--max-rows must be a positive integer.");
                    }
                    continue;
                }

                if (arg.StartsWith("-", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Unknown analyze option: {arg}");
                }

                input ??= arg;
            }

            return new AnalyzeOptions
            {
                InputFile = input ?? string.Empty,
                MarkdownReportPath = markdown,
                JsonReportPath = json,
                MaxRows = maxRows
            };
        }
    }

    private sealed class MasterOptions
    {
        public string PortName { get; private init; } = "COM1";
        public int BaudRate { get; private init; } = 9600;
        public int DataBits { get; private init; } = 8;
        public Parity Parity { get; private init; } = Parity.Even;
        public StopBits StopBits { get; private init; } = StopBits.One;
        public byte LinkAddress { get; private init; } = 1;
        public byte CommonAddress { get; private init; } = 1;
        public int DurationSeconds { get; private init; } = 30;
        public int ResponseTimeoutMs { get; private init; } = 1500;
        public int Class2PollIntervalMs { get; private init; } = 500;
        public int Class1DrainDelayMs { get; private init; } = 20;
        public int BusyBackoffMs { get; private init; } = 250;
        public int StartupDelayMs { get; private init; } = 300;
        public int MaxClass1DrainFrames { get; private init; } = 64;
        public int TimeoutBurstLimit { get; private init; } = 3;
        public int TimeoutRecoveryBackoffMs { get; private init; } = 250;
        public bool ResetRemoteLinkOnConnect { get; private init; }
        public bool ResetFcbOnConnect { get; private init; } = true;
        public bool GiOnConnect { get; private init; } = true;
        public bool ClockSyncOnConnect { get; private init; }
        public bool ResetFcbAfterTimeoutBurst { get; private init; } = true;
        public bool Simulate { get; private init; }
        public string? MarkdownReportPath { get; private init; }
        public string? JsonReportPath { get; private init; }
        public int MaxEvents { get; private init; } = 300;
        public string? MappingProfilePath { get; private init; }

        public Iec103MasterSettings ToSettings() => new()
        {
            PortName = PortName,
            BaudRate = BaudRate,
            DataBits = DataBits,
            Parity = Parity,
            StopBits = StopBits,
            LinkAddress = LinkAddress,
            CommonAddress = CommonAddress,
            ResponseTimeoutMs = ResponseTimeoutMs,
            Class2PollIntervalMs = Class2PollIntervalMs,
            Class1DrainDelayMs = Class1DrainDelayMs,
            BusyBackoffMs = BusyBackoffMs,
            StartupDelayMs = StartupDelayMs,
            MaxClass1DrainFrames = MaxClass1DrainFrames,
            MaxConsecutiveTimeoutsBeforeResetFcb = TimeoutBurstLimit,
            TimeoutRecoveryBackoffMs = TimeoutRecoveryBackoffMs,
            ResetRemoteLinkOnConnect = ResetRemoteLinkOnConnect,
            ResetFcbOnConnect = ResetFcbOnConnect,
            SendGeneralInterrogationOnConnect = GiOnConnect,
            SendClockSyncOnConnect = ClockSyncOnConnect,
            ResetFcbAfterTimeoutBurst = ResetFcbAfterTimeoutBurst,
            UseSimulatedSlave = Simulate,
            TargetProfile = Simulate ? "generic relay demo slave" : "IEC-103 protection relay",
            MappingProfilePath = MappingProfilePath ?? string.Empty
        };

        public static MasterOptions Parse(string[] args)
        {
            var port = "COM1";
            var baud = 9600;
            var dataBits = 8;
            var parity = Parity.Even;
            var stopBits = StopBits.One;
            byte link = 1;
            byte ca = 1;
            var duration = 30;
            var timeout = 1500;
            var class2 = 500;
            var class1Delay = 20;
            var busyBackoff = 250;
            var startupDelay = 300;
            var maxClass1Drain = 64;
            var timeoutBurst = 3;
            var timeoutBackoff = 250;
            var resetLink = false;
            var resetFcb = true;
            var gi = true;
            var clockSync = false;
            var timeoutReset = true;
            var simulate = false;
            string? markdown = null;
            string? json = null;
            string? mapping = null;
            var maxEvents = 300;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                switch (arg.ToLowerInvariant())
                {
                    case "--port":
                    case "-p":
                        port = RequireValue(args, ref i, arg);
                        break;
                    case "--baud":
                        baud = ParseInt(RequireValue(args, ref i, arg), arg, min: 300);
                        break;
                    case "--databits":
                        dataBits = ParseInt(RequireValue(args, ref i, arg), arg, min: 5, max: 8);
                        break;
                    case "--parity":
                        parity = ParseEnum<Parity>(RequireValue(args, ref i, arg), arg);
                        break;
                    case "--stopbits":
                        stopBits = ParseStopBits(RequireValue(args, ref i, arg));
                        break;
                    case "--link":
                        link = ParseByte(RequireValue(args, ref i, arg), arg);
                        break;
                    case "--ca":
                        ca = ParseByte(RequireValue(args, ref i, arg), arg);
                        break;
                    case "--duration":
                        duration = ParseInt(RequireValue(args, ref i, arg), arg, min: 1);
                        break;
                    case "--timeout":
                        timeout = ParseInt(RequireValue(args, ref i, arg), arg, min: 50);
                        break;
                    case "--class2-interval":
                        class2 = ParseInt(RequireValue(args, ref i, arg), arg, min: 20);
                        break;
                    case "--class1-delay":
                        class1Delay = ParseInt(RequireValue(args, ref i, arg), arg, min: 0);
                        break;
                    case "--busy-backoff":
                        busyBackoff = ParseInt(RequireValue(args, ref i, arg), arg, min: 0);
                        break;
                    case "--startup-delay":
                        startupDelay = ParseInt(RequireValue(args, ref i, arg), arg, min: 0);
                        break;
                    case "--max-class1-drain":
                        maxClass1Drain = ParseInt(RequireValue(args, ref i, arg), arg, min: 1);
                        break;
                    case "--timeout-burst":
                        timeoutBurst = ParseInt(RequireValue(args, ref i, arg), arg, min: 1);
                        break;
                    case "--timeout-recovery-backoff":
                        timeoutBackoff = ParseInt(RequireValue(args, ref i, arg), arg, min: 0);
                        break;
                    case "--reset-link":
                        resetLink = true;
                        break;
                    case "--no-reset-fcb":
                        resetFcb = false;
                        break;
                    case "--no-gi":
                        gi = false;
                        break;
                    case "--clock-sync":
                        clockSync = true;
                        break;
                    case "--no-timeout-reset":
                        timeoutReset = false;
                        break;
                    case "--simulate":
                        simulate = true;
                        break;
                    case "--report":
                    case "-r":
                        markdown = RequireValue(args, ref i, arg);
                        break;
                    case "--json":
                        json = RequireValue(args, ref i, arg);
                        break;
                    case "--max-events":
                        maxEvents = ParseInt(RequireValue(args, ref i, arg), arg, min: 1);
                        break;
                    case "--mapping":
                    case "--profile":
                        mapping = RequireValue(args, ref i, arg);
                        break;
                    default:
                        throw new ArgumentException($"Unknown master option: {arg}");
                }
            }

            return new MasterOptions
            {
                PortName = port,
                BaudRate = baud,
                DataBits = dataBits,
                Parity = parity,
                StopBits = stopBits,
                LinkAddress = link,
                CommonAddress = ca,
                DurationSeconds = duration,
                ResponseTimeoutMs = timeout,
                Class2PollIntervalMs = class2,
                Class1DrainDelayMs = class1Delay,
                BusyBackoffMs = busyBackoff,
                StartupDelayMs = startupDelay,
                MaxClass1DrainFrames = maxClass1Drain,
                TimeoutBurstLimit = timeoutBurst,
                TimeoutRecoveryBackoffMs = timeoutBackoff,
                ResetRemoteLinkOnConnect = resetLink,
                ResetFcbOnConnect = resetFcb,
                GiOnConnect = gi,
                ClockSyncOnConnect = clockSync,
                ResetFcbAfterTimeoutBurst = timeoutReset,
                Simulate = simulate,
                MarkdownReportPath = markdown,
                JsonReportPath = json,
                MaxEvents = maxEvents,
                MappingProfilePath = mapping
            };
        }
    }


    private sealed class SlaveOptions
    {
        public string PortName { get; private init; } = "COM2";
        public int BaudRate { get; private init; } = 9600;
        public int DataBits { get; private init; } = 8;
        public Parity Parity { get; private init; } = Parity.Even;
        public StopBits StopBits { get; private init; } = StopBits.One;
        public byte LinkAddress { get; private init; } = 1;
        public byte CommonAddress { get; private init; } = 1;
        public int DurationSeconds { get; private init; } = 300;
        public int ResponseTimeoutMs { get; private init; } = 1500;
        public int TurnaroundDelayMs { get; private init; } = 8;
        public int SpontaneousAfterClass2Polls { get; private init; } = 4;
        public bool SeedGiEnd { get; private init; } = true;
        public bool EnableSpontaneousDemoEvent { get; private init; } = true;
        public bool DfcBusyMode { get; private init; }
        public bool SilentMode { get; private init; }
        public bool BadChecksumMode { get; private init; }
        public bool EnableProtectionBehavior { get; private init; } = true;
        public int InitialFaultDelaySeconds { get; private init; } = 3;
        public int FaultRepeatDelaySeconds { get; private init; } = 10;
        public int AutoResetSeconds { get; private init; } = 20;
        public int TripDelayMs { get; private init; } = 200;
        public int RandomSeed { get; private init; } = 103;
        public byte ResetCommandFun { get; private init; } = 255;
        public byte ResetCommandInf { get; private init; } = 19;

        public Iec103SlaveSimulatorSettings ToSettings() => new()
        {
            PortName = PortName,
            BaudRate = BaudRate,
            DataBits = DataBits,
            Parity = Parity,
            StopBits = StopBits,
            LinkAddress = LinkAddress,
            CommonAddress = CommonAddress,
            ResponseTimeoutMs = ResponseTimeoutMs,
            TurnaroundDelayMs = TurnaroundDelayMs,
            SpontaneousAfterClass2Polls = SpontaneousAfterClass2Polls,
            SeedGiEnd = SeedGiEnd,
            EnableSpontaneousDemoEvent = EnableSpontaneousDemoEvent,
            DfcBusyMode = DfcBusyMode,
            SilentMode = SilentMode,
            BadChecksumMode = BadChecksumMode,
            EnableProtectionBehavior = EnableProtectionBehavior,
            InitialFaultDelaySeconds = InitialFaultDelaySeconds,
            FaultRepeatDelaySeconds = FaultRepeatDelaySeconds,
            AutoResetSeconds = AutoResetSeconds,
            TripDelayMs = TripDelayMs,
            RandomSeed = RandomSeed,
            ResetCommandFun = ResetCommandFun,
            ResetCommandInf = ResetCommandInf
        };

        public static SlaveOptions Parse(string[] args)
        {
            var port = "COM2";
            var baud = 9600;
            var dataBits = 8;
            var parity = Parity.Even;
            var stopBits = StopBits.One;
            byte link = 1;
            byte ca = 1;
            var duration = 300;
            var timeout = 1500;
            var turnaround = 8;
            var spontaneousAfter = 4;
            var giEnd = true;
            var spontaneous = true;
            var dfcBusy = false;
            var silent = false;
            var badChecksum = false;
            var enableProtection = true;
            var initialFaultDelay = 3;
            var faultRepeatDelay = 10;
            var autoReset = 20;
            var tripDelay = 200;
            var randomSeed = 103;
            byte resetFun = 255;
            byte resetInf = 19;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg.ToLowerInvariant())
                {
                    case "--port":
                    case "-p":
                        port = RequireValue(args, ref i, arg);
                        break;
                    case "--baud":
                        baud = ParseInt(RequireValue(args, ref i, arg), arg, min: 300);
                        break;
                    case "--databits":
                        dataBits = ParseInt(RequireValue(args, ref i, arg), arg, min: 5, max: 8);
                        break;
                    case "--parity":
                        parity = ParseEnum<Parity>(RequireValue(args, ref i, arg), arg);
                        break;
                    case "--stopbits":
                        stopBits = ParseStopBits(RequireValue(args, ref i, arg));
                        break;
                    case "--link":
                        link = ParseByte(RequireValue(args, ref i, arg), arg);
                        break;
                    case "--ca":
                        ca = ParseByte(RequireValue(args, ref i, arg), arg);
                        break;
                    case "--duration":
                        duration = ParseInt(RequireValue(args, ref i, arg), arg, min: 1);
                        break;
                    case "--timeout":
                        timeout = ParseInt(RequireValue(args, ref i, arg), arg, min: 50);
                        break;
                    case "--turnaround":
                        turnaround = ParseInt(RequireValue(args, ref i, arg), arg, min: 0);
                        break;
                    case "--spontaneous-after":
                        spontaneousAfter = ParseInt(RequireValue(args, ref i, arg), arg, min: 1);
                        break;
                    case "--missing-gi-end":
                        giEnd = false;
                        break;
                    case "--no-spontaneous":
                        spontaneous = false;
                        break;
                    case "--dfc-busy":
                        dfcBusy = true;
                        break;
                    case "--silent":
                        silent = true;
                        break;
                    case "--bad-checksum":
                        badChecksum = true;
                        break;
                    case "--no-protection-demo":
                        enableProtection = false;
                        break;
                    case "--initial-fault-delay":
                        initialFaultDelay = ParseInt(RequireValue(args, ref i, arg), arg, min: 1);
                        break;
                    case "--fault-repeat-delay":
                        faultRepeatDelay = ParseInt(RequireValue(args, ref i, arg), arg, min: 1);
                        break;
                    case "--auto-reset":
                        autoReset = ParseInt(RequireValue(args, ref i, arg), arg, min: 1);
                        break;
                    case "--trip-delay":
                        tripDelay = ParseInt(RequireValue(args, ref i, arg), arg, min: 1);
                        break;
                    case "--random-seed":
                        randomSeed = ParseInt(RequireValue(args, ref i, arg), arg, min: 0);
                        break;
                    case "--reset-fun":
                        resetFun = ParseByte(RequireValue(args, ref i, arg), arg);
                        break;
                    case "--reset-inf":
                        resetInf = ParseByte(RequireValue(args, ref i, arg), arg);
                        break;
                    default:
                        throw new ArgumentException($"Unknown slave option: {arg}");
                }
            }

            return new SlaveOptions
            {
                PortName = port,
                BaudRate = baud,
                DataBits = dataBits,
                Parity = parity,
                StopBits = stopBits,
                LinkAddress = link,
                CommonAddress = ca,
                DurationSeconds = duration,
                ResponseTimeoutMs = timeout,
                TurnaroundDelayMs = turnaround,
                SpontaneousAfterClass2Polls = spontaneousAfter,
                SeedGiEnd = giEnd,
                EnableSpontaneousDemoEvent = spontaneous,
                DfcBusyMode = dfcBusy,
                SilentMode = silent,
                BadChecksumMode = badChecksum,
                EnableProtectionBehavior = enableProtection,
                InitialFaultDelaySeconds = initialFaultDelay,
                FaultRepeatDelaySeconds = faultRepeatDelay,
                AutoResetSeconds = autoReset,
                TripDelayMs = tripDelay,
                RandomSeed = randomSeed,
                ResetCommandFun = resetFun,
                ResetCommandInf = resetInf
            };
        }
    }

    private static string RequireValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index++;
        return args[index];
    }

    private static int ParseInt(string raw, string optionName, int min, int max = int.MaxValue)
    {
        if (!int.TryParse(raw, out var value) || value < min || value > max)
        {
            throw new ArgumentException($"{optionName} must be an integer between {min} and {max}.");
        }

        return value;
    }

    private static byte ParseByte(string raw, string optionName)
    {
        if (!byte.TryParse(raw, out var value))
        {
            throw new ArgumentException($"{optionName} must be 0..255.");
        }

        return value;
    }

    private static T ParseEnum<T>(string raw, string optionName) where T : struct
    {
        if (!Enum.TryParse<T>(raw, ignoreCase: true, out var value))
        {
            throw new ArgumentException($"{optionName} value '{raw}' is invalid.");
        }

        return value;
    }

    private static StopBits ParseStopBits(string raw)
    {
        if (raw.Equals("1", StringComparison.OrdinalIgnoreCase) || raw.Equals("one", StringComparison.OrdinalIgnoreCase))
        {
            return StopBits.One;
        }

        if (raw.Equals("2", StringComparison.OrdinalIgnoreCase) || raw.Equals("two", StringComparison.OrdinalIgnoreCase))
        {
            return StopBits.Two;
        }

        if (raw.Equals("1.5", StringComparison.OrdinalIgnoreCase) || raw.Equals("onepointfive", StringComparison.OrdinalIgnoreCase))
        {
            return StopBits.OnePointFive;
        }

        return ParseEnum<StopBits>(raw, "--stopbits");
    }
}
