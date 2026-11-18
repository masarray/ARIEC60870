// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace ARIEC60870.Repository.Tests;

public sealed class HelpCenterLayoutTests
{
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void HelpCenterUsesSharedArSeeDesignLanguageAndIndependentSearchRow()
    {
        var document = XDocument.Load(Source("HelpWindow.xaml"));
        var window = document.Root!;
        Assert.Equal("ArSee Help Center", (string?)window.Attribute("Title"));
        Assert.Equal("{StaticResource AppFont}", (string?)window.Attribute("FontFamily"));
        Assert.Equal("{StaticResource SurfaceBrush}", (string?)window.Attribute("Background"));
        Assert.Equal("{StaticResource SurfaceBrush}",
            (string?)window.Elements().Single(x => x.Name.LocalName == "Grid")
                .Attribute("Background"));

        var search = Named(document, "SearchBox");
        Assert.Equal("2", (string?)search.Parent?.Attribute("Grid.Row"));
        Assert.Equal("SearchBox_TextChanged", (string?)search.Attribute("TextChanged"));
        Assert.Null(search.Attribute("Text")); // Never insert placeholder into the query.
        Assert.Equal("Search help topics", (string?)search.Attribute("AutomationProperties.Name"));

        var actions = document.Descendants().Where(x => x.Name.LocalName == "Button").ToArray();
        Assert.Contains(actions, x => (string?)x.Attribute(Xaml + "Name") == "OpenOnlineButton" &&
            (string?)x.Attribute("Style") == "{StaticResource ArSeeToolbarActionButtonStyle}");
        Assert.Contains(actions, x => (string?)x.Attribute(Xaml + "Name") == "ClearSearchButton" &&
            (string?)x.Attribute("Click") == "ClearSearchButton_Click");
        Assert.Contains(document.Descendants(), x => x.Name.LocalName == "ColumnDefinition" &&
            (string?)x.Attribute("Width") == "*");
        Assert.Contains(document.Descendants(), x => x.Name.LocalName == "Border" &&
            (string?)x.Attribute("Background") == "{StaticResource ArSeeContentWhiteBrush}");
    }

    [Fact]
    public void HelpArticlesHaveNoFixedWidthBulletOrClampedNavigationSummary()
    {
        var document = XDocument.Load(Source("HelpWindow.xaml"));
        var articleScroll = Named(document, "TopicScrollViewer");
        Assert.Equal("Disabled", (string?)articleScroll.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal("Auto", (string?)articleScroll.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("TopicContentPanel", (string?)articleScroll
            .Descendants().Single(x => x.Name.LocalName == "StackPanel")
            .Attribute(Xaml + "Name"));

        var topicList = Named(document, "TopicListBox");
        Assert.Equal("{StaticResource HelpTopicListItem}", (string?)topicList.Attribute("ItemContainerStyle"));
        var summary = topicList.Descendants().Single(x => x.Name.LocalName == "TextBlock" &&
            (string?)x.Attribute("Text") == "{Binding Summary}");
        Assert.Null(summary.Attribute("MaxHeight"));
        Assert.Equal("Wrap", (string?)summary.Attribute("TextWrapping"));

        var source = File.ReadAllText(Source("HelpWindow.xaml.cs"));
        Assert.Contains("row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });", source);
        Assert.Contains("TopicScrollViewer.ScrollToTop();", source);
        Assert.DoesNotContain("MaxWidth = 660", source);
        Assert.DoesNotContain("SearchBox.GotKeyboardFocus", source);
    }

    [Fact]
    public void HelpSearchPreservesContextRoutingAndSupportsEmptyResults()
    {
        var xaml = XDocument.Load(Source("HelpWindow.xaml"));
        Assert.Equal("HelpWindow_PreviewKeyDown",
            (string?)xaml.Root?.Attribute("PreviewKeyDown"));
        Assert.Equal("ClearSearchButton_Click",
            (string?)Named(xaml, "ClearSearchButton").Attribute("Click"));
        var code = File.ReadAllText(Source("HelpWindow.xaml.cs"));
        Assert.Contains("private void RenderNoResults()", code);
        Assert.Contains("TopicContentPanel.Children.Clear();", code);
        Assert.Contains("SearchBox.Clear();", code);
        Assert.Contains("Keyboard.Modifiers == ModifierKeys.Control", code);
        Assert.Contains("Key.Escape", code);
    }

    [Fact]
    public void PreviewIsRealWpfAndArtifactsRequirePassedSmoke()
    {
        var acceptance = File.ReadAllText(Source("HelpWindow.UiAcceptance.cs"));
        var app = File.ReadAllText(Source("App.xaml.cs"));
        Assert.Contains("RenderTargetBitmap", acceptance);
        Assert.Contains("bitmap.Render(viewport);", acceptance);
        Assert.DoesNotContain("bitmap.Render(this);", acceptance);
        Assert.Contains("en-trace-780x540.png", acceptance);
        Assert.Contains("id-trace-780x540.png", acceptance);
        Assert.Contains("No matching topics", acceptance);
        Assert.Contains("--help-ui-acceptance", app);
    }

    [Fact]
    public void ReadingSurfaceBrushIsDeclaredOnceInGlobalTheme()
    {
        var theme = XDocument.Load(Source("Resources/ModernTheme.xaml"));
        Assert.Single(theme.Descendants(), x =>
            (string?)x.Attribute(Xaml + "Key") == "ArSeeContentWhiteBrush");
    }

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(x => (string?)x.Attribute(Xaml + "Name") == name);

    private static string Source(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null;
             dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, "src", "ARIEC60870.Desktop",
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) return full;
        }
        throw new FileNotFoundException("Cannot locate Help Center source.");
    }
}
