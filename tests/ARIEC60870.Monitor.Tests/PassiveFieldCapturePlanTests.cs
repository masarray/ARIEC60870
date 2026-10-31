// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO.Ports;
using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class PassiveFieldCapturePlanTests
{
    private static Dictionary<string, string> Args() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["scada-com"] = "COM7",
        ["rtu-com"] = "COM8",
        ["output"] = "C:\\ARIECFieldCapture",
        ["baud"] = "1200",
        ["seconds"] = "180",
        ["parity"] = "Even",
        ["stop-bits"] = "Two",
        ["link-width"] = "2",
        ["cot-width"] = "1",
        ["ca-width"] = "1",
        ["ioa-width"] = "2"
    };

    [Fact]
    public void CreatesTwoRx1200BaudFieldPlanWithoutHardware()
    {
        var plan = PassiveFieldCapturePlan.Parse(Args());
        Assert.Equal(2, plan.Options.StreamCount);
        Assert.Equal(180, plan.DurationSeconds);
        Assert.True(plan.Options.RecordFullSession);
        Assert.Equal(1200, plan.Options.ScadaTx.BaudRate);
        Assert.Equal(Parity.Even, plan.Options.RtuTx.Parity);
        Assert.Equal(StopBits.Two, plan.Options.RtuTx.StopBits);
        Assert.Equal(2, plan.Options.LinkAddressWidth);
        Assert.Equal(1, plan.Options.CotWidth);
        Assert.Equal(1, plan.Options.CommonAddressWidth);
        Assert.Equal(2, plan.Options.InformationAddressWidth);
        Assert.Empty(plan.MissingPorts(["COM8", "com7"]));
    }

    [Fact]
    public void FourRxCarriesCorrectIndependentLinkADirectionsAndLinkBDirections()
    {
        var args = Args();
        args["link-b-scada-com"] = "COM9";
        args["link-b-rtu-com"] = "COM10";
        var plan = PassiveFieldCapturePlan.Parse(args);
        Assert.Equal(4, plan.Options.StreamCount);
        Assert.Equal(["COM7", "COM8", "COM9", "COM10"],
            plan.Options.Sources.Select(s => s.PortName).ToArray());
        Assert.Contains("Link B", plan.Options.Sources[2].PhysicalTransmitter);
        Assert.Contains("SCADA", plan.Options.Sources[2].PhysicalTransmitter);
        Assert.Contains("RTU", plan.Options.Sources[3].PhysicalTransmitter);
        Assert.Empty(plan.MissingPorts(["COM7", "COM8", "COM9", "COM10"]));
        Assert.Equal(["COM10"], plan.MissingPorts(["COM7", "COM8", "COM9"]));
    }

    [Theory]
    [InlineData("link-b-scada-com")]
    [InlineData("link-b-rtu-com")]
    public void IncompleteLinkBIsRejectedBeforeOpeningAnyPort(string oneSide)
    {
        var args = Args();
        args[oneSide] = "COM9";
        Assert.Throws<ArgumentException>(() => PassiveFieldCapturePlan.Parse(args));
    }

    [Fact]
    public void DuplicateLinkABCOMAcrossLinkBIsRejected()
    {
        var args = Args();
        args["link-b-scada-com"] = "com8";
        args["link-b-rtu-com"] = "COM10";
        Assert.Throws<ArgumentException>(() => PassiveFieldCapturePlan.Parse(args));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("86401")]
    [InlineData("-1")]
    public void InvalidDurationCannotStartAnUnboundedCapture(string seconds)
    {
        var args = Args();
        args["seconds"] = seconds;
        Assert.ThrowsAny<Exception>(() => PassiveFieldCapturePlan.Parse(args));
    }

    [Theory]
    [InlineData("data-bits", "7")]
    [InlineData("link-width", "3")]
    [InlineData("ioa-width", "4")]
    [InlineData("frame-gap-ms", "50")]
    [InlineData("baud", "0")]
    public void UnsafeOrUnsupportedFramingIsRejected(string key, string value)
    {
        var args = Args();
        args[key] = value;
        Assert.ThrowsAny<ArgumentException>(() => PassiveFieldCapturePlan.Parse(args));
    }

    [Fact]
    public void UnexpectedOptionsAreRejectedInsteadOfSilentlyIgnored()
    {
        var args = Args();
        args["disable-safety"] = "true";
        Assert.Throws<ArgumentException>(() => PassiveFieldCapturePlan.Parse(args));
    }

    [Fact]
    public void NoArchiveDestinationIsAcceptedOnlyForLiveGuiNotThisFieldRecorder()
    {
        var args = Args();
        args.Remove("output");
        Assert.Throws<ArgumentException>(() => PassiveFieldCapturePlan.Parse(args));
    }

    [Fact]
    public void InventoryIsAdvisoryAndDoesNotOpenOrMutateHardware()
    {
        var plan = PassiveFieldCapturePlan.Parse(Args());
        Assert.Equal(["COM7", "COM8"], plan.MissingPorts(Array.Empty<string>()));
        Assert.Empty(plan.MissingPorts(["COM8", "com7", "COM42"]));
    }
}
