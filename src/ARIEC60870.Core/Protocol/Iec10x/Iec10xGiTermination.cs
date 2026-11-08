// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>
/// Observed on-wire GI termination, not matching-request/transaction proof.
/// Session and GI casebook still own source/CA/QOI correlation.
/// </summary>
public static class Iec10xGiTermination
{
    public static bool IsObserved(Iec10xAsduDecode? asdu) =>
        asdu is { IsHeaderValid: true, TypeId: 100, CauseOfTransmission: 10,
                  IsTest: false, IsNegativeConfirm: false, ObjectCount: 1,
                  Issues.Count: 0 } &&
        asdu.Objects.Count == 1 &&
        asdu.InformationObjectAddress == 0 &&
        asdu.CommandDetails?.QualifierValue is >= 20 and <= 36;
}
