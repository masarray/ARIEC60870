// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ARIEC60870.Core.Mapping;

namespace ARIEC60870.Desktop;

/// <summary>
/// Actual Windows WPF acceptance for the first-visible analog scaling UI.
/// Runs with the existing passive GUI smoke: no COM, TCP, RTU, or settings write.
/// This is not evidence of external RTU engineering calibration.
/// </summary>
public sealed partial class SignalListEditorWindow
{
    public static async Task VerifyScalingFirstViewportAsync()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "profiles",
            "utility_fat_iec10x_default_profile.json");
        var profile = Iec10xPointMappingProfile.LoadFromFile(bundled);
        var editor = new SignalListEditorWindow(profile, bundled)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Width = 1360,
            Height = 820
        };
        try
        {
            editor.Show();
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);

            var normalized = editor._rows.Single(r => r.Ca == "105" &&
                r.Ioa == "790449" && r.TypeId == "9");
            editor.SetCurrentCell(normalized);
            editor.UpdateDetailSelection();
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            editor.UpdateLayout();

            if (!ReferenceEquals(editor._activeDetailRow, normalized) ||
                editor._scalingScroll.Visibility != Visibility.Visible ||
                editor._detailScroll.Visibility != Visibility.Collapsed ||
                !editor._scalingTabButton.IsEnabled ||
                editor._grid.FrozenColumnCount != 4 ||
                normalized.ScalingMode != "TwoPoint")
                throw new InvalidOperationException(
                    "TI9 POAQ must open the scaling page with the identity columns frozen.");

            foreach (var label in new[]
            {
                "X0 · source low", "X100 · source high",
                "Y0 · output low", "Y100 · output high"
            })
                AssertInputAboveFold(editor._scalingScroll, label);

            var q = editor._rows.Single(r => r.Ca == "105" &&
                r.Ioa == "790447" && r.TypeId == "11");
            editor.SetCurrentCell(q);
            editor.UpdateDetailSelection();
            if (editor._scalingScroll.Visibility != Visibility.Visible ||
                !editor._scalingSourceNote.Text.Contains("signed I16", StringComparison.Ordinal) ||
                !editor._scalingSourceNote.Text.Contains("MVar", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "TI11 reactive power must expose scaling and warn against guessed MVar.");

            var digital = editor._rows.First(r => r.TypeId == "30");
            editor.SetCurrentCell(digital);
            editor.UpdateDetailSelection();
            if (editor._detailScroll.Visibility != Visibility.Visible ||
                editor._scalingScroll.Visibility != Visibility.Collapsed ||
                editor._scalingTabButton.IsEnabled)
                throw new InvalidOperationException(
                    "Digital/command signals must default to Mapping & Commands.");

            // Changing the selected row must not mutate field calibration.
            if (normalized.X0 != "0" || normalized.X100 != "1" ||
                normalized.Y0 != "0" || normalized.Y100 != "1000" ||
                q.Unit != "counts" || editor._dirty)
                throw new InvalidOperationException(
                    "Inspector navigation changed source-defined scaling or dirtied the profile.");
        }
        finally
        {
            editor.Close();
        }
    }

    private static void AssertInputAboveFold(ScrollViewer viewer, string label)
    {
        var heading = FindLabel(viewer, label) ??
            throw new InvalidOperationException("Missing TwoPoint input: " + label);
        if (heading.Parent is not FrameworkElement field)
            throw new InvalidOperationException("Missing actual edit field for " + label);

        var bounds = field.TransformToAncestor(viewer)
            .TransformBounds(new Rect(0, 0, field.ActualWidth, field.ActualHeight));
        if (viewer.ViewportHeight <= 0 ||
            bounds.Top < -1 || bounds.Bottom > viewer.ViewportHeight + 1 ||
            field.ActualHeight < 30)
            throw new InvalidOperationException(
                $"{label} is below the fold: y={bounds.Top:0.#}..{bounds.Bottom:0.#}, " +
                $"viewport={viewer.ViewportHeight:0.#}. Scaling endpoints must be visible without scrolling.");
    }

    private static TextBlock? FindLabel(DependencyObject root, string label)
    {
        if (root is TextBlock block && block.Text == label)
            return block;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = FindLabel(VisualTreeHelper.GetChild(root, i), label);
            if (child is not null) return child;
        }
        return null;
    }
}
