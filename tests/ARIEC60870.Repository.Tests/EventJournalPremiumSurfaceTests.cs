// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Xml.Linq;
using Xunit;

namespace ARIEC60870.Repository.Tests;

/// <summary>
/// Protect the deliberately distinct Event Journal pill and circular button
/// materials against a future "radius-only" regression. Real WPF acceptance
/// separately validates the instantiated templates and layout.
/// </summary>
public sealed class EventJournalPremiumSurfaceTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void JournalToolbarHasTwoDistinctPremiumMaterials()
    {
        var document = XDocument.Load(FindMainWindowXaml());
        var pill = Resource(document, "ControlTemplate", "EventJournalPremiumPillTemplate");
        var circle = Resource(document, "ControlTemplate", "EventJournalPremiumCircleTemplate");

        var pillSurface = Named(pill, "Border", "EventJournalPillSurface");
        Assert.Equal("16", (string?)pillSurface.Attribute("CornerRadius"));
        Assert.Contains("EventJournalPillShadow", (string?)pillSurface.Attribute("Effect"));
        Assert.Contains("TemplateBinding Background", (string?)pillSurface.Attribute("Background"));

        var circleSurface = Named(circle, "Ellipse", "EventJournalCircleSurface");
        Assert.Contains("EventJournalCircleShadow", (string?)circleSurface.Attribute("Effect"));
        Assert.Contains("TemplateBinding Background", (string?)circleSurface.Attribute("Fill"));
        Assert.Equal("1", (string?)circleSurface.Attribute("StrokeThickness"));

        foreach (var brush in new[] {
                     "EventJournalPillIdleBrush", "EventJournalPillHoverBrush",
                     "EventJournalPillPressedBrush", "EventJournalPillSelectedBrush",
                     "EventJournalCircleIdleBrush", "EventJournalCircleHoverBrush",
                     "EventJournalCirclePressedBrush" })
            Assert.True(Resource(document, "LinearGradientBrush", brush)
                            .Elements().Count() >= 2, brush + " lost its gradient");

        foreach (var style in new[] { "EventJournalActionButtonStyle",
                                      "EventJournalActionToggleStyle" })
        {
            var element = Resource(document, "Style", style);
            Assert.Contains(element.Elements(), e => e.Name.LocalName == "Setter" &&
                (string?)e.Attribute("Property") == "Height" &&
                (string?)e.Attribute("Value") == "32");
            Assert.Contains(element.Elements(), e => e.Name.LocalName == "Setter" &&
                (string?)e.Attribute("Property") == "Template" &&
                ((string?)e.Attribute("Value"))?.Contains("EventJournalPremiumPillTemplate") == true);
        }

        var navigation = Resource(document, "Style", "EventJournalCircleNavigationStyle");
        Assert.Contains(navigation.Elements(), e => e.Name.LocalName == "Setter" &&
            (string?)e.Attribute("Property") == "Template" &&
            ((string?)e.Attribute("Value"))?.Contains("EventJournalPremiumCircleTemplate") == true);
        Assert.Contains(navigation.Elements(), e => e.Name.LocalName == "Setter" &&
            (string?)e.Attribute("Property") == "Width" &&
            (string?)e.Attribute("Value") == "32");
    }

    [Theory]
    [InlineData("EventHistoryPreviousButton", "M15,18 L9,12 L15,6")]
    [InlineData("EventHistoryNextButton", "M9,18 L15,12 L9,6")]
    public void NavigationUsesSmallRoundedLucideChevronInSeparateCircle(string name, string expected)
    {
        var document = XDocument.Load(FindMainWindowXaml());
        var button = document.Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == name);
        Assert.Equal("Button", button.Name.LocalName);
        Assert.Contains("EventJournalCircleNavigationStyle",
            (string?)button.Attribute("Style"));
        var path = button.Elements().Single(e => e.Name.LocalName == "Path");
        Assert.Equal("12", (string?)path.Attribute("Width"));
        Assert.Equal("12", (string?)path.Attribute("Height"));
        Assert.Equal("2", (string?)path.Attribute("StrokeThickness"));
        Assert.Equal("Round", (string?)path.Attribute("StrokeStartLineCap"));
        Assert.Equal("Round", (string?)path.Attribute("StrokeEndLineCap"));
        Assert.Equal(expected, (string?)path.Attribute("Data"));
    }

    private static XElement Resource(XDocument document, string tag, string key)
        => document.Descendants().Single(e => e.Name.LocalName == tag &&
                                               (string?)e.Attribute(Xaml + "Key") == key);

    private static XElement Named(XElement template, string tag, string name)
        => template.Descendants().Single(e => e.Name.LocalName == tag &&
                                           (string?)e.Attribute(Xaml + "Name") == name);

    private static string FindMainWindowXaml()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null;
             dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "src", "ARIEC60870.Desktop", "MainWindow.xaml");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Unable to locate MainWindow.xaml in source checkout.");
    }
}
