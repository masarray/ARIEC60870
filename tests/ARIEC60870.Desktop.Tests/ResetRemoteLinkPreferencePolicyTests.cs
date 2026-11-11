// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class ResetRemoteLinkPreferencePolicyTests
{
    [Theory]
    [InlineData(Iec60870ProtocolMode.Iec101, false, true)]
    [InlineData(Iec60870ProtocolMode.Iec101, true, false)]
    [InlineData(Iec60870ProtocolMode.Iec103, false, false)]
    [InlineData(Iec60870ProtocolMode.Iec104, false, false)]
    public void DefaultIsOnForActiveIec101Only(
        Iec60870ProtocolMode mode, bool passive, bool expected)
        => Assert.Equal(expected, ResetRemoteLinkPreferencePolicy.OnConnect(
            mode, passive, null, null));

    [Fact]
    public void ExplicitOffOverridesActiveIec101ButCannotMakePassiveTransmit()
    {
        Assert.False(ResetRemoteLinkPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec101, false, false, null));
        Assert.False(ResetRemoteLinkPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec101, true, true, null));
        Assert.True(ResetRemoteLinkPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec101, false, true, false));
    }

    [Fact]
    public void LegacyIec103OffCannotDisableFreshIec101()
    {
        var saved = ResetRemoteLinkPreferencePolicy.MigrateLegacy("Iec103", false);
        Assert.Null(saved.Iec101);
        Assert.Equal(false, saved.Iec103);
        Assert.True(ResetRemoteLinkPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec101, false, saved.Iec101, saved.Iec103));
    }

    [Fact]
    public void LegacyExplicitOffForIec101IsNotSilentlyOverridden()
    {
        var saved = ResetRemoteLinkPreferencePolicy.MigrateLegacy("Iec101", false);
        Assert.Equal(false, saved.Iec101);
        Assert.False(ResetRemoteLinkPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec101, false, saved.Iec101, saved.Iec103));
        var passive = ResetRemoteLinkPreferencePolicy.MigrateLegacy(
            "Iec101PassiveMonitor", false);
        Assert.Null(passive.Iec101);
        Assert.Null(passive.Iec103);
    }

    [Fact]
    public void Iec103ExplicitOnDoesNotCarryAcrossProtocol()
    {
        var saved = ResetRemoteLinkPreferencePolicy.MigrateLegacy("Iec103", true);
        Assert.True(ResetRemoteLinkPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec103, false, saved.Iec101, saved.Iec103));
        Assert.True(ResetRemoteLinkPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec101, false, saved.Iec101, saved.Iec103));
        Assert.False(ResetRemoteLinkPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec104, false, saved.Iec101, saved.Iec103));
    }
}
