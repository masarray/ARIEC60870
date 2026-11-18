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
        Assert.Equal("1", (string?)search.Parent?.Attribute("Grid.Column"));
        Assert.Equal("2", (string?)Named(document, "SearchChrome").Attribute("Grid.Row"));
        Assert.Equal("Center", (string?)search.Attribute("VerticalAlignment"));
        Assert.Equal("{StaticResource HelpSearchInput}", (string?)search.Attribute("Style"));
        Assert.Equal("SearchBox_TextChanged", (string?)search.Attribute("TextChanged"));
        Assert.Null(search.Attribute("Text")); // Never insert placeholder into the query.
        Assert.Equal("Search help topics", (string?)search.Attribute("AutomationProperties.Name"));

        var actions = document.Descendants().Where(x => x.Name.LocalName == "Button").ToArray();
        Assert.Contains(actions, x => (string?)x.Attribute(Xaml + "Name") == "OpenOnlineButton" &&
            (string?)x.Attribute("Style") == "{StaticResource ArSeeToolbarActionButtonStyle}");
        Assert.Contains(actions, x => (string?)x.Attribute(Xaml + "Name") == "ClearSearchButton" &&
            (string?)x.Attribute("Click") == "ClearSearchButton_Click");
        Assert.DoesNotContain(actions, x => (string?)x.Attribute(Xaml + "Name") == "CloseHelpButton");
        Assert.DoesNotContain(actions, x => (string?)x.Attribute("Click") == "CloseButton_Click");
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
        var chapterRow = Named(document, "ChapterRow");
        var title = chapterRow.Descendants().Single(x => x.Name.LocalName == "TextBlock" &&
            (string?)x.Attribute("Text") == "{Binding Title}");
        Assert.Equal("Wrap", (string?)title.Attribute("TextWrapping"));
        Assert.Equal("{Binding BadgeBackground}",
            (string?)Named(document, "ProtocolBadge").Attribute("Background"));
        Assert.Equal("Collapsed", (string?)Named(document, "SectionRow").Attribute("Visibility"));
        Assert.Contains(document.Descendants(), x => x.Name.LocalName == "DataTrigger" &&
            (string?)x.Attribute("Value") == "section");
        Assert.DoesNotContain(topicList.Descendants(), x =>
            (string?)x.Attribute("Text") == "{Binding Summary}" ||
            (string?)x.Attribute("Text") == "{Binding Category}");

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
        Assert.Contains("id-iec101-780x540.png", acceptance);
        Assert.Contains("No matching topics", acceptance);
        Assert.Contains("--help-ui-acceptance", app);
    }

    [Fact]
    public void EnlargedHelpCenterMakesArticleDominantAndUsesSharedMaterialIcons()
    {
        var document = XDocument.Load(Source("HelpWindow.xaml"));
        Assert.Equal("1180", (string?)document.Root?.Attribute("Width"));
        Assert.Equal("820", (string?)document.Root?.Attribute("Height"));
        Assert.Equal("780", (string?)document.Root?.Attribute("MinWidth"));

        var topics = Named(document, "HelpTopicsPanel");
        var article = Named(document, "HelpArticlePanel");
        Assert.Same(topics.Parent, article.Parent);
        var columns = topics.Parent!.Elements()
            .Single(x => x.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().ToArray();
        Assert.Equal("246", (string?)columns[0].Attribute("Width"));
        Assert.Equal("*", (string?)columns[2].Attribute("Width"));
        var search = Named(document, "SearchBox");
        Assert.Same(topics, search.Ancestors().Single(x =>
            (string?)x.Attribute(Xaml + "Name") == "HelpTopicsPanel"));

        var icon = Named(document, "SearchGlyphPath");
        Assert.Equal("Path", icon.Name.LocalName);
        Assert.Equal("{StaticResource ArSeeMaterialOutlinedSearch}", (string?)icon.Attribute("Data"));
        Assert.Equal("Center", (string?)icon.Attribute("VerticalAlignment"));
        Assert.Null(document.Descendants().FirstOrDefault(x =>
            (string?)x.Attribute(Xaml + "Name") == "TopicHeroIconPath"));
        Assert.Null(document.Descendants().FirstOrDefault(x =>
            (string?)x.Attribute("Data") == "{Binding IconGeometry}"));
        Assert.Equal("{Binding Badge}", (string?)Named(document, "ProtocolBadge")
            .Descendants().Single(x => x.Name.LocalName == "TextBlock").Attribute("Text"));
        Assert.DoesNotContain("{Binding Icon}", File.ReadAllText(Source("HelpWindow.xaml")));

        var shared = XDocument.Load(Source("Resources/LucideIcons.xaml"));
        foreach (var key in new[] {
            "ArSeeMaterialOutlinedBook", "ArSeeMaterialOutlinedCode", "ArSeeMaterialOutlinedLink",
            "ArSeeMaterialOutlinedTarget", "ArSeeMaterialOutlinedClock",
            "ArSeeMaterialOutlinedDescription", "ArSeeMaterialOutlinedOpenExternal",
            "ArSeeMaterialOutlinedChart", "ArSeeMaterialOutlinedBug",
            "ArSeeMaterialOutlinedSearch", "ArSeeMaterialOutlinedClear" })
        {
            Assert.Single(shared.Descendants(), x =>
                (string?)x.Attribute(Xaml + "Key") == key);
        }

        var code = File.ReadAllText(Source("HelpWindow.xaml.cs"));
        Assert.Contains("GetTopicMaterialGeometry(topic.Key)", code);
        Assert.Contains("GetTopicBadge(topic.Key)", code);
        Assert.Contains("GetTopicBadge(string key)", code);
        Assert.Contains("item.Kind == \"section\"", code);
        Assert.Contains("target.BringIntoView();", code);
        Assert.Contains("\"IEC101\"", code);
        Assert.Contains("\"IEC103\"", code);
        Assert.Contains("\"IEC104\"", code);
        Assert.Contains("OpenOnlineLabel.Text =", code);
        Assert.DoesNotContain("BuildOnlineButtonContent", code);
        Assert.DoesNotContain("CloseHelpButton", code);
        Assert.DoesNotContain("HelpSubtitleText", code);
        var acceptance = File.ReadAllText(Source("HelpWindow.UiAcceptance.cs"));
        Assert.Contains("SearchGlyphPath.TranslatePoint(", acceptance);
        Assert.Contains("en-handbook-default-window.png", acceptance);
        Assert.Contains("id-iec101-780x540.png", acceptance);
        Assert.Contains("requestedWindowSizes", acceptance);
        Assert.Contains("topicPaneWidth", acceptance);
        Assert.Contains("HelpArticlePanel.ActualWidth", acceptance);
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
