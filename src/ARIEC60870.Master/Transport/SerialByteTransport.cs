// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Master.Transport;

public sealed class SerialByteTransport : IByteTransport, ITransportDiagnosticSource
{
    private readonly Iec103MasterSettings _settings;
    private SerialPort? _serialPort;
    private readonly object _diagnosticsLock = new();
    private readonly List<TransportDiagnostic> _diagnostics = new();

    public SerialByteTransport(Iec103MasterSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool IsOpen => _serialPort?.IsOpen == true;

    public ValueTask OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_serialPort?.IsOpen == true)
        {
            return ValueTask.CompletedTask;
        }

        var port = new SerialPort(_settings.PortName, _settings.BaudRate, _settings.Parity, _settings.DataBits, _settings.StopBits)
        {
            ReadTimeout = _settings.ResponseTimeoutMs,
            // Keep write timeout independent from protocol response timeout. In field use, response
            // timeout may be intentionally short (for fast polling), while SerialPort.Write still
            // needs enough room for USB/RS-485 driver latency and OS scheduling. A too-short write
            // timeout creates noisy System.TimeoutException failures before the protocol timeout logic
            // can produce a clean diagnostic row.
            WriteTimeout = _settings.SerialWriteTimeoutMs,
            Handshake = _settings.SerialHandshake,
            DtrEnable = _settings.SerialDtrEnable
        };

        // The OS owns RTS when hardware flow control is selected; assigning
        // RtsEnable in that mode may throw and misrepresent actual line state.
        if (_settings.SerialHandshake is not (Handshake.RequestToSend or Handshake.RequestToSendXOnXOff))
        {
            port.RtsEnable = _settings.SerialRtsEnable;
        }

        try
        {
            port.Open();
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            _serialPort = port;
            RecordInformational("IEC60870-SERIAL-OPEN-PROFILE",
                $"Serial open: {_settings.SerialSummary}; {_settings.SerialControlLineSummary}; {ReadModemInputs(port)}.",
                "This is a local OS/adapter observation, not proof that the SICAM RTU received frames.");
        }
        catch
        {
            port.Dispose();
            throw;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serialPort = Interlocked.Exchange(ref _serialPort, null);
        if (serialPort is null)
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            if (serialPort.IsOpen)
            {
                RecordInformational("IEC60870-SERIAL-CLOSE-PROFILE",
                    $"Before close: {_settings.SerialControlLineSummary}; {ReadModemInputs(serialPort)}.",
                    "Modem inputs describe the local serial adapter, not IEC-101 secondary link health.");
                serialPort.Close();
            }
        }
        catch (Exception ex)
        {
            RecordDiagnostic(
                code: "IEC103-TRANSPORT-CLOSE",
                message: "Serial port close exception captured",
                exception: ex,
                recommendation: "Stop request was handled. If the COM port remains locked, unplug/replug the USB converter or restart the application before retrying.");
        }

        try
        {
            serialPort.Dispose();
        }
        catch (Exception ex)
        {
            RecordDiagnostic(
                code: "IEC103-TRANSPORT-DISPOSE",
                message: "Serial port dispose exception captured",
                exception: ex,
                recommendation: "Dispose exception was contained. If the COM port cannot be reopened, check USB/serial driver stability and reconnect the converter.");
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var serialPort = _serialPort;
        if (serialPort?.IsOpen != true)
        {
            throw new InvalidOperationException("Serial port is not open.");
        }

        var data = buffer.ToArray();
        var writeClock = Stopwatch.StartNew();
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                serialPort.Write(data, 0, data.Length);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Serial transport write was cancelled or closed.", ex, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            var portState = ReadWriteState(serialPort);
            RecordDiagnostic(
                code: "IEC60870-TRANSPORT-WRITE-TIMEOUT",
                message: "Serial write timed out",
                exception: ex,
                recommendation: _settings.SerialHandshake is Handshake.RequestToSend or Handshake.RequestToSendXOnXOff
                    ? "RTS/CTS flow control is selected. CTS=0 may withhold TX. Compare the known-working IEC_Test settings and hardware wiring before changing production flow control."
                    : "CTS flow gating is not selected. CTS=0 alone does not explain this write timeout. Compare the converter, COM driver, baud/framing and RTS/DTR against IEC_Test.",
                context: $"COM={SafePortName(serialPort)}; attemptedTX={data.Length} bytes; elapsed={writeClock.ElapsedMilliseconds} ms; writeLimit={serialPort.WriteTimeout} ms; {portState}");
            TryDiscardOutputBuffer(serialPort);
            throw new IOException($"Serial write timed out on {SafePortName(serialPort)} after {writeClock.ElapsedMilliseconds} ms (limit {serialPort.WriteTimeout} ms); {portState}.", ex);
        }
        catch (InvalidOperationException ex)
        {
            RecordDiagnostic(
                code: "IEC60870-TRANSPORT-WRITE-CLOSED",
                message: "Serial port closed while writing",
                exception: ex,
                recommendation: "The COM port closed while ARIEC60870 was sending a frame. Check USB/serial adapter stability and make sure another tool is not taking ownership of the same port.");
            throw new IOException($"Serial port {SafePortName(serialPort)} closed while writing.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            RecordDiagnostic(
                code: "IEC60870-TRANSPORT-WRITE-ACCESS",
                message: "Serial port access was denied while writing",
                exception: ex,
                recommendation: "Another application or driver may own the COM port. Close other serial tools, unplug/replug the adapter, then reopen the session.");
            throw new IOException($"Serial port {SafePortName(serialPort)} denied write access.", ex);
        }
        catch (IOException ex)
        {
            RecordDiagnostic(
                code: "IEC60870-TRANSPORT-WRITE-IO",
                message: "Serial write I/O exception",
                exception: ex,
                recommendation: "Check the serial adapter, cable, port driver, and RS-485 converter direction control. Reconnect the adapter if the port remains unhealthy.");
            throw;
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length == 0) return 0;

        var serialPort = _serialPort;
        if (serialPort?.IsOpen != true)
            throw new InvalidOperationException("Serial port is not open.");

        // All Master framing readers supply array-backed Memory<byte>.
        // Read directly into the requested slice: allocating a temporary byte[]
        // on EVERY serial read becomes a per-byte GC hotspot at low baud.
        // Non-array-backed Memory is supported through a bounded pooled fallback.
        byte[] destination;
        int destinationOffset;
        byte[]? rented = null;
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(
                (ReadOnlyMemory<byte>)buffer, out var segment) && segment.Array is not null)
        {
            destination = segment.Array;
            destinationOffset = segment.Offset;
        }
        else
        {
            rented = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
            destination = rented;
            destinationOffset = 0;
        }

        try
        {
            // Do not use blocking SerialPort.Read with ReadTimeout. Preserve the
            // prior BytesToRead/cancellable bounded wait and error diagnostics.
            var timeoutMs = Math.Max(1, _settings.ResponseTimeoutMs);
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_serialPort?.IsOpen != true || !serialPort.IsOpen)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(
                            "Serial transport read was cancelled or closed.", cancellationToken);
                    throw new InvalidOperationException("Serial port closed unexpectedly while reading.");
                }

                int available;
                try { available = serialPort.BytesToRead; }
                catch (InvalidOperationException ex) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(
                        "Serial transport read was cancelled or closed.", ex, cancellationToken);
                }

                if (available > 0)
                {
                    var requested = Math.Min(available, buffer.Length);
                    try
                    {
                        var read = serialPort.Read(destination, destinationOffset, requested);
                        if (read > 0 && rented is not null)
                            rented.AsMemory(0, read).CopyTo(buffer);
                        return read;
                    }
                    catch (TimeoutException)
                    {
                        // BytesToRead can race the driver. A normal race is not
                        // an operator-facing transport exception.
                        return 0;
                    }
                    catch (InvalidOperationException ex) when (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(
                            "Serial transport read was cancelled or closed.", ex, cancellationToken);
                    }
                }

                await Task.Delay(Math.Min(10, timeoutMs), cancellationToken).ConfigureAwait(false);
            }
            return 0;
        }
        finally
        {
            if (rented is not null)
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public void Dispose()
    {
        CloseAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public IReadOnlyList<TransportDiagnostic> DrainDiagnostics()
    {
        lock (_diagnosticsLock)
        {
            var copy = _diagnostics.ToArray();
            _diagnostics.Clear();
            return copy;
        }
    }


    private void TryDiscardOutputBuffer(SerialPort serialPort)
    {
        try
        {
            if (serialPort.IsOpen)
            {
                serialPort.DiscardOutBuffer();
            }
        }
        catch (Exception ex)
        {
            RecordDiagnostic(
                code: "IEC60870-TRANSPORT-DISCARD-OUT",
                message: "Serial output buffer cleanup failed after write fault",
                exception: ex,
                recommendation: "The COM port driver may be stuck after the previous write fault. Reconnect the USB/serial adapter before retrying.");
        }
    }

    private static string ReadWriteState(SerialPort port)
    {
        string pending;
        try { pending = $"TX queued={port.BytesToWrite} bytes"; }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException)
        {
            pending = $"TX queue unavailable ({ex.GetType().Name})";
        }
        return $"{ReadModemInputs(port)}, {pending}";
    }

    // CTS/DSR/DCD are locally observed input states, never interpreted as RTU link health.
    private static string ReadModemInputs(SerialPort port)
    {
        try
        {
            return $"CTS={(port.CtsHolding ? 1 : 0)}, DSR={(port.DsrHolding ? 1 : 0)}, DCD={(port.CDHolding ? 1 : 0)}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException)
        {
            return $"CTS/DSR/DCD unavailable ({ex.GetType().Name})";
        }
    }

    private void RecordInformational(string code, string detail, string recommendation)
    {
        lock (_diagnosticsLock)
        {
            _diagnostics.Add(new TransportDiagnostic
            {
                Severity = "Info", Source = "SerialTransport", Code = code,
                Message = "Serial adapter settings and modem input snapshot",
                Detail = detail, Recommendation = recommendation
            });
        }
    }

    private static string SafePortName(SerialPort serialPort)
    {
        try
        {
            return serialPort.PortName;
        }
        catch
        {
            return "serial port";
        }
    }

    private void RecordDiagnostic(string code, string message, Exception exception, string recommendation, string? context = null)
    {
        lock (_diagnosticsLock)
        {
            _diagnostics.Add(new TransportDiagnostic
            {
                Severity = "Warning",
                Source = "SerialTransport",
                Code = code,
                Message = message,
                Detail = $"{exception.Message}; {_settings.SerialControlLineSummary}" +
                         (string.IsNullOrWhiteSpace(context) ? string.Empty : $"; {context}"),
                Recommendation = recommendation,
                ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                ExceptionMessage = exception.Message,
                ExceptionStackTrace = exception.ToString()
            });
        }
    }
}
