// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using ARIEC60870.Desktop.ViewModels;

namespace ARIEC60870.Desktop.Reporting;

public sealed record EvidencePdfReportModel(
    string ReportId,
    DateTime CreatedLocal,
    string SourceWorkspace,
    string ProtocolMode,
    string VerdictStatus,
    string VerdictSummary,
    string VerdictTone,
    IReadOnlyList<EvidenceSmartFinding> SmartFindings,
    IReadOnlyList<KeyValuePair<string, string>> ReportInfo,
    IReadOnlyList<KeyValuePair<string, string>> SessionCounters,
    IReadOnlyList<KeyValuePair<string, string>> CommunicationSetup,
    IReadOnlyList<EvidenceRow> GiRows,
    IReadOnlyList<EvidenceRow> CommandRows,
    IReadOnlyList<EvidenceRow> SoeRows,
    IReadOnlyList<EvidenceRow> ImportantRows,
    int TotalRows,
    long FirstSequence,
    long LastSequence,
    string FramesSha256)
{
    public EvidencePdfReportModel(
        string reportId,
        DateTime createdLocal,
        string sourceWorkspace,
        string protocolMode,
        string verdictStatus,
        string verdictSummary,
        string verdictTone,
        IReadOnlyList<KeyValuePair<string, string>> reportInfo,
        IReadOnlyList<KeyValuePair<string, string>> sessionCounters,
        IReadOnlyList<KeyValuePair<string, string>> communicationSetup,
        IReadOnlyList<EvidenceRow> giRows,
        IReadOnlyList<EvidenceRow> commandRows,
        IReadOnlyList<EvidenceRow> soeRows,
        IReadOnlyList<EvidenceRow> importantRows,
        int totalRows,
        long firstSequence,
        long lastSequence,
        string framesSha256)
        : this(
            reportId,
            createdLocal,
            sourceWorkspace,
            protocolMode,
            verdictStatus,
            verdictSummary,
            verdictTone,
            Array.Empty<EvidenceSmartFinding>(),
            reportInfo,
            sessionCounters,
            communicationSetup,
            giRows,
            commandRows,
            soeRows,
            importantRows,
            totalRows,
            firstSequence,
            lastSequence,
            framesSha256)
    {
    }
}
