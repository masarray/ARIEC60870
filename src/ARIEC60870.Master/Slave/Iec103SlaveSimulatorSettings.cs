// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO.Ports;

namespace ARIEC60870.Master.Slave;

public sealed class Iec103SlaveSimulatorSettings
{
    public string PortName { get; init; } = "COM2";
    public int BaudRate { get; init; } = 9600;
    public int DataBits { get; init; } = 8;
    public Parity Parity { get; init; } = Parity.Even;
    public StopBits StopBits { get; init; } = StopBits.One;
    public byte LinkAddress { get; init; } = 1;
    public byte CommonAddress { get; init; } = 1;
    public int ResponseTimeoutMs { get; init; } = 1500;
    public int TurnaroundDelayMs { get; init; } = 8;
    public int SpontaneousAfterClass2Polls { get; init; } = 4;
    public bool SeedGiEnd { get; init; } = true;
    public bool EnableSpontaneousDemoEvent { get; init; } = true;
    public bool EnableProtectionBehavior { get; init; } = true;
    public int InitialFaultDelaySeconds { get; init; } = 3;
    public int FaultRepeatDelaySeconds { get; init; } = 10;
    public int AutoResetSeconds { get; init; } = 20;
    public int TripDelayMs { get; init; } = 200;
    public int RandomSeed { get; init; } = 103;
    public byte ResetCommandFun { get; init; } = 255;
    public byte ResetCommandInf { get; init; } = 19;
    public bool DfcBusyMode { get; init; }
    public bool SilentMode { get; init; }
    public bool BadChecksumMode { get; init; }

    public string SerialSummary => $"{PortName}, {BaudRate} bps, {DataBits}{ParityToText(Parity)}{StopBitsToText(StopBits)}, Link={LinkAddress}, CA={CommonAddress}";

    private static string ParityToText(Parity parity) => parity switch
    {
        Parity.Even => "E",
        Parity.Odd => "O",
        Parity.None => "N",
        _ => parity.ToString()[0].ToString().ToUpperInvariant()
    };

    private static string StopBitsToText(StopBits stopBits) => stopBits switch
    {
        StopBits.One => "1",
        StopBits.Two => "2",
        StopBits.OnePointFive => "1.5",
        _ => stopBits.ToString()
    };
}
