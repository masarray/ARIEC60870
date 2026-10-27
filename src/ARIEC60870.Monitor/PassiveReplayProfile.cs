// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Monitor;

/// <summary>
/// Read-only interpretation of an existing binary journal. A different profile
/// can change frame recognition/ASDU decoding, never the original captured bytes
/// or the recording's authenticated manifest.
/// </summary>
public sealed record PassiveReplayProfile(
    int LinkAddressWidth,
    int CotWidth,
    int CommonAddressWidth,
    int InformationAddressWidth,
    int FrameGapMilliseconds,
    string LinkMode)
{
    public static PassiveReplayProfile FromManifest(PassiveCaptureManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var result = new PassiveReplayProfile(manifest.LinkAddressWidth, manifest.CotWidth,
            manifest.CommonAddressWidth, manifest.InformationAddressWidth,
            manifest.FrameGapMilliseconds, manifest.LinkMode);
        result.Validate();
        return result;
    }

    public void Validate()
    {
        if (LinkAddressWidth is < 0 or > 2 || CotWidth is < 1 or > 2 ||
            CommonAddressWidth is < 1 or > 2 || InformationAddressWidth is < 1 or > 3 ||
            FrameGapMilliseconds is < 100 or > 10000 ||
            LinkMode is not ("Auto" or "Balanced" or "Unbalanced"))
            throw new ArgumentOutOfRangeException(nameof(LinkAddressWidth),
                "Invalid replay field widths, idle-gap policy or passive link mode.");
    }

    public string Describe() => $"Link={LinkAddressWidth}, COT={CotWidth}, " +
        $"CA={CommonAddressWidth}, IOA={InformationAddressWidth}, " +
        $"Gap={FrameGapMilliseconds} ms, Mode={LinkMode}";
}
