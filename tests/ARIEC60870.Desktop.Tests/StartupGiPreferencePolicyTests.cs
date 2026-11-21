// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class StartupGiPreferencePolicyTests
{
    [Theory]
    [InlineData(Iec60870ProtocolMode.Iec101, false, true)]
    [InlineData(Iec60870ProtocolMode.Iec101, true, false)]
    [InlineData(Iec60870ProtocolMode.Iec103, false, true)]
    [InlineData(Iec60870ProtocolMode.Iec104, false, true)]
    public void FreshProtocolDefaultsRequestGiExceptPassive(
        Iec60870ProtocolMode mode, bool passive, bool expected)
        => Assert.Equal(expected, StartupGiPreferencePolicy.OnConnect(
            mode, passive, null, null, null));

    [Fact]
    public void LegacyIec103OffDoesNotDisableFirstActiveIec101Connect()
    {
        var old = StartupGiPreferencePolicy.MigrateLegacy("Iec103", false);
        Assert.Null(old.Iec101);
        Assert.False(old.Iec103);
        Assert.True(StartupGiPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec101, false, old.Iec101, old.Iec103, old.Iec104));
        Assert.False(StartupGiPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec103, false, old.Iec101, old.Iec103, old.Iec104));
    }

    [Fact]
    public void SavedIec101OffIsNotSilentlyOverridden()
    {
        var old = StartupGiPreferencePolicy.MigrateLegacy("Iec101", false);
        Assert.False(StartupGiPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec101, false, old.Iec101, old.Iec103, old.Iec104));
        Assert.True(StartupGiPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec104, false, old.Iec101, old.Iec103, old.Iec104));
    }

    [Fact]
    public void ExplicitOffIsIndependentForAllThreeProtocols()
    {
        Assert.False(StartupGiPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec101, false, false, true, true));
        Assert.False(StartupGiPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec103, false, true, false, true));
        Assert.False(StartupGiPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec104, false, true, true, false));
        Assert.True(StartupGiPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec101, false, true, false, false));
        Assert.False(StartupGiPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec101, true, true, true, true));
    }

    [Fact]
    public void LegacyPassivePreferenceNeverAuthorizesTransmissions()
    {
        var old = StartupGiPreferencePolicy.MigrateLegacy("Iec101PassiveMonitor", true);
        Assert.Null(old.Iec101);
        Assert.Null(old.Iec103);
        Assert.Null(old.Iec104);
        Assert.False(StartupGiPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec101, true, old.Iec101, old.Iec103, old.Iec104));
    }

    [Fact]
    public void LegacyIec104OffDoesNotDisableIec101OrIec103()
    {
        var old = StartupGiPreferencePolicy.MigrateLegacy("Iec104", false);
        Assert.False(old.Iec104);
        Assert.True(StartupGiPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec101, false, old.Iec101, old.Iec103, old.Iec104));
        Assert.True(StartupGiPreferencePolicy.OnConnect(
            Iec60870ProtocolMode.Iec103, false, old.Iec101, old.Iec103, old.Iec104));
    }
}
