// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Evidence-only presentation of private ASDUs. Vendor CA is not an
/// authoritative process-station address and the payload remains opaque.
/// </summary>
public static class Iec10xVendorAsduPresentation
{
    public static string RawAddress(Iec10xAsduDecode asdu)
    {
        ArgumentNullException.ThrowIfNull(asdu);
        var width = asdu.CommonAddressWidth == 1 ? "X2" : "X4";
        return asdu.CommonAddress.ToString(CultureInfo.InvariantCulture) +
            " (0x" + asdu.CommonAddress.ToString(width, CultureInfo.InvariantCulture) + ")";
    }

    public static string Address(Iec10xAsduDecode asdu)
    {
        ArgumentNullException.ThrowIfNull(asdu);
        return asdu.IsOpaqueVendorSpecific
            ? "Raw ASDU CA " + RawAddress(asdu) + " · vendor-defined"
            : "CA " + asdu.CommonAddress.ToString(CultureInfo.InvariantCulture);
    }

    public static string Explanation(Iec10xAsduDecode asdu)
    {
        ArgumentNullException.ThrowIfNull(asdu);
        return asdu.TypeId == 135
            ? "TI 135 is vendor-specific. Siemens SICAM documents a system-data container using this type; " +
              "payload meaning and sender identity are not established by Type ID alone. " +
              "Raw ASDU CA is preserved, not interpreted as a process-station address."
            : "Vendor-defined information elements are opaque. Raw ASDU CA is preserved, " +
              "not interpreted as a process-station address. Standard IOA, quality and timestamp are unavailable.";
    }
}
