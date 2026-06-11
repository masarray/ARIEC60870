// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Net.Sockets;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Master.Transport;

public sealed class TcpClientByteTransport : IByteTransport, ITransportDiagnosticSource
{
    private readonly Iec103MasterSettings _settings;
    private readonly object _diagnosticsLock = new();
    private readonly List<TransportDiagnostic> _diagnostics = new();
    private TcpClient? _client;
    private NetworkStream? _stream;

    public TcpClientByteTransport(Iec103MasterSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool IsOpen => _client?.Connected == true && _stream is not null;

    public async ValueTask OpenAsync(CancellationToken cancellationToken)
    {
        if (IsOpen) return;
        _client = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(100, _settings.ResponseTimeoutMs));
        await _client.ConnectAsync(_settings.TcpHost, _settings.TcpPort, timeout.Token).ConfigureAwait(false);
        _stream = _client.GetStream();
    }

    public ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { _stream?.Dispose(); }
        catch (Exception ex) { RecordDiagnostic("IEC104-TCP-CLOSE", "TCP stream close exception captured", ex, "Usually safe during Stop. If repeated, check network adapter and remote server state."); }
        finally { _stream = null; }

        try { _client?.Close(); _client?.Dispose(); }
        catch (Exception ex) { RecordDiagnostic("IEC104-TCP-DISPOSE", "TCP client dispose exception captured", ex, "Retry after closing duplicate sessions to the same IEC-104 endpoint."); }
        finally { _client = null; }
        return ValueTask.CompletedTask;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("TCP stream is not open.");
        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("TCP stream is not open.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(1, _settings.ResponseTimeoutMs));
        try
        {
            return await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
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

    public void Dispose() => CloseAsync(CancellationToken.None).GetAwaiter().GetResult();
    public async ValueTask DisposeAsync() => await CloseAsync(CancellationToken.None).ConfigureAwait(false);

    private void RecordDiagnostic(string code, string message, Exception exception, string recommendation)
    {
        lock (_diagnosticsLock)
        {
            _diagnostics.Add(new TransportDiagnostic
            {
                Severity = "Warning",
                Source = "TcpClientTransport",
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
