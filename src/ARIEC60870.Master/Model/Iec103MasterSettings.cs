// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO.Ports;

namespace ARIEC60870.Master.Model;

/// <summary>
/// Single-connection IEC-103 master configuration.
/// The product direction is intentionally master-to-relay first:
/// one COM connection to one protection relay acting as IEC-103 slave.
/// </summary>
public sealed class Iec103MasterSettings
{
    public Iec60870ProtocolMode ProtocolMode { get; set; } = Iec60870ProtocolMode.Iec103;
    public string TcpHost { get; set; } = "127.0.0.1";
    public int TcpPort { get; set; } = 2404;
    public int CauseOfTransmissionSize { get; set; } = 2;
    public int CommonAddressSize { get; set; } = 2;
    public int InformationObjectAddressSize { get; set; } = 3;
    public int LinkAddressSize { get; set; } = 1;
    public string TransmissionMode { get; set; } = "Unbalanced";
    public int Iec104T0TimeoutMs { get; set; } = 30000;
    public int Iec104T1AckTimeoutMs { get; set; } = 15000;
    public int Iec104T2AckDelayMs { get; set; } = 10000;
    public int Iec104T3TestIntervalMs { get; set; } = 20000;
    public int Iec104KMaxUnacknowledged { get; set; } = 12;
    public int Iec104WReceiveWindow { get; set; } = 8;

    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public Parity Parity { get; set; } = Parity.Even;
    public StopBits StopBits { get; set; } = StopBits.One;

    public int LinkAddress { get; set; } = 1;
    public int CommonAddress { get; set; } = 1;

    public bool UseSimulatedSlave { get; set; }
    public string TargetProfile { get; set; } = "IEC-103 protection relay";
    public string MappingProfilePath { get; set; } = string.Empty;

    public int ResponseTimeoutMs { get; set; } = 1500;
    public int Class2PollIntervalMs { get; set; } = 500;
    public int Class1DrainDelayMs { get; set; } = 20;
    public int BusyBackoffMs { get; set; } = 250;
    public int StartupDelayMs { get; set; } = 300;
    public int MaxClass1DrainFrames { get; set; } = 64;
    public int MaxConsecutiveClass1BeforeClass2 { get; set; } = 4;
    public int MaxConsecutiveTimeoutsBeforeResetFcb { get; set; } = 3;
    public int TimeoutRecoveryBackoffMs { get; set; } = 250;

    public bool ResetRemoteLinkOnConnect { get; set; } = false;
    public bool ResetFcbOnConnect { get; set; } = false;
    public bool SendGeneralInterrogationOnConnect { get; set; } = true;
    public bool SendClockSyncOnConnect { get; set; } = false;
    public bool RequestClass2ImmediatelyAfterStartup { get; set; } = true;
    public bool ResetFcbAfterTimeoutBurst { get; set; } = true;

    /// <summary>
    /// Public reports should not expose local folder/customer paths by default.
    /// Enable this only for private debugging where the full workstation path is useful.
    /// </summary>
    public bool IncludeLocalPathsInReports { get; set; } = false;

    /// <summary>
    /// Returns a copy suitable for Markdown/JSON evidence export.
    /// Operational settings are preserved, while local path fields are reduced to file names
    /// unless IncludeLocalPathsInReports is explicitly enabled.
    /// </summary>
    public Iec103MasterSettings CreateReportSnapshot()
    {
        var copy = (Iec103MasterSettings)MemberwiseClone();
        if (!IncludeLocalPathsInReports && !string.IsNullOrWhiteSpace(copy.MappingProfilePath))
        {
            copy.MappingProfilePath = Path.GetFileName(copy.MappingProfilePath);
        }

        return copy;
    }

    /// <summary>
    /// Memory guard for long polling sessions. Counters always keep full totals, but retained
    /// evidence is bounded so the desktop app and JSON export stay responsive.
    /// </summary>
    public int MaxRetainedEvidenceEvents { get; set; } = 10000;
    public int MaxRetainedRelayEvents { get; set; } = 5000;
    public int MaxRetainedFindings { get; set; } = 1000;

    public static Iec103MasterSettings CreateDefault() => new();

    public string SerialSummary => ProtocolMode == Iec60870ProtocolMode.Iec104
        ? (UseSimulatedSlave
            ? $"Simulated IEC-104 server, TCP={TcpHost}:{TcpPort}, CA={CommonAddress} ({CommonAddressSize} octet), COT={CauseOfTransmissionSize}, IOA={InformationObjectAddressSize}, t3={Iec104T3TestIntervalMs}ms"
            : $"IEC-104 TCP {TcpHost}:{TcpPort}, CA={CommonAddress} ({CommonAddressSize} octet), COT={CauseOfTransmissionSize}, IOA={InformationObjectAddressSize}, t1={Iec104T1AckTimeoutMs}ms/t3={Iec104T3TestIntervalMs}ms")
        : UseSimulatedSlave
            ? $"Simulated {TargetProfile}, Link={LinkAddress} ({LinkAddressSize} octet), CA={CommonAddress} ({CommonAddressSize} octet), COT={CauseOfTransmissionSize}, IOA={InformationObjectAddressSize}"
            : $"{ProtocolModeText} {PortName}, {BaudRate} bps, {DataBits}{ParityText}{StopBitsText}, Link={LinkAddress} ({LinkAddressSize} octet), CA={CommonAddress} ({CommonAddressSize} octet), COT={CauseOfTransmissionSize}, IOA={InformationObjectAddressSize}";

    public string ProtocolModeText => ProtocolMode switch
    {
        Iec60870ProtocolMode.Iec101 => "IEC-101",
        Iec60870ProtocolMode.Iec104 => "IEC-104",
        _ => "IEC-103"
    };

    private string ParityText => Parity switch
    {
        Parity.None => "N",
        Parity.Odd => "O",
        Parity.Even => "E",
        Parity.Mark => "M",
        Parity.Space => "S",
        _ => Parity.ToString()
    };

    private string StopBitsText => StopBits switch
    {
        StopBits.One => "1",
        StopBits.Two => "2",
        StopBits.OnePointFive => "1.5",
        _ => StopBits.ToString()
    };
}
