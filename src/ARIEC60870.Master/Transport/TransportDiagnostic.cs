// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Transport;

public sealed class TransportDiagnostic
{
    public string Severity { get; init; } = "Warning";
    public string Source { get; init; } = "Transport";
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string ExceptionType { get; init; } = string.Empty;
    public string ExceptionMessage { get; init; } = string.Empty;
    public string ExceptionStackTrace { get; init; } = string.Empty;
}

public interface ITransportDiagnosticSource
{
    IReadOnlyList<TransportDiagnostic> DrainDiagnostics();
}
