// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;

namespace ARIEC60870.Master;

public interface IProtocolMasterSession
{
    event EventHandler<Iec103MasterEvidenceEvent>? EvidenceReceived;
    event EventHandler<Iec103MasterFinding>? FindingRaised;
    Task<Iec103MasterRunResult> RunAsync(CancellationToken cancellationToken);
    Task<Iec103MasterRunResult> RunForAsync(TimeSpan duration, CancellationToken cancellationToken);
}
