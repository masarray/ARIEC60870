// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;

namespace ARIEC60870.Master.Iec101.Redundancy;

public sealed class Iec101DualLinkRedundancyOptions
{
    public Iec103MasterSettings BaseSettings { get; set; } = Iec103MasterSettings.CreateDefault();
    public Iec101DualLinkEndpoint LinkA { get; set; } = new() { Name = "Link A", PortName = "COM1", LinkAddress = 1 };
    public Iec101DualLinkEndpoint LinkB { get; set; } = new() { Name = "Link B", PortName = "COM2", LinkAddress = 1 };
    public string PreferredActiveLink { get; set; } = "A";

    public TimeSpan StandbySupervisionInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan RecoveryBackoff { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan AntiPingPongWindow { get; set; } = TimeSpan.FromSeconds(5);
    public int ActiveFailureThreshold { get; set; } = 2;
    public int StandbyFailureThreshold { get; set; } = 2;
    public int StandbyRecoveryGoodResponseThreshold { get; set; } = 2;
    public Iec101DualLinkFailbackPolicy FailbackPolicy { get; set; } = Iec101DualLinkFailbackPolicy.ManualOnly;

    public Iec101PostSwitchGiPolicy PostSwitchGiPolicy { get; set; } = Iec101PostSwitchGiPolicy.Required;
    public bool DrainClass1BeforePostSwitchGi { get; set; } = true;
    public bool AllowStandbyClass1Polling { get; set; } = false;
    public bool AllowStandbyClass2Polling { get; set; } = false;
    public bool CommandOnActiveOnly { get; set; } = true;

    public void Validate()
    {
        if (BaseSettings is null) throw new InvalidOperationException("IEC-101 base settings are required.");
        if (LinkA is null || LinkB is null) throw new InvalidOperationException("Both IEC-101 dual-link endpoints are required.");
        if (string.IsNullOrWhiteSpace(LinkA.Name)) LinkA.Name = "Link A";
        if (string.IsNullOrWhiteSpace(LinkB.Name)) LinkB.Name = "Link B";
        if (string.Equals(LinkA.Name, LinkB.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("IEC-101 dual-link endpoint names must be unique.");
        }
        if (!BaseSettings.UseSimulatedSlave && string.Equals(LinkA.PortName, LinkB.PortName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("IEC-101 dual-link mode requires two different serial ports for real-device mode.");
        }
        if (BaseSettings.ProtocolMode != Iec60870ProtocolMode.Iec101)
        {
            BaseSettings.ProtocolMode = Iec60870ProtocolMode.Iec101;
        }
        if (BaseSettings.LinkAddressSize is < 1 or > 2)
        {
            throw new InvalidOperationException("IEC-101 dual-link master requires 1 or 2 octet link addresses.");
        }
        ActiveFailureThreshold = Math.Max(1, ActiveFailureThreshold);
        StandbyFailureThreshold = Math.Max(1, StandbyFailureThreshold);
        StandbyRecoveryGoodResponseThreshold = Math.Max(1, StandbyRecoveryGoodResponseThreshold);
        StandbySupervisionInterval = TimeSpan.FromMilliseconds(Math.Max(250, StandbySupervisionInterval.TotalMilliseconds));
        RecoveryBackoff = TimeSpan.FromMilliseconds(Math.Max(250, RecoveryBackoff.TotalMilliseconds));
        AntiPingPongWindow = TimeSpan.FromMilliseconds(Math.Max(500, AntiPingPongWindow.TotalMilliseconds));
    }
}
