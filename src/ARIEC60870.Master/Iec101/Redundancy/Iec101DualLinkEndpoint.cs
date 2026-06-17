// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;

namespace ARIEC60870.Master.Iec101.Redundancy;

public sealed class Iec101DualLinkEndpoint
{
    public string Name { get; set; } = "Link";
    public string PortName { get; set; } = "COM1";
    public int LinkAddress { get; set; } = 1;

    public Iec103MasterSettings ApplyTo(Iec103MasterSettings baseSettings)
    {
        if (baseSettings is null) throw new ArgumentNullException(nameof(baseSettings));
        var copy = baseSettings.CreateReportSnapshot();
        copy.ProtocolMode = Iec60870ProtocolMode.Iec101;
        copy.PortName = PortName;
        copy.LinkAddress = LinkAddress;
        copy.TargetProfile = string.IsNullOrWhiteSpace(baseSettings.TargetProfile)
            ? "IEC-101 dual link outstation"
            : baseSettings.TargetProfile;
        return copy;
    }

    public override string ToString()
        => $"{Name}: {PortName}, Link={LinkAddress}";
}
