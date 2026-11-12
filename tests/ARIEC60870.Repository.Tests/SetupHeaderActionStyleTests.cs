// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace ARIEC60870.Repository.Tests;

public sealed class SetupHeaderActionStyleTests
{
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ConfigHeaderKeepsNativeHandlersAndUsesSuppliedSvgPath()
    {
        var doc = XDocument.Load(Source("MainWindow.xaml"));
        var buttons = doc.Descendants().Where(x => x.Name.LocalName == "Button").ToArray();
        var connect = buttons.Single(x => (string?)x.Attribute(Xaml + "Name") == "SetupConnectButton");
        var close = buttons.Single(x => (string?)x.Attribute(Xaml + "Name") == "SetupCloseButton");

        Assert.Equal("CloseSetupAndConnect_Click", (string?)connect.Attribute("Click"));
        Assert.Equal("CloseSetup_Click", (string?)close.Attribute("Click"));
        Assert.Equal("{StaticResource SetupConnectButtonStyle}", (string?)connect.Attribute("Style"));
        Assert.Equal("{StaticResource SetupCloseIconButtonStyle}", (string?)close.Attribute("Style"));
        Assert.Equal("Close configuration", (string?)close.Attribute(
            XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation") +
            "AutomationProperties.Name"));
        Assert.Null(close.Attribute("Content"));

        var glyph = close.Elements().Single(x => x.Name.LocalName == "Path");
        Assert.Equal("SetupCloseIcon", (string?)glyph.Attribute(Xaml + "Name"));
        Assert.Equal(
            "M6.4,19L5,17.6L10.6,12L5,6.4L6.4,5L12,10.6L17.6,5L19,6.4L13.4,12L19,17.6L17.6,19L12,13.4L6.4,19Z",
            (string?)glyph.Attribute("Data"));
        Assert.Equal("18", (string?)glyph.Attribute("Width"));
        Assert.Equal("18", (string?)glyph.Attribute("Height"));
        Assert.Contains("Foreground", (string?)glyph.Attribute("Fill"));
    }

    [Fact]
    public void ConfigButtonsHaveIndependentHoverAndPressedVisualStates()
    {
        var doc = XDocument.Load(Source("Resources/ModernTheme.xaml"));
        var connect = Resource(doc, "Style", "SetupConnectButtonStyle");
        var close = Resource(doc, "Style", "SetupCloseIconButtonStyle");
        Assert.Equal("{StaticResource ArSeeWorkspaceActionButtonStyle}",
            (string?)connect.Attribute("BasedOn"));
        Assert.Equal("36", SetterValue(close, "Height"));
        Assert.Equal("36", SetterValue(close, "Width"));

        var connectTemplate = Resource(doc, "ControlTemplate", "SetupConnectButtonTemplate");
        var closeTemplate = Resource(doc, "ControlTemplate", "SetupCloseIconButtonTemplate");
        Assert.Equal("{StaticResource SetupConnectButtonTemplate}", SetterValue(connect, "Template"));
        Assert.Equal("{StaticResource SetupCloseIconButtonTemplate}", SetterValue(close, "Template"));

        foreach (var key in new[] {
            "SetupConnectIdleBrush", "SetupConnectHoverBrush", "SetupConnectPressedBrush" })
        {
            var gradient = Resource(doc, "LinearGradientBrush", key);
            Assert.Equal(3, gradient.Elements().Count(x => x.Name.LocalName == "GradientStop"));
        }

        var connectHover = Trigger(connectTemplate, "IsMouseOver");
        var connectPressed = Trigger(connectTemplate, "IsPressed");
        Assert.Equal("{StaticResource SetupConnectHoverBrush}",
            TriggerSetterValue(connectHover, "SetupConnectSurface", "Background"));
        Assert.Equal("{StaticResource SetupConnectPressedBrush}",
            TriggerSetterValue(connectPressed, "SetupConnectSurface", "Background"));
        Assert.Contains(connectPressed.Descendants(),
            x => x.Name.LocalName == "TranslateTransform" &&
                 (string?)x.Attribute("Y") == "1");

        var closeHover = Trigger(closeTemplate, "IsMouseOver");
        var closePressed = Trigger(closeTemplate, "IsPressed");
        Assert.Equal("#FFF0EF", TriggerSetterValue(closeHover, "SetupCloseSurface", "Background"));
        Assert.Equal("#B34D56", TriggerSetterValue(closeHover, null, "Foreground"));
        Assert.Equal("#EF3346", TriggerSetterValue(closePressed, "SetupCloseSurface", "Background"));
        Assert.Equal("#FFFFFF", TriggerSetterValue(closePressed, null, "Foreground"));
    }

    private static XElement Resource(XDocument doc, string element, string key)
        => doc.Descendants().Single(x => x.Name.LocalName == element &&
                                          (string?)x.Attribute(Xaml + "Key") == key);

    private static XElement Trigger(XElement template, string property)
        => template.Descendants().Single(x => x.Name.LocalName == "Trigger" &&
            (string?)x.Attribute("Property") == property &&
            (string?)x.Attribute("Value") == "True");

    private static string? TriggerSetterValue(XElement trigger, string? target, string property)
        => trigger.Elements().Single(x => x.Name.LocalName == "Setter" &&
            (string?)x.Attribute("Property") == property &&
            (string?)x.Attribute("TargetName") == target).Attribute("Value")?.Value;

    private static string? SetterValue(XElement style, string property)
        => style.Elements().Single(x => x.Name.LocalName == "Setter" &&
                                      (string?)x.Attribute("Property") == property)
            .Attribute("Value")?.Value;

    private static string Source(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null;
             dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, "src", "ARIEC60870.Desktop",
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) return full;
        }
        throw new FileNotFoundException("Cannot locate desktop XAML source for Config style acceptance.");
    }
}
