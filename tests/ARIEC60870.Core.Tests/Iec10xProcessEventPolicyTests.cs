// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Core.Tests;

/// <summary>Shared event policy must stay identical for Master and RX writer.</summary>
public sealed class Iec10xProcessEventPolicyTests
{
    [Fact]
    public void UntypedCyclicMeasurementCannotBePromotedButReceivedObservationRemainsRecorded()
    {
        Assert.False(Iec10xProcessEventPolicy.ShouldRecordProcessType(13, 20,
            changed: false, completeTypedMeasurementPayload: false));
        Assert.True(Iec10xProcessEventPolicy.ShouldRecordProcessType(13, 3,
            changed: false, completeTypedMeasurementPayload: false));
    }

    [Theory]
    [InlineData(31, 3, false, true)]
    [InlineData(31, 11, false, true)]
    [InlineData(31, 20, false, false)]
    [InlineData(31, 20, true, true)]
    [InlineData(13, 20, false, true)]
    [InlineData(13, 1, false, true)]
    [InlineData(1, 20, false, false)]
    [InlineData(1, 20, true, true)]
    [InlineData(5, 20, true, false)]
    [InlineData(33, 3, false, true)]
    [InlineData(100, 3, true, false)]
    [InlineData(31, 44, true, true)] // preserves historical Master change policy
    public void CanonicalProcessObservationEligibility(
        int type, int cot, bool changed, bool expected)
        => Assert.Equal(expected,
            Iec10xProcessEventPolicy.ShouldRecordProcessType(type, cot, changed));
}
