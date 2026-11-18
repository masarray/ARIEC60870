// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ARIEC60870.Core.Mapping;

namespace ARIEC60870.Desktop;

public sealed partial class SignalListEditorWindow
{
    // An optional explicit action under More. The two Q tables do not identify
    // their CA/IOA association; do not pick one on the operator's behalf.
    private MenuItem CreateScadaReferenceRangesMenu()
    {
        var root = new MenuItem
        {
            Header = "SCADA reference range for selected signal...",
            FontSize = 12,
            ToolTip = "Preview one unverified SCADA range. Nothing is applied automatically."
        };
        foreach (var (id, caption) in new[]
        {
            (Iec10xScadaRangeId.ReactiveDiriSendiri, "Q Diri Sendiri · ±2078"),
            (Iec10xScadaRangeId.ReactiveLab, "Q Lab · ±260"),
            (Iec10xScadaRangeId.PoaqFeedback, "POAQ feedback · 0–1200")
        })
        {
            var item = new MenuItem { Header = caption, FontSize = 12 };
            item.Click += (_, _) => ApplyScadaReferenceRange(id);
            root.Items.Add(item);
        }
        root.SubmenuOpened += (_, _) =>
        {
            Iec10xPointMappingEntry? point = null;
            try { point = GetCurrentRow()?.ToPoint(); }
            catch (Exception) { /* Invalid source: refuse any template. */ }
            foreach (var item in root.Items.OfType<MenuItem>())
            {
                if (item == root.Items[0])
                    item.IsEnabled = point is not null &&
                        Iec10xScadaRangeReferences.TryForPoint(point,
                            Iec10xScadaRangeId.ReactiveDiriSendiri, out _);
                else if (item == root.Items[1])
                    item.IsEnabled = point is not null &&
                        Iec10xScadaRangeReferences.TryForPoint(point,
                            Iec10xScadaRangeId.ReactiveLab, out _);
                else
                    item.IsEnabled = point is not null &&
                        Iec10xScadaRangeReferences.TryForPoint(point,
                            Iec10xScadaRangeId.PoaqFeedback, out _);
            }
        };
        return root;
    }

    private static IReadOnlyList<SignalGridCellChange> PlanScadaReferenceChanges(
        SignalListEditorRow row, Iec10xScadaRangeReference range)
    {
        static string Number(double value) =>
            value.ToString("G17", CultureInfo.InvariantCulture);

        var fields = new (string Field, string Value)[]
        {
            (nameof(SignalListEditorRow.ScalingMode), "TwoPoint"),
            (nameof(SignalListEditorRow.X0), Number(range.X0)),
            (nameof(SignalListEditorRow.X100), Number(range.X100)),
            (nameof(SignalListEditorRow.Y0), Number(range.Y0)),
            (nameof(SignalListEditorRow.Y100), Number(range.Y100)),
            (nameof(SignalListEditorRow.Unit), range.Unit),
            (nameof(SignalListEditorRow.EngineeringMin), Number(range.Y0)),
            (nameof(SignalListEditorRow.EngineeringMax), Number(range.Y100))
        };
        return fields
            .Where(x => SignalGridEditModel.Read(row, x.Field) != x.Value)
            .Select(x => new SignalGridCellChange(row, x.Field,
                SignalGridEditModel.Read(row, x.Field), x.Value))
            .ToArray();
    }

    private void ApplyScadaReferenceRange(Iec10xScadaRangeId referenceId)
    {
        try
        {
            var selected = GetCurrentRow() ??
                throw new InvalidOperationException("Select one signal in the grid.");
            var original = selected.ToPoint();
            if (!Iec10xScadaRangeReferences.TryForPoint(original,
                referenceId, out var range) || range is null)
                throw new InvalidOperationException(
                    "This reference requires CA 105 and the exact Q TI11 " +
                    "or POAQ TI9 IOA. Command points cannot be calibrated this way.");

            var changes = PlanScadaReferenceChanges(selected, range);
            if (changes.Count == 0)
            {
                UpdateStatus("Selected signal already has these reference endpoints.", success: true);
                return;
            }

            var note = referenceId == Iec10xScadaRangeId.PoaqFeedback
                ? "POAQ is incoming TI9 feedback, 0–1200 by later operator correction. " +
                  "Outgoing TI48 POOP command remains 0–1000. " +
                  "X100=1 is the conceptual IEC normalized boundary."
                : "The SCADA note lists Q Diri Sendiri and Q Lab separately, " +
                  "but does NOT identify which one belongs to this IOA. " +
                  "The MVar label and decoded SVA-to-SCADA raw equivalence " +
                  "must be verified on the actual RTU.";

            var prompt =
                $"Selected: {selected.Name}\nCA {selected.Ca} · IOA {selected.Ioa} · TI {selected.TypeId}\n" +
                $"Reference: {range.Label}\n\n" +
                $"X: {NumberForPreview(range.X0)} → {NumberForPreview(range.X100)}\n" +
                $"Y: {NumberForPreview(range.Y0)} → {NumberForPreview(range.Y100)} {range.Unit}\n\n" +
                note + "\n\n" +
                "Only this signal's display scaling is staged; raw HEX, " +
                "IEC quality, other IOAs and command encoding remain unchanged. " +
                "Undo (Ctrl+Z) is available. Save As a NEW project profile " +
                "after comparing with eTerra.\n\n" +
                "Apply this unverified reference to the selected signal?";
            if (ModernMessageBox.Show(this, prompt,
                "SCADA reference scaling · explicit selection",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            ApplyNewGridTransaction(changes, range.Label, warnOnBulk: false);

            // The selected SCADA range is reference-only until field FAT. A
            // new calibration must not overwrite the original station profile.
            // Reuse the existing Sync protection gate in SaveCurrent; after the
            // first Save As to a distinct file, further edits can use that copy.
            if (_protectedSyncSourcePath is null &&
                !string.IsNullOrWhiteSpace(SavedProfilePath))
            {
                _protectedSyncSourcePath = SavedProfilePath;
                SavedProfilePath = string.Empty;
                _pathBox.Text = "New copy required — choose Save As";
                _profilePathLabel.Text = "Unsaved profile copy";
                _profilePathLabel.ToolTip = _protectedSyncSourcePath;
                _profileNameBox.ToolTip = "Original preserved: " + _protectedSyncSourcePath;
                _saved = false;
                UpdateStatus("SCADA reference staged. Save As a new profile; original is protected.", success: true);
            }

            SetInspectorVisible(true);
            ShowInspectorTab(scaling: true);
        }
        catch (Exception ex)
        {
            UpdateStatus("SCADA range not applied: " + ex.Message, danger: true);
        }
    }

    private static string NumberForPreview(double x) =>
        x.ToString("G12", CultureInfo.InvariantCulture);
}
