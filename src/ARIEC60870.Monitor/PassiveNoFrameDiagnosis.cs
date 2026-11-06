// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Monitor;

/// <summary>Pure, truthful receiver-to-WPF diagnosis; no serial I/O or protocol mutation.</summary>
public static class PassiveNoFrameDiagnosis
{
    public sealed record Result(string Title, string Detail);

    public static Result Evaluate(PassiveMonitorProgress progress,
        string firstPort, string secondPort, string linkBPorts,
        int sourceFrameRows, int pendingUiEvidence)
    {
        var sum = progress.ScadaBytes + progress.RtuBytes +
            progress.LinkBScadaBytes + progress.LinkBRtuBytes;
        var ports = $"{firstPort}={progress.ScadaBytes:N0} B · {secondPort}={progress.RtuBytes:N0} B" +
            (progress.StreamCount == 4 ? $" · Link B {linkBPorts}={progress.LinkBScadaBytes:N0}/{progress.LinkBRtuBytes:N0} B" : "");
        if (progress.Status is "Idle" or "Starting")
            return new Result("Opening receive-only COM ports",
                ports + " · No traffic is proven until physical UART bytes arrive.");
        if (sum == 0)
            return new Result("No UART bytes received",
                ports + " · Check RX-only tap wiring/ground, port selection and baud/parity/stop settings. " +
                "Passive mode does not transmit IEC-101 polls.");
        if (progress.ValidFt12Frames == 0)
            return new Result("UART bytes received, no complete valid IEC-101 frame",
                ports + $" · bytes total={sum:N0} · incomplete={progress.IncompleteFrameBytes:N0}" +
                $" · untrusted candidates={progress.UntrustedFrameCandidates:N0}. " +
                "Inspect raw samples for baud/framing, checksum or partial-frame issues.");
        return new Result("Decoded frames not visible in Raw Frames",
            ports + $" · valid FT1.2={progress.ValidFt12Frames:N0}" +
            $" · preview published={progress.FramePreviewsPublished:N0}" +
            $" · preview consumed={progress.FramePreviewsConsumed:N0}" +
            $" · source rows={sourceFrameRows:N0} · pending UI={pendingUiEvidence:N0}. " +
            "Review Trace filters/selection, projection queue and bounded preview retention.");
    }
}
