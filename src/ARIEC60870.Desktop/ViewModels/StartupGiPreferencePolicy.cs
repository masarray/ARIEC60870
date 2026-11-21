// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Protocol-scoped, operator-owned automatic General Interrogation policy.
/// A saved OFF for another mode must never silently suppress IEC-101 startup GI.
/// No active startup command is authorized in IEC-101 passive mode.
/// </summary>
public static class StartupGiPreferencePolicy
{
    public static bool OnConnect(Iec60870ProtocolMode mode, bool isPassive101,
        bool? iec101OperatorOverride, bool? iec103OperatorOverride, bool? iec104OperatorOverride)
        => mode switch
        {
            Iec60870ProtocolMode.Iec101 => !isPassive101 && (iec101OperatorOverride ?? true),
            Iec60870ProtocolMode.Iec103 => iec103OperatorOverride ?? true,
            Iec60870ProtocolMode.Iec104 => iec104OperatorOverride ?? true,
            _ => false
        };

    /// <summary>
    /// Historical setup JSON stored one shared checkbox. Conserve a saved OFF
    /// only for the protocol in which it was stored: the original user's OFF
    /// is respected, while an IEC-103 OFF cannot suppress IEC-101 after switching.
    /// An RX-only capture never creates active transmit authorization.
    /// </summary>
    public static (bool? Iec101, bool? Iec103, bool? Iec104) MigrateLegacy(
        string? savedProtocolMode, bool legacySendGi)
    {
        if (savedProtocolMode?.Contains("Passive", StringComparison.OrdinalIgnoreCase) == true)
            return (null, null, null);
        if (savedProtocolMode?.Contains("101", StringComparison.OrdinalIgnoreCase) == true)
            return (legacySendGi, null, null);
        if (savedProtocolMode?.Contains("104", StringComparison.OrdinalIgnoreCase) == true)
            return (null, null, legacySendGi);
        if (savedProtocolMode?.Contains("103", StringComparison.OrdinalIgnoreCase) == true)
            return (null, legacySendGi, null);
        return (null, null, null);
    }
}
