// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>Config-only policy; never changes an active IEC-101 channel or validates wire frames.</summary>
public static class BackupLinkAddressPolicy
{
    // Legacy preferences have no explicit flag. Preserve a distinct saved B address
    // as intentional; equal legacy addresses can safely continue following A.
    public static bool IsExplicit(bool? savedChoice, int addressA, int addressB) =>
        savedChoice ?? addressA != addressB;

    public static int InitialAddress(int addressA, int addressB, bool explicitlyConfigured) =>
        explicitlyConfigured ? addressB : addressA;

    public static bool IsDifferent(int addressA, int addressB) => addressA != addressB;
}
