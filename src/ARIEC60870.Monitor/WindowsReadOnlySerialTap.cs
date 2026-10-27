// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO.Ports;

namespace ARIEC60870.Monitor;

/// <summary>
/// Receives UART bytes only. Plant-side TXD, RTS and DTR must be physically unconnected.
/// SerialPort.Open can pulse modem outputs even if disabled; software is NOT isolation.
/// </summary>
public sealed class WindowsReadOnlySerialTap : IReadOnlySerialTap
{
    private readonly PassiveSerialSource _settings;
    private SerialPort? _port;
    private long _driverErrorCount;

    public WindowsReadOnlySerialTap(PassiveSerialSource settings) => _settings = settings;

    public string PortName => _settings.PortName;
    public long DriverErrorCount => Interlocked.Read(ref _driverErrorCount);

    public void Open()
    {
        if (_port is not null) throw new InvalidOperationException("Tap already opened.");
        var p = new SerialPort(_settings.PortName, _settings.BaudRate,
            _settings.Parity, _settings.DataBits, _settings.StopBits)
        {
            Handshake = Handshake.None,
            RtsEnable = false,
            DtrEnable = false,
            ReadTimeout = 200,
            WriteTimeout = 200,
            ReadBufferSize = 32768,
            ParityReplace = 0
        };
        p.ErrorReceived += OnErrorReceived;
        try
        {
            p.Open();
            // IMPORTANT: no Write(), BreakState, DiscardInBuffer(), DiscardOutBuffer() or control-line assertions.
            _port = p;
        }
        catch
        {
            p.ErrorReceived -= OnErrorReceived;
            p.Dispose();
            throw;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var p = _port ?? throw new InvalidOperationException("Serial tap not open.");
        try { return p.Read(buffer, offset, count); }
        catch (TimeoutException) { return 0; }
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e) =>
        Interlocked.Increment(ref _driverErrorCount);

    public void Close()
    {
        var p = Interlocked.Exchange(ref _port, null);
        if (p is null) return;
        p.ErrorReceived -= OnErrorReceived;
        try { p.Close(); }
        finally { p.Dispose(); }
    }

    public void Dispose() => Close();
}
