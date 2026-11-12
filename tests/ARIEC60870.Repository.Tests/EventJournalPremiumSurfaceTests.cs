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
        var document = XDocument.Load(FindThemeXaml());
        var pill = Resource(document, "ControlTemplate", "EventJournalPremiumPillTemplate");
        var circle = Resource(document, "ControlTemplate", "EventJournalPremiumCircleTemplate");

        var pillSurface = Named(pill, "Border", "EventJournalPillSurface");
        Assert.Equal("{StaticResource ArSeeToolbarPillCornerRadius}", (string?)pillSurface.Attribute("CornerRadius"));
        Assert.Contains("EventJournalPillShadow", (string?)pillSurface.Attribute("Effect"));
        Assert.Contains("TemplateBinding Background", (string?)pillSurface.Attribute("Background"));

        var circleSurface = Named(circle, "Ellipse", "EventJournalCircleSurface");
        Assert.Contains("EventJournalCircleShadow", (string?)circleSurface.Attribute("Effect"));
        Assert.Contains("TemplateBinding Background", (string?)circleSurface.Attribute("Fill"));
        Assert.Equal("1", (string?)circleSurface.Attribute("StrokeThickness"));
        Assert.True((string?)circleSurface.Attribute("Margin") == "1",
            "30-DIP visible circle must fit inside the preserved 32-DIP button.");

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
                (string?)e.Attribute("Value") == "{StaticResource ArSeeToolbarPillHeight}");
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
            (string?)e.Attribute("Value") == "{StaticResource ArSeeToolbarCircleDiameter}");
    }

    [Fact]
    public void NavbarPillGeometryIsTheGlobalTextButtonShape()
    {
        var main = XDocument.Load(FindMainWindowXaml());
        var theme = XDocument.Load(FindThemeXaml());
        Assert.Equal("42", Resource(theme, "Double", "ArSeePillHeight").Value);
        Assert.Equal("19", Resource(theme, "CornerRadius", "ArSeePillCornerRadius").Value);
        Assert.Equal("17", Resource(theme, "CornerRadius", "ArSeePillInsetCornerRadius").Value);
        Assert.Equal("36", Resource(theme, "Double", "ArSeeToolbarPillHeight").Value);
        Assert.Equal("18", Resource(theme, "CornerRadius", "ArSeeToolbarPillCornerRadius").Value);
        Assert.Equal("16", Resource(theme, "CornerRadius", "ArSeeToolbarPillInsetCornerRadius").Value);
        Assert.Equal("28", Resource(theme, "Double", "ArSeeToolbarCircleDiameter").Value);
        Assert.Equal("206", Resource(theme, "Double", "ArSeeToolbarSearchWidth").Value);
        foreach (var key in new[] { "SegmentedNavButton", "PrimaryButton" })
        {
            var style = Resource(theme, "Style", key);
            Assert.Equal("{StaticResource ArSeePillCornerRadius}",
                (string?)Named(style, "Border", "Chrome").Attribute("CornerRadius"));
            Assert.Contains(style.Elements(), e => e.Name.LocalName == "Setter" &&
                (string?)e.Attribute("Property") == (key == "PrimaryButton" ? "MinHeight" : "Height") &&
                (string?)e.Attribute("Value") == "{StaticResource ArSeePillHeight}");
        }
        var search = main.Descendants().Single(e =>
            (string?)e.Attribute(Xaml + "Name") == "EventsSearchShell");
        Assert.Equal("{StaticResource ArSeeToolbarSearchShellStyle}", (string?)search.Attribute("Style"));
        var searchStyle = Resource(theme, "Style", "ArSeeToolbarSearchShellStyle");
        Assert.Contains(searchStyle.Elements(), e => e.Name.LocalName == "Setter" &&
            (string?)e.Attribute("Property") == "Height" &&
            (string?)e.Attribute("Value") == "{StaticResource ArSeeToolbarPillHeight}");
        Assert.Contains(searchStyle.Elements(), e => e.Name.LocalName == "Setter" &&
            (string?)e.Attribute("Property") == "CornerRadius" &&
            (string?)e.Attribute("Value") == "{StaticResource ArSeeToolbarPillCornerRadius}");
        var pill = Resource(theme, "ControlTemplate", "EventJournalPremiumPillTemplate");
        Assert.Equal("{StaticResource ArSeeToolbarPillCornerRadius}",
            (string?)Named(pill, "Border", "EventJournalPillSurface").Attribute("CornerRadius"));
        // Compact circular chevrons now live inside a shared, full-height pager capsule.
        var circle = Resource(theme, "Style", "EventJournalCircleNavigationStyle");
        Assert.Contains(circle.Elements(), e => e.Name.LocalName == "Setter" &&
            (string?)e.Attribute("Property") == "Width" &&
            (string?)e.Attribute("Value") == "{StaticResource ArSeeToolbarCircleDiameter}");
        Assert.Contains(circle.Elements(), e => e.Name.LocalName == "Setter" &&
            (string?)e.Attribute("Property") == "Height" &&
            (string?)e.Attribute("Value") == "{StaticResource ArSeeToolbarCircleDiameter}");
    }

    private static string FindThemeXaml()
        => Path.Combine(Path.GetDirectoryName(FindMainWindowXaml())!, "Resources", "ModernTheme.xaml");

    [Fact]
    public void EventsToolbarSearchAndPagerShareTheCompactPillDesignTokens()
    {
        var doc = XDocument.Load(FindMainWindowXaml());
        var shell = Named(doc.Root!, "Border", "EventsSearchShell");
        Assert.Equal("{StaticResource ArSeeToolbarSearchWidth}", (string?)shell.Attribute("Width"));
        Assert.Equal("{StaticResource ArSeeToolbarSearchShellStyle}", (string?)shell.Attribute("Style"));

        var searchLayout = shell.Descendants().Single(e => e.Name.LocalName == "DockPanel");
        Assert.Equal("7,0,10,0", (string?)searchLayout.Attribute("Margin"));
        var searchText = Named(shell, "TextBox", "EventsQuickFindBox");
        Assert.Equal("0", (string?)searchText.Attribute("Padding"));
        var icon = searchLayout.Elements().Single(e => e.Name.LocalName == "Path");
        Assert.Equal("0,0,5,0", (string?)icon.Attribute("Margin"));

        var clear = Named(shell, "Button", "EventsSearchClearButton");
        Assert.Equal("{StaticResource ArSeeToolbarSearchClearStyle}", (string?)clear.Attribute("Style"));
        Assert.Equal("Clear event search", (string?)clear.Attribute("AutomationProperties.Name"));
        var clearIcon = clear.Elements().Single(e => e.Name.LocalName == "Path");
        Assert.Contains("AncestorType", (string?)clearIcon.Attribute("Stroke"));

        var clearStyle = Resource(XDocument.Load(FindThemeXaml()), "Style", "EventJournalSearchClearStyle");
        Assert.Contains(clearStyle.Descendants(), e => e.Name.LocalName == "Grid" &&
            (string?)e.Attribute("Background") == "Transparent");
        Assert.Contains(clearStyle.Descendants(), e => e.Name.LocalName == "Trigger" &&
            (string?)e.Attribute("Property") == "IsMouseOver" &&
            e.Descendants().Any(setter => (string?)setter.Attribute("Value") == "#B55B66"));
        Assert.Contains(clearStyle.Descendants(), e => e.Name.LocalName == "Trigger" &&
            (string?)e.Attribute("Property") == "IsPressed" &&
            e.Descendants().Any(setter => (string?)setter.Attribute("Value") == "#E11D48"));

        var pager = Named(doc.Root!, "Border", "EventHistoryPagerShell");
        Assert.Equal("{StaticResource ArSeeToolbarPillHeight}", (string?)pager.Attribute("Height"));
        Assert.Equal("{StaticResource ArSeeToolbarPillCornerRadius}",
            (string?)pager.Attribute("CornerRadius"));
        Assert.Equal("1", (string?)pager.Attribute("BorderThickness"));
        var children = pager.Elements().Single(e => e.Name.LocalName == "StackPanel").Elements().ToArray();
        Assert.Equal(3, children.Length);
        Assert.Equal("EventHistoryPreviousButton", (string?)children[0].Attribute(Xaml + "Name"));
        Assert.Equal("EventHistoryPageText", (string?)children[1].Attribute(Xaml + "Name"));
        Assert.Equal("EventHistoryNextButton", (string?)children[2].Attribute(Xaml + "Name"));

        var first = Named(doc.Root!, "Button", "EventHistoryFirstButton");
        Assert.Equal("First Event", (string?)first.Attribute("Content"));
        Assert.Equal("EventHistoryFirst_Click", (string?)first.Attribute("Click"));
        Assert.Equal("{StaticResource EventJournalActionButtonStyle}", (string?)first.Attribute("Style"));
        var latest = Named(doc.Root!, "Button", "EventHistoryLatestButton");
        Assert.Equal("Latest Event", (string?)latest.Attribute("Content"));
        Assert.Equal("{StaticResource EventJournalActionButtonStyle}", (string?)latest.Attribute("Style"));
    }

    [Fact]
    public void EveryObservationWorkspaceUsesTheSameCompactSearchComponent()
    {
        var main = XDocument.Load(FindMainWindowXaml());
        var theme = XDocument.Load(FindThemeXaml());
        var shellStyle = Resource(theme, "Style", "ArSeeToolbarSearchShellStyle");
        var clearStyle = Resource(theme, "Style", "ArSeeToolbarSearchClearStyle");
        var actionStyle = Resource(theme, "Style", "ArSeeToolbarActionButtonStyle");
        var workspaceStyle = Resource(theme, "Style", "ArSeeWorkspaceActionButtonStyle");
        Assert.Equal("{StaticResource ArSeeToolbarActionButtonStyle}",
            (string?)workspaceStyle.Attribute("BasedOn"));
        Assert.Equal("{StaticResource EventJournalSearchClearStyle}",
            (string?)clearStyle.Attribute("BasedOn"));
        Assert.Equal("{StaticResource EventJournalActionButtonStyle}",
            (string?)actionStyle.Attribute("BasedOn"));
        Assert.Contains(shellStyle.Elements(), e => e.Name.LocalName == "Setter" &&
            (string?)e.Attribute("Property") == "Height" &&
            (string?)e.Attribute("Value") == "{StaticResource ArSeeToolbarPillHeight}");

        foreach (var (shellName, clearName, inputName) in new[] {
                     ("TraceWindowSearchBar", "TraceSearchClearButton", "TraceQuickFindBox"),
                     ("ValuesSearchShell", "ValuesSearchClearButton", "ValuesQuickFindBox"),
                     ("EventsSearchShell", "EventsSearchClearButton", "EventsQuickFindBox"),
                     ("ControlsSearchShell", "ControlsSearchClearButton", "ControlsQuickFindBox"),
                     ("GISearchShell", "GISearchClearButton", "GiQuickFindBox") })
        {
            var shell = Named(main.Root!, "Border", shellName);
            Assert.Equal("{StaticResource ArSeeToolbarSearchShellStyle}",
                (string?)shell.Attribute("Style"));
            var dock = shell.Descendants().Single(e => e.Name.LocalName == "DockPanel");
            Assert.Equal("7,0,10,0", (string?)dock.Attribute("Margin"));
            var input = Named(shell, "TextBox", inputName);
            Assert.Equal("0", (string?)input.Attribute("Padding"));
            Assert.Equal("12", (string?)input.Attribute("FontSize"));
            Assert.Equal("QuickFindTextChanged", (string?)input.Attribute("TextChanged"));
            var clear = Named(shell, "Button", clearName);
            Assert.Equal("{StaticResource ArSeeToolbarSearchClearStyle}",
                (string?)clear.Attribute("Style"));
            Assert.Equal("QuickFindClear_Click", (string?)clear.Attribute("Click"));
            Assert.Equal("Collapsed", (string?)clear.Attribute("Visibility"));
            var glyph = clear.Elements().Single(e => e.Name.LocalName == "Path");
            Assert.Contains("AncestorType", (string?)glyph.Attribute("Stroke"));
            var searchIcon = dock.Elements().Single(e => e.Name.LocalName == "Path");
            Assert.Equal("0,0,5,0", (string?)searchIcon.Attribute("Margin"));
        }
        Assert.Equal("{StaticResource ArSeeWorkspaceActionButtonStyle}",
            (string?)Named(main.Root!, "Button", "GiStagesToggleButton").Attribute("Style"));
        var archive = Named(main.Root!, "Border", "TraceArchiveQueryShell");
        Assert.Equal("{StaticResource ArSeeToolbarSearchShellStyle}",
            (string?)archive.Attribute("Style"));
        Assert.Equal("{StaticResource ArSeeToolbarSearchWidth}",
            (string?)archive.Attribute("Width"));
        Assert.Equal("0", (string?)Named(archive, "TextBox", "TraceRecordedQueryBox").Attribute("Padding"));
        foreach (var name in new[] { "TraceRecordedFindButton" })
            Assert.Equal("{StaticResource ArSeeToolbarActionButtonStyle}",
                (string?)Named(main.Root!, "Button", name).Attribute("Style"));
        foreach (var name in new[] { "TraceRecordedPrevButton", "TraceRecordedNextButton" })
            Assert.Equal("{StaticResource EventJournalCircleNavigationStyle}",
                (string?)Named(main.Root!, "Button", name).Attribute("Style"));
        Assert.Equal("{StaticResource ArSeeToolbarSearchClearStyle}",
            (string?)Named(main.Root!, "Button", "TraceRecordedCancelButton").Attribute("Style"));
    }

    [Fact]
    public void DisabledControlsRetainTheirCircleOrPillElevation()
    {
        var doc = XDocument.Load(FindThemeXaml());
        foreach (var template in new[] {
                     "EventJournalPremiumPillTemplate",
                     "EventJournalPremiumCircleTemplate" })
        {
            var trigger = Resource(doc, "ControlTemplate", template)
                .Descendants().Single(e => e.Name.LocalName == "Trigger" &&
                    (string?)e.Attribute("Property") == "IsEnabled" &&
                    (string?)e.Attribute("Value") == "False");
            Assert.Contains(trigger.Elements(), e => e.Name.LocalName == "Setter" &&
                (string?)e.Attribute("Property") == "Opacity");
            Assert.False(trigger.Elements().Any(e =>
                (string?)e.Attribute("Property") == "Effect"),
                template + " removed its depth when disabled.");
        }
    }

    [Theory]
    [InlineData("EventHistoryPreviousButton", "LucideChevronLeft", "M15,18 L9,12 L15,6")]
    [InlineData("EventHistoryNextButton", "LucideChevronRight", "M9,18 L15,12 L9,6")]
    public void NavigationUsesSmallRoundedLucideChevronInSeparateCircle(string name, string resource, string expected)
    {
        var document = XDocument.Load(FindMainWindowXaml());
        var button = document.Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == name);
        Assert.Equal("Button", button.Name.LocalName);
        Assert.Contains("EventJournalCircleNavigationStyle",
            (string?)button.Attribute("Style"));
        var path = button.Elements().Single(e => e.Name.LocalName == "Path");
        Assert.Equal("10", (string?)path.Attribute("Width"));
        Assert.Equal("10", (string?)path.Attribute("Height"));
        Assert.Equal("2", (string?)path.Attribute("StrokeThickness"));
        Assert.Equal("Round", (string?)path.Attribute("StrokeStartLineCap"));
        Assert.Equal("Round", (string?)path.Attribute("StrokeEndLineCap"));
        Assert.Equal("{StaticResource " + resource + "}", (string?)path.Attribute("Data"));
        var lucide = XDocument.Load(FindIconResourcesXaml());
        var geometry = lucide.Descendants().Single(e => e.Name.LocalName == "Geometry" &&
            (string?)e.Attribute(Xaml + "Key") == resource);
        Assert.Equal(expected, geometry.Value);
    }

    private static string FindIconResourcesXaml()
        => Path.Combine(Path.GetDirectoryName(FindMainWindowXaml())!, "Resources", "LucideIcons.xaml");

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
