// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

            // New daily mapping workspace: full engineering details are retained
            // but the normal 1360px grid does not require horizontal scavenging.
            if (editor._grid.Columns.Count != 18 ||
                editor._grid.Columns.Count(col => col.Visibility == Visibility.Visible) != 8 ||
                editor._advancedColumns.Count != 10)
                throw new InvalidOperationException(
                    "Essential Signal Map grid must present eight identity columns and retain all advanced fields.");
            editor.ToggleExtraColumns();
            if (editor._grid.Columns.Any(col => col.Visibility != Visibility.Visible))
                throw new InvalidOperationException("Advanced Signal Map columns did not become visible.");
            editor.ToggleExtraColumns();

            var view = CollectionViewSource.GetDefaultView(editor._rows) ??
                throw new InvalidOperationException("Signal Map collection view was not constructed.");
            editor._searchBox.Text = "790449";
            if (view.Cast<SignalListEditorRow>().Count() != 1 ||
                !ReferenceEquals(view.Cast<SignalListEditorRow>().Single(), normalized))
                throw new InvalidOperationException("IOA quick search did not locate the exact TI9 row.");
            editor._searchBox.Text = "TRAFO SOGI POAQ";
            if (!view.Cast<SignalListEditorRow>().Contains(normalized))
                throw new InvalidOperationException("Multi-term signal search did not match bay and mnemonic.");
            editor._searchBox.Clear();
            editor._bayFilter.SelectedItem = "FEEDER";
            if (!view.Cast<SignalListEditorRow>().Any() ||
                view.Cast<SignalListEditorRow>().Any(row =>
                    !string.Equals(row.BayType, "FEEDER", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Bay filter leaked signals from other bays.");
            editor._kindFilter.SelectedItem = "Measured analog";
            if (!view.Cast<SignalListEditorRow>().Any() ||
                view.Cast<SignalListEditorRow>().Any(row => !IsMeasuredAnalog(row)))
                throw new InvalidOperationException("Measured signal filter returned non-measurement rows.");
            editor.ClearFilters();
            if (view.Cast<SignalListEditorRow>().Count() != editor._rows.Count)
                throw new InvalidOperationException("Clear filters failed to restore the full signal list.");

            var name = ComposeSignalName(new SignalListEditorRow
            {
                BayType = "TRAFO SOGI",
                TestCategory = "TM",
                Mnemonic = "POAQ",
                Name = "Legacy point 2"
            });
            if (name != "TRAFO SOGI TM POAQ 2" ||
                ComposeSignalName(new SignalListEditorRow { BayType = "TRAFO SOGI" }) is not null ||
                editor._dirty)
                throw new InvalidOperationException(
                    "Name suggestion must be explicit, require three labels, preserve optional # and not dirty navigation.");
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
