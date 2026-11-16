// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Iec101;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101ObservedCaPolicyTests
{
    [Fact]
    public void ValidSingleStationProcessEvidencePermitsOneGiRetryCandidate()
    {
        var policy = new Iec101ObservedCaPolicy();
        policy.Observe(Process(ca: 105));
        Assert.Null(policy.GiRetryCandidate);

        policy.Observe(Process(ca: 105, cot: 20));
        Assert.Null(policy.GiRetryCandidate);
        policy.Observe(Process(ca: 105));
        Assert.Equal(105, policy.GiRetryCandidate);
        Assert.False(policy.HasConflictingStations);

        policy.Reset();
        Assert.Null(policy.GiRetryCandidate);
    }

    [Fact]
    public void DifferentValidStationsMakeLearningAmbiguousWithoutMajorityVote()
    {
        var policy = new Iec101ObservedCaPolicy();
        policy.Observe(Process(ca: 105));
        policy.Observe(Process(ca: 106));
        policy.Observe(Process(ca: 105));
        policy.Observe(Process(ca: 105));
        Assert.True(policy.HasConflictingStations);
        Assert.Null(policy.GiRetryCandidate);
    }

    [Fact]
    public void AnotherStationInvalidatesAlreadyObservedCandidate()
    {
        var policy = new Iec101ObservedCaPolicy();
        policy.Observe(Process(ca: 105));
        policy.Observe(Process(ca: 105));
        Assert.Null(policy.GiRetryCandidate);
        policy.Observe(Process(ca: 105));
        Assert.Equal(105, policy.GiRetryCandidate);
        policy.Observe(Process(ca: 106));
        Assert.Null(policy.GiRetryCandidate);
    }

    [Theory]
    [InlineData(100, 3)] // GI control, not measured process data
    [InlineData(45, 3)]  // command, never a station-discovery signal
    [InlineData(128, 3)] // vendor-private ASDU layout
    [InlineData(1, 6)]   // ACTCON is not process state
    [InlineData(1, 7)]   // negative/positive activation acknowledgement
    [InlineData(1, 10)]  // ACTTERM is not process state
    [InlineData(1, 0)]   // undefined cause
    public void UnsupportedTypesAndCausesCannotTeachStation(byte type, int cot)
    {
        var policy = new Iec101ObservedCaPolicy();
        policy.Observe(Process(typeId: type, cot: cot));
        policy.Observe(Process(typeId: type, cot: cot));
        Assert.Null(policy.GiRetryCandidate);
    }

    [Fact]
    public void TestNegativeMalformedPartialAndCommandAsdusDoNotContribute()
    {
        var policy = new Iec101ObservedCaPolicy();
        policy.Observe(Process(test: true));
        policy.Observe(Process(negative: true));
        policy.Observe(Process(command: true));
        policy.Observe(Process(headerValid: false));
        policy.Observe(Process(malformed: true));
        policy.Observe(Process(objectIssue: true));
        policy.Observe(Process(ca: 0));
        policy.Observe(Process(objectCount: 2)); // decoded only one of two objects
        policy.Observe(Process(objectCount: 0));
        Assert.Null(policy.GiRetryCandidate);
        Assert.False(policy.HasConflictingStations);

        policy.Observe(Process(ca: 105));
        Assert.Null(policy.GiRetryCandidate); // invalid samples never incremented hits
        policy.Observe(Process(ca: 105));
        Assert.Null(policy.GiRetryCandidate);
        policy.Observe(Process(ca: 105));
        Assert.Equal(105, policy.GiRetryCandidate);
    }

    [Fact]
    public void GlobalBroadcastCaAndZeroAreNeverStationEvidence()
    {
        var policy = new Iec101ObservedCaPolicy();
        for (var i = 0; i < Iec101ObservedCaPolicy.MinimumProcessAsdus + 1; i++)
        {
            policy.Observe(Process(ca: ushort.MaxValue));
            policy.Observe(Process(ca: 0));
        }
        Assert.Null(policy.GiRetryCandidate);
        Assert.False(policy.HasConflictingStations);
        for (var i = 0; i < Iec101ObservedCaPolicy.MinimumProcessAsdus; i++)
            policy.Observe(Process(ca: 105));
        Assert.Equal(105, policy.GiRetryCandidate);
    }

    [Fact]
    public void ConcurrentDualReaderObservationsFailClosedAfterStationConflict()
    {
        var policy = new Iec101ObservedCaPolicy();
        Parallel.For(0, 200, i => policy.Observe(Process(ca: i % 2 == 0 ? 105 : 106)));
        Assert.True(policy.HasConflictingStations);
        Assert.Null(policy.GiRetryCandidate);
    }

    private static Iec10xAsduDecode Process(
        int ca = 105, byte typeId = 1, int cot = 3, bool test = false,
        bool negative = false, bool command = false, bool headerValid = true,
        bool malformed = false, bool objectIssue = false, int objectCount = 1)
        => new()
        {
            IsHeaderValid = headerValid,
            CommonAddress = ca,
            CommonAddressWidth = 2,
            TypeId = typeId,
            CauseOfTransmission = cot,
            IsTest = test,
            IsNegativeConfirm = negative,
            IsControlCommand = command,
            ObjectCount = objectCount,
            Objects = objectCount == 0 ? Array.Empty<Iec10xInformationObject>() :
            [
                new Iec10xInformationObject
                {
                    InformationObjectAddress = 100,
                    Issues = objectIssue ? ["Incomplete object payload"] : Array.Empty<string>()
                }
            ],
            Issues = malformed ? ["Invalid ASDU payload"] : Array.Empty<string>()
        };
}
