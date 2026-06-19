// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ARIEC60870.Core.Mapping;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

/// <summary>
/// Modern WPF database editor for IEC-101/104 IOA mapping profiles.
/// The editor stays inside the same desktop app so field engineers can correct
/// a project database during FAT/SAT without touching JSON manually.
/// </summary>
public sealed class SignalListEditorWindow : Window
{
    private static readonly Brush AppBackgroundBrush = BrushFromRgb(0xF5, 0xF7, 0xFB);
    private static readonly Brush CardBrush = Brushes.White;
    private static readonly Brush CardAltBrush = BrushFromRgb(0xF8, 0xFB, 0xFF);
    private static readonly Brush BorderBrushSoft = BrushFromRgb(0xDF, 0xE7, 0xF2);
    private static readonly Brush AccentBrush = BrushFromRgb(0x1D, 0x6E, 0xF2);
    private static readonly Brush AccentDarkBrush = BrushFromRgb(0x0F, 0x3D, 0x91);
    private static readonly Brush AccentSoftBrush = BrushFromRgb(0xE8, 0xF1, 0xFF);
    private static readonly Brush TextMainBrush = BrushFromRgb(0x10, 0x17, 0x28);
    private static readonly Brush TextMutedBrush = BrushFromRgb(0x5B, 0x68, 0x7C);
    private static readonly Brush DangerBrush = BrushFromRgb(0xB4, 0x23, 0x18);
    private static readonly Brush DangerSoftBrush = BrushFromRgb(0xFE, 0xF3, 0xF2);
    private static readonly Brush SuccessBrush = BrushFromRgb(0x04, 0x7A, 0x48);
    private static readonly Brush SuccessSoftBrush = BrushFromRgb(0xEC, 0xFD, 0xF3);

    private readonly ObservableCollection<SignalListEditorRow> _rows = new();
    private readonly TextBox _profileNameBox = new();
    private readonly TextBox _pathBox = new();
    private readonly TextBox _searchBox = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _rowCountText = new();
    private readonly TextBlock _dirtyText = new();
    private readonly TextBlock _selectionTitle = new();
    private readonly TextBlock _selectionSubtitle = new();
    private readonly Border _selectionBadge = CreatePill("No selection", TextMutedBrush, BrushFromRgb(0xF1, 0xF5, 0xF9), BorderBrushSoft);
    private readonly DataGrid _grid = new();
    private readonly ScrollViewer _detailScroll = new();
    private Iec10xPointMappingProfile _template;
    private bool _dirty;
    private bool _saved;
    private bool _loadingRows;
    private bool _isCellEditing;
    private string? _pendingCellEditText;
    private bool _replaceNextCellText;

    public SignalListEditorWindow(Iec10xPointMappingProfile profile, string? profilePath)
    {
        _template = CloneProfile(profile?.HasPoints == true ? profile : CreateBlankProfile());
        SavedProfilePath = profilePath ?? string.Empty;
        Profile = CloneProfile(_template);

        Title = "Signal List Editor - IEC-101/104 IOA Mapping";
        Width = 1360;
        Height = 820;
        MinWidth = 1120;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = AppBackgroundBrush;
        FontFamily = new FontFamily("Segoe UI");
        Foreground = TextMainBrush;

        Content = BuildLayout();
        LoadRowsFromProfile(_template);
        UpdateStatus("Ready. Arrow keys move between cells. Type to replace the selected cell, F2/Enter edits, Tab commits, Esc cancels.", success: true);
    }

    public Iec10xPointMappingProfile Profile { get; private set; }
    public string SavedProfilePath { get; private set; }

    private UIElement BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var hero = BuildHero();
        root.Children.Add(hero);

        var toolbarCard = BuildToolbarCard();
        Grid.SetRow(toolbarCard, 1);
        root.Children.Add(toolbarCard);

        var workspace = BuildWorkspace();
        Grid.SetRow(workspace, 2);
        root.Children.Add(workspace);

        var footer = BuildFooter();
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private UIElement BuildHero()
    {
        var card = CreateCard(new Thickness(0, 0, 0, 14), new Thickness(18, 16, 18, 16));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.Child = grid;

        var left = new StackPanel { Orientation = Orientation.Vertical };
        left.Children.Add(new TextBlock
        {
            Text = "Signal List Editor",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMainBrush,
            Margin = new Thickness(0, 0, 0, 3)
        });
        left.Children.Add(new TextBlock
        {
            Text = "Edit IEC-101/104 IOA mapping, command policy, feedback IOA, state map, class, COT, and engineering metadata without touching JSON manually.",
            FontSize = 12.5,
            Foreground = TextMutedBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 900
        });
        grid.Children.Add(left);

        var metrics = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _rowCountText.Text = "0 signals";
        _dirtyText.Text = "Clean";
        metrics.Children.Add(CreateMetricChip("Database", _rowCountText, AccentBrush, AccentSoftBrush));
        metrics.Children.Add(CreateMetricChip("State", _dirtyText, SuccessBrush, SuccessSoftBrush));
        Grid.SetColumn(metrics, 1);
        grid.Children.Add(metrics);

        return card;
    }

    private UIElement BuildToolbarCard()
    {
        var card = CreateCard(new Thickness(0, 0, 0, 14), new Thickness(14, 12, 14, 12));
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.Child = grid;

        _profileNameBox.Text = _template.ProfileName;
        _profileNameBox.Style = CreateTextBoxStyle();
        _profileNameBox.Margin = new Thickness(0, 0, 10, 0);
        _profileNameBox.ToolTip = "Profile name saved into the JSON database";
        _profileNameBox.TextChanged += (_, _) => MarkDirty();
        grid.Children.Add(_profileNameBox);

        _pathBox.Text = SavedProfilePath;
        _pathBox.IsReadOnly = true;
        _pathBox.Style = CreateTextBoxStyle(readOnly: true);
        _pathBox.Margin = new Thickness(0, 0, 0, 0);
        Grid.SetColumn(_pathBox, 1);
        grid.Children.Add(_pathBox);

        var actionRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(actionRow, 1);
        Grid.SetColumnSpan(actionRow, 2);
        grid.Children.Add(actionRow);

        _searchBox.Style = CreateTextBoxStyle();
        _searchBox.Margin = new Thickness(0, 0, 12, 0);
        _searchBox.TextChanged += (_, _) => RefreshFilter();
        _searchBox.ToolTip = "Filter by CA, IOA, name, group, mnemonic, policy, or description";
        _searchBox.Text = string.Empty;
        _searchBox.Tag = "Search signal list...";
        actionRow.Children.Add(_searchBox);

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        AddToolbarButton(toolbar, "+ Add", AddRow_Click);
        AddToolbarButton(toolbar, "Duplicate", DuplicateRow_Click);
        AddToolbarButton(toolbar, "Delete", DeleteRow_Click, danger: true);
        AddToolbarButton(toolbar, "Load", LoadList_Click);
        AddToolbarButton(toolbar, "Save", SaveList_Click);
        AddToolbarButton(toolbar, "Save As", SaveAs_Click);
        AddToolbarButton(toolbar, "Validate", Validate_Click);
        AddToolbarButton(toolbar, "Save & Apply", SaveApply_Click, isPrimary: true);
        Grid.SetColumn(toolbar, 1);
        actionRow.Children.Add(toolbar);

        return card;
    }

    private UIElement BuildWorkspace()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

        var tableCard = CreateCard(new Thickness(0), new Thickness(0));
        tableCard.Child = _grid;
        ConfigureGrid();
        grid.Children.Add(tableCard);

        var detail = BuildDetailPanel();
        Grid.SetColumn(detail, 2);
        grid.Children.Add(detail);

        return grid;
    }

    private void ConfigureGrid()
    {
        _grid.ItemsSource = _rows;
        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = false;
        _grid.CanUserAddRows = false;
        _grid.CanUserDeleteRows = false;
        _grid.CanUserResizeRows = false;
        _grid.CanUserReorderColumns = true;
        _grid.CanUserResizeColumns = true;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.SelectionUnit = DataGridSelectionUnit.CellOrRowHeader;
        _grid.EnableRowVirtualization = true;
        _grid.EnableColumnVirtualization = true;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _grid.RowHeight = 36;
        _grid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.Background = Brushes.Transparent;
        _grid.BorderThickness = new Thickness(0);
        _grid.RowBackground = Brushes.White;
        _grid.AlternatingRowBackground = BrushFromRgb(0xFB, 0xFD, 0xFF);
        _grid.HorizontalGridLinesBrush = BrushFromRgb(0xEA, 0xF0, 0xF8);
        _grid.VerticalGridLinesBrush = Brushes.Transparent;
        _grid.FontSize = 12.5;
        _grid.ColumnHeaderHeight = 38;
        _grid.ColumnHeaderStyle = CreateColumnHeaderStyle();
        _grid.CellStyle = CreateCellStyle();
        _grid.RowStyle = CreateRowStyle();
        _grid.SelectionChanged += (_, _) => UpdateDetailSelection();
        _grid.SelectedCellsChanged += (_, _) => UpdateDetailSelection();
        _grid.CurrentCellChanged += (_, _) => UpdateDetailSelection();
        _grid.PreviewKeyDown += Grid_PreviewKeyDown;
        _grid.PreviewTextInput += Grid_PreviewTextInput;
        _grid.PreparingCellForEdit += Grid_PreparingCellForEdit;
        _grid.CellEditEnding += Grid_CellEditEnding;
        _grid.BeginningEdit += Grid_BeginningEdit;

        AddTextColumn("CA", nameof(SignalListEditorRow.Ca), 72);
        AddTextColumn("IOA", nameof(SignalListEditorRow.Ioa), 96);
        AddTextColumn("Type ID", nameof(SignalListEditorRow.TypeId), 78);
        AddTextColumn("Name", nameof(SignalListEditorRow.Name), 220, minWidth: 140);
        AddTextColumn("Group", nameof(SignalListEditorRow.Group), 136, minWidth: 110);
        AddTextColumn("Type / Role", nameof(SignalListEditorRow.SignalType), 190, minWidth: 150);
        AddTextColumn("Unit", nameof(SignalListEditorRow.Unit), 72);
        AddTextColumn("Scale", nameof(SignalListEditorRow.Scale), 76);
        AddTextColumn("Class", nameof(SignalListEditorRow.ExpectedClass), 70);
        AddTextColumn("COT", nameof(SignalListEditorRow.ExpectedCot), 70);
        AddTextColumn("Command policy", nameof(SignalListEditorRow.CommandPolicy), 160, minWidth: 130);
        AddTextColumn("Feedback IOA", nameof(SignalListEditorRow.FeedbackIoa), 112);
        AddTextColumn("State map", nameof(SignalListEditorRow.StateMap), 180, minWidth: 140);
        AddTextColumn("Mnemonic", nameof(SignalListEditorRow.Mnemonic), 104);
        AddTextColumn("Bay", nameof(SignalListEditorRow.BayType), 128);
        AddTextColumn("Description", nameof(SignalListEditorRow.Description), 260, minWidth: 180);
    }

    private void Grid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        _isCellEditing = true;
        UpdateStatus("Fast edit mode. Enter/Tab commits, Esc cancels. Arrow keys move between cells after the edit is committed.");
    }

    private void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        _isCellEditing = false;
        MarkDirty();
        Dispatcher.BeginInvoke(new Action(UpdateDetailSelection), DispatcherPriority.Background);
    }

    private void Grid_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is not TextBox box)
        {
            ClearPendingCellEdit();
            return;
        }

        box.Focus();
        if (_replaceNextCellText)
        {
            box.Text = _pendingCellEditText ?? string.Empty;
            box.CaretIndex = box.Text.Length;
        }
        else
        {
            box.SelectAll();
        }

        ClearPendingCellEdit();
    }

    private void Grid_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_isCellEditing || string.IsNullOrEmpty(e.Text) || HasCommandModifier()) return;
        if (!EnsureEditableCurrentCell()) return;

        _pendingCellEditText = e.Text;
        _replaceNextCellText = true;
        if (_grid.BeginEdit(e))
        {
            e.Handled = true;
        }
        else
        {
            ClearPendingCellEdit();
        }
    }

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isCellEditing) return;
        if (HasCommandModifier()) return;

        if (e.Key is Key.F2 or Key.Enter)
        {
            if (BeginSelectedCellEdit(replaceText: null, e))
            {
                e.Handled = true;
            }
            return;
        }

        if (e.Key is Key.Delete or Key.Back)
        {
            if (BeginSelectedCellEdit(replaceText: string.Empty, e))
            {
                e.Handled = true;
            }
        }
    }

    private bool BeginSelectedCellEdit(string? replaceText, RoutedEventArgs editingEventArgs)
    {
        if (!EnsureEditableCurrentCell()) return false;
        _pendingCellEditText = replaceText;
        _replaceNextCellText = replaceText is not null;
        if (_grid.BeginEdit(editingEventArgs)) return true;

        ClearPendingCellEdit();
        return false;
    }

    private bool EnsureEditableCurrentCell()
    {
        if (_grid.CurrentCell.Item is SignalListEditorRow && _grid.CurrentCell.Column is { IsReadOnly: false })
        {
            return true;
        }

        var row = GetCurrentRow();
        if (row is null || _grid.Columns.Count == 0) return false;
        SetCurrentCell(row, Math.Max(0, _grid.Columns.IndexOf(_grid.CurrentColumn)));
        return _grid.CurrentCell.Column is { IsReadOnly: false };
    }

    private static bool HasCommandModifier()
    {
        var modifiers = Keyboard.Modifiers;
        return (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0;
    }

    private void ClearPendingCellEdit()
    {
        _pendingCellEditText = null;
        _replaceNextCellText = false;
    }

    private SignalListEditorRow? GetCurrentRow()
    {
        if (_grid.CurrentItem is SignalListEditorRow current) return current;
        if (_grid.SelectedItem is SignalListEditorRow selected) return selected;
        return _grid.SelectedCells.FirstOrDefault().Item as SignalListEditorRow;
    }

    private void SetCurrentCell(SignalListEditorRow row, int columnIndex = 0)
    {
        if (_grid.Columns.Count == 0) return;
        var safeColumnIndex = Math.Clamp(columnIndex, 0, _grid.Columns.Count - 1);
        var column = _grid.Columns[safeColumnIndex];
        var cell = new DataGridCellInfo(row, column);
        _grid.SelectedCells.Clear();
        _grid.CurrentCell = cell;
        _grid.SelectedCells.Add(cell);
        _grid.SelectedItem = row;
        _grid.ScrollIntoView(row, column);
        _grid.Focus();
    }

    private UIElement BuildDetailPanel()
    {
        var card = CreateCard(new Thickness(0), new Thickness(16, 14, 16, 14));
        var root = new DockPanel();
        card.Child = root;

        var header = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = "Selected signal",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMainBrush
        });
        header.Children.Add(new TextBlock
        {
            Text = "Arrow keys move cells. Type to replace the selected cell, F2/Enter edits, Tab commits. You can also edit the selected signal here.",
            FontSize = 12,
            Foreground = TextMutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 8)
        });
        _selectionTitle.Text = "No row selected";
        _selectionTitle.FontSize = 13.5;
        _selectionTitle.FontWeight = FontWeights.SemiBold;
        _selectionTitle.Foreground = AccentDarkBrush;
        _selectionSubtitle.Text = "Select a signal from the table.";
        _selectionSubtitle.FontSize = 12;
        _selectionSubtitle.Foreground = TextMutedBrush;
        header.Children.Add(_selectionTitle);
        header.Children.Add(_selectionSubtitle);
        header.Children.Add(_selectionBadge);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 12, 0, 0) };
        var validateButton = CreateToolbarButton("Validate row", ValidateSelectedRow_Click, false, false);
        validateButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        validateButton.Margin = new Thickness(0, 0, 0, 8);
        footer.Children.Add(validateButton);
        var duplicateButton = CreateToolbarButton("Duplicate selected", DuplicateRow_Click, false, false);
        duplicateButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        footer.Children.Add(duplicateButton);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        AddEditorField(panel, "CA", nameof(SignalListEditorRow.Ca), "Common Address", compact: true);
        AddEditorField(panel, "IOA", nameof(SignalListEditorRow.Ioa), "Information Object Address", compact: true);
        AddEditorField(panel, "Type ID", nameof(SignalListEditorRow.TypeId), "IEC 60870 type identifier", compact: true);
        AddEditorField(panel, "Name", nameof(SignalListEditorRow.Name));
        AddEditorField(panel, "Group", nameof(SignalListEditorRow.Group));
        AddEditorField(panel, "Type / Role", nameof(SignalListEditorRow.SignalType));
        AddEditorField(panel, "Unit", nameof(SignalListEditorRow.Unit), compact: true);
        AddEditorField(panel, "Scale", nameof(SignalListEditorRow.Scale), compact: true);
        AddEditorField(panel, "Class", nameof(SignalListEditorRow.ExpectedClass), "Expected IEC-101 class", compact: true);
        AddEditorField(panel, "COT", nameof(SignalListEditorRow.ExpectedCot), "Expected cause of transmission", compact: true);
        AddEditorCombo(panel, "Command policy", nameof(SignalListEditorRow.CommandPolicy), new[] { "MonitorOnly", "DoubleCommand", "SingleCommand", "SetpointNormalized", "RegulatingStep" });
        AddEditorField(panel, "Feedback IOA", nameof(SignalListEditorRow.FeedbackIoa));
        AddEditorField(panel, "State map", nameof(SignalListEditorRow.StateMap), "Example: 0=Off; 1=On");
        AddEditorField(panel, "Mnemonic", nameof(SignalListEditorRow.Mnemonic));
        AddEditorField(panel, "Bay", nameof(SignalListEditorRow.BayType));
        AddEditorField(panel, "Description", nameof(SignalListEditorRow.Description), multiline: true);

        _detailScroll.Content = panel;
        _detailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _detailScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        root.Children.Add(_detailScroll);

        return card;
    }

    private UIElement BuildFooter()
    {
        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var statusCard = new Border
        {
            Background = CardBrush,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(13, 9, 13, 9)
        };
        _statusText.TextWrapping = TextWrapping.Wrap;
        _statusText.FontSize = 12.5;
        _statusText.Foreground = TextMutedBrush;
        statusCard.Child = _statusText;
        footer.Children.Add(statusCard);

        var closeButton = CreateToolbarButton("Close", (_, _) => Close(), false, false);
        closeButton.Margin = new Thickness(10, 0, 0, 0);
        closeButton.MinWidth = 86;
        Grid.SetColumn(closeButton, 1);
        footer.Children.Add(closeButton);

        return footer;
    }

    private static Border CreateCard(Thickness margin, Thickness padding) => new()
    {
        Background = CardBrush,
        BorderBrush = BorderBrushSoft,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(18),
        Padding = padding,
        Margin = margin,
        SnapsToDevicePixels = true
    };

    private static StackPanel CreateMetricChip(string label, TextBlock valueBlock, Brush accent, Brush background)
    {
        var chip = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(10, 0, 0, 0),
            MinWidth = 108
        };
        var border = new Border
        {
            Background = background,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 8, 12, 8)
        };
        var content = new StackPanel { Orientation = Orientation.Vertical };
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10.5,
            Foreground = TextMutedBrush,
            FontWeight = FontWeights.SemiBold
        });
        valueBlock.FontSize = 13;
        valueBlock.FontWeight = FontWeights.SemiBold;
        valueBlock.Foreground = accent;
        content.Children.Add(valueBlock);
        border.Child = content;
        chip.Children.Add(border);
        return chip;
    }

    private static Border CreatePill(string text, Brush foreground, Brush background, Brush borderBrush)
    {
        return new Border
        {
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = foreground
            }
        };
    }

    private void AddToolbarButton(Panel parent, string text, RoutedEventHandler handler, bool isPrimary = false, bool danger = false)
    {
        var button = CreateToolbarButton(text, handler, isPrimary, danger);
        parent.Children.Add(button);
    }

    private static Button CreateToolbarButton(string text, RoutedEventHandler handler, bool isPrimary, bool danger)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(6, 4, 0, 4),
            MinHeight = 34,
            FontSize = 12.2,
            FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = danger ? DangerBrush : isPrimary ? Brushes.White : AccentDarkBrush,
            Background = danger ? DangerSoftBrush : isPrimary ? AccentBrush : AccentSoftBrush,
            BorderBrush = danger ? BrushFromRgb(0xFE, 0xCD, 0xCA) : isPrimary ? AccentBrush : BorderBrushSoft,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Style = CreateRoundedButtonStyle()
        };
        button.Click += handler;
        return button;
    }

    private static Style CreateRoundedButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, CreateButtonTemplate()));
        style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.9));
        style.Triggers.Add(hover);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        style.Triggers.Add(disabled);

        return style;
    }

    private static ControlTemplate CreateButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Chrome";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private static Style CreateTextBoxStyle(bool readOnly = false)
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, readOnly ? CardAltBrush : Brushes.White));
        style.Setters.Add(new Setter(Control.ForegroundProperty, readOnly ? TextMutedBrush : TextMainBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrushSoft));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(11, 7, 11, 7)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
        style.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, AccentBrush));
        return style;
    }

    private static Style CreateColumnHeaderStyle()
    {
        var style = new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, BrushFromRgb(0xF3, 0xF7, 0xFC)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextMutedBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrushSoft));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 0, 10, 0)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12.2));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        return style;
    }

    private static Style CreateCellStyle()
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, BrushFromRgb(0xE9, 0xF2, 0xFF)));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, TextMainBrush));
        style.Triggers.Add(selected);
        return style;
    }

    private static Style CreateRowStyle()
    {
        var style = new Style(typeof(DataGridRow));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, BrushFromRgb(0xE9, 0xF2, 0xFF)));
        style.Triggers.Add(selected);
        return style;
    }

    private void AddTextColumn(string header, string path, double width, double? minWidth = null)
    {
        var binding = new Binding(path)
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };

        var viewStyle = new Style(typeof(TextBlock));
        viewStyle.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(10, 0, 10, 0)));
        viewStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        viewStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));

        var editStyle = new Style(typeof(TextBox));
        editStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        editStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
        editStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 0, 9, 0)));
        editStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        editStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));

        var column = new DataGridTextColumn
        {
            Header = header,
            Binding = binding,
            Width = new DataGridLength(width),
            MinWidth = minWidth ?? Math.Min(width, 84),
            ElementStyle = viewStyle,
            EditingElementStyle = editStyle,
            IsReadOnly = false
        };
        _grid.Columns.Add(column);
    }

    private void AddEditorField(Panel parent, string label, string path, string? hint = null, bool compact = false, bool multiline = false)
    {
        var container = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 9) };
        container.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMutedBrush,
            Margin = new Thickness(0, 0, 0, 3)
        });
        var box = new TextBox
        {
            Style = CreateTextBoxStyle(),
            MinHeight = multiline ? 70 : 34,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled
        };
        box.SetBinding(TextBox.TextProperty, new Binding(path)
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        container.Children.Add(box);
        if (!string.IsNullOrWhiteSpace(hint))
        {
            container.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 10.8,
                Foreground = TextMutedBrush,
                Margin = new Thickness(1, 3, 0, 0)
            });
        }
        parent.Children.Add(container);
    }

    private void AddEditorCombo(Panel parent, string label, string path, IEnumerable<string> values)
    {
        var container = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 9) };
        container.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMutedBrush,
            Margin = new Thickness(0, 0, 0, 3)
        });
        var combo = new ComboBox
        {
            ItemsSource = values,
            IsEditable = true,
            MinHeight = 34,
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 12.5,
            Background = Brushes.White,
            BorderBrush = BorderBrushSoft,
            Foreground = TextMainBrush
        };
        combo.SetBinding(ComboBox.TextProperty, new Binding(path)
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        container.Children.Add(combo);
        parent.Children.Add(container);
    }

    private void LoadRowsFromProfile(Iec10xPointMappingProfile profile)
    {
        _loadingRows = true;
        try
        {
            _rows.Clear();
            foreach (var point in profile.Points.OrderBy(x => x.Ioa).ThenBy(x => x.TypeId ?? 0))
            {
                AddEditorRow(new SignalListEditorRow(point), markDirty: false);
            }
            _dirty = false;
            RefreshFilter();
            UpdateCounters();
            if (_rows.Count > 0)
            {
                SetCurrentCell(_rows[0]);
            }
            UpdateDetailSelection();
        }
        finally
        {
            _loadingRows = false;
        }
    }

    private void AddEditorRow(SignalListEditorRow row, bool markDirty)
    {
        row.PropertyChanged += Row_PropertyChanged;
        _rows.Add(row);
        if (markDirty) MarkDirty();
        UpdateCounters();
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loadingRows) return;
        UpdateDetailSelection();
        MarkDirty();
    }

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        var nextIoa = _rows.Count == 0 ? 1 : _rows.Select(x => ParseInt(x.Ioa) ?? 0).DefaultIfEmpty(0).Max() + 1;
        var row = new SignalListEditorRow
        {
            Ca = _template.CommonAddress?.ToString(CultureInfo.InvariantCulture) ?? "105",
            Ioa = nextIoa.ToString(CultureInfo.InvariantCulture),
            TypeId = "30",
            Name = "New signal",
            Group = "User",
            SignalType = "M_SP_TB_1",
            Unit = string.Empty,
            Scale = "1",
            ExpectedClass = "1",
            ExpectedCot = "3",
            CommandPolicy = "MonitorOnly",
            Description = "Created in Signal List Editor"
        };
        AddEditorRow(row, markDirty: true);
        SetCurrentCell(row);
        UpdateStatus("New editable signal row added. Complete CA, IOA, Type ID, and engineering name.");
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null) return;
        row.PropertyChanged -= Row_PropertyChanged;
        _rows.Remove(row);
        MarkDirty();
        UpdateCounters();
        UpdateDetailSelection();
        UpdateStatus("Selected signal removed. Save the list to persist the change.");
    }

    private void DuplicateRow_Click(object sender, RoutedEventArgs e)
    {
        var source = GetCurrentRow();
        if (source is null) return;
        var copy = source.Clone();
        copy.Ioa = ((ParseInt(source.Ioa) ?? 0) + 1).ToString(CultureInfo.InvariantCulture);
        copy.Name = string.IsNullOrWhiteSpace(copy.Name) ? "Copied signal" : copy.Name + " copy";
        AddEditorRow(copy, markDirty: true);
        SetCurrentCell(copy);
        UpdateStatus("Selected signal duplicated. Review IOA and feedback IOA before saving.");
    }

    private void LoadList_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "IOA profile JSON (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var profile = Iec10xPointMappingProfile.LoadFromFile(dialog.FileName);
            _template = CloneProfile(profile);
            Profile = CloneProfile(profile);
            SavedProfilePath = dialog.FileName;
            _pathBox.Text = SavedProfilePath;
            _profileNameBox.Text = profile.ProfileName;
            LoadRowsFromProfile(profile);
            _dirty = false;
            UpdateCounters();
            UpdateStatus($"Loaded {profile.Points.Count} point(s) from {Path.GetFileName(dialog.FileName)}.", success: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load signal list", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveList_Click(object sender, RoutedEventArgs e) => SaveCurrent(closeAfter: false, forceSaveAs: false);
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveCurrent(closeAfter: false, forceSaveAs: true);
    private void SaveApply_Click(object sender, RoutedEventArgs e) => SaveCurrent(closeAfter: true, forceSaveAs: false);

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = BuildProfileFromRows();
            profile.Validate();
            UpdateStatus($"Validated OK: {profile.Points.Count} point(s), {profile.Points.Count(x => !string.IsNullOrWhiteSpace(x.CommandPolicy) && !x.CommandPolicy.Equals("MonitorOnly", StringComparison.OrdinalIgnoreCase))} command-capable point(s).", success: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Signal list validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            UpdateStatus("Validation failed. Fix the highlighted data in the table or selected-signal editor.", danger: true);
        }
    }

    private void ValidateSelectedRow_Click(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            UpdateStatus("Select a signal row first.");
            return;
        }

        var errors = new List<string>();
        if (ParseInt(row.Ca) is null) errors.Add("CA must be a number.");
        if ((ParseInt(row.Ioa) ?? 0) <= 0) errors.Add("IOA must be greater than zero.");
        if (ParseInt(row.TypeId) is null) errors.Add("Type ID should be a number.");
        if (string.IsNullOrWhiteSpace(row.Name)) errors.Add("Name should not be empty.");
        if (!string.IsNullOrWhiteSpace(row.FeedbackIoa) && ParseInt(row.FeedbackIoa) is null) errors.Add("Feedback IOA must be a number.");
        if (!string.IsNullOrWhiteSpace(row.StateMap) && !row.StateMap.Contains('=')) errors.Add("State map should use key=value format, for example 0=Off; 1=On.");

        if (errors.Count == 0)
        {
            UpdateStatus($"Selected row looks OK: CA={row.Ca}, IOA={row.Ioa}, Type ID={row.TypeId}.", success: true);
            return;
        }

        MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Validate selected signal", MessageBoxButton.OK, MessageBoxImage.Warning);
        UpdateStatus("Selected row needs correction before saving.", danger: true);
    }

    private bool SaveCurrent(bool closeAfter, bool forceSaveAs)
    {
        try
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);

            var profile = BuildProfileFromRows();
            profile.Validate();
            var target = SavedProfilePath;
            if (forceSaveAs || string.IsNullOrWhiteSpace(target))
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "IOA profile JSON (*.json)|*.json|All files (*.*)|*.*",
                    FileName = MakeSafeFileName(profile.ProfileName) + ".json"
                };
                if (dialog.ShowDialog(this) != true) return false;
                target = dialog.FileName;
            }
            profile.SaveToFile(target);
            SavedProfilePath = target;
            _pathBox.Text = SavedProfilePath;
            Profile = profile;
            _saved = true;
            _dirty = false;
            UpdateCounters();
            UpdateStatus($"Saved {profile.Points.Count} point(s) to {Path.GetFileName(target)}.", success: true);
            if (closeAfter)
            {
                DialogResult = true;
                Close();
            }
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save signal list", MessageBoxButton.OK, MessageBoxImage.Warning);
            UpdateStatus("Save failed. Review numeric fields, state map, and required IOA values.", danger: true);
            return false;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_dirty)
        {
            var result = MessageBox.Show(this, "Signal list has unsaved changes. Close without saving?", "Signal List Editor", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        if (_saved && DialogResult != true)
        {
            DialogResult = true;
        }
        base.OnClosing(e);
    }

    private Iec10xPointMappingProfile BuildProfileFromRows()
    {
        var profile = CloneProfile(_template);
        profile.ProfileName = string.IsNullOrWhiteSpace(_profileNameBox.Text) ? "User IOA Mapping Profile" : _profileNameBox.Text.Trim();
        profile.Source = "Edited in ARIEC60870 Signal List Editor";
        profile.Points = _rows.Select(x => x.ToPoint()).ToList();
        return profile;
    }

    private void MarkDirty()
    {
        if (_loadingRows) return;
        _dirty = true;
        UpdateCounters();
        UpdateStatus($"Unsaved changes. Rows={_rows.Count}.");
    }

    private void UpdateStatus(string text, bool success = false, bool danger = false)
    {
        _statusText.Text = text;
        _statusText.Foreground = danger ? DangerBrush : success ? SuccessBrush : TextMutedBrush;
    }

    private void UpdateCounters()
    {
        _rowCountText.Text = _rows.Count == 1 ? "1 signal" : $"{_rows.Count} signals";
        _dirtyText.Text = _dirty ? "Unsaved" : "Clean";
        _dirtyText.Foreground = _dirty ? DangerBrush : SuccessBrush;
        if (_dirtyText.Parent is StackPanel) { }
    }

    private void RefreshFilter()
    {
        var view = CollectionViewSource.GetDefaultView(_rows);
        if (view is null) return;
        var query = _searchBox.Text?.Trim();
        view.Filter = item =>
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            if (item is not SignalListEditorRow row) return false;
            return Contains(row.Ca, query) || Contains(row.Ioa, query) || Contains(row.TypeId, query) ||
                   Contains(row.Name, query) || Contains(row.Group, query) || Contains(row.SignalType, query) ||
                   Contains(row.CommandPolicy, query) || Contains(row.FeedbackIoa, query) || Contains(row.StateMap, query) ||
                   Contains(row.Mnemonic, query) || Contains(row.BayType, query) || Contains(row.Description, query);
        };
        view.Refresh();
        UpdateCounters();
    }

    private static bool Contains(string? source, string query) =>
        !string.IsNullOrEmpty(source) && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private void UpdateDetailSelection()
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            _detailScroll.DataContext = null;
            _selectionTitle.Text = "No row selected";
            _selectionSubtitle.Text = "Select a signal from the table.";
            SetPillText(_selectionBadge, "No selection", TextMutedBrush, BrushFromRgb(0xF1, 0xF5, 0xF9));
            return;
        }

        _detailScroll.DataContext = row;
        _selectionTitle.Text = string.IsNullOrWhiteSpace(row.Name) ? $"IOA {row.Ioa}" : row.Name;
        _selectionSubtitle.Text = $"CA {Safe(row.Ca)} · IOA {Safe(row.Ioa)} · Type {Safe(row.TypeId)}";
        var policy = string.IsNullOrWhiteSpace(row.CommandPolicy) ? "MonitorOnly" : row.CommandPolicy;
        var isCommand = !policy.Equals("MonitorOnly", StringComparison.OrdinalIgnoreCase);
        SetPillText(_selectionBadge, isCommand ? "Command capable" : "Monitor only", isCommand ? AccentBrush : SuccessBrush, isCommand ? AccentSoftBrush : SuccessSoftBrush);
    }

    private static string Safe(string? text) => string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();

    private static void SetPillText(Border pill, string text, Brush foreground, Brush background)
    {
        pill.Background = background;
        if (pill.Child is TextBlock block)
        {
            block.Text = text;
            block.Foreground = foreground;
        }
    }

    private static Iec10xPointMappingProfile CreateBlankProfile() => new()
    {
        ProfileName = "User IOA Mapping Profile",
        Region = "Global",
        Source = "Created in ARIEC60870 Signal List Editor",
        CommonAddress = 105,
        DefaultSettings = new Iec10xInteroperabilityDefaults
        {
            CommonAddress = 105,
            LinkAddress = 105,
            LinkAddressSize = 2,
            CauseOfTransmissionSize = 2,
            CommonAddressSize = 2,
            InformationObjectAddressSize = 3,
            BaudRate = 1200,
            SerialMode = "8E1",
            TransmissionMode = "Unbalanced",
            TcpPort = 2404
        }
    };

    private static Iec10xPointMappingProfile CloneProfile(Iec10xPointMappingProfile profile)
    {
        var json = JsonSerializer.Serialize(profile);
        return JsonSerializer.Deserialize<Iec10xPointMappingProfile>(json) ?? CreateBlankProfile();
    }

    private static string MakeSafeFileName(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((text ?? "IOA_Profile").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "IOA_Profile" : safe;
    }

    internal static int? ParseInt(string? text)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    internal static double ParseDouble(string? text, double defaultValue)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : defaultValue;
    }

    private static Brush BrushFromRgb(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed class SignalListEditorRow : INotifyPropertyChanged
{
    private string _ca = string.Empty;
    private string _ioa = string.Empty;
    private string _typeId = string.Empty;
    private string _name = string.Empty;
    private string _group = string.Empty;
    private string _signalType = string.Empty;
    private string _unit = string.Empty;
    private string _scale = "1";
    private string _expectedClass = string.Empty;
    private string _expectedCot = string.Empty;
    private string _commandPolicy = string.Empty;
    private string _feedbackIoa = string.Empty;
    private string _stateMap = string.Empty;
    private string _mnemonic = string.Empty;
    private string _bayType = string.Empty;
    private string _description = string.Empty;

    public SignalListEditorRow() { }

    public SignalListEditorRow(Iec10xPointMappingEntry point)
    {
        Ca = point.Ca?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        Ioa = point.Ioa.ToString(CultureInfo.InvariantCulture);
        TypeId = point.TypeId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        Name = point.Name;
        Group = point.Group;
        SignalType = point.SignalType;
        Unit = point.Unit;
        Scale = point.Scale.ToString(CultureInfo.InvariantCulture);
        ExpectedClass = point.ExpectedClass?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ExpectedCot = point.ExpectedCot?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        CommandPolicy = point.CommandPolicy;
        FeedbackIoa = point.FeedbackIoa?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        StateMap = point.StateMap.Count == 0 ? string.Empty : string.Join("; ", point.StateMap.Select(x => x.Key + "=" + x.Value));
        Mnemonic = point.Mnemonic;
        BayType = point.BayType;
        Description = point.Description;
    }

    public string Ca { get => _ca; set => SetField(ref _ca, value); }
    public string Ioa { get => _ioa; set => SetField(ref _ioa, value); }
    public string TypeId { get => _typeId; set => SetField(ref _typeId, value); }
    public string Name { get => _name; set => SetField(ref _name, value); }
    public string Group { get => _group; set => SetField(ref _group, value); }
    public string SignalType { get => _signalType; set => SetField(ref _signalType, value); }
    public string Unit { get => _unit; set => SetField(ref _unit, value); }
    public string Scale { get => _scale; set => SetField(ref _scale, value); }
    public string ExpectedClass { get => _expectedClass; set => SetField(ref _expectedClass, value); }
    public string ExpectedCot { get => _expectedCot; set => SetField(ref _expectedCot, value); }
    public string CommandPolicy { get => _commandPolicy; set => SetField(ref _commandPolicy, value); }
    public string FeedbackIoa { get => _feedbackIoa; set => SetField(ref _feedbackIoa, value); }
    public string StateMap { get => _stateMap; set => SetField(ref _stateMap, value); }
    public string Mnemonic { get => _mnemonic; set => SetField(ref _mnemonic, value); }
    public string BayType { get => _bayType; set => SetField(ref _bayType, value); }
    public string Description { get => _description; set => SetField(ref _description, value); }

    public SignalListEditorRow Clone() => new(ToPoint());

    public Iec10xPointMappingEntry ToPoint() => new()
    {
        Ca = SignalListEditorWindow.ParseInt(Ca),
        Ioa = SignalListEditorWindow.ParseInt(Ioa) ?? 0,
        TypeId = SignalListEditorWindow.ParseInt(TypeId),
        Name = string.IsNullOrWhiteSpace(Name) ? $"IOA {Ioa}" : Name.Trim(),
        Group = string.IsNullOrWhiteSpace(Group) ? "Unassigned" : Group.Trim(),
        SignalType = SignalType?.Trim() ?? string.Empty,
        Unit = Unit?.Trim() ?? string.Empty,
        Scale = SignalListEditorWindow.ParseDouble(Scale, 1.0),
        ExpectedClass = SignalListEditorWindow.ParseInt(ExpectedClass),
        ExpectedCot = SignalListEditorWindow.ParseInt(ExpectedCot),
        CommandPolicy = string.IsNullOrWhiteSpace(CommandPolicy) ? "MonitorOnly" : CommandPolicy.Trim(),
        FeedbackIoa = SignalListEditorWindow.ParseInt(FeedbackIoa),
        Mnemonic = Mnemonic?.Trim() ?? string.Empty,
        BayType = BayType?.Trim() ?? string.Empty,
        Description = Description?.Trim() ?? string.Empty,
        StateMap = ParseStateMap(StateMap)
    };

    private static Dictionary<string, string> ParseStateMap(string? text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return map;
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0 || idx >= part.Length - 1) continue;
            var key = part[..idx].Trim();
            var value = part[(idx + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0) map[key] = value;
        }
        return map;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref string field, string? value, [CallerMemberName] string? propertyName = null)
    {
        var normalized = value ?? string.Empty;
        if (field == normalized) return;
        field = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
