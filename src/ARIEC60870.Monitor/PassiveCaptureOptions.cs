// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO.Ports;

namespace ARIEC60870.Monitor;

/// <summary>A physical RX-only tap on one transmitter, not a master/slave endpoint.</summary>
public sealed record PassiveSerialSource(
    string PortName,
    string PhysicalTransmitter,
    int BaudRate = 9600,
    Parity Parity = Parity.Even,
    int DataBits = 8,
    StopBits StopBits = StopBits.One);

/// <summary>One full-duplex RS-232 path: independent listen-only receivers for both TX wires.</summary>
public sealed record PassiveCaptureOptions(
    string OutputDirectory,
    PassiveSerialSource ScadaTx,
    PassiveSerialSource RtuTx,
    int ChannelCapacity = 256,
    int ReadChunkBytes = 4096,
    int LinkAddressWidth = 1,
    int CotWidth = 2,
    int CommonAddressWidth = 2,
    int InformationAddressWidth = 3,
    int FrameGapMilliseconds = 900,
    string LinkMode = "Auto")
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory)) throw new ArgumentException("Capture output directory is required.");
        if (string.IsNullOrWhiteSpace(ScadaTx.PortName) || string.IsNullOrWhiteSpace(RtuTx.PortName))
            throw new ArgumentException("Both RX-only COM ports are required.");
        if (string.Equals(ScadaTx.PortName.Trim(), RtuTx.PortName.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose two distinct monitor COM ports.");
        foreach (var s in new[] { ScadaTx, RtuTx })
        {
            // IEC-101 is octet-oriented FT1.2, so 7-bit UART modes would corrupt
            // legitimate start/ACK bytes such as E5/A2; keep 8 data bits.
            if (s.BaudRate is < 300 or > 921600 || s.DataBits != 8 || s.StopBits is not (StopBits.One or StopBits.Two))
                throw new ArgumentException("Invalid serial framing for " + s.PortName);
            if (!Enum.IsDefined(s.Parity) || !Enum.IsDefined(s.StopBits))
                throw new ArgumentException("Unsupported serial mode for " + s.PortName);
        }
        if (ScadaTx.BaudRate != RtuTx.BaudRate || ScadaTx.Parity != RtuTx.Parity ||
            ScadaTx.DataBits != RtuTx.DataBits || ScadaTx.StopBits != RtuTx.StopBits)
            throw new ArgumentException("Both directions of the same serial link must use matching UART framing.");
        if (LinkAddressWidth is < 0 or > 2 || CotWidth is < 1 or > 2 ||
            CommonAddressWidth is < 1 or > 2 || InformationAddressWidth is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(LinkAddressWidth), "Invalid IEC-101 interoperability field widths.");
        if (FrameGapMilliseconds is < 100 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(FrameGapMilliseconds), "Frame gap must be 100–10000 ms.");
        if (LinkMode is not ("Auto" or "Balanced" or "Unbalanced"))
            throw new ArgumentException("IEC-101 passive link mode must be Auto, Balanced or Unbalanced.");
        if (ChannelCapacity is < 16 or > 8192 || ReadChunkBytes is < 128 or > 16384)
            throw new ArgumentOutOfRangeException(nameof(ChannelCapacity), "Channel capacity or chunk length outside bounded limits.");
    }
}

/// <summary>Receive-only contract: intentionally exposes no Write, Break, GI, RTS or DTR.</summary>
public interface IReadOnlySerialTap : IDisposable
{
    string PortName { get; }
    long DriverErrorCount { get; }
    void Open();
    /// <summary>Returns 0 for a normal no-data timeout. Throws for genuine driver failure.</summary>
    int Read(byte[] buffer, int offset, int count);
    void Close();
}

public sealed record CapturedStreamSummary(
    byte StreamId,
    string PortName,
    string PhysicalTransmitter,
    long Chunks,
    long Bytes,
    long DriverErrors,
    string File,
    string RawSha256 = "",
    int BaudRate = 0,
    string Parity = "",
    int DataBits = 0,
    string StopBits = "");

public sealed record PassiveCaptureManifest
{
    public string Format { get; init; } = "ARIEC_PASSIVE_JOURNAL_P0_V1";
    public string SessionId { get; init; } = string.Empty;
    public string Status { get; init; } = "Unknown";
    public string[] Warnings { get; init; } = Array.Empty<string>();
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public long StopwatchOriginTicks { get; init; }
    public long StopwatchFrequency { get; init; }
    public int LinkAddressWidth { get; init; } = 1;
    public int CotWidth { get; init; } = 2;
    public int CommonAddressWidth { get; init; } = 2;
    public int InformationAddressWidth { get; init; } = 3;
    public int FrameGapMilliseconds { get; init; } = 900;
    public string LinkMode { get; init; } = "Auto";
    public string TimestampPolicy { get; init; } = "Monotonic Stopwatch at serial driver read completion; NOT per-byte wire timing.";
    public string IntegrityPolicy { get; init; } = "No application loss detected does not guarantee zero USB/OS/physical losses.";
    public CapturedStreamSummary[] Streams { get; init; } = Array.Empty<CapturedStreamSummary>();
    public string[] PhysicalSafetyLimitations { get; init; } = new[]
    {
        "Two RX COMs observe two TX directions of ONE duplex RS-232 link, not both redundant SCADA links.",
        "Monitor TXD/RTS/DTR must be physically disconnected from plant; software flags cannot guarantee non-interference.",
        "Use an electrically approved isolated/high-impedance tap and preserve original handshaking."
    };
}
