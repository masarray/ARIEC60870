// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.Reporting;

public sealed record EvidenceSmartFinding(
    EvidenceSmartFindingSeverity Severity,
    string Code,
    string Problem,
    string Why,
    string Evidence,
    string Solution,
    string Confidence,
    long Sequence);

public enum EvidenceSmartFindingSeverity
{
    Info,
    Warning,
    Error
}
