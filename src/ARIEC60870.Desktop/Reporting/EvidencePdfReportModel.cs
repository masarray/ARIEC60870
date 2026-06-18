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
    string FramesSha256);
