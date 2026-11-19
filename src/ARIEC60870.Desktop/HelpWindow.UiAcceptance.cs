// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ARIEC60870.Desktop;

/// <summary>
/// Screenshots and measured invariants from the actual WPF Help Center on Windows.
/// This is a logical-DIP viewport test, not an OS-level multi-DPI verification.
/// It does not open any protocol connection or affect user settings.
/// </summary>
public partial class HelpWindow
{
    internal async Task RunHelpUiAcceptanceAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        // The same canonical data file must be packaged and readable offline.
        if (!_topics.Any(topic => topic.Key == "learn-use-start") ||
            !_topics.Any(topic => topic.Key == "learn-use-setup") ||
            !_topics.Any(topic => topic.Key == "learn-use-signal-map") ||
            !_topics.Any(topic => topic.Key == "learn-use-passive") ||
            !_topics.Any(topic => topic.Key == "learn-use-evidence") ||
            !_topics.Any(topic => topic.Key == "learn-iec-family") ||
            !_topics.Any(topic => topic.Key == "learn-iec101") ||
            !_topics.Any(topic => topic.Key == "learn-iec103") ||
            !_topics.Any(topic => topic.Key == "learn-iec104") ||
            !_topics.Any(topic => topic.Key == "learn-nuc") ||
            !_topics.Any(topic => topic.Key == "learn-redundancy"))
            throw new InvalidOperationException("The shared offline engineering handbook was not packaged.");
        var captures = new List<object>();

        await CaptureAsync("en-user-guide-default-window.png", "learn-use-start", 1180, 820);
        VerifyOperatingGuideStructure();
        await CaptureAsync("en-signal-map-guide-1180x820.png", "learn-use-signal-map", 1180, 820);
        VerifyOperatingGuideStructure();
        await CaptureAsync("en-handbook-default-window.png", "learn-iec-family", 1180, 820);
        await CaptureAsync("en-iec104-trace-lab-default.png", "learn-lab-104", 1180, 820);
        VerifyGuidedLabInteraction();
        await CaptureAsync("en-trace-780x540.png", "frame-trace", 780, 540);

        LanguageComboBox.SelectedIndex = 1;
        await CaptureAsync("id-user-guide-setup-780x540.png", "learn-use-setup", 780, 540);
        VerifyOperatingGuideStructure();
        await CaptureAsync("id-signal-map-guide-780x540.png", "learn-use-signal-map", 780, 540);
        VerifyOperatingGuideStructure();
        await CaptureAsync("id-iec101-780x540.png", "learn-iec101", 780, 540);
        if (TopicTitleText.Text != "IEC 60870-5-101")
            throw new InvalidOperationException("Indonesian Help Center translation did not render.");
        var nestedIec101 = TopicListBox.Items.OfType<HelpTopicListItem>()
            .Where(item => item.Kind == "section" && item.Key == "learn-iec101").ToArray();
        if (nestedIec101.Length < 4 ||
            !TopicListBox.Items.OfType<HelpTopicListItem>().Any(item => item.Badge == "IEC103") ||
            !TopicListBox.Items.OfType<HelpTopicListItem>().Any(item => item.Badge == "IEC104"))
            throw new InvalidOperationException("Book chapter hierarchy or IEC protocol badges are missing.");
        TopicListBox.SelectedItem = nestedIec101[^1];
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        if (TopicScrollViewer.VerticalOffset < 20)
            throw new InvalidOperationException("Clicking a nested IEC101 subchapter did not scroll the article.");
        SaveRenderedViewport(outputDirectory, "id-iec101-nested-subchapter-780x540.png");
        await CaptureAsync("id-iec101-trace-lab-780x540.png", "learn-lab-101", 780, 540);

        LanguageComboBox.SelectedIndex = 0;
        await CaptureAsync("en-class1-780x540.png", "class-polling", 780, 540);

        // Search-state screenshots are labeled for the full viewport,
        // not the compact width left from the preceding IEC-101 capture.
        Width = 1080;
        Height = 750;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        SearchBox.Text = "Class 1";
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        if (TopicListBox.Items.Count == 0 ||
            ClearSearchButton.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Search results or clear affordance are missing.");
        SaveRenderedViewport(outputDirectory, "en-search-results-1080x750.png");

        SearchBox.Text = "never-a-real-arsee-help-topic-2026";
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        if (TopicListBox.Items.Count != 0 ||
            NoTopicsText.Visibility != Visibility.Visible ||
            TopicContentPanel.Children.Count != 0 ||
            TopicTitleText.Text != "No matching topics")
            throw new InvalidOperationException("No-results state is inconsistent with search.");
        SaveRenderedViewport(outputDirectory, "en-empty-results-1080x750.png");

        // Simulate contextual F1 navigation while a stale search is active.
        SelectTopic("command-flow");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        if (SearchBox.Text.Length != 0 || TopicListBox.SelectedItem is null ||
            TopicTitleText.Text != "Command flow")
            throw new InvalidOperationException("Contextual Help did not restore navigation.");

        File.WriteAllText(Path.Combine(outputDirectory, "acceptance.json"),
            JsonSerializer.Serialize(new
            {
                result = "passed",
                renderer = "native WPF RenderTargetBitmap",
                dpiQualification = "logical DIP viewports only; operator must review Windows 125% and 150% DPI",
                screenshots = 13,
                requestedWindowSizes = new[] { "default: 1180x820 DIP", "search: 1080x750 DIP", "compact: 780x540 DIP" },
                note = "Windows runner may constrain the requested size; actual measured logical dimensions accompany each screenshot.",
                languages = new[] { "en", "id" },
                cases = new[] { "offline bilingual stepwise ArSee operating guide", "user guide chapter hierarchy", "preconditions, action steps and troubleshooting", "Signal Map field-by-field bilingual procedure at default and compact sizes", "offline bilingual engineering handbook", "IEC protocol badges", "nested subchapter deep navigation", "validated IEC104 trace lab", "IEC101 compact lab", "explanation reveal", "trace", "polling", "filtered search", "empty results", "contextual F1 recovery" },
                measurements = captures
            }, new JsonSerializerOptions { WriteIndented = true }));

        async Task CaptureAsync(string filename, string topic, int width, int height)
        {
            Width = width;
            Height = height;
            SelectTopic(topic);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            UpdateLayout();
            ValidateLayout();
            captures.Add(new
            {
                file = filename,
                topic,
                language = _language,
                logicalViewportWidth = ActualWidth,
                logicalViewportHeight = ActualHeight,
                articleViewport = TopicScrollViewer.ViewportWidth,
                articleExtent = TopicScrollViewer.ExtentWidth,
                topicCount = TopicListBox.Items.Count,
                topicPaneWidth = HelpTopicsPanel.ActualWidth,
                articlePaneWidth = HelpArticlePanel.ActualWidth
            });
            SaveRenderedViewport(outputDirectory, filename);
        }
    }

    private void VerifyOperatingGuideStructure()
    {
        if (!TopicListBox.Items.OfType<HelpTopicListItem>().Any(item =>
                item.Kind == "group" && item.Title.Contains(
                    _language == "id" ? "PANDUAN PENGGUNA" : "USER GUIDE",
                    StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("ArSee user manual is missing its navigation group.");

        var workflows = TopicContentPanel.Children.OfType<Border>()
            .Select(section => section.Child)
            .OfType<StackPanel>()
            .SelectMany(section => section.Children.OfType<Border>())
            .Where(panel => Equals(panel.Tag, "ArSeeUserWorkflow"))
            .ToArray();
        if (workflows.Length < 3)
            throw new InvalidOperationException("Expected stepwise ArSee operating procedures are not rendered.");
        foreach (var panel in workflows)
        {
            if (panel.Child is not StackPanel content)
                throw new InvalidOperationException("Operating procedure panel is empty.");
            var labels = content.Children.OfType<TextBlock>().Select(label => label.Text).ToArray();
            if (!labels.Any(label => label == (_language == "id" ? "LANGKAH-LANGKAH" : "STEP BY STEP")) ||
                !labels.Any(label => label == (_language == "id" ? "HASIL YANG DIHARAPKAN" : "EXPECTED RESULT")) ||
                !labels.Any(label => label == (_language == "id" ? "JIKA BELUM BERHASIL" : "IF IT DOES NOT WORK")) ||
                content.Children.OfType<Grid>().Count() < 3)
                throw new InvalidOperationException("Operating guide steps, results or diagnostics are missing.");
        }
    }

    private void VerifyGuidedLabInteraction()
    {
        // Inspect the actual WPF teaching control, not the JSON model alone.
        var firstLab = TopicContentPanel.Children.OfType<Border>()
            .Select(section => section.Child)
            .OfType<StackPanel>()
            .SelectMany(content => content.Children.OfType<Border>())
            .FirstOrDefault(block => Equals(block.Tag, "GuidedTraceLab"));
        if (firstLab?.Child is not StackPanel content)
            throw new InvalidOperationException("R3 guided trace lesson did not render in the WPF article.");
        var answer = content.Children.OfType<TextBlock>()
            .SingleOrDefault(text => Equals(text.Tag, "HelpLabAnswer"));
        var reveal = content.Children.OfType<Button>()
            .SingleOrDefault(button => Equals(button.Tag, "HelpLabReveal"));
        if (answer is null || reveal is null || answer.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Exercise explanation is not collapsed on first render.");
        reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (answer.Visibility != Visibility.Visible || answer.Text.Length < 24)
            throw new InvalidOperationException("Reveal explanation button did not expose the lesson answer.");
        reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (answer.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Hide explanation button did not restore the initial state.");
    }

    private void ValidateLayout()
    {
        if (!SearchBox.IsVisible || !TopicListBox.IsVisible || !TopicScrollViewer.IsVisible ||
            TopicScrollViewer.ViewportWidth < 240 ||
            TopicScrollViewer.ExtentWidth > TopicScrollViewer.ViewportWidth + 2)
            throw new InvalidOperationException(
                $"Help article exceeds viewport: extent {TopicScrollViewer.ExtentWidth}, viewport {TopicScrollViewer.ViewportWidth}.");

        var actionsLeft = LanguageComboBox.TranslatePoint(new Point(), this).X;
        var titleRight = HelpTitleText.TranslatePoint(new Point(HelpTitleText.ActualWidth, 0), this).X;
        if (titleRight > actionsLeft - 7)
            throw new InvalidOperationException(
                $"Header title overlaps actions: title right {titleRight}, actions left {actionsLeft}.");

        if (ActualWidth >= 950 && HelpArticlePanel.ActualWidth < 2.9 * HelpTopicsPanel.ActualWidth)
            throw new InvalidOperationException("The article is no longer the dominant Help Center panel.");

        if (_selectedTopicKey == "learn-iec-family" &&
            string.IsNullOrWhiteSpace(SearchBox.Text) &&
            FindNavigationScrollViewer(TopicListBox) is ScrollViewer navigation &&
            navigation.VerticalOffset > 12)
            throw new InvalidOperationException(
                $"The handbook index did not open at chapter one: offset {navigation.VerticalOffset:F1}.");

        if (SearchGlyphPath.Data is null ||
            TopicListBox.Items.OfType<HelpTopicListItem>().Any(item => item.IconGeometry is null))
            throw new InvalidOperationException("Outlined Material icon geometry is missing.");

        // Measure actual WPF control coordinates, not XAML intent. In the same
        // 36-DIP SearchChrome row, icon, editable text and watermark centers
        // must agree even at minimum logical window size.
        var iconCenter = SearchGlyphPath.TranslatePoint(
            new Point(SearchGlyphPath.ActualWidth / 2, SearchGlyphPath.ActualHeight / 2),
            SearchChrome).Y;
        var inputCenter = SearchBox.TranslatePoint(
            new Point(SearchBox.ActualWidth / 2, SearchBox.ActualHeight / 2),
            SearchChrome).Y;
        if (Math.Abs(iconCenter - inputCenter) > 1.5)
            throw new InvalidOperationException(
                $"Search icon/input center mismatch: {iconCenter:F1} vs {inputCenter:F1} DIP.");
        if (SearchWatermarkText.IsVisible)
        {
            var watermarkCenter = SearchWatermarkText.TranslatePoint(
                new Point(SearchWatermarkText.ActualWidth / 2,
                          SearchWatermarkText.ActualHeight / 2), SearchChrome).Y;
            if (Math.Abs(iconCenter - watermarkCenter) > 1.5)
                throw new InvalidOperationException(
                    $"Search icon/placeholder center mismatch: {iconCenter:F1} vs {watermarkCenter:F1} DIP.");
        }

        if (SearchChrome.ActualHeight < 35)
            throw new InvalidOperationException("Search field was rendered below its 36-DIP rhythm.");

        if (TopicListBox.Items.Count == 0 || TopicContentPanel.Children.Count < 2)
            throw new InvalidOperationException("Selected article did not render section content.");
    }

    private void SaveRenderedViewport(string directory, string filename)
    {
        UpdateLayout();
        // Render the actual WPF client visual. Window.ActualHeight also includes
        // non-client chrome not drawn by RenderTargetBitmap; rendering Window itself
        // previously left a black/transparent strip at the bottom of evidence PNGs.
        if (Content is not FrameworkElement viewport)
            throw new InvalidOperationException("Help Center client root is missing.");
        viewport.UpdateLayout();
        var renderWidth = Math.Max(1, (int)Math.Ceiling(viewport.ActualWidth));
        var renderHeight = Math.Max(1, (int)Math.Ceiling(viewport.ActualHeight));
        var bitmap = new RenderTargetBitmap(renderWidth, renderHeight,
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(viewport);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, filename));
        encoder.Save(stream);
    }
}
