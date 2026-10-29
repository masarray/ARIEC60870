// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec10xInteroperabilityProfileTests
{
    private static Iec103MasterSettings Session(Iec60870ProtocolMode protocol) => new()
    {
        ProtocolMode = protocol,
        LinkAddressSize = protocol == Iec60870ProtocolMode.Iec104 ? 0 : 1,
        CauseOfTransmissionSize = protocol == Iec60870ProtocolMode.Iec103 ? 1 : 2,
        CommonAddressSize = protocol == Iec60870ProtocolMode.Iec103 ? 1 : 2,
        InformationObjectAddressSize = protocol == Iec60870ProtocolMode.Iec103 ? 1 : 3,
        TransmissionMode = "Unbalanced",
        LinkAddress = 105,
        CommonAddress = 1
    };

    [Theory]
    [InlineData(Iec60870ProtocolMode.Iec101)]
    [InlineData(Iec60870ProtocolMode.Iec103)]
    [InlineData(Iec60870ProtocolMode.Iec104)]
    public void GenericProfilesAreValidAndRoundTripExactly(Iec60870ProtocolMode protocol)
    {
        var p = Iec10xInteroperabilityProfile.Generic(protocol);
        p.ValidateDefinition();
        var roundTrip = Iec10xInteroperabilityProfile.Import(p.Export());
        Assert.Equal(p, roundTrip);
        Assert.Equal(p.Fingerprint, roundTrip.Fingerprint);
        roundTrip.ValidateActive(Session(protocol));
    }

    [Fact]
    public void HistoricalNucIsOptInAndNeverSilentlyMandatesGiAfterSwitch()
    {
        var generic = Iec10xInteroperabilityProfile.Generic(Iec60870ProtocolMode.Iec101);
        var historical = Iec10xInteroperabilityProfile.HistoricalNuc101();
        Assert.NotEqual(generic.Id, historical.Id);
        Assert.True(historical.IsHistoricalNationalConvention);
        Assert.Equal("Disabled", historical.GiAfterSwitch);
        historical.ValidateActive(Session(Iec60870ProtocolMode.Iec101));
    }

    [Fact]
    public void WrongModeOrWidthIsRejectedBeforeActiveTransmission()
    {
        var p = Iec10xInteroperabilityProfile.Generic(Iec60870ProtocolMode.Iec101);
        Assert.Throws<InvalidOperationException>(() =>
            p.ValidateActive(new Iec103MasterSettings { ProtocolMode = Iec60870ProtocolMode.Iec101, LinkAddressSize = 2, CauseOfTransmissionSize = 2, CommonAddressSize = 2, InformationObjectAddressSize = 3, TransmissionMode = "Unbalanced" }));
    }

    [Fact]
    public void InvalidProfileCannotEnableUnsupportedBalancedTransmission()
    {
        var p = Iec10xInteroperabilityProfile.Generic(Iec60870ProtocolMode.Iec101)
            with { LinkMode = "Balanced" };
        var settings = Session(Iec60870ProtocolMode.Iec101);
        settings.TransmissionMode = "Balanced";
        Assert.Throws<InvalidOperationException>(() => p.ValidateActive(settings));
    }

    [Theory]
    [InlineData(0, 2, 2, 3)]
    [InlineData(1, 0, 2, 3)]
    [InlineData(1, 2, 3, 3)]
    [InlineData(1, 2, 2, 4)]
    public void MalformedFieldWidthsFailClosed(int link, int cot, int ca, int ioa)
    {
        var p = Iec10xInteroperabilityProfile.Generic(Iec60870ProtocolMode.Iec101) with
        { LinkWidth = link, CotWidth = cot, CaWidth = ca, IoaWidth = ioa };
        Assert.Throws<InvalidDataException>(() => p.ValidateDefinition());
    }

    [Fact]
    public void TcpAsduWidthsAreFixedAndCannotUseSerialGiPolicy()
    {
        var p = Iec10xInteroperabilityProfile.Generic(Iec60870ProtocolMode.Iec104);
        Assert.Throws<InvalidDataException>(() => (p with { CotWidth = 1 }).ValidateDefinition());
        Assert.Throws<InvalidDataException>(() => (p with { LinkWidth = 1 }).ValidateDefinition());
        Assert.Throws<InvalidDataException>(() => (p with { GiAfterSwitch = "OnFailover" }).ValidateDefinition());
    }

    [Fact]
    public void VersionedImportRejectsUnknownFieldsAndFalseHistoricalClaims()
    {
        var p = Iec10xInteroperabilityProfile.HistoricalNuc101();
        Assert.Throws<InvalidDataException>(() =>
            Iec10xInteroperabilityProfile.Import(p.Export().Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99")));
        Assert.Throws<InvalidDataException>(() =>
            Iec10xInteroperabilityProfile.Import(p.Export().Replace("\"historical-nuc101\"", "\"unverified-convention\"")));
        Assert.Throws<InvalidDataException>(() =>
            Iec10xInteroperabilityProfile.Import(p.Export().Replace("\"Edition\"", "\"UnsupportedMember\"")));
        Assert.Throws<InvalidDataException>(() => Iec10xInteroperabilityProfile.Import("{ invalid-json"));
    }

    [Fact]
    public void AddressWidthGuardRejectsOverflowAtConfiguredWidth()
    {
        var p = Iec10xInteroperabilityProfile.Generic(Iec60870ProtocolMode.Iec101);
        var settings = Session(Iec60870ProtocolMode.Iec101);
        settings.LinkAddress = 256;
        Assert.Throws<InvalidOperationException>(() => p.ValidateActive(settings));
        settings.LinkAddress = 1;
        settings.CommonAddress = 65536;
        Assert.Throws<InvalidOperationException>(() => p.ValidateActive(settings));
    }

    [Fact]
    public void ReportSnapshotRetainsAuditableEdition()
    {
        var p = Iec10xInteroperabilityProfile.HistoricalNuc101();
        var s = Session(Iec60870ProtocolMode.Iec101);
        s.InteroperabilityProfileId = p.Id;
        s.InteroperabilityProfileEdition = p.Edition;
        s.InteroperabilityProfileFingerprint = p.Fingerprint;
        Assert.Equal(p.Fingerprint, s.CreateReportSnapshot().InteroperabilityProfileFingerprint);
    }
}
