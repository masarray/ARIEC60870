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

        _serialPort = new SerialPort(_settings.PortName, _settings.BaudRate, _settings.Parity, _settings.DataBits, _settings.StopBits)
        {
            ReadTimeout = _settings.ResponseTimeoutMs,
            // Keep write timeout independent from protocol response timeout. In field use, response
            // timeout may be intentionally short (for fast polling), while SerialPort.Write still
            // needs enough room for USB/RS-485 driver latency and OS scheduling. A too-short write
            // timeout creates noisy System.TimeoutException failures before the protocol timeout logic
            // can produce a clean diagnostic row.
            WriteTimeout = Math.Max(2000, _settings.ResponseTimeoutMs),
            Handshake = Handshake.None,
            DtrEnable = false,
            RtsEnable = true
        };

        _serialPort.Open();
        _serialPort.DiscardInBuffer();
        _serialPort.DiscardOutBuffer();

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
            RecordDiagnostic(
                code: "IEC60870-TRANSPORT-WRITE-TIMEOUT",
                message: "Serial write timed out",
                exception: ex,
                recommendation: "The serial driver did not accept outgoing bytes before the write timeout. Check USB/RS-485 converter state, COM port ownership, wiring, RTS direction control, and retry after reconnecting the adapter. The protocol session will stop cleanly instead of crashing the UI.");
            TryDiscardOutputBuffer(serialPort);
            throw new IOException($"Serial write timed out on {SafePortName(serialPort)} after {serialPort.WriteTimeout} ms. Check USB/RS-485 converter, wiring, RTS direction control, and COM port ownership.", ex);
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
        if (buffer.Length == 0)
        {
            return 0;
        }

        var serialPort = _serialPort;
        if (serialPort?.IsOpen != true)
        {
            throw new InvalidOperationException("Serial port is not open.");
        }

        // Do not use SerialPort.Read as a blocking call with ReadTimeout. Some drivers throw
        // System.TimeoutException as a first-chance exception for normal no-data conditions, which
        // makes field debugging noisy. Poll BytesToRead with a bounded delay instead; real transport
        // exceptions still bubble to the master session and are converted into Diagnostics rows.
        var timeoutMs = Math.Max(1, _settings.ResponseTimeoutMs);
        var stopwatch = Stopwatch.StartNew();
        var temp = new byte[buffer.Length];

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_serialPort?.IsOpen != true || !serialPort.IsOpen)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("Serial transport read was cancelled or closed.", cancellationToken);
                }

                throw new InvalidOperationException("Serial port closed unexpectedly while reading.");
            }

            int available;
            try
            {
                available = serialPort.BytesToRead;
            }
            catch (InvalidOperationException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Serial transport read was cancelled or closed.", ex, cancellationToken);
            }

            if (available > 0)
            {
                var requested = Math.Min(Math.Min(available, temp.Length), buffer.Length);
                try
                {
                    var read = serialPort.Read(temp, 0, requested);
                    if (read > 0)
                    {
                        temp.AsMemory(0, read).CopyTo(buffer);
                    }

                    return read;
                }
                catch (TimeoutException)
                {
                    // Normal no-data race after BytesToRead. Keep it local; do not surface as UI exception.
                    return 0;
                }
                catch (InvalidOperationException ex) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("Serial transport read was cancelled or closed.", ex, cancellationToken);
                }
            }

            await Task.Delay(Math.Min(10, timeoutMs), cancellationToken).ConfigureAwait(false);
        }

        return 0;
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

    private void RecordDiagnostic(string code, string message, Exception exception, string recommendation)
    {
        lock (_diagnosticsLock)
        {
            _diagnostics.Add(new TransportDiagnostic
            {
                Severity = "Warning",
                Source = "SerialTransport",
                Code = code,
                Message = message,
                Detail = exception.Message,
                Recommendation = recommendation,
                ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                ExceptionMessage = exception.Message,
                ExceptionStackTrace = exception.ToString()
            });
        }
    }
}
