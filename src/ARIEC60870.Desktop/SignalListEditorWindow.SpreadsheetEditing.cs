// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ARIEC60870.Core.Mapping;

namespace ARIEC60870.Desktop;

// A clipboard operation is a proposed transaction, never a sequence of live
// per-cell writes. This is intentionally independent from the WPF visual tree.
internal sealed record SignalGridCellChange(
    SignalListEditorRow Row, string Field, string Before, string After);

internal static class SignalGridEditModel
{
    // One allowlist for copy, paste, undo and row edit rollback. No reflection
    // into computed properties, identity internals or protocol state.
    internal static readonly string[] Fields =
    [
        nameof(SignalListEditorRow.Ca), nameof(SignalListEditorRow.Ioa),
        nameof(SignalListEditorRow.TypeId), nameof(SignalListEditorRow.Name),
        nameof(SignalListEditorRow.BayType), nameof(SignalListEditorRow.TestCategory),
        nameof(SignalListEditorRow.Mnemonic), nameof(SignalListEditorRow.ScalingMode),
        nameof(SignalListEditorRow.Unit), nameof(SignalListEditorRow.Group),
        nameof(SignalListEditorRow.SignalType), nameof(SignalListEditorRow.Scale),
        nameof(SignalListEditorRow.ExpectedClass), nameof(SignalListEditorRow.ExpectedCot),
        nameof(SignalListEditorRow.CommandPolicy), nameof(SignalListEditorRow.FeedbackIoa),
        nameof(SignalListEditorRow.StateMap), nameof(SignalListEditorRow.Description),
        nameof(SignalListEditorRow.Offset), nameof(SignalListEditorRow.RatioPrimary),
        nameof(SignalListEditorRow.RatioSecondary), nameof(SignalListEditorRow.NormalizedFullScale),
        nameof(SignalListEditorRow.X0), nameof(SignalListEditorRow.X100),
        nameof(SignalListEditorRow.Y0), nameof(SignalListEditorRow.Y100),
        nameof(SignalListEditorRow.EngineeringMin), nameof(SignalListEditorRow.EngineeringMax)
    ];

    private static readonly IReadOnlyDictionary<string, PropertyInfo> Properties =
        Fields.ToDictionary(name => name, name =>
            typeof(SignalListEditorRow).GetProperty(name) ??
            throw new InvalidOperationException("Unknown Signal Map property: " + name),
            StringComparer.Ordinal);

    internal static string Read(SignalListEditorRow row, string field) =>
        Properties.TryGetValue(field, out var property)
            ? (string?)property.GetValue(row) ?? string.Empty
            : throw new InvalidOperationException("Unsupported Signal Map field: " + field);

    internal static void Write(SignalListEditorRow row, string field, string value)
    {
        if (!Properties.TryGetValue(field, out var property))
            throw new InvalidOperationException("Unsupported Signal Map field: " + field);
        property.SetValue(row, value);
    }

    // Excel emits tab-delimited text with quoted cells, doubled quotes and
    // CRLF line breaks. Keep embedded quoted newlines; refuse unterminated
    // quotes and oversized input instead of silently shifting CA/IOA columns.
    internal static IReadOnlyList<IReadOnlyList<string>> ParseTsv(string clipboard)
    {
        if (clipboard.Length > 2_000_000)
            throw new InvalidOperationException("Clipboard exceeds 2 MB; paste a smaller selection.");
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var fieldStarted = false;
        for (var i = 0; i < clipboard.Length; i++)
        {
            var ch = clipboard[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < clipboard.Length && clipboard[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (quoted)
                {
                    quoted = false;
                }
                else if (!fieldStarted)
                {
                    quoted = true;
                    fieldStarted = true;
                }
                else
                {
                    // A quote in unquoted text is a literal character.
                    cell.Append('"');
                }
                continue;
            }
            if (!quoted && (ch == '\t' || ch == '\r' || ch == '\n'))
            {
                row.Add(cell.ToString());
                cell.Clear();
                fieldStarted = false;
                if (ch == '\t') continue;
                rows.Add(row);
                row = new List<string>();
                if (ch == '\r' && i + 1 < clipboard.Length && clipboard[i + 1] == '\n') i++;
                continue;
            }
            cell.Append(ch);
            fieldStarted = true;
        }
        if (quoted)
            throw new InvalidOperationException("Clipboard has an unfinished quoted field.");
        if (cell.Length > 0 || fieldStarted || row.Count > 0 || rows.Count == 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }
        if (rows.Count > 10_000 || rows.Sum(x => x.Count) > 40_000)
            throw new InvalidOperationException("Clipboard exceeds the 40,000-cell safe edit limit.");
        if (rows.Any(x => x.Count != rows[0].Count))
            throw new InvalidOperationException("Clipboard rows have different column counts. Check tab delimiters.");
        return rows;
    }

    internal static void ValidateEntry(string field, string value)
    {
        if (value.Length > 4096)
            throw new InvalidOperationException($"{field} exceeds 4,096 characters.");

        static int Integer(string text, string label, int min, int max)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                || n < min || n > max)
                throw new InvalidOperationException($"{label} must be an integer between {min} and {max}.");
            return n;
        }

        static void Number(string text, string label, bool optional = false, bool positive = false)
        {
            if (optional && string.IsNullOrWhiteSpace(text)) return;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                || !double.IsFinite(n) || (positive && n <= 0))
                throw new InvalidOperationException(
                    $"{label} must be a finite number" + (positive ? " greater than zero." : "."));
        }

        switch (field)
        {
            case nameof(SignalListEditorRow.Ca):
                if (!string.IsNullOrWhiteSpace(value)) Integer(value, "Common address", 0, 65535);
                break;
            case nameof(SignalListEditorRow.Ioa):
                Integer(value, "IOA", 1, 0xFFFFFF);
                break;
            case nameof(SignalListEditorRow.TypeId):
                if (!string.IsNullOrWhiteSpace(value)) Integer(value, "IEC Type ID", 0, 255);
                break;
            case nameof(SignalListEditorRow.FeedbackIoa):
                if (!string.IsNullOrWhiteSpace(value)) Integer(value, "Feedback IOA", 1, 0xFFFFFF);
                break;
            case nameof(SignalListEditorRow.ExpectedClass):
                if (!string.IsNullOrWhiteSpace(value)) Integer(value, "Class", 0, 255);
                break;
            case nameof(SignalListEditorRow.ExpectedCot):
                if (!string.IsNullOrWhiteSpace(value)) Integer(value, "COT", 0, 63);
                break;
            case nameof(SignalListEditorRow.Name):
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException("Signal name may not be blank.");
                break;
            case nameof(SignalListEditorRow.ScalingMode):
                if (value is not ("TwoPoint" or "Factor"))
                    throw new InvalidOperationException("Scaling mode must be Factor or TwoPoint.");
                break;
            case nameof(SignalListEditorRow.Scale):
            case nameof(SignalListEditorRow.Offset):
                Number(value, field);
                break;
            case nameof(SignalListEditorRow.RatioPrimary):
            case nameof(SignalListEditorRow.RatioSecondary):
            case nameof(SignalListEditorRow.NormalizedFullScale):
                Number(value, field, positive: true);
                break;
            case nameof(SignalListEditorRow.X0):
            case nameof(SignalListEditorRow.X100):
            case nameof(SignalListEditorRow.Y0):
            case nameof(SignalListEditorRow.Y100):
            case nameof(SignalListEditorRow.EngineeringMin):
            case nameof(SignalListEditorRow.EngineeringMax):
                Number(value, field, optional: true);
                break;
            case nameof(SignalListEditorRow.StateMap):
                foreach (var pair in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var equal = pair.IndexOf('=');
                    if (equal <= 0 || equal >= pair.Length - 1 ||
                        string.IsNullOrWhiteSpace(pair[..equal]) ||
                        string.IsNullOrWhiteSpace(pair[(equal + 1)..]))
                        throw new InvalidOperationException(
                            "State map must contain complete key=value pairs separated by semicolons.");
                }
                break;
        }
    }

    internal static IReadOnlyList<SignalGridCellChange> PlanPaste(
        IReadOnlyList<SignalListEditorRow> visibleRows,
        IReadOnlyList<string> visibleFields,
        int startRow, int startColumn, string clipboard)
    {
        var values = ParseTsv(clipboard);
        if (startRow < 0 || startColumn < 0 ||
            startRow + values.Count > visibleRows.Count ||
            startColumn + values[0].Count > visibleFields.Count)
            throw new InvalidOperationException(
                "Paste does not fit visible rows/columns. No rows were added or changed.");

        var changes = new List<SignalGridCellChange>();
        for (var rowIndex = 0; rowIndex < values.Count; rowIndex++)
        {
            for (var colIndex = 0; colIndex < values[rowIndex].Count; colIndex++)
            {
                var item = visibleRows[startRow + rowIndex];
                var field = visibleFields[startColumn + colIndex];
                var before = Read(item, field);
                var after = values[rowIndex][colIndex];
                // WPF row setter canonicalizes the scaling-mode token.
                if (field == nameof(SignalListEditorRow.ScalingMode))
                    after = string.Equals(after, "TwoPoint", StringComparison.OrdinalIgnoreCase)
                        ? "TwoPoint" : string.Equals(after, "Factor", StringComparison.OrdinalIgnoreCase)
                            ? "Factor" : after;
                if (before != after)
                    changes.Add(new SignalGridCellChange(item, field, before, after));
            }
        }
        return changes;
    }

    internal static IReadOnlyList<SignalGridCellChange> PlanFillDown(
        IReadOnlyList<SignalListEditorRow> visibleRows,
        IReadOnlyList<string> visibleFields,
        IReadOnlyCollection<(int Row, int Column)> selected)
    {
        if (selected.Count < 2)
            throw new InvalidOperationException("Select at least two cells vertically, then press Ctrl+D.");
        var rowIndices = selected.Select(x => x.Row).Distinct().OrderBy(x => x).ToArray();
        var colIndices = selected.Select(x => x.Column).Distinct().OrderBy(x => x).ToArray();
        if (rowIndices.Length < 2 ||
            rowIndices[^1] - rowIndices[0] + 1 != rowIndices.Length ||
            colIndices[^1] - colIndices[0] + 1 != colIndices.Length ||
            selected.Count != rowIndices.Length * colIndices.Length ||
            rowIndices[0] < 0 || rowIndices[^1] >= visibleRows.Count ||
            colIndices[0] < 0 || colIndices[^1] >= visibleFields.Count)
            throw new InvalidOperationException(
                "Fill Down requires a contiguous rectangular selection with two or more rows.");

        var changes = new List<SignalGridCellChange>();
        foreach (var col in colIndices)
        {
            var field = visibleFields[col];
            var source = Read(visibleRows[rowIndices[0]], field);
            foreach (var row in rowIndices.Skip(1))
            {
                var target = visibleRows[row];
                var before = Read(target, field);
                if (before != source)
                    changes.Add(new SignalGridCellChange(target, field, before, source));
            }
        }
        return changes;
    }
}

public sealed partial class SignalListEditorWindow
{
    private readonly Stack<IReadOnlyList<SignalGridCellChange>> _undoGrid = new();
    private readonly Stack<IReadOnlyList<SignalGridCellChange>> _redoGrid = new();
    private IReadOnlyList<SignalListEditorRow> _cleanGridRows = Array.Empty<SignalListEditorRow>();
    private string _cleanGridProfileName = string.Empty;
    private (SignalListEditorRow Row, string Field, string Before)? _activeGridEdit;
    private bool _applyingGridTransaction;

    private void InitializeSpreadsheetGrid()
    {
        _grid.SelectionMode = DataGridSelectionMode.Extended;
        _grid.ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader;
        var menu = new ContextMenu();
        var copy = new MenuItem
        {
            Header = "Copy selected cells",
            InputGestureText = "Ctrl+C",
            Command = ApplicationCommands.Copy,
            CommandTarget = _grid
        };
        menu.Items.Add(copy);
        menu.Items.Add(GridMenuItem("Paste from Excel / TSV", "Ctrl+V", (_, _) => PasteGridClipboard()));
        menu.Items.Add(GridMenuItem("Fill down selection", "Ctrl+D", (_, _) => FillDownGridSelection()));
        menu.Items.Add(new Separator());
        menu.Items.Add(GridMenuItem("Undo last grid edit", "Ctrl+Z", (_, _) => UndoGridAction()));
        menu.Items.Add(GridMenuItem("Redo last grid edit", "Ctrl+Y", (_, _) => RedoGridAction()));
        _grid.ContextMenu = menu;
    }

    private static MenuItem GridMenuItem(string text, string shortcut, RoutedEventHandler action)
    {
        var item = new MenuItem { Header = text, InputGestureText = shortcut };
        item.Click += action;
        return item;
    }

    private bool HandleSpreadsheetKey(KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (!ctrl) return false;
        switch (e.Key)
        {
            case Key.V: PasteGridClipboard(); e.Handled = true; return true;
            case Key.D: FillDownGridSelection(); e.Handled = true; return true;
            case Key.Z:
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) RedoGridAction();
                else UndoGridAction();
                e.Handled = true; return true;
            case Key.Y: RedoGridAction(); e.Handled = true; return true;
            default: return false; // Ctrl+C is native WPF DataGrid copy.
        }
    }

    private (SignalListEditorRow[] Rows, string[] Fields) VisibleGrid()
    {
        var rows = CollectionViewSource.GetDefaultView(_rows).Cast<SignalListEditorRow>().ToArray();
        var fields = _grid.Columns.Where(x => x.Visibility == Visibility.Visible)
            .OrderBy(x => x.DisplayIndex)
            .Select(column => (column as DataGridTextColumn)?.Binding as Binding)
            .Select(binding => binding?.Path.Path ??
                throw new InvalidOperationException("Grid contains a non-text edit column."))
            .ToArray();
        return (rows, fields);
    }

    private void PasteGridClipboard()
    {
        try
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
                throw new InvalidOperationException("Clipboard contains no text. Copy cells from Excel first.");
            var (rows, fields) = VisibleGrid();
            var current = _grid.CurrentCell;
            var rowIndex = Array.IndexOf(rows, current.Item as SignalListEditorRow);
            var colIndex = Array.IndexOf(fields,
                (current.Column as DataGridTextColumn)?.Binding is Binding b ? b.Path.Path : string.Empty);
            if (rowIndex < 0 || colIndex < 0)
                throw new InvalidOperationException("Select the starting cell in the visible grid before pasting.");
            var changes = SignalGridEditModel.PlanPaste(rows, fields,
                rowIndex, colIndex, Clipboard.GetText(TextDataFormat.UnicodeText));
            ApplyNewGridTransaction(changes, "Paste", warnOnBulk: true);
        }
        catch (Exception ex)
        {
            UpdateStatus("Paste cancelled: " + ex.Message, danger: true);
        }
    }

    private void FillDownGridSelection()
    {
        try
        {
            var (rows, fields) = VisibleGrid();
            var selected = _grid.SelectedCells.Select(cell =>
                (Row: Array.IndexOf(rows, cell.Item as SignalListEditorRow),
                Column: Array.IndexOf(fields,
                    (cell.Column as DataGridTextColumn)?.Binding is Binding b ? b.Path.Path : string.Empty)))
                .ToArray();
            var changes = SignalGridEditModel.PlanFillDown(rows, fields, selected);
            ApplyNewGridTransaction(changes, "Fill Down", warnOnBulk: true);
        }
        catch (Exception ex)
        {
            UpdateStatus("Fill Down cancelled: " + ex.Message, danger: true);
        }
    }

    private void ApplyNewGridTransaction(IReadOnlyList<SignalGridCellChange> changes,
        string caption, bool warnOnBulk)
    {
        if (changes.Count == 0)
        {
            UpdateStatus("No values changed.", success: true);
            return;
        }
        // Validate against the entire prospective profile, not one cell at a
        // time. One invalid CA/IOA/type/ratio aborts every proposed change.
        ValidateGridTransaction(changes);
        var identities = changes.Any(x => x.Field is
            nameof(SignalListEditorRow.Ca) or nameof(SignalListEditorRow.Ioa) or
            nameof(SignalListEditorRow.TypeId));
        var rowCount = changes.Select(x => x.Row).Distinct().Count();
        if (warnOnBulk && (rowCount > 1 || identities))
        {
            var warning = $"{caption} {changes.Count} cell(s) in {rowCount} signal(s)" +
                (identities ? " including IEC identity (CA / IOA / Type ID)" : "") +
                "? All changes were validated in memory; nothing has been saved or applied.";
            if (ModernMessageBox.Show(this, warning, "Confirm Signal Map edit",
                    MessageBoxButton.YesNo, identities ? MessageBoxImage.Warning : MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                UpdateStatus(caption + " cancelled. Existing values preserved.");
                return;
            }
        }
        ApplyGridValues(changes, forward: true);
        RememberGridTransaction(changes);
        UpdateStatus($"{caption}: {changes.Count} cell(s) changed across {rowCount} signal(s). Ctrl+Z reverses; Save & Apply persists.", success: true);
    }

    private void ValidateGridTransaction(IReadOnlyList<SignalGridCellChange> changes)
    {
        var staged = changes.Select(x => x.Row).Distinct()
            .ToDictionary(row => row, row => row.Clone());
        foreach (var change in changes)
        {
            if (!_rows.Contains(change.Row))
                throw new InvalidOperationException("A target signal was removed from the mapping.");
            SignalGridEditModel.ValidateEntry(change.Field, change.After);
            SignalGridEditModel.Write(staged[change.Row], change.Field, change.After);
        }
        var trial = CloneProfile(_template);
        trial.Points = _rows.Select(row =>
        {
            var candidate = staged.TryGetValue(row, out var draft) ? draft : row;
            try { return candidate.ToPoint(); }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"CA {candidate.Ca}, IOA {candidate.Ioa}: {ex.Message}", ex);
            }
        }).ToList();
        trial.Validate();
    }

    private void ApplyGridValues(IReadOnlyList<SignalGridCellChange> changes, bool forward)
    {
        _applyingGridTransaction = true;
        try
        {
            foreach (var change in changes)
                SignalGridEditModel.Write(change.Row, change.Field,
                    forward ? change.After : change.Before);
        }
        finally { _applyingGridTransaction = false; }
        RefreshBayChoices();
        RefreshFilter();
        UpdateDetailSelection();
        RecalculateGridDirty();
    }

    private void RememberGridTransaction(IReadOnlyList<SignalGridCellChange> changes)
    {
        if (changes.Count == 0) return;
        _undoGrid.Push(changes);
        if (_undoGrid.Count > 100)
        {
            // Retain most recent 100 transactions without unbounded snapshots.
            var newest = _undoGrid.Take(100).Reverse().ToArray();
            _undoGrid.Clear();
            foreach (var action in newest) _undoGrid.Push(action);
        }
        _redoGrid.Clear();
    }

    private void UndoGridAction() => TravelGridHistory(_undoGrid, _redoGrid, forward: false, "Undo");
    private void RedoGridAction() => TravelGridHistory(_redoGrid, _undoGrid, forward: true, "Redo");

    private void TravelGridHistory(Stack<IReadOnlyList<SignalGridCellChange>> source,
        Stack<IReadOnlyList<SignalGridCellChange>> destination, bool forward, string caption)
    {
        if (source.Count == 0)
        {
            UpdateStatus("Nothing to " + caption.ToLowerInvariant() + ".");
            return;
        }
        var changes = source.Peek();
        if (changes.Any(x => !_rows.Contains(x.Row) ||
            SignalGridEditModel.Read(x.Row, x.Field) != (forward ? x.Before : x.After)))
        {
            UpdateStatus($"{caption} refused: a target cell was subsequently modified or removed. No values changed.", danger: true);
            return;
        }
        ApplyGridValues(changes, forward);
        source.Pop();
        destination.Push(changes);
        UpdateStatus($"{caption}: {changes.Count} cell(s) restored. Save & Apply is still required.", success: true);
    }

    private void OnGridEditStarted(DataGridBeginningEditEventArgs e)
    {
        var field = ((e.Column as DataGridTextColumn)?.Binding as Binding)?.Path.Path;
        _activeGridEdit = e.Row.Item is SignalListEditorRow row && field is not null
            ? (row, field, SignalGridEditModel.Read(row, field)) : null;
    }

    private void OnGridEditEnded(DataGridCellEditEndingEventArgs e)
    {
        var original = _activeGridEdit;
        _activeGridEdit = null;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (e.EditAction == DataGridEditAction.Cancel)
            {
                RecalculateGridDirty();
                UpdateStatus("Cell edit cancelled; previous value restored.");
            }
            else if (original is { } start && _rows.Contains(start.Row))
            {
                var after = SignalGridEditModel.Read(start.Row, start.Field);
                if (start.Before != after)
                    RememberGridTransaction([new SignalGridCellChange(
                        start.Row, start.Field, start.Before, after)]);
            }
            if (_searchBox.Text.Length > 0 ||
                _bayFilter.SelectedIndex > 0 || _kindFilter.SelectedIndex > 0)
                RefreshFilter();
        }), DispatcherPriority.Background);
    }

    private void ResetGridCheckpoint()
    {
        _cleanGridRows = _rows.Select(x => x.Clone()).ToArray();
        _cleanGridProfileName = _profileNameBox.Text;
        _undoGrid.Clear();
        _redoGrid.Clear();
    }

    private void RecalculateGridDirty()
    {
        _dirty = _rows.Count != _cleanGridRows.Count ||
            _cleanGridProfileName != _profileNameBox.Text ||
            Enumerable.Range(0, Math.Min(_rows.Count, _cleanGridRows.Count)).Any(index =>
                SignalGridEditModel.Fields.Any(field =>
                    SignalGridEditModel.Read(_rows[index], field) !=
                    SignalGridEditModel.Read(_cleanGridRows[index], field)));
        UpdateCounters();
    }
}
