// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            editor.UpdateLayout();

            // Data-first default: no permanently pinned 405px inspector, no
            // training subtitle, no double-card toolbar or large footer.
            if (editor._inspectorOpen ||
                editor._inspectorColumn.Width.Value != 0 ||
                editor._inspectorPanel?.Visibility != Visibility.Collapsed ||
                !editor._grid.Columns[3].Width.IsStar ||
                editor._grid.ActualWidth < editor.ActualWidth * 0.87 ||
                editor._layoutRoot.RowDefinitions[0].ActualHeight > 48 ||
                editor._layoutRoot.RowDefinitions[1].ActualHeight > 55 ||
                editor._layoutRoot.RowDefinitions[3].ActualHeight > 32)
                throw new InvalidOperationException(
                    "Signal Map must open as a grid-first compact workspace with inspector hidden.");
            // Top toolbar must reuse the exact Main Window pill/circle/token
            // objects. A merely similar custom template is a design regression.
            var workspaceStyle = (Style)Application.Current.FindResource("ArSeeWorkspaceActionButtonStyle");
            var primaryStyle = (Style)Application.Current.FindResource("ArSeeWorkspacePrimaryActionButtonStyle");
            var circleStyle = (Style)Application.Current.FindResource("ArSeeToolbarFullHeightCircleStyle");
            var clearStyle = (Style)Application.Current.FindResource("ArSeeToolbarSearchClearStyle");
            var materialPathStyle = (Style)Application.Current.FindResource("MaterialToolbarIconPath");
            var addAction = FindButtonByName(editor._layoutRoot, "SignalMapAddButton");
            var saveAction = FindButtonByName(editor._layoutRoot, "SignalMapSaveButton");
            var applyAction = FindButtonByName(editor._layoutRoot, "SignalMapSaveApplyButton");
            var moreAction = FindButtonByName(editor._layoutRoot, "SignalMapMoreButton");
            if (addAction is null || saveAction is null || applyAction is null || moreAction is null ||
                !ReferenceEquals(addAction.Style, workspaceStyle) ||
                !ReferenceEquals(saveAction.Style, workspaceStyle) ||
                !ReferenceEquals(applyAction.Style, primaryStyle) ||
                !ReferenceEquals(editor._detailsButton.Style, workspaceStyle) ||
                !ReferenceEquals(moreAction.Style, circleStyle) ||
                !ReferenceEquals(editor._resetFiltersButton.Style, clearStyle) ||
                editor._resetFiltersButton.Visibility != Visibility.Collapsed ||
                moreAction.Width != (double)Application.Current.FindResource("ArSeeToolbarPillHeight") ||
                moreAction.Height != addAction.Height ||
                moreAction.Height != saveAction.Height ||
                moreAction.Height != applyAction.Height)
                throw new InvalidOperationException(
                    "Signal Map More circle must match the pill height and use the shared theme token.");
            AssertMaterialIconText(addAction, "MaterialAdd", materialPathStyle);
            AssertMaterialIconText(saveAction, "MaterialSave", materialPathStyle);
            AssertMaterialIconText(applyAction, "MaterialCheckCircle", materialPathStyle);
            AssertMaterialIconText(editor._detailsButton, "MaterialViewSidebar", materialPathStyle);
            AssertMaterialIconOnly(moreAction, "MaterialMoreVert", materialPathStyle);
            AssertMaterialIconOnly(editor._resetFiltersButton, "MaterialClose", materialPathStyle);
            if (System.Windows.Automation.AutomationProperties.GetName(moreAction) != "More actions" ||
                System.Windows.Automation.AutomationProperties.GetName(editor._resetFiltersButton) !=
                "Clear search and filters" || moreAction.ContextMenu is null)
                throw new InvalidOperationException(
                    "Icon-only toolbar actions need accessible names and a working More menu.");

            // Exercise actual button Click, not only the internal show/hide method.
            editor._detailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!editor._inspectorOpen ||
                !ReferenceEquals(editor._detailsButton.Style, primaryStyle))
                throw new InvalidOperationException("Details icon/text click did not open the inspector.");
            editor._detailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (editor._inspectorOpen ||
                !ReferenceEquals(editor._detailsButton.Style, workspaceStyle))
                throw new InvalidOperationException("Details icon/text click did not restore grid-first mode.");
            // The two top rows use optical vertical centering instead of
            // independent offsets for search, filters and action buttons.
            AssertAlignedControlCenters(editor._searchBox, editor._bayFilter,
                editor._kindFilter, editor._detailsButton, moreAction, addAction, saveAction, applyAction);
            if (editor._profileNameBox.VerticalContentAlignment != VerticalAlignment.Center ||
                editor._searchBox.VerticalContentAlignment != VerticalAlignment.Center)
                throw new InvalidOperationException(
                    "Top metadata and search input must center their editable text.");
            CaptureUiReviewImage(editor, "00-grid-first-1360x820");
            editor.SetInspectorVisible(true);
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            editor.UpdateLayout();
            if (!editor._inspectorOpen ||
                editor._inspectorColumn.ActualWidth < 280 ||
                editor._inspectorSplitter?.Visibility != Visibility.Visible ||
                editor._grid.Columns[3].Width.IsStar ||
                editor._grid.Columns[3].ActualWidth > 210)
                throw new InvalidOperationException(
                    "Engineering inspector did not open as a resizable side panel.");

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

            // Inspector tabs must reuse the exact shared Main Window toggle
            // pill style and locally bundled Google Material filled geometry.
            var toggleStyle = (Style)Application.Current.FindResource("EventJournalActionToggleStyle");
            if (!ReferenceEquals(editor._scalingTabButton.Style, toggleStyle) ||
                !ReferenceEquals(editor._mappingTabButton.Style, toggleStyle) ||
                editor._scalingTabButton.IsChecked != true ||
                editor._mappingTabButton.IsChecked != false)
                throw new InvalidOperationException(
                    "Scaling/Mapping must be mutually exclusive shared-theme toggle pills.");
            AssertMaterialToggle(editor._scalingTabButton, "MaterialTune", materialPathStyle);
            AssertMaterialToggle(editor._mappingTabButton, "MaterialDeviceHub", materialPathStyle);
            AssertAlignedControlCenters(editor._scalingTabButton, editor._mappingTabButton);
            AssertInspectorField(editor._scalingScroll, "Unit");
            AssertInspectorField(editor._scalingScroll, "Scaling mode");
            AssertInspectorField(editor._scalingScroll, "X0 · source low");
            AssertInspectorField(editor._scalingScroll, "X100 · source high");
            AssertInspectorField(editor._scalingScroll, "Y0 · output low");
            AssertInspectorField(editor._scalingScroll, "Y100 · output high");
            if (editor._selectionTitle.VerticalAlignment != VerticalAlignment.Center ||
                editor._selectionSubtitle.VerticalAlignment != VerticalAlignment.Center)
                throw new InvalidOperationException("Inspector identity text is not vertically centered.");

            // WPF Click route must switch the shared toggle states without
            // modifying or dirtying the selected signal.
            editor._mappingTabButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (editor._mappingTabButton.IsChecked != true ||
                editor._scalingTabButton.IsChecked != false ||
                editor._detailScroll.Visibility != Visibility.Visible || editor._dirty)
                throw new InvalidOperationException("Mapping tab did not select cleanly on actual Click.");
            editor._scalingTabButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (editor._scalingTabButton.IsChecked != true ||
                editor._mappingTabButton.IsChecked != false ||
                editor._scalingScroll.Visibility != Visibility.Visible || editor._dirty)
                throw new InvalidOperationException("Scaling tab did not restore cleanly on actual Click.");
            editor.UpdateLayout();

            foreach (var label in new[]
            {
                "X0 · source low", "X100 · source high",
                "Y0 · output low", "Y100 · output high"
            })
                AssertInputAboveFold(editor._scalingScroll, label);
            CaptureUiReviewImage(editor, "01-analog-scaling-1360x820");

            var q = editor._rows.Single(r => r.Ca == "105" &&
                r.Ioa == "790447" && r.TypeId == "11");
            editor.SetCurrentCell(q);
            editor.UpdateDetailSelection();
            if (editor._scalingScroll.Visibility != Visibility.Visible ||
                !editor._scalingSourceNote.Text.Contains("signed I16", StringComparison.Ordinal) ||
                !editor._scalingSourceNote.Text.Contains("MVar", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "TI11 reactive power must expose source-aware TwoPoint scaling and actual RTU FAT caveat.");

            var digital = editor._rows.First(r => r.TypeId == "30");
            editor.SetCurrentCell(digital);
            editor.UpdateDetailSelection();
            if (editor._detailScroll.Visibility != Visibility.Visible ||
                editor._scalingScroll.Visibility != Visibility.Collapsed ||
                editor._scalingTabButton.IsEnabled)
                throw new InvalidOperationException(
                    "Digital/command signals must default to Mapping & Commands.");
            editor.UpdateLayout();
            if (editor._mappingTabButton.IsChecked != true ||
                editor._scalingTabButton.IsChecked != false)
                throw new InvalidOperationException(
                    "Selecting a digital point must select the Mapping pill.");
            AssertInspectorField(editor._detailScroll, "Common address (CA)");
            AssertInspectorField(editor._detailScroll, "Information object (IOA)");
            AssertInspectorField(editor._detailScroll, "Signal name");
            var generateName = FindButtonByName(editor._layoutRoot, "SignalMapGenerateNameButton");
            var validateSignal = FindButtonByName(editor._layoutRoot, "SignalMapValidateSignalButton");
            if (generateName is null || validateSignal is null ||
                !ReferenceEquals(generateName.Style, workspaceStyle) ||
                !ReferenceEquals(validateSignal.Style, workspaceStyle))
                throw new InvalidOperationException(
                    "Mapping inspector actions must use the Main Window shared button style.");
            AssertMaterialIconText(generateName, "MaterialAutoFixHigh", materialPathStyle);
            AssertMaterialIconText(validateSignal, "MaterialCheckCircle", materialPathStyle);
            // Dropdown descriptions must not become actual profile values or
            // silently rewrite a paired TI/role when another choice is selected.
            var tiCombo = AssertChoiceField(editor._detailScroll,
                "IEC Type ID (TI)", "30", "31");
            var roleCombo = AssertChoiceField(editor._detailScroll,
                "IEC Type / role", digital.SignalType, "M_DP_TB_1");
            AssertChoiceField(editor._detailScroll, "Class", digital.ExpectedClass, "1");
            AssertChoiceField(editor._detailScroll, "COT", digital.ExpectedCot, "3");
            AssertChoiceField(editor._detailScroll, "Command policy", digital.CommandPolicy, "MonitorOnly");
            AssertChoiceField(editor._detailScroll, "TYPE (TM / TSD...)", digital.TestCategory, "TSD");
            if (tiCombo.Text != digital.TypeId || roleCombo.Text != digital.SignalType ||
                !tiCombo.IsEditable || !roleCombo.IsEditable || editor._dirty)
                throw new InvalidOperationException(
                    "Choice fields must show exact raw codes and remain losslessly editable.");
            CaptureUiReviewImage(editor, "02-digital-mapping-1360x820");

            var oldTi = digital.TypeId;
            var oldRole = digital.SignalType;
            var differentTi = tiCombo.Items.OfType<ComboBoxItem>()
                .First(x => TextSearch.GetText(x) == "31");
            tiCombo.SelectedItem = differentTi;
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (digital.TypeId != "31" || digital.SignalType != oldRole)
                throw new InvalidOperationException(
                    "Selecting TI 31 must write numeric 31 only, without modifying IEC role.");
            tiCombo.SelectedIndex = -1;
            tiCombo.Text = oldTi;
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            editor.RecalculateGridDirty();
            if (digital.TypeId != oldTi || digital.SignalType != oldRole || editor._dirty)
                throw new InvalidOperationException(
                    "Reverting IEC TI choice must restore the original clean profile.");

            // Unknown project/vendor value must survive row navigation unchanged.
            var customRole = "M_CUSTOM_PROJECT";
            roleCombo.Text = customRole;
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (digital.SignalType != customRole)
                throw new InvalidOperationException(
                    "Editable choice dropdown failed to preserve custom IEC role.");
            roleCombo.Text = oldRole;
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            editor.RecalculateGridDirty();
            if (digital.SignalType != oldRole || editor._dirty)
                throw new InvalidOperationException(
                    "Restoring original role must leave mapping clean.");

            // Changing the selected row must not mutate field calibration.
            if (normalized.X0 != "0" || normalized.X100 != "1" ||
                normalized.Y0 != "0" || normalized.Y100 != "1200" ||
                q.Unit != "MVar" || q.ScalingMode != "TwoPoint" ||
                q.X0 != "-32768" || q.X100 != "32767" ||
                q.Y0 != "-2078" || q.Y100 != "2078" || editor._dirty)
                throw new InvalidOperationException(
                    "Inspector navigation changed operator-mapped source scaling or dirtied the profile.");

            // Check both distinct Lab SOGI bays in the actual packaged WPF
            // Signal Map, not only the core JSON serializer.
            foreach (var ioa in new[] { "790439", "790443" })
            {
                var labQ = editor._rows.Single(r => r.Ca == "105" &&
                    r.Ioa == ioa && r.TypeId == "11");
                if (labQ.Unit != "MVar" || labQ.ScalingMode != "TwoPoint" ||
                    labQ.X0 != "-32768" || labQ.X100 != "32767" ||
                    labQ.Y0 != "-260" || labQ.Y100 != "260")
                    throw new InvalidOperationException(
                        "Operator Lab SOGI 3/4 Q reference missing for IOA " + ioa);
            }

            // New daily mapping workspace: full engineering details are retained
            // but the normal 1360px grid does not require horizontal scavenging.
            if (editor._grid.Columns.Count != 18 ||
                editor._grid.Columns.Count(col => col.Visibility == Visibility.Visible) != 8 ||
                editor._advancedColumns.Count != 10 ||
                editor._grid.Columns[3].Width.IsStar)
                throw new InvalidOperationException(
                    "Essential Signal Map grid must present eight identity columns and retain all advanced fields.");
            editor.ToggleExtraColumns();
            if (editor._grid.Columns.Any(col => col.Visibility != Visibility.Visible))
                throw new InvalidOperationException("Advanced Signal Map columns did not become visible.");
            editor.ToggleExtraColumns();

            var view = CollectionViewSource.GetDefaultView(editor._rows) ??
                throw new InvalidOperationException("Signal Map collection view was not constructed.");
            editor._searchBox.Text = "790449";
            if (editor._resetFiltersButton.Visibility != Visibility.Visible)
                throw new InvalidOperationException(
                    "Material clear-filter button should appear only for active search/filter state.");
            var quickMatches = view.Cast<SignalListEditorRow>().ToArray();
            if (quickMatches.Length != 1 || !ReferenceEquals(quickMatches[0], normalized))
                throw new InvalidOperationException(
                    $"IOA quick search: expected 790449 TI9 only; got {quickMatches.Length}. " +
                    $"Bay filter='{editor._bayFilter.SelectedItem}', kind='{editor._kindFilter.SelectedItem}', " +
                    $"query='{editor._searchBox.Text}', visible IOAs=[{string.Join(", ", quickMatches.Select(x => x.Ioa + "/" + x.TypeId))}].");
            editor.SetCurrentCell(normalized);
            editor.UpdateLayout();
            CaptureUiReviewImage(editor, "03-ioa-find-1360x820");
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
            if (editor._resetFiltersButton.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Clear-filter icon did not disappear after reset.");
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

            // Actual Windows WPF grid contract plus deterministic clipboard
            // planning. This never touches the engineer's system clipboard or
            // brings up a modal confirmation during GUI smoke.
            if (editor._grid.SelectionMode != DataGridSelectionMode.Extended ||
                editor._grid.ClipboardCopyMode != DataGridClipboardCopyMode.ExcludeHeader ||
                editor._grid.ContextMenu is null)
                throw new InvalidOperationException(
                    "Signal Map must expose native multi-cell selection and clipboard actions.");

            var quoted = SignalGridEditModel.ParseTsv("A\t\"B\tC\"\r\nD\t\"line1\nline2\"\r\n");
            if (quoted.Count != 2 || quoted[0].Count != 2 ||
                quoted[0][1] != "B\tC" || quoted[1][1] != "line1\nline2")
                throw new InvalidOperationException("Excel quoted TSV parsing is lossy.");

            var expectedCount = editor._rows.Count;
            var originalName = normalized.Name;
            var originalQName = q.Name;
            var combined = SignalGridEditModel.PlanPaste(
                [normalized, q], [nameof(SignalListEditorRow.Name)],
                0, 0, "New name 1\r\nNew name 2\r\n");
            if (combined.Count != 2 || normalized.Name != originalName || q.Name != originalQName)
                throw new InvalidOperationException("Clipboard planning mutated live values.");
            editor.ApplyNewGridTransaction(combined, "WPF test paste", warnOnBulk: false);
            if (normalized.Name != "New name 1" || q.Name != "New name 2" ||
                editor._rows.Count != expectedCount || !editor._dirty)
                throw new InvalidOperationException("Atomic 2-row edit failed or did not set Unsaved.");
            editor.UndoGridAction();
            if (normalized.Name != originalName || q.Name != originalQName || editor._dirty)
                throw new InvalidOperationException("Undo must restore both names and original Clean state.");
            editor.RedoGridAction();
            if (normalized.Name != "New name 1" || q.Name != "New name 2" || !editor._dirty)
                throw new InvalidOperationException("Redo did not restore both fields.");
            editor.UndoGridAction();

            var down = SignalGridEditModel.PlanFillDown(
                [normalized, q], [nameof(SignalListEditorRow.Name)],
                new (int Row, int Column)[] { (0, 0), (1, 0) });
            if (down.Count != 1 || down[0].After != normalized.Name ||
                q.Name != originalQName)
                throw new InvalidOperationException("Fill Down must stage the top-cell value, not mutate it.");
            var nonRectangularRejected = false;
            try
            {
                SignalGridEditModel.PlanFillDown([normalized, q],
                    [nameof(SignalListEditorRow.Name), nameof(SignalListEditorRow.Ioa)],
                    new (int Row, int Column)[] { (0, 0), (1, 1) });
            }
            catch (InvalidOperationException) { nonRectangularRejected = true; }
            if (!nonRectangularRejected)
                throw new InvalidOperationException("Sparse Fill Down selection was not rejected.");

            var badIoa = SignalGridEditModel.PlanPaste(
                [normalized], [nameof(SignalListEditorRow.Ioa)],
                0, 0, "invalid-ioa");
            var badRejected = false;
            try { editor.ValidateGridTransaction(badIoa); }
            catch (InvalidOperationException) { badRejected = true; }
            if (!badRejected || normalized.Ioa != "790449" || editor._dirty)
                throw new InvalidOperationException("Invalid IOA staging must reject without partial writes.");

            var clippedRejected = false;
            try
            {
                SignalGridEditModel.PlanPaste([normalized],
                    [nameof(SignalListEditorRow.Name)], 0, 0, "First\nSecond");
            }
            catch (InvalidOperationException) { clippedRejected = true; }
            if (!clippedRejected || editor._rows.Count != expectedCount || editor._dirty)
                throw new InvalidOperationException("Out-of-bounds clipboard paste must be atomic.");

            var savedName = normalized.Name;
            normalized.BeginEdit();
            normalized.Name = "typing that will be cancelled";
            normalized.CancelEdit();
            editor.RecalculateGridDirty();
            if (normalized.Name != savedName || editor._dirty)
                throw new InvalidOperationException(
                    "Esc/IEditableObject must roll back a live binding and restore Clean state.");

            // Exercise the real DataGrid target resolution and edit lifecycle,
            // not merely the clipboard planner's pure model.
            editor.ClearFilters();
            editor.SetCurrentCell(normalized, 3); // Signal name in visible order
            editor.PasteGridText("WPF visible cell paste");
            if (normalized.Name != "WPF visible cell paste" || !editor._dirty)
                throw new InvalidOperationException(
                    "Real WPF current-cell paste did not update the intended Name column.");
            editor.UndoGridAction();
            if (normalized.Name != savedName || editor._dirty)
                throw new InvalidOperationException("WPF current-cell paste was not undoable.");

            editor.SetCurrentCell(normalized, 3);
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (!editor._grid.BeginEdit())
                throw new InvalidOperationException("F2-style native DataGrid BeginEdit was refused.");
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            var editBox = editor._grid.Columns[3].GetCellContent(normalized) as TextBox;
            if (editBox is null)
                throw new InvalidOperationException("Native WPF Name cell did not expose its TextBox editor.");
            editBox.Text = "this native edit must cancel";
            BindingOperations.GetBindingExpression(editBox, TextBox.TextProperty)?.UpdateSource();
            editor._grid.CancelEdit(DataGridEditingUnit.Cell);
            editor._grid.CancelEdit(DataGridEditingUnit.Row);
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            editor.RecalculateGridDirty();
            if (normalized.Name != savedName || editor._dirty)
                throw new InvalidOperationException("Native WPF CancelEdit leaked typed data to the row.");

            editor.SetCurrentCell(normalized, 3);
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (!editor._grid.BeginEdit())
                throw new InvalidOperationException("Second native WPF BeginEdit was refused.");
            var committedBox = editor._grid.Columns[3].GetCellContent(normalized) as TextBox;
            if (committedBox is null)
                throw new InvalidOperationException("Native WPF edit control was not restored.");
            committedBox.Text = "Native WPF committed";
            BindingOperations.GetBindingExpression(committedBox, TextBox.TextProperty)?.UpdateSource();
            if (!editor._grid.CommitEdit(DataGridEditingUnit.Cell, true) ||
                !editor._grid.CommitEdit(DataGridEditingUnit.Row, true))
                throw new InvalidOperationException("Native WPF cell/row commit was rejected.");
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (normalized.Name != "Native WPF committed" || !editor._dirty)
                throw new InvalidOperationException("Native WPF commit did not update bound row.");
            editor.UndoGridAction();
            if (normalized.Name != savedName || editor._dirty)
                throw new InvalidOperationException("Native WPF committed edit was not undoable.");

            // The field worksheet is an explicit export, never part of
            // navigation, live polling or a default GUI side effect.
            var moreMenu = editor.BuildMoreActionsMenu();
            if (!moreMenu.Items.OfType<MenuItem>().Any(x =>
                    string.Equals(x.Header as string,
                        "Export blank Field FAT worksheet...", StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    "Operator cannot find the blank FAT export in the Signal Map More menu.");
            var worksheet = Iec10xFieldFatWorksheet.ExportCsv(editor.BuildProfileFromRows());
            if (worksheet.Split("\"NOT TESTED\"", StringSplitOptions.None).Length - 1 != 31 ||
                editor._dirty)
                throw new InvalidOperationException(
                    "Real WPF FAT worksheet must have 31 untouched observations with no fabricated PASS.");

            editor.Width = 1140;
            editor.SetCurrentCell(normalized);
            editor.UpdateDetailSelection();
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            editor.UpdateLayout();
            foreach (var label in new[]
            {
                "X0 · source low", "X100 · source high",
                "Y0 · output low", "Y100 · output high"
            })
                AssertInputAboveFold(editor._scalingScroll, label);
            CaptureUiReviewImage(editor, "04-analog-scaling-1140x820");
            editor.SetInspectorVisible(false);
            await editor.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            editor.UpdateLayout();
            if (editor._inspectorOpen ||
                editor._grid.ActualWidth < editor.ActualWidth * 0.87 ||
                !editor._grid.Columns[3].Width.IsStar)
                throw new InvalidOperationException(
                    "Closing the inspector must return full width to the Signal Map grid.");
            CaptureUiReviewImage(editor, "05-grid-first-1140x820");

            // Exercise actual WPF status labels with the same state gate
            // used by Load, Save and Save & Apply. Never open a file dialog
            // or write the packaged reference in this acceptance runner.
            if (editor._activation.HasUnappliedProfile || editor._dirtyText.Text != "Clean")
                throw new InvalidOperationException("Opening Signal Map must start from the active clean profile.");
            editor._activation.PreviewLoaded();
            editor.UpdateCounters();
            if (editor._dirtyText.Text != "Not applied" ||
                !editor._activation.MustConfirmDiscard(false, applied: false))
                throw new InvalidOperationException("Preview must not masquerade as active Values mapping.");
            editor._activation.SavedWithoutApply();
            editor.UpdateCounters();
            if (editor._dirtyText.Text != "Not applied")
                throw new InvalidOperationException("Plain Save must not activate the preview in Values.");
            editor._activation.Applied();
            editor.UpdateCounters();
            if (editor._dirtyText.Text != "Clean" ||
                editor._activation.MustConfirmDiscard(false, applied: true))
                throw new InvalidOperationException("Explicit Apply must restore the clean active state.");
        }
        finally
        {
            // Failed WPF acceptance must never leave interactive confirmation
            // dialogs blocking CI or risk writing the bundled reference.
            editor._dirty = false;
            editor._activation.Applied();
            editor.Close();
        }
    }

    private static void CaptureUiReviewImage(Window editor, string filename)
    {
        var directory = Environment.GetEnvironmentVariable("SIGNAL_MAP_SMOKE_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        editor.UpdateLayout();
        var width = (int)Math.Ceiling(editor.ActualWidth);
        var height = (int)Math.Ceiling(editor.ActualHeight);
        if (width < 600 || height < 500)
            throw new InvalidOperationException("Signal Map review screenshot has invalid WPF viewport dimensions.");

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(editor);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, filename + ".png"));
        png.Save(stream);
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

    private static Button? FindButtonByName(DependencyObject root, string name)
    {
        if (root is Button button && button.Name == name) return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindButtonByName(VisualTreeHelper.GetChild(root, i), name);
            if (found is not null) return found;
        }
        return null;
    }

    private static void AssertMaterialIconText(Button button, string geometry, Style iconStyle)
    {
        if (button.Content is not StackPanel row || row.Children.Count != 2 ||
            row.Children[0] is not System.Windows.Shapes.Path icon ||
            row.Children[1] is not TextBlock label ||
            string.IsNullOrWhiteSpace(label.Text) ||
            !ReferenceEquals(icon.Style, iconStyle) ||
            !ReferenceEquals(icon.Data, Application.Current.FindResource(geometry)) ||
            button.ActualHeight < 30)
            throw new InvalidOperationException(
                $"Material icon-text button '{geometry}' is not using a real shared WPF vector/token.");
    }

    private static void AssertMaterialIconOnly(Button button, string geometry, Style iconStyle)
    {
        if (button.Content is not System.Windows.Shapes.Path icon ||
            !ReferenceEquals(icon.Style, iconStyle) ||
            !ReferenceEquals(icon.Data, Application.Current.FindResource(geometry)))
            throw new InvalidOperationException(
                $"Material icon-only button '{geometry}' lost its shared vector/icon style.");
    }

    private static void AssertAlignedControlCenters(params FrameworkElement[] elements)
    {
        if (elements.Length < 2) return;
        var root = Window.GetWindow(elements[0]) ??
            throw new InvalidOperationException("Toolbar controls are not attached to a window.");
        var ys = elements.Select(element =>
            element.TransformToAncestor(root)
                .Transform(new Point(0, element.ActualHeight / 2)).Y).ToArray();
        if (ys.Max() - ys.Min() > 2.5)
            throw new InvalidOperationException(
                "Toolbar/control vertical-center drift: " +
                string.Join(", ", ys.Select(x => x.ToString("0.##"))));
    }

    private static void AssertMaterialToggle(ToggleButton button, string geometry, Style pathStyle)
    {
        if (button.Content is not StackPanel content ||
            content.Children.Count != 2 ||
            content.Children[0] is not System.Windows.Shapes.Path icon ||
            content.Children[1] is not TextBlock caption ||
            !ReferenceEquals(icon.Style, pathStyle) ||
            !ReferenceEquals(icon.Data, Application.Current.FindResource(geometry)) ||
            caption.VerticalAlignment != VerticalAlignment.Center ||
            button.ActualHeight < 34)
            throw new InvalidOperationException(
                "Inspector tab lost Material icon, typography centering or shared toggle style: " +
                geometry);
    }

    private static void AssertInspectorField(ScrollViewer root, string label)
    {
        var caption = FindLabel(root, label) ??
            throw new InvalidOperationException("Missing inspector field: " + label);
        if (caption.Parent is not StackPanel container ||
            container.Children.Count < 2 ||
            container.Children[1] is not Control input ||
            caption.VerticalAlignment != VerticalAlignment.Center ||
            input.VerticalContentAlignment != VerticalAlignment.Center ||
            caption.Margin.Bottom != 4 ||
            Math.Abs(container.Margin.Bottom - FieldGap) > 0.1 ||
            input.ActualHeight < 32)
            throw new InvalidOperationException(
                "Inconsistent field label, spacing, or vertical centering: " + label);
    }

    private static ComboBox AssertChoiceField(ScrollViewer viewer, string label,
        string originalValue, string requiredChoice)
    {
        var caption = FindLabel(viewer, label) ??
            throw new InvalidOperationException("Missing choice label: " + label);
        if (caption.Parent is not StackPanel container ||
            container.Children.Count < 2 ||
            container.Children[1] is not ComboBox combo ||
            combo.Text != originalValue ||
            !combo.IsEditable ||
            combo.Items.Count == 0 ||
            !combo.Items.OfType<ComboBoxItem>().Any(item =>
                TextSearch.GetText(item) == requiredChoice &&
                item.Content is StackPanel panel && panel.Children.Count == 2 &&
                panel.Children[0] is TextBlock heading && heading.Text.Length > 0 &&
                panel.Children[1] is TextBlock detail && detail.Text.Length > 0))
            throw new InvalidOperationException(
                "Descriptive but lossless IEC dropdown is missing: " + label);
        AssertInspectorField(viewer, label);
        return combo;
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
