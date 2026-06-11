// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

public sealed class DiagnosticRow
{
    public DiagnosticRow(Iec103MasterEvidenceEvent item)
    {
        Time = item.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
        Severity = NormalizeSeverity(item.Category);
        Source = item.DirectionText == "STATE" ? item.State.ToString() : item.DirectionText;
        Code = item.Category;
        Message = string.IsNullOrWhiteSpace(item.OperatorMessage) ? item.Summary : item.OperatorMessage;
        Detail = string.IsNullOrWhiteSpace(item.ProtocolMeaning) ? item.Detail : item.ProtocolMeaning;
        Recommendation = item.OperatorAction;
        ExceptionType = item.ExceptionType;
        StackTrace = item.ExceptionStackTrace;
        RawHex = string.IsNullOrWhiteSpace(item.RawHex) ? "-" : item.RawHex;
    }

    public DiagnosticRow(Iec103MasterFinding finding)
    {
        Time = finding.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
        Severity = finding.Severity.ToString();
        Source = "Finding";
        Code = finding.Id;
        Message = finding.Title;
        Detail = finding.Evidence;
        Recommendation = finding.Recommendation;
        ExceptionType = string.Empty;
        StackTrace = string.Empty;
        RawHex = "-";
    }

    public DiagnosticRow(string severity, string source, string code, string message, string detail, string recommendation, Exception? exception = null)
    {
        Time = DateTime.Now.ToString("HH:mm:ss.fff");
        Severity = severity;
        Source = source;
        Code = code;
        Message = message;
        Detail = detail;
        Recommendation = recommendation;
        ExceptionType = exception?.GetType().FullName ?? string.Empty;
        StackTrace = exception?.ToString() ?? string.Empty;
        RawHex = "-";
    }

    public string Time { get; }
    public string Severity { get; }
    public string Source { get; }
    public string Code { get; }
    public string Message { get; }
    public string Detail { get; }
    public string Recommendation { get; }
    public string ExceptionType { get; }
    public string StackTrace { get; }
    public string RawHex { get; }

    public string ToClipboardText()
    {
        return string.Join(Environment.NewLine,
            $"Time          : {Time}",
            $"Severity      : {Severity}",
            $"Source        : {Source}",
            $"Code          : {Code}",
            $"Message       : {Message}",
            $"Detail        : {Detail}",
            $"Recommendation: {Recommendation}",
            $"Exception     : {ExceptionType}",
            $"Raw Hex       : {RawHex}",
            "Stack Trace    :",
            string.IsNullOrWhiteSpace(StackTrace) ? "-" : StackTrace);
    }

    private static string NormalizeSeverity(string category)
    {
        if (category.Contains("Error", StringComparison.OrdinalIgnoreCase) || category.Contains("Fault", StringComparison.OrdinalIgnoreCase))
        {
            return "Error";
        }

        if (category.Contains("Warning", StringComparison.OrdinalIgnoreCase) || category.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "Warning";
        }

        if (category.Contains("Diagnostic", StringComparison.OrdinalIgnoreCase))
        {
            return "Diagnostic";
        }

        return "Info";
    }
}
