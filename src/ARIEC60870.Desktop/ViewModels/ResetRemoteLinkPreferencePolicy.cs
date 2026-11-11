// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Protocol-scoped UI policy for the IEC-101 FC0 (reset remote LINK) startup
/// option. FC0 does not reset the RTU process or user application.
/// Passive monitoring never transmits FC0. Explicit operator OFF is retained.
/// </summary>
public static class ResetRemoteLinkPreferencePolicy
{
    public static bool OnConnect(Iec60870ProtocolMode mode, bool isPassive101,
        bool? iec101OperatorOverride, bool? iec103OperatorOverride)
        => mode switch
        {
            Iec60870ProtocolMode.Iec101 => !isPassive101 && (iec101OperatorOverride ?? true),
            Iec60870ProtocolMode.Iec103 => iec103OperatorOverride ?? false,
            _ => false
        };

    /// <summary>
    /// Old preference files had a single bool, whose meaning depended on
    /// the saved protocol. Never carry a legacy IEC-103 OFF into IEC-101.
    /// Existing IEC-101 OFF remains OFF to avoid surprising RTU resets.
    /// </summary>
    public static (bool? Iec101, bool? Iec103) MigrateLegacy(
        string? savedProtocolMode, bool legacyResetEnabled)
    {
        if (savedProtocolMode?.Contains("Passive", StringComparison.OrdinalIgnoreCase) == true)
            return (null, null);
        if (savedProtocolMode?.Contains("101", StringComparison.OrdinalIgnoreCase) == true)
            return (legacyResetEnabled, null);
        if (savedProtocolMode?.Contains("103", StringComparison.OrdinalIgnoreCase) == true)
            return (null, legacyResetEnabled);
        return (null, null);
    }
}
