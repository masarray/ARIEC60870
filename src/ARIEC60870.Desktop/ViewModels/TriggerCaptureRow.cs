// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

public sealed record TriggerCaptureRow(
    string CaptureId,
    string CompletedLocalTime,
    string Severity,
    string Code,
    string Title,
    string Detail,
    int RowCount,
    long TriggerRow,
    string FilePath)
{
    public string ToDetailText()
        => $"""
Capture ID: {CaptureId}
Completed: {CompletedLocalTime}
Severity: {Severity}
Trigger: {Code}
Title: {Title}
Rows: {RowCount}
Trigger row: #{TriggerRow}
File: {FilePath}

Detail:
{Detail}
""";
}
