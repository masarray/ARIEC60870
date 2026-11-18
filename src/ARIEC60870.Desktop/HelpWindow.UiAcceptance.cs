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
        var captures = new List<object>();

        await CaptureAsync("en-trace-1080x750.png", "frame-trace", 1080, 750);
        await CaptureAsync("en-trace-780x540.png", "frame-trace", 780, 540);

        LanguageComboBox.SelectedIndex = 1;
        await CaptureAsync("id-trace-780x540.png", "frame-trace", 780, 540);
        if (TopicTitleText.Text != "Membaca frame trace")
            throw new InvalidOperationException("Indonesian Help Center translation did not render.");

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
                screenshots = 6,
                viewports = new[] { "1080x750", "780x540" },
                languages = new[] { "en", "id" },
                cases = new[] { "trace", "polling", "filtered search", "empty results", "contextual F1 recovery" },
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
                topicCount = TopicListBox.Items.Count
            });
            SaveRenderedViewport(outputDirectory, filename);
        }
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
