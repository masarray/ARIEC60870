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
public sealed partial class SignalListEditorWindow : Window
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
    private readonly int? _observedCommonAddress;
    private readonly TextBox _profileNameBox = new();
    private readonly TextBox _pathBox = new();
    private readonly TextBox _searchBox = new();
    private readonly TextBlock _searchWatermark = new();
    private readonly ComboBox _bayFilter = new();
    private readonly ComboBox _kindFilter = new();
    private readonly List<DataGridColumn> _advancedColumns = new();
    private bool _allColumnsVisible;
    private const string AllBays = "All bays";
    private const string AllKinds = "All signals";
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _rowCountText = new();
    private readonly TextBlock _dirtyText = new();
    private readonly TextBlock _selectionTitle = new();
    private readonly TextBlock _selectionSubtitle = new();
    private readonly Border _selectionBadge = CreatePill("No selection", TextMutedBrush, BrushFromRgb(0xF1, 0xF5, 0xF9), BorderBrushSoft);
    private readonly DataGrid _grid = new();
    private readonly ScrollViewer _detailScroll = new();
    private readonly ScrollViewer _scalingScroll = new();
    private readonly Button _scalingTabButton = new();
    private readonly Button _mappingTabButton = new();
    private readonly TextBlock _scalingSourceNote = new();
    private SignalListEditorRow? _activeDetailRow;
    private bool _showScalingTab = true;
    private Iec10xPointMappingProfile _template;
    private bool _dirty;
    private bool _saved;
    private string? _protectedSyncSourcePath;
    private bool _loadingRows;
    private bool _isCellEditing;
    private string? _pendingCellEditText;
    private bool _replaceNextCellText;

    public SignalListEditorWindow(Iec10xPointMappingProfile profile, string? profilePath,
        int? observedCommonAddress = null)
    {
        _observedCommonAddress = observedCommonAddress is >= 0 and <= 65535
            ? observedCommonAddress : null;
        _template = CloneProfile(profile?.HasPoints == true ? profile : CreateBlankProfile());
        SavedProfilePath = profilePath ?? string.Empty;
        Profile = CloneProfile(_template);

        Title = "Signal Map - IEC-101/104 IOA Mapping";
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
        var root = new Grid { Margin = new Thickness(14) };
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
        var card = CreateCard(new Thickness(0, 0, 0, 11), new Thickness(16, 11, 16, 11));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.Child = grid;

        var left = new StackPanel { Orientation = Orientation.Vertical };
        left.Children.Add(new TextBlock
        {
            Text = "Signal Map",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMainBrush,
            Margin = new Thickness(0, 0, 0, 3)
        });
        left.Children.Add(new TextBlock
        {
            Text = "Find an IOA or bay, edit the selected signal, then validate and apply. All IEC settings remain available in Details.",
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
        var card = CreateCard(new Thickness(0, 0, 0, 10), new Thickness(12, 8, 12, 8));
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.Child = root;

        _profileNameBox.Text = _template.ProfileName;
        _profileNameBox.Style = CreateTextBoxStyle();
        _profileNameBox.Margin = new Thickness(0, 0, 8, 0);
        _profileNameBox.ToolTip = "Editable profile name";
        _profileNameBox.TextChanged += (_, _) => MarkDirty();
        root.Children.Add(_profileNameBox);

        _pathBox.Text = SavedProfilePath;
        _pathBox.IsReadOnly = true;
        _pathBox.Style = CreateTextBoxStyle(readOnly: true);
        _pathBox.ToolTip = "Current JSON file. Use More > Save As to create a copy.";
        Grid.SetColumn(_pathBox, 1);
        root.Children.Add(_pathBox);

        var actions = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(139) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(actions, 1);
        Grid.SetColumnSpan(actions, 2);
        root.Children.Add(actions);

        var searchArea = new Grid { Margin = new Thickness(0, 0, 7, 0) };
        _searchBox.Style = CreateTextBoxStyle();
        _searchBox.ToolTip = "Search all fields: name, CA, IOA, Type ID, bay, mnemonic, TYPE, group, unit or description (Ctrl+F)";
        _searchBox.TextChanged += (_, _) =>
        {
            _searchWatermark.Visibility = string.IsNullOrEmpty(_searchBox.Text) ?
                Visibility.Visible : Visibility.Collapsed;
            RefreshFilter();
        };
        _searchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !string.IsNullOrEmpty(_searchBox.Text))
            {
                _searchBox.Clear();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                var first = CollectionViewSource.GetDefaultView(_rows)?.Cast<SignalListEditorRow>().FirstOrDefault();
                if (first is not null) SetCurrentCell(first);
                e.Handled = first is not null;
            }
        };
        searchArea.Children.Add(_searchBox);
        _searchWatermark.Text = "Search IOA, name, bay...  Ctrl+F";
        _searchWatermark.Foreground = TextMutedBrush;
        _searchWatermark.FontSize = 11.5;
        _searchWatermark.Margin = new Thickness(10, 0, 0, 0);
        _searchWatermark.VerticalAlignment = VerticalAlignment.Center;
        _searchWatermark.IsHitTestVisible = false;
        searchArea.Children.Add(_searchWatermark);
        actions.Children.Add(searchArea);

        ConfigureFilterCombo(_bayFilter, "Filter by bay", actions, 1);
        ConfigureFilterCombo(_kindFilter, "Filter by signal kind", actions, 2);
        _kindFilter.ItemsSource = new[] { AllKinds, "Measured analog", "Digital status", "Commands" };
        _kindFilter.SelectedIndex = 0;

        var clear = CreateToolbarButton("×", (_, _) => ClearFilters(), false, false);
        clear.Margin = new Thickness(0, 0, 4, 0);
        clear.MinWidth = 29;
        clear.Padding = new Thickness(5, 5, 5, 5);
        clear.ToolTip = "Clear search and both filters";
        Grid.SetColumn(clear, 3);
        actions.Children.Add(clear);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        AddToolbarButton(toolbar, "+ Add", AddRow_Click);
        AddToolbarButton(toolbar, "Duplicate", DuplicateRow_Click);
        AddToolbarButton(toolbar, "Delete", DeleteRow_Click, danger: true);
        AddToolbarButton(toolbar, "Save", SaveList_Click);

        var more = CreateToolbarButton("More ▾", (_, _) => { }, false, false);
        var moreMenu = BuildMoreActionsMenu();
        more.ContextMenu = moreMenu;
        more.Click += (_, _) =>
        {
            moreMenu.PlacementTarget = more;
            moreMenu.Placement = PlacementMode.Bottom;
            moreMenu.IsOpen = true;
        };
        toolbar.Children.Add(more);
        AddToolbarButton(toolbar, "Save & Apply", SaveApply_Click, isPrimary: true);
        Grid.SetColumn(toolbar, 4);
        actions.Children.Add(toolbar);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _searchBox.Focus();
                _searchBox.SelectAll();
                e.Handled = true;
            }
        };
        return card;
    }

    private void ConfigureFilterCombo(ComboBox combo, string tip, Grid parent, int column)
    {
        combo.MinHeight = 33;
        combo.FontSize = 11.5;
        combo.Padding = new Thickness(6, 5, 6, 5);
        combo.Margin = new Thickness(0, 0, 7, 0);
        combo.Background = Brushes.White;
        combo.BorderBrush = BorderBrushSoft;
        combo.ToolTip = tip;
        combo.SelectionChanged += (_, _) => RefreshFilter();
        Grid.SetColumn(combo, column);
        parent.Children.Add(combo);
    }

    private ContextMenu BuildMoreActionsMenu()
    {
        var menu = new ContextMenu();
        void Item(string caption, RoutedEventHandler action)
        {
            var item = new MenuItem { Header = caption, FontSize = 12 };
            item.Click += action;
            menu.Items.Add(item);
        }
        Item("Load profile...", LoadList_Click);
        Item("Save As new profile...", SaveAs_Click);
        Item("Validate entire list", Validate_Click);
        Item("Sync 31 I/O reference...", SyncScadaIoList_Click);
        if (_observedCommonAddress.HasValue)
            Item($"Align profile to CA {_observedCommonAddress.Value}...", AlignToObservedCa_Click);
        menu.Items.Add(new Separator());
        Item("Show / hide extra table columns", (_, _) => ToggleExtraColumns());
        return menu;
    }

    private void ClearFilters()
    {
        _searchBox.Clear();
        _bayFilter.SelectedIndex = 0;
        _kindFilter.SelectedIndex = 0;
        RefreshFilter();
    }

    private UIElement BuildWorkspace()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(405) });

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

        // Daily mapping columns fit the default window. Protocol detail remains
        // editable in the inspector; advanced table columns are opt-in.
        AddTextColumn("CA", nameof(SignalListEditorRow.Ca), 60);
        AddTextColumn("IOA", nameof(SignalListEditorRow.Ioa), 92);
        AddTextColumn("TI", nameof(SignalListEditorRow.TypeId), 55);
        AddTextColumn("Signal name", nameof(SignalListEditorRow.Name), 225, minWidth: 155);
        AddTextColumn("Bay", nameof(SignalListEditorRow.BayType), 114);
        AddTextColumn("TYPE", nameof(SignalListEditorRow.TestCategory), 74);
        AddTextColumn("Mnemonic", nameof(SignalListEditorRow.Mnemonic), 94);
        AddTextColumn("Scaling", nameof(SignalListEditorRow.ScalingMode), 90);
        AddTextColumn("Unit", nameof(SignalListEditorRow.Unit), 66, advanced: true);
        AddTextColumn("Group", nameof(SignalListEditorRow.Group), 120, advanced: true);
        AddTextColumn("IEC role", nameof(SignalListEditorRow.SignalType), 150, advanced: true);
        AddTextColumn("Scale", nameof(SignalListEditorRow.Scale), 76, advanced: true);
        AddTextColumn("Class", nameof(SignalListEditorRow.ExpectedClass), 70, advanced: true);
        AddTextColumn("COT", nameof(SignalListEditorRow.ExpectedCot), 70, advanced: true);
        AddTextColumn("Command policy", nameof(SignalListEditorRow.CommandPolicy), 155, advanced: true);
        AddTextColumn("Feedback IOA", nameof(SignalListEditorRow.FeedbackIoa), 105, advanced: true);
        AddTextColumn("State map", nameof(SignalListEditorRow.StateMap), 160, advanced: true);
        AddTextColumn("Description", nameof(SignalListEditorRow.Description), 220, advanced: true);
        _grid.FrozenColumnCount = 4; // Keep CA, IOA, Type and Name visible while reviewing any column.
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
        var card = CreateCard(new Thickness(0), new Thickness(14, 12, 14, 12));
        var root = new DockPanel { LastChildFill = true };
        card.Child = root;

        // Keep signal identity and edit actions visible while switching pages.
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        _selectionTitle.Text = "No row selected";
        _selectionTitle.FontSize = 13.2;
        _selectionTitle.FontWeight = FontWeights.SemiBold;
        _selectionTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _selectionTitle.ToolTip = "Full signal name";
        _selectionTitle.Foreground = AccentDarkBrush;
        _selectionSubtitle.Text = "Select a signal from the table.";
        _selectionSubtitle.FontSize = 11.5;
        _selectionSubtitle.Foreground = TextMutedBrush;
        header.Children.Add(_selectionTitle);
        header.Children.Add(_selectionSubtitle);
        // Keep the identity line compact; the full policy remains editable on
        // Mapping & Commands without consuming another row of the viewport.
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var tabs = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ConfigureInspectorTabButton(_scalingTabButton, "Engineering Scaling", scaling: true);
        ConfigureInspectorTabButton(_mappingTabButton, "Mapping & Commands", scaling: false);
        tabs.Children.Add(_scalingTabButton);
        Grid.SetColumn(_mappingTabButton, 1);
        tabs.Children.Add(_mappingTabButton);
        DockPanel.SetDock(tabs, Dock.Top);
        root.Children.Add(tabs);

        // The toolbar already exposes Validate and Duplicate. Reserve the
        // vertical workspace for engineering input instead of redundant buttons.

        // Scaling is now a first-class page, instead of being buried after
        // seven unrelated fields inside a long inspector.
        var scaling = new StackPanel();
        _scalingSourceNote.TextWrapping = TextWrapping.Wrap;
        _scalingSourceNote.FontSize = 10.8;
        _scalingSourceNote.Foreground = TextMutedBrush;
        _scalingSourceNote.Margin = new Thickness(0, 0, 0, 6);
        scaling.Children.Add(_scalingSourceNote);

        // Put mode and unit on one line: both TwoPoint endpoint rows should
        // be available above the fold in the normal 820px editor window.
        var scalingSelectors = new Grid();
        scalingSelectors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scalingSelectors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var unitColumn = new StackPanel { Margin = new Thickness(0, 0, 5, 0) };
        AddEditorField(unitColumn, "Unit", nameof(SignalListEditorRow.Unit));
        var modeColumn = new StackPanel { Margin = new Thickness(5, 0, 0, 0) };
        AddEditorCombo(modeColumn, "Scaling mode", nameof(SignalListEditorRow.ScalingMode),
            new[] { "Factor", "TwoPoint" }, allowCustom: false);
        scalingSelectors.Children.Add(unitColumn);
        Grid.SetColumn(modeColumn, 1);
        scalingSelectors.Children.Add(modeColumn);
        scaling.Children.Add(scalingSelectors);

        // Factor and TwoPoint never compound. Display only the active mode.
        var linearPanel = new StackPanel();
        var linearStyle = new Style(typeof(StackPanel));
        linearStyle.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        var showLinear = new DataTrigger
        {
            Binding = new Binding(nameof(SignalListEditorRow.ScalingMode)),
            Value = "TwoPoint"
        };
        showLinear.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible));
        linearStyle.Triggers.Add(showLinear);
        linearPanel.Style = linearStyle;
        linearPanel.Children.Add(new TextBlock
        {
            Text = "TwoPoint: X = decoded IEC input; Y = engineering output. Factor/ratio is not applied again.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10.8,
            Foreground = TextMutedBrush,
            Margin = new Thickness(0, 0, 0, 8)
        });
        AddTwoPointPair(linearPanel, "X0 · source low", nameof(SignalListEditorRow.X0),
            "X100 · source high", nameof(SignalListEditorRow.X100));
        AddTwoPointPair(linearPanel, "Y0 · output low", nameof(SignalListEditorRow.Y0),
            "Y100 · output high", nameof(SignalListEditorRow.Y100));
        scaling.Children.Add(linearPanel);

        var factorPanel = new StackPanel();
        var factorStyle = new Style(typeof(StackPanel));
        var hideFactor = new DataTrigger
        {
            Binding = new Binding(nameof(SignalListEditorRow.ScalingMode)),
            Value = "TwoPoint"
        };
        hideFactor.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        factorStyle.Triggers.Add(hideFactor);
        factorPanel.Style = factorStyle;
        AddTwoPointPair(factorPanel, "Scale", nameof(SignalListEditorRow.Scale),
            "Offset", nameof(SignalListEditorRow.Offset));
        AddTwoPointPair(factorPanel, "Ratio primary", nameof(SignalListEditorRow.RatioPrimary),
            "Ratio secondary", nameof(SignalListEditorRow.RatioSecondary));
        AddEditorField(factorPanel, "Normalized full-scale (NVA only)", nameof(SignalListEditorRow.NormalizedFullScale),
            "Used by TI9/10/21/34; do not apply twice if the gateway has already scaled.");
        scaling.Children.Add(factorPanel);

        _scalingScroll.Content = scaling;
        _scalingScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scalingScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        var details = new StackPanel();
        AddSectionHeading(details, "Identity & naming", "Use the IOA / IEC Type from the RTU configuration.");
        AddTwoPointPair(details, "Common address (CA)", nameof(SignalListEditorRow.Ca),
            "Information object (IOA)", nameof(SignalListEditorRow.Ioa));
        AddEditorField(details, "IEC Type ID (TI)", nameof(SignalListEditorRow.TypeId), compact: true);
        AddEditorField(details, "Signal name", nameof(SignalListEditorRow.Name));
        AddTwoPointPair(details, "Bay type", nameof(SignalListEditorRow.BayType),
            "TYPE (TM / TSD...)", nameof(SignalListEditorRow.TestCategory));
        AddTwoPointPair(details, "Mnemonic", nameof(SignalListEditorRow.Mnemonic),
            "Group", nameof(SignalListEditorRow.Group));

        var composeName = CreateToolbarButton("Generate name · Bay + TYPE + Mnemonic", ComposeSignalName_Click, false, false);
        composeName.HorizontalAlignment = HorizontalAlignment.Stretch;
        composeName.Margin = new Thickness(0, 0, 0, 13);
        composeName.ToolTip = "Explicit action only. Keeps an existing trailing numeric suffix; never rewrites names automatically.";
        details.Children.Add(composeName);

        AddSectionHeading(details, "Protocol & commands", "Advanced settings remain editable here.");
        AddEditorField(details, "IEC Type / role", nameof(SignalListEditorRow.SignalType));
        AddTwoPointPair(details, "Class", nameof(SignalListEditorRow.ExpectedClass),
            "COT", nameof(SignalListEditorRow.ExpectedCot));
        AddEditorCombo(details, "Command policy", nameof(SignalListEditorRow.CommandPolicy),
            new[] { "MonitorOnly", "DoubleCommand", "SingleCommand", "SetpointNormalized", "RegulatingStep" });
        AddEditorField(details, "Feedback IOA", nameof(SignalListEditorRow.FeedbackIoa));
        AddEditorField(details, "State map", nameof(SignalListEditorRow.StateMap), "Example: 0=Off; 1=On");

        AddSectionHeading(details, "Engineering metadata & notes", "Reference limits do not scale or clamp decoded IEC values.");
        AddEditorField(details, "Engineering unit", nameof(SignalListEditorRow.Unit));
        AddTwoPointPair(details, "Reference min", nameof(SignalListEditorRow.EngineeringMin),
            "Reference max", nameof(SignalListEditorRow.EngineeringMax));
        AddEditorField(details, "Description", nameof(SignalListEditorRow.Description), multiline: true);
        var validateRow = CreateToolbarButton("Validate selected signal", ValidateSelectedRow_Click, false, false);
        validateRow.HorizontalAlignment = HorizontalAlignment.Stretch;
        validateRow.Margin = new Thickness(0, 0, 0, 7);
        details.Children.Add(validateRow);
        _detailScroll.Content = details;
        _detailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _detailScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        var tabContent = new Grid();
        tabContent.Children.Add(_scalingScroll);
        tabContent.Children.Add(_detailScroll);
        root.Children.Add(tabContent);
        ShowInspectorTab(scaling: true);
        return card;
    }

    private static void AddSectionHeading(Panel parent, string title, string hint)
    {
        parent.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12.3,
            FontWeight = FontWeights.SemiBold,
            Foreground = AccentDarkBrush,
            Margin = new Thickness(0, 6, 0, 2)
        });
        parent.Children.Add(new TextBlock
        {
            Text = hint,
            FontSize = 10.7,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TextMutedBrush,
            Margin = new Thickness(0, 0, 0, 10)
        });
    }

    private static string? ComposeSignalName(SignalListEditorRow row)
    {
        var parts = new[] { row.BayType, row.TestCategory, row.Mnemonic }
            .Select(x => string.Join(" ", (x ?? string.Empty).Split(' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .ToArray();
        if (parts.Any(string.IsNullOrWhiteSpace))
            return null;

        // Preserve optional LAB signal number, when the existing name has one.
        var lastToken = (row.Name ?? string.Empty).Trim().Split(' ',
            StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        var suffix = lastToken.All(char.IsDigit) && lastToken.Length > 0
            ? " " + lastToken : string.Empty;
        return string.Join(" ", parts) + suffix;
    }

    private void ComposeSignalName_Click(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null) return;
        var name = ComposeSignalName(row);
        if (name is null)
        {
            UpdateStatus("Complete Bay type, business TYPE and Mnemonic before generating a name.", danger: true);
            ShowInspectorTab(scaling: false);
            return;
        }
        row.Name = name; // Explicit click only; normal editing never auto-renames a signal.
        UpdateStatus($"Generated signal name: {name}. Review and save when ready.", success: true);
    }

    private void ConfigureInspectorTabButton(Button button, string text, bool scaling)
    {
        button.Content = text;
        button.Name = scaling ? "EngineeringScalingTab" : "MappingCommandsTab";
        button.FontSize = 11.3;
        button.MinHeight = 33;
        button.Margin = scaling ? new Thickness(0, 0, 4, 0) : new Thickness(4, 0, 0, 0);
        button.Padding = new Thickness(5, 7, 5, 7);
        button.Style = CreateRoundedButtonStyle();
        button.Cursor = Cursors.Hand;
        button.Click += (_, _) => ShowInspectorTab(scaling);
    }

    private void ShowInspectorTab(bool scaling)
    {
        _showScalingTab = scaling;
        _scalingScroll.Visibility = scaling ? Visibility.Visible : Visibility.Collapsed;
        _detailScroll.Visibility = scaling ? Visibility.Collapsed : Visibility.Visible;
        _scalingTabButton.Background = scaling ? AccentBrush : AccentSoftBrush;
        _scalingTabButton.Foreground = scaling ? Brushes.White : AccentDarkBrush;
        _scalingTabButton.BorderBrush = scaling ? AccentBrush : BorderBrushSoft;
        _mappingTabButton.Background = scaling ? AccentSoftBrush : AccentBrush;
        _mappingTabButton.Foreground = scaling ? AccentDarkBrush : Brushes.White;
        _mappingTabButton.BorderBrush = scaling ? BorderBrushSoft : AccentBrush;
        if (scaling) _scalingScroll.ScrollToTop();
        else _detailScroll.ScrollToTop();
    }

    private static bool IsMeasuredAnalog(SignalListEditorRow row) =>
        ParseInt(row.TypeId) is 9 or 10 or 11 or 12 or 13 or 14 or 21 or 34 or 35 or 36;

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

    private void AddTextColumn(string header, string path, double width, double? minWidth = null, bool advanced = false)
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
        if (advanced)
        {
            column.Visibility = Visibility.Collapsed;
            _advancedColumns.Add(column);
        }
    }

    private void ToggleExtraColumns()
    {
        _allColumnsVisible = !_allColumnsVisible;
        foreach (var column in _advancedColumns)
            column.Visibility = _allColumnsVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus(_allColumnsVisible
            ? "All columns shown. Scroll horizontally to inspect protocol fields."
            : "Essential columns shown. Select a row to edit full mapping and scaling.");
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
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10.8,
                Foreground = TextMutedBrush,
                Margin = new Thickness(1, 3, 0, 0)
            });
        }
        parent.Children.Add(container);
    }

    private void AddTwoPointPair(Panel parent, string leftLabel, string leftPath,
        string rightLabel, string rightPath)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 0, 5, 0) };
        var right = new StackPanel { Margin = new Thickness(5, 0, 0, 0) };
        AddEditorField(left, leftLabel, leftPath, compact: true);
        AddEditorField(right, rightLabel, rightPath, compact: true);
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        parent.Children.Add(grid);
    }

    private void AddEditorCombo(Panel parent, string label, string path,
        IEnumerable<string> values, bool allowCustom = true)
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
            IsEditable = allowCustom,
            MinHeight = 34,
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 12.5,
            Background = Brushes.White,
            BorderBrush = BorderBrushSoft,
            Foreground = TextMainBrush
        };
        combo.SetBinding(allowCustom ? ComboBox.TextProperty : Selector.SelectedItemProperty,
            new Binding(path)
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
            ClearFilters();
            _rows.Clear();
            foreach (var point in profile.Points.OrderBy(x => x.Ioa).ThenBy(x => x.TypeId ?? 0))
            {
                AddEditorRow(new SignalListEditorRow(point), markDirty: false);
            }
            _dirty = false;
            RefreshBayChoices();
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
        if (!_loadingRows) RefreshBayChoices();
        if (markDirty) MarkDirty();
        if (!_loadingRows) UpdateCounters();
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loadingRows) return;
        if (e.PropertyName == nameof(SignalListEditorRow.BayType))
            RefreshBayChoices();
        if (_searchBox.Text.Length > 0 || _bayFilter.SelectedIndex > 0 || _kindFilter.SelectedIndex > 0)
            RefreshFilter();
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
            Offset = "0",
            RatioPrimary = "1",
            RatioSecondary = "1",
            NormalizedFullScale = "1",
            ExpectedClass = "1",
            ExpectedCot = "3",
            CommandPolicy = "MonitorOnly",
            Description = "Created in Signal List Editor"
        };
        ClearFilters();
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
        RefreshBayChoices();
        RefreshFilter();
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
        ClearFilters();
        AddEditorRow(copy, markDirty: true);
        SetCurrentCell(copy);
        UpdateStatus("Selected signal duplicated. Review IOA and feedback IOA before saving.");
    }

    private void AlignToObservedCa_Click(object sender, RoutedEventArgs e)
    {
        if (!_observedCommonAddress.HasValue)
            return;

        try
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            var original = BuildProfileFromRows();
            // This operation must refuse mixed-CA profiles rather than
            // silently reinterpret points belonging to multiple stations.
            var aligned = original.RebindSingleStationCommonAddress(_observedCommonAddress.Value);
            var decision = ModernMessageBox.Show(this,
                $"Rebind ALL {aligned.Points.Count} points (including command/feedback definitions) " +
                $"to ASDU CA={_observedCommonAddress.Value}? This does NOT change IEC-101 link address, " +
                "rewrite prior wire evidence, or establish that the RTU signal list is approved. " +
                "The existing file will be preserved; Save & Apply opens Save As for a new profile copy.",
                "Confirm single-station CA alignment", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (decision != MessageBoxResult.Yes)
                return;

            _template = aligned;
            _profileNameBox.Text = aligned.ProfileName;
            LoadRowsFromProfile(aligned);
            SavedProfilePath = string.Empty; // Never overwrite the shipped/profile source.
            _pathBox.Text = "New profile copy — choose Save As";
            _saved = false;
            MarkDirty();
            UpdateStatus($"Prepared CA={_observedCommonAddress.Value} for {aligned.Points.Count} points. Save & Apply a new copy, then reconnect.");
        }
        catch (Exception ex)
        {
            ModernMessageBox.Show(this, ex.Message, "Align profile CA", MessageBoxButton.OK, MessageBoxImage.Warning);
            UpdateStatus("CA alignment refused. Inspect mixed-CA profile or invalid entries.", danger: true);
        }
    }

    private void SyncScadaIoList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            var current = BuildProfileFromRows();
            var packaged = Path.Combine(AppContext.BaseDirectory, "profiles",
                "utility_fat_iec10x_default_profile.json");
            var checkout = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "profiles",
                "utility_fat_iec10x_default_profile.json"));
            var referencePath = new[] { packaged, checkout }.FirstOrDefault(File.Exists);
            if (referencePath is null)
                throw new FileNotFoundException(
                    "The bundled 31-point SCADA 101 reference was not installed. Nothing was changed.");

            var reference = Iec10xPointMappingProfile.LoadFromFile(referencePath);
            var preview = Iec10xScadaIoListProfileSync.PrepareCopy(current, reference);
            if (preview.MatchedPoints == 0)
            {
                UpdateStatus("No exact CA=105 / IOA / Type ID matches. Other stations were left untouched.", danger: true);
                return;
            }
            if (!preview.HasChanges)
            {
                UpdateStatus($"All {preview.MatchedPoints} matching signals already agree with the bundled I/O labels and safe raw units.", success: true);
                return;
            }

            var prompt =
                $"Compare the current Signal List with the bundled SCADA 101 I/O list?\n\n" +
                $"Exact CA=105 / IOA / IEC Type matches: {preview.MatchedPoints}\n" +
                $"Signal names to update: {preview.RenamedPoints}\n" +
                $"Business labels to update: {preview.UpdatedBusinessMetadata}\n" +
                $"Uncalibrated TI11 MVar labels to change to raw counts: {preview.ResetUnverifiedReactiveUnits}\n" +
                $"Uncalibrated TI9 range updated to document 0..1000: {preview.AppliedDocumentedNormalizedRanges}\n\n" +
                "Approved Factor/TwoPoint calibration, CT/PT ratios, CA, commands, feedback, and other stations stay unchanged. " +
                "Only labels, uncalibrated legacy Q units, and a legacy TI9 identity reading for the documented 0..1000 range are reconciled. " +
                "The existing profile file remains untouched; you must Save & Apply a new copy.\n\n" +
                "Prepare the updated copy?";
            if (ModernMessageBox.Show(this, prompt, "Sync 31-point SCADA I/O list",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            _protectedSyncSourcePath ??= SavedProfilePath;
            _template = preview.UpdatedProfile;
            _profileNameBox.Text = preview.UpdatedProfile.ProfileName;
            LoadRowsFromProfile(preview.UpdatedProfile);
            SavedProfilePath = string.Empty;
            _pathBox.Text = "New copy required — choose Save As";
            _saved = false;
            MarkDirty();
            UpdateStatus($"Prepared {preview.MatchedPoints} matching points. Save & Apply to a NEW profile file; original preserved.", success: true);
        }
        catch (Exception ex)
        {
            ModernMessageBox.Show(this, ex.Message, "SCADA I/O list sync",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            UpdateStatus("SCADA I/O list sync refused. The active profile was not changed.", danger: true);
        }
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
            _protectedSyncSourcePath = null;
            _pathBox.Text = SavedProfilePath;
            _profileNameBox.Text = profile.ProfileName;
            LoadRowsFromProfile(profile);
            _dirty = false;
            UpdateCounters();
            UpdateStatus($"Loaded {profile.Points.Count} point(s) from {Path.GetFileName(dialog.FileName)}.", success: true);
        }
        catch (Exception ex)
        {
            ModernMessageBox.Show(this, ex.Message, "Load signal list", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            ModernMessageBox.Show(this, ex.Message, "Signal list validation", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        foreach (var (label, entered, requirePositive) in new[]
        {
            ("Scale", row.Scale, false), ("Offset", row.Offset, false),
            ("Ratio primary", row.RatioPrimary, true), ("Ratio secondary", row.RatioSecondary, true),
            ("Normalized full-scale", row.NormalizedFullScale, true)
        })
        {
            if (!double.TryParse(entered, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ||
                !double.IsFinite(v) || requirePositive && v <= 0)
                errors.Add($"{label} must be a finite number" + (requirePositive ? " greater than zero." : "."));
        }

        foreach (var (label, text) in new[]
        {
            ("Engineering minimum", row.EngineeringMin),
            ("Engineering maximum", row.EngineeringMax)
        })
        {
            if (!string.IsNullOrWhiteSpace(text) &&
                (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ||
                 !double.IsFinite(v)))
                errors.Add($"{label} must be a finite number or empty.");
        }

        if (!string.Equals(row.ScalingMode, "Factor", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(row.ScalingMode, "TwoPoint", StringComparison.OrdinalIgnoreCase))
            errors.Add("Scaling mode must be Factor or TwoPoint.");
        if (string.Equals(row.ScalingMode, "TwoPoint", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var point = row.ToPoint();
                new Iec10xPointMappingProfile { Points = [point] }.Validate();
            }
            catch (InvalidOperationException ex)
            {
                errors.Add(ex.Message);
            }
        }

        if (errors.Count == 0)
        {
            UpdateStatus($"Selected row looks OK: CA={row.Ca}, IOA={row.Ioa}, Type ID={row.TypeId}.", success: true);
            return;
        }

        ModernMessageBox.Show(this, string.Join(Environment.NewLine, errors), "Validate selected signal", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (!string.IsNullOrWhiteSpace(_protectedSyncSourcePath) &&
                string.Equals(Path.GetFullPath(target), Path.GetFullPath(_protectedSyncSourcePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                ModernMessageBox.Show(this,
                    "Choose a different file name. Sync must not overwrite the original Signal List.",
                    "Preserve original profile", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
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
            ModernMessageBox.Show(this, ex.Message, "Save signal list", MessageBoxButton.OK, MessageBoxImage.Warning);
            UpdateStatus("Save failed. Review numeric fields, state map, and required IOA values.", danger: true);
            return false;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_dirty)
        {
            var result = ModernMessageBox.Show(this, "Signal list has unsaved changes. Close without saving?", "Signal List Editor", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
        var shown = CollectionViewSource.GetDefaultView(_rows)?.Cast<object>().Count() ?? _rows.Count;
        _rowCountText.Text = shown == _rows.Count
            ? (_rows.Count == 1 ? "1 signal" : $"{_rows.Count} signals")
            : $"{shown} / {_rows.Count} signals";
        _dirtyText.Text = _dirty ? "Unsaved" : "Clean";
        _dirtyText.Foreground = _dirty ? DangerBrush : SuccessBrush;
        if (_dirtyText.Parent is StackPanel) { }
    }

    private void RefreshBayChoices()
    {
        var selected = _bayFilter.SelectedItem as string ?? AllBays;
        var bays = _rows.Select(x => x.BayType?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _bayFilter.ItemsSource = new[] { AllBays }.Concat(bays).ToArray();
        _bayFilter.SelectedItem = bays.Contains(selected, StringComparer.OrdinalIgnoreCase)
            ? bays.First(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase))
            : AllBays;
    }

    private void RefreshFilter()
    {
        var view = CollectionViewSource.GetDefaultView(_rows);
        if (view is null) return;
        var terms = (_searchBox.Text ?? string.Empty).Split(' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bay = _bayFilter.SelectedItem as string ?? AllBays;
        var kind = _kindFilter.SelectedItem as string ?? AllKinds;
        view.Filter = item =>
        {
            if (item is not SignalListEditorRow row) return false;
            if (bay != AllBays && !string.Equals(row.BayType?.Trim(), bay, StringComparison.OrdinalIgnoreCase))
                return false;
            var command = !string.IsNullOrWhiteSpace(row.CommandPolicy) &&
                !string.Equals(row.CommandPolicy, "MonitorOnly", StringComparison.OrdinalIgnoreCase);
            if (kind == "Commands" && !command) return false;
            if (kind == "Measured analog" && (command || !IsMeasuredAnalog(row))) return false;
            if (kind == "Digital status" && (command || IsMeasuredAnalog(row))) return false;
            return terms.All(term =>
                Contains(row.Ca, term) || Contains(row.Ioa, term) || Contains(row.TypeId, term) ||
                Contains(row.Name, term) || Contains(row.Group, term) || Contains(row.SignalType, term) ||
                Contains(row.Unit, term) || Contains(row.ScalingMode, term) ||
                Contains(row.CommandPolicy, term) || Contains(row.FeedbackIoa, term) ||
                Contains(row.StateMap, term) || Contains(row.Mnemonic, term) ||
                Contains(row.BayType, term) || Contains(row.TestCategory, term) ||
                Contains(row.Description, term));
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
            _activeDetailRow = null;
            _detailScroll.DataContext = null;
            _scalingScroll.DataContext = null;
            _scalingTabButton.IsEnabled = false;
            ShowInspectorTab(scaling: false);
            _selectionTitle.Text = "No row selected";
            _selectionSubtitle.Text = "Select a signal from the table.";
            SetPillText(_selectionBadge, "No selection", TextMutedBrush, BrushFromRgb(0xF1, 0xF5, 0xF9));
            return;
        }

        var selectionChanged = !ReferenceEquals(row, _activeDetailRow);
        _activeDetailRow = row;
        _detailScroll.DataContext = row;
        _scalingScroll.DataContext = row;
        _selectionTitle.Text = string.IsNullOrWhiteSpace(row.Name) ? $"IOA {row.Ioa}" : row.Name;
        _selectionSubtitle.Text = $"CA {Safe(row.Ca)} · IOA {Safe(row.Ioa)} · TI {Safe(row.TypeId)}";
        var policy = string.IsNullOrWhiteSpace(row.CommandPolicy) ? "MonitorOnly" : row.CommandPolicy;
        var isCommand = !policy.Equals("MonitorOnly", StringComparison.OrdinalIgnoreCase);
        SetPillText(_selectionBadge, isCommand ? "Command capable" : "Monitor only", isCommand ? AccentBrush : SuccessBrush, isCommand ? AccentSoftBrush : SuccessSoftBrush);

        var isAnalog = IsMeasuredAnalog(row) && !isCommand;
        _scalingTabButton.IsEnabled = isAnalog;
        _scalingSourceNote.Text = ParseInt(row.TypeId) switch
        {
            9 or 10 or 21 or 34 => "NVA: X is the decoded normalized fraction [-1, 1), not the signed I16 bytes.",
            11 or 12 or 35 => "SVA: X is signed I16 (-32768..32767). Raw counts are not MVar without approved RTU scaling.",
            13 or 14 or 36 => "Float: X is the decoded IEEE-754 value. Use identity where the RTU already sends engineering units.",
            _ => "Scaling is only available for IEC measured-value types."
        };
        // Default to scaling for analog measurements; keep a manually
        // selected tab while the user edits fields on the same signal.
        if (selectionChanged || !isAnalog && _showScalingTab)
            ShowInspectorTab(scaling: isAnalog);
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

    internal static double ParseEngineeringDouble(string? text, string label)
    {
        // Never silently convert an invalid operator-entered ratio/scale to 1.
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value))
            throw new InvalidOperationException($"{label} must be a finite numeric value (use dot decimal).");
        return value;
    }

    internal static double? ParseOptionalEngineeringDouble(string? text, string label) =>
        string.IsNullOrWhiteSpace(text) ? null : ParseEngineeringDouble(text, label);

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
    private string _offset = "0";
    private string _ratioPrimary = "1";
    private string _ratioSecondary = "1";
    private string _normalizedFullScale = "1";
    private string _scalingMode = "Factor";
    private string _x0 = string.Empty;
    private string _x100 = string.Empty;
    private string _y0 = string.Empty;
    private string _y100 = string.Empty;
    private string _expectedClass = string.Empty;
    private string _expectedCot = string.Empty;
    private string _commandPolicy = string.Empty;
    private string _feedbackIoa = string.Empty;
    private string _stateMap = string.Empty;
    private string _mnemonic = string.Empty;
    private string _bayType = string.Empty;
    private string _testCategory = string.Empty;
    private string _engineeringMin = string.Empty;
    private string _engineeringMax = string.Empty;
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
        Offset = point.Offset.ToString(CultureInfo.InvariantCulture);
        RatioPrimary = point.RatioPrimary.ToString(CultureInfo.InvariantCulture);
        RatioSecondary = point.RatioSecondary.ToString(CultureInfo.InvariantCulture);
        NormalizedFullScale = point.NormalizedFullScale.ToString(CultureInfo.InvariantCulture);
        ScalingMode = point.ScalingMode;
        X0 = point.X0?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        X100 = point.X100?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        Y0 = point.Y0?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        Y100 = point.Y100?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ExpectedClass = point.ExpectedClass?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ExpectedCot = point.ExpectedCot?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        CommandPolicy = point.CommandPolicy;
        FeedbackIoa = point.FeedbackIoa?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        StateMap = point.StateMap.Count == 0 ? string.Empty : string.Join("; ", point.StateMap.Select(x => x.Key + "=" + x.Value));
        Mnemonic = point.Mnemonic;
        BayType = point.BayType;
        TestCategory = point.TestCategory;
        EngineeringMin = point.EngineeringMin?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        EngineeringMax = point.EngineeringMax?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
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
    public string Offset { get => _offset; set => SetField(ref _offset, value); }
    public string RatioPrimary { get => _ratioPrimary; set => SetField(ref _ratioPrimary, value); }
    public string RatioSecondary { get => _ratioSecondary; set => SetField(ref _ratioSecondary, value); }
    public string NormalizedFullScale { get => _normalizedFullScale; set => SetField(ref _normalizedFullScale, value); }
    public string ScalingMode
    {
        get => _scalingMode;
        set => SetField(ref _scalingMode,
            string.Equals(value, "TwoPoint", StringComparison.OrdinalIgnoreCase) ? "TwoPoint" :
            string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "Factor", StringComparison.OrdinalIgnoreCase) ? "Factor" : value?.Trim());
    }
    public string X0 { get => _x0; set => SetField(ref _x0, value); }
    public string X100 { get => _x100; set => SetField(ref _x100, value); }
    public string Y0 { get => _y0; set => SetField(ref _y0, value); }
    public string Y100 { get => _y100; set => SetField(ref _y100, value); }
    public string ExpectedClass { get => _expectedClass; set => SetField(ref _expectedClass, value); }
    public string ExpectedCot { get => _expectedCot; set => SetField(ref _expectedCot, value); }
    public string CommandPolicy { get => _commandPolicy; set => SetField(ref _commandPolicy, value); }
    public string FeedbackIoa { get => _feedbackIoa; set => SetField(ref _feedbackIoa, value); }
    public string StateMap { get => _stateMap; set => SetField(ref _stateMap, value); }
    public string Mnemonic { get => _mnemonic; set => SetField(ref _mnemonic, value); }
    public string BayType { get => _bayType; set => SetField(ref _bayType, value); }
    public string TestCategory { get => _testCategory; set => SetField(ref _testCategory, value); }
    public string EngineeringMin { get => _engineeringMin; set => SetField(ref _engineeringMin, value); }
    public string EngineeringMax { get => _engineeringMax; set => SetField(ref _engineeringMax, value); }
    public string Description { get => _description; set => SetField(ref _description, value); }

    // Duplicate is an editor operation, NOT a save/validation boundary.
    // Preserve in-progress strings verbatim, even when a Scale or ratio cell
    // is empty/invalid, so a normal Duplicate click cannot throw and discard
    // unsaved Signal List changes.
    public SignalListEditorRow Clone() => new()
    {
        Ca = Ca,
        Ioa = Ioa,
        TypeId = TypeId,
        Name = Name,
        Group = Group,
        SignalType = SignalType,
        Unit = Unit,
        Scale = Scale,
        Offset = Offset,
        RatioPrimary = RatioPrimary,
        RatioSecondary = RatioSecondary,
        NormalizedFullScale = NormalizedFullScale,
        ScalingMode = ScalingMode,
        X0 = X0,
        X100 = X100,
        Y0 = Y0,
        Y100 = Y100,
        ExpectedClass = ExpectedClass,
        ExpectedCot = ExpectedCot,
        CommandPolicy = CommandPolicy,
        FeedbackIoa = FeedbackIoa,
        StateMap = StateMap,
        Mnemonic = Mnemonic,
        BayType = BayType,
        TestCategory = TestCategory,
        EngineeringMin = EngineeringMin,
        EngineeringMax = EngineeringMax,
        Description = Description
    };

    public Iec10xPointMappingEntry ToPoint() => new()
    {
        Ca = SignalListEditorWindow.ParseInt(Ca),
        Ioa = SignalListEditorWindow.ParseInt(Ioa) ?? 0,
        TypeId = SignalListEditorWindow.ParseInt(TypeId),
        Name = string.IsNullOrWhiteSpace(Name) ? $"IOA {Ioa}" : Name.Trim(),
        Group = string.IsNullOrWhiteSpace(Group) ? "Unassigned" : Group.Trim(),
        SignalType = SignalType?.Trim() ?? string.Empty,
        Unit = Unit?.Trim() ?? string.Empty,
        Scale = SignalListEditorWindow.ParseEngineeringDouble(Scale, "Scale"),
        Offset = SignalListEditorWindow.ParseEngineeringDouble(Offset, "Offset"),
        RatioPrimary = SignalListEditorWindow.ParseEngineeringDouble(RatioPrimary, "Ratio primary"),
        RatioSecondary = SignalListEditorWindow.ParseEngineeringDouble(RatioSecondary, "Ratio secondary"),
        NormalizedFullScale = SignalListEditorWindow.ParseEngineeringDouble(NormalizedFullScale, "Normalized full-scale"),
        ScalingMode = ScalingMode?.Trim() ?? string.Empty,
        X0 = SignalListEditorWindow.ParseOptionalEngineeringDouble(X0, "X0"),
        X100 = SignalListEditorWindow.ParseOptionalEngineeringDouble(X100, "X100"),
        Y0 = SignalListEditorWindow.ParseOptionalEngineeringDouble(Y0, "Y0"),
        Y100 = SignalListEditorWindow.ParseOptionalEngineeringDouble(Y100, "Y100"),
        ExpectedClass = SignalListEditorWindow.ParseInt(ExpectedClass),
        ExpectedCot = SignalListEditorWindow.ParseInt(ExpectedCot),
        CommandPolicy = string.IsNullOrWhiteSpace(CommandPolicy) ? "MonitorOnly" : CommandPolicy.Trim(),
        FeedbackIoa = SignalListEditorWindow.ParseInt(FeedbackIoa),
        Mnemonic = Mnemonic?.Trim() ?? string.Empty,
        BayType = BayType?.Trim() ?? string.Empty,
        TestCategory = TestCategory?.Trim() ?? string.Empty,
        EngineeringMin = SignalListEditorWindow.ParseOptionalEngineeringDouble(EngineeringMin, "Engineering minimum"),
        EngineeringMax = SignalListEditorWindow.ParseOptionalEngineeringDouble(EngineeringMax, "Engineering maximum"),
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
