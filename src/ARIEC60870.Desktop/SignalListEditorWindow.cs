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
using System.Windows.Data;
using ARIEC60870.Core.Mapping;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

/// <summary>
/// Lightweight WPF database editor for IEC-101/104 IOA mapping profiles.
/// The editor intentionally stays inside the same desktop app so field engineers can
/// correct a project database during FAT/SAT without touching JSON manually.
/// </summary>
public sealed class SignalListEditorWindow : Window
{
    private readonly ObservableCollection<SignalListEditorRow> _rows = new();
    private Iec10xPointMappingProfile _template;
    private readonly TextBox _profileNameBox = new();
    private readonly TextBox _pathBox = new();
    private readonly TextBlock _statusText = new();
    private readonly DataGrid _grid = new();
    private bool _dirty;
    private bool _saved;

    public SignalListEditorWindow(Iec10xPointMappingProfile profile, string? profilePath)
    {
        _template = CloneProfile(profile?.HasPoints == true ? profile : CreateBlankProfile());
        SavedProfilePath = profilePath ?? string.Empty;
        Profile = CloneProfile(_template);

        Title = "Signal List Editor - IEC-101/104 IOA Mapping";
        Width = 1180;
        Height = 760;
        MinWidth = 980;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = BuildLayout();
        LoadRowsFromProfile(_template);
        UpdateStatus("Ready. Edit rows, then Save List or Save && Apply.");
    }

    public Iec10xPointMappingProfile Profile { get; private set; }
    public string SavedProfilePath { get; private set; }

    private UIElement BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Signal List Editor",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(title);

        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(header, 1);

        _profileNameBox.Text = _template.ProfileName;
        _profileNameBox.Margin = new Thickness(0, 0, 10, 0);
        _profileNameBox.ToolTip = "Profile name saved into the JSON database";
        _profileNameBox.TextChanged += (_, _) => MarkDirty();
        header.Children.Add(_profileNameBox);

        _pathBox.Text = SavedProfilePath;
        _pathBox.IsReadOnly = true;
        _pathBox.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(_pathBox, 1);
        header.Children.Add(_pathBox);

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        AddToolbarButton(toolbar, "Add Row", AddRow_Click);
        AddToolbarButton(toolbar, "Del Row", DeleteRow_Click);
        AddToolbarButton(toolbar, "Duplicate", DuplicateRow_Click);
        AddToolbarButton(toolbar, "Load List", LoadList_Click);
        AddToolbarButton(toolbar, "Save List", SaveList_Click);
        AddToolbarButton(toolbar, "Save As", SaveAs_Click);
        AddToolbarButton(toolbar, "Validate", Validate_Click);
        AddToolbarButton(toolbar, "Save && Apply", SaveApply_Click, isPrimary: true);
        Grid.SetColumn(toolbar, 2);
        header.Children.Add(toolbar);
        root.Children.Add(header);

        _grid.ItemsSource = _rows;
        _grid.AutoGenerateColumns = false;
        _grid.CanUserAddRows = false;
        _grid.CanUserDeleteRows = false;
        _grid.EnableRowVirtualization = true;
        _grid.EnableColumnVirtualization = true;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _grid.RowHeight = 32;
        _grid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.CellEditEnding += (_, _) => MarkDirty();
        AddTextColumn("CA", nameof(SignalListEditorRow.Ca), 70);
        AddTextColumn("IOA", nameof(SignalListEditorRow.Ioa), 100);
        AddTextColumn("Type ID", nameof(SignalListEditorRow.TypeId), 78);
        AddTextColumn("Name", nameof(SignalListEditorRow.Name), 260, star: true);
        AddTextColumn("Group", nameof(SignalListEditorRow.Group), 130);
        AddTextColumn("Type / Role", nameof(SignalListEditorRow.SignalType), 180);
        AddTextColumn("Unit", nameof(SignalListEditorRow.Unit), 70);
        AddTextColumn("Scale", nameof(SignalListEditorRow.Scale), 80);
        AddTextColumn("Class", nameof(SignalListEditorRow.ExpectedClass), 70);
        AddTextColumn("COT", nameof(SignalListEditorRow.ExpectedCot), 70);
        AddTextColumn("Command policy", nameof(SignalListEditorRow.CommandPolicy), 160);
        AddTextColumn("Feedback IOA", nameof(SignalListEditorRow.FeedbackIoa), 110);
        AddTextColumn("State map", nameof(SignalListEditorRow.StateMap), 180);
        AddTextColumn("Mnemonic", nameof(SignalListEditorRow.Mnemonic), 100);
        AddTextColumn("Bay", nameof(SignalListEditorRow.BayType), 130);
        AddTextColumn("Description", nameof(SignalListEditorRow.Description), 260);
        Grid.SetRow(_grid, 2);
        root.Children.Add(_grid);

        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _statusText.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(_statusText);
        var closeButton = new Button { Content = "Close", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(10, 0, 0, 0) };
        closeButton.Click += (_, _) => Close();
        Grid.SetColumn(closeButton, 1);
        footer.Children.Add(closeButton);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private static void AddToolbarButton(Panel parent, string text, RoutedEventHandler handler, bool isPrimary = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(4, 0, 0, 0),
            FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal
        };
        button.Click += handler;
        parent.Children.Add(button);
    }

    private void AddTextColumn(string header, string path, double width, bool star = false)
    {
        var binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
        var column = new DataGridTextColumn
        {
            Header = header,
            Binding = binding,
            Width = star ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
            MinWidth = Math.Min(width, 90)
        };
        _grid.Columns.Add(column);
    }

    private void LoadRowsFromProfile(Iec10xPointMappingProfile profile)
    {
        _rows.Clear();
        foreach (var point in profile.Points.OrderBy(x => x.Ioa).ThenBy(x => x.TypeId ?? 0))
        {
            _rows.Add(new SignalListEditorRow(point));
        }
        _dirty = false;
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
            SignalType = "M_SP_TB_1 single point with CP56Time2a",
            ExpectedClass = "1",
            ExpectedCot = "3",
            CommandPolicy = "MonitorOnly"
        };
        _rows.Add(row);
        _grid.SelectedItem = row;
        _grid.ScrollIntoView(row);
        MarkDirty();
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is not SignalListEditorRow row) return;
        _rows.Remove(row);
        MarkDirty();
    }

    private void DuplicateRow_Click(object sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is not SignalListEditorRow source) return;
        var copy = source.Clone();
        copy.Ioa = ((ParseInt(source.Ioa) ?? 0) + 1).ToString(CultureInfo.InvariantCulture);
        copy.Name += " copy";
        _rows.Add(copy);
        _grid.SelectedItem = copy;
        _grid.ScrollIntoView(copy);
        MarkDirty();
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
            UpdateStatus($"Loaded {profile.Points.Count} point(s) from {Path.GetFileName(dialog.FileName)}.");
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
            UpdateStatus($"Validated OK: {profile.Points.Count} point(s), {profile.Points.Count(x => !string.IsNullOrWhiteSpace(x.CommandPolicy) && !x.CommandPolicy.Equals("MonitorOnly", StringComparison.OrdinalIgnoreCase))} command-capable point(s).");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Signal list validation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool SaveCurrent(bool closeAfter, bool forceSaveAs)
    {
        try
        {
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
            UpdateStatus($"Saved {profile.Points.Count} point(s) to {Path.GetFileName(target)}.");
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
        _dirty = true;
        UpdateStatus($"Unsaved changes. Rows={_rows.Count}.");
    }

    private void UpdateStatus(string text) => _statusText.Text = text;

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
