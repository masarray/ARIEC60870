// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Monitor;

/// <summary>Pure RX / queue / writer / decoder / WPF diagnosis.</summary>
public static class PassiveNoFrameDiagnosis
{
    public sealed record Result(string Title, string Detail);

    public static Result Evaluate(PassiveMonitorProgress progress,
        string firstPort, string secondPort, string linkBPorts,
        int sourceFrameRows, int pendingUiEvidence)
    {
        // Compatibility fallback for older synthetic progress fixtures.
        // Live session snapshots have independent, source-owned reader counters.
        var firstRead = Math.Max(progress.ReaderScadaBytes, progress.ScadaBytes);
        var secondRead = Math.Max(progress.ReaderRtuBytes, progress.RtuBytes);
        var bFirst = Math.Max(progress.ReaderLinkBScadaBytes, progress.LinkBScadaBytes);
        var bSecond = Math.Max(progress.ReaderLinkBRtuBytes, progress.LinkBRtuBytes);
        var read = firstRead + secondRead + bFirst + bSecond;
        var committed = progress.ScadaBytes + progress.RtuBytes +
            progress.LinkBScadaBytes + progress.LinkBRtuBytes;
        var queued = progress.QueuedScadaBytes + progress.QueuedRtuBytes +
            progress.QueuedLinkBScadaBytes + progress.QueuedLinkBRtuBytes;
        var ports = $"{firstPort} read={firstRead:N0}/commit={progress.ScadaBytes:N0} B · " +
            $"{secondPort} read={secondRead:N0}/commit={progress.RtuBytes:N0} B" +
            (progress.StreamCount == 4
                ? $" · Link B {linkBPorts} read={bFirst:N0}/{bSecond:N0} commit={progress.LinkBScadaBytes:N0}/{progress.LinkBRtuBytes:N0} B"
                : "");
        var skips = progress.RawPreviewDropped > 0
            ? $" · raw GUI sample skips={progress.RawPreviewDropped:N0}" : "";
        if (progress.Status is "Idle" or "Starting")
            return new Result("Opening receive-only COM ports",
                ports + " · COM open is not evidence of UART activity.");
        if (read == 0)
            return new Result("No UART bytes received",
                ports + " · Check RX-only tap, configured COMs and UART profile. Passive mode never polls.");
        if (committed < read)
            return new Result("UART bytes received; writer pipeline pending",
                ports + $" · not queued={Math.Max(0, read - queued):N0} B" +
                $" · queued not committed={Math.Max(0, queued - committed):N0} B" + skips +
                ". Do not diagnose silent UART while Read is positive.");
        if (progress.ValidFt12Frames == 0)
            return new Result("UART bytes received, no complete valid IEC-101 frame",
                ports + $" · bytes total={read:N0} · incomplete={progress.IncompleteFrameBytes:N0}" +
                $" · untrusted candidates={progress.UntrustedFrameCandidates:N0}" + skips +
                ". Inspect original HEX and configured framing.");
        return new Result("Decoded frames not visible in Raw Frames",
            ports + $" · valid FT1.2={progress.ValidFt12Frames:N0}" +
            $" · preview published={progress.FramePreviewsPublished:N0}" +
            $" · preview consumed={progress.FramePreviewsConsumed:N0}" +
            $" · source rows={sourceFrameRows:N0} · pending UI={pendingUiEvidence:N0}" + skips +
            ". Review Trace filters and bounded preview retention.");
    }
}
