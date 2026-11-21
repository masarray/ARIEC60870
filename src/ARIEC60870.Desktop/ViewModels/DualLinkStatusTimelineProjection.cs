// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Iec101.Redundancy;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// A typed, UI-only operator observation of a redundancy snapshot.
/// This is NOT an IEC-101 secondary frame or a substitute for original evidence.
/// </summary>
public sealed record DualLinkStatusMilestone(
    DateTime ObservedUtc, string Link, string Event, string Reason, string Result);

/// <summary>
/// Projects only meaningful changes, not one row per successful FC9/status
/// probe. Called by the WPF Dispatcher from the existing snapshot callback.
/// Protocol ownership, link decisions, and the source journal stay in Master.
/// </summary>
public sealed class DualLinkStatusTimelineProjection
{
    private Iec101RedundancyControllerState? _controller;
    private string _activeLink = string.Empty;
    private bool? _linkAHealthy;
    private bool? _linkBHealthy;
    private Iec101ApplicationImageState? _imageState;

    public void Reset()
    {
        _controller = null;
        _activeLink = string.Empty;
        _linkAHealthy = null;
        _linkBHealthy = null;
        _imageState = null;
    }

    public IReadOnlyList<DualLinkStatusMilestone> Observe(
        Iec101RedundancySessionSnapshot snapshot, DateTime? observedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var now = observedUtc ?? DateTime.UtcNow;
        var changes = new List<DualLinkStatusMilestone>(5);

        if (_controller is null)
            changes.Add(new(now, "Controller", "Session observed",
                "Redundancy monitoring initialized",
                $"Controller: {snapshot.ControllerState}. Snapshot only; initial COM open is not RTU link proof."));
        else if (_controller != snapshot.ControllerState &&
                 snapshot.ControllerState is
                     Iec101RedundancyControllerState.Healthy or
                     Iec101RedundancyControllerState.Degraded or
                     Iec101RedundancyControllerState.NoAvailableLink or
                     Iec101RedundancyControllerState.Switching or
                     Iec101RedundancyControllerState.Recovering or
                     Iec101RedundancyControllerState.Faulted or
                     Iec101RedundancyControllerState.Stopped)
            changes.Add(new(now, "Controller", "State changed",
                $"{_controller} → {snapshot.ControllerState}",
                "Controller state from redundancy engine snapshot."));
        _controller = snapshot.ControllerState;

        if (!string.IsNullOrWhiteSpace(snapshot.ActiveLinkName) &&
            !string.Equals(_activeLink, snapshot.ActiveLinkName, StringComparison.Ordinal))
        {
            changes.Add(new(now, snapshot.ActiveLinkName, "Active assigned",
                $"{snapshot.ActiveLinkName} selected for active ownership",
                "Ownership assignment; health still requires a valid secondary response."));
            _activeLink = snapshot.ActiveLinkName;
        }

        AddHealthTransition(changes, now, "Link A", snapshot.LinkA, ref _linkAHealthy);
        AddHealthTransition(changes, now, "Link B", snapshot.LinkB, ref _linkBHealthy);

        if (_imageState != snapshot.ApplicationImageState)
        {
            var label = snapshot.ApplicationImageState == Iec101ApplicationImageState.Empty
                ? "Image empty" : "Image state";
            changes.Add(new(now, "Controller", label,
                $"Application image: {snapshot.ApplicationImageState}",
                snapshot.ApplicationImageState == Iec101ApplicationImageState.Empty
                    ? "No application image proven yet. Link health alone does not establish GI coverage."
                    : $"Image objects: {snapshot.ApplicationImageObjectCount}. Verify GI evidence separately."));
            _imageState = snapshot.ApplicationImageState;
        }

        return changes;
    }

    private static void AddHealthTransition(
        List<DualLinkStatusMilestone> changes, DateTime now, string link,
        Iec101RedundancyChannelSnapshot channel, ref bool? previous)
    {
        // Early COM-open or initial false is not a verified RTU health event.
        if ((!previous.HasValue && channel.IsHealthy) ||
            (previous.HasValue && previous.Value != channel.IsHealthy))
        {
            changes.Add(new(now, link,
                channel.IsHealthy ? "Link healthy" : "Link degraded",
                channel.IsHealthy
                    ? "Validated RTU secondary response"
                    : "Previously healthy link is no longer verified",
                $"{channel.PortName} · Link {channel.LinkAddress} · " +
                $"Role {channel.Role} · RX {channel.RxFrames} · " +
                $"timeouts {channel.ConsecutiveTimeouts}; snapshot, not a synthetic frame."));
        }

        previous = channel.IsHealthy;
    }
}
