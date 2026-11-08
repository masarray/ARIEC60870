// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ARIEC60870.Desktop.ViewModels;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

/// <summary>
/// Native, modeless engineering byte inspector: physical HEX grid ↔ canonical
/// decoded field ranges. Created only after user double-click/context action;
/// nothing is allocated in the IEC receive path.
/// </summary>
public sealed class FrameByteMapWindow : Window
{
    private static readonly Brush Ink = SolidColorBrushFrom(34, 59, 79);
    private static readonly Brush Secondary = SolidColorBrushFrom(94, 112, 129);
    private static readonly Brush BorderInk = SolidColorBrushFrom(211, 221, 232);
    private static readonly Brush SelectionFill = SolidColorBrushFrom(207, 230, 247);
    private static readonly Brush White = Brushes.White;

    private readonly FrameByteInspectorProjection _source;
    private readonly ListBox _fieldList;
    private readonly TextBlock _selectedTitle;
    private readonly TextBox _selectedDetail;
    private readonly ScrollViewer _hexViewport;
    private readonly List<Border> _byteCells = new();
    private readonly Dictionary<FrameByteInspectorProjection.Field, ListBoxItem> _fieldItems = new();
    private FrameByteInspectorProjection.Field? _selected;
    private int _activeByte = -1;

    public int OriginalByteCount => _source.Bytes.Count;
    public int FieldCount => _source.Fields.Count;
    public int HighlightedByteCount => _byteCells.Count(c => ReferenceEquals(c.Background, SelectionFill));
    public string? SelectedFieldKey => _selected?.Key;
    public string SelectedFieldHex => _selected?.Hex ?? "";
    public double HexViewportHeight => _hexViewport.ActualHeight;
    public string OriginalHex => _source.OriginalHex;
    public string FieldReport => _source.ExportAllFields();

    public FrameByteMapWindow(EvidenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _source = FrameByteInspectorProjection.Create(row);
        var uiFont = Application.Current.TryFindResource("AppFont") as FontFamily ??
            new FontFamily("Inter, Segoe UI");
        var hexFont = new FontFamily("Cascadia Mono, Consolas");

        Title = $"Frame #{row.Sequence} · HEX Byte Inspector";
        Width = 1050;
        Height = 670;
        MinWidth = 760;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = uiFont;
        FontSize = 11.5;
        Background = SolidColorBrushFrom(246, 249, 252);

        var root = new Grid { Margin = new Thickness(13, 12, 13, 12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(2, 0, 0, 11) };
        header.Children.Add(new TextBlock
        {
            Text = $"Frame #{row.Sequence}   {row.Direction}   {row.Time}   ·   {row.ProtocolName}",
            FontSize = 13, FontWeight = FontWeights.Medium, Foreground = Ink
        });
        header.Children.Add(new TextBlock
        {
            Text = $"{_source.Bytes.Count} original bytes  ·  {_source.Fields.Count} verified field ranges  ·  Click a field to highlight its original bytes",
            FontSize = 10.8, Foreground = Secondary, Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.13, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(split, 1);
        root.Children.Add(split);

        var left = Section("ORIGINAL HEX · OFFSET 00–0F", out var leftContent);
        Grid.SetColumn(left, 0);
        split.Children.Add(left);
        var hexPanel = new StackPanel { Margin = new Thickness(9, 10, 7, 10) };
        _hexViewport = new ScrollViewer
        {
            Content = hexPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Focusable = false
        };
        leftContent.Children.Add(_hexViewport);
        BuildHexGrid(hexPanel, hexFont);

        var right = Section("DECODED PROTOCOL FIELDS", out var rightContent);
        Grid.SetColumn(right, 2);
        split.Children.Add(right);
        _fieldList = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = White,
            FontFamily = uiFont,
            FontSize = 11.1,
            Margin = new Thickness(5, 5, 3, 5),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ScrollViewer = { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        foreach (var field in _source.Fields)
        {
            var item = new ListBoxItem
            {
                Tag = field, Padding = new Thickness(7, 6, 6, 6),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            var label = new StackPanel();
            label.Children.Add(new TextBlock
            {
                Text = field.Name, FontWeight = FontWeights.Medium,
                Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis
            });
            label.Children.Add(new TextBlock
            {
                Text = field.Group + "   ·   Offset " + field.Offset +
                    "–" + (field.EndExclusive - 1) + "   ·   " + field.Hex,
                Foreground = Secondary, FontSize = 10.1,
                FontFamily = uiFont, TextTrimming = TextTrimming.CharacterEllipsis
            });
            item.Content = label;
            _fieldItems.Add(field, item);
            _fieldList.Items.Add(item);
        }
        _fieldList.SelectionChanged += (_, _) =>
        {
            var selected = (_fieldList.SelectedItem as ListBoxItem)?.Tag as FrameByteInspectorProjection.Field;
            SelectFieldInternal(selected, selected?.Offset ?? -1);
        };
        rightContent.Children.Add(_fieldList);

        var detailBorder = new Border
        {
            Background = White, BorderBrush = BorderInk, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(11, 8, 11, 6)
        };
        var detailGrid = new Grid();
        detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        detailGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _selectedTitle = new TextBlock
        {
            Text = "Select a field or HEX byte", FontSize = 11.3,
            FontWeight = FontWeights.Medium, Foreground = Ink
        };
        _selectedDetail = new TextBox
        {
            Style = new Style(typeof(TextBox)),
            IsReadOnly = true, BorderThickness = new Thickness(0),
            Background = White, Foreground = Secondary, FontFamily = uiFont,
            FontSize = 10.8, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.IsNullOrWhiteSpace(_source.Warning)
                ? "Only source-backed fields appear here. Select a field to inspect byte origin."
                : _source.Warning
        };
        Grid.SetRow(_selectedDetail, 1);
        detailGrid.Children.Add(_selectedTitle);
        detailGrid.Children.Add(_selectedDetail);
        detailBorder.Child = detailGrid;
        Grid.SetRow(detailBorder, 2);
        root.Children.Add(detailBorder);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        AddButton(actions, "Copy selected field", uiFont,
            () => Clipboard.SetText(_source.ExportSelectedField(_selected)));
        AddButton(actions, "Copy original HEX", uiFont, () => Clipboard.SetText(row.RawHex));
        AddButton(actions, "Copy all fields", uiFont, () => Clipboard.SetText(_source.ExportAllFields()));
        AddButton(actions, "Export .txt", uiFont, () =>
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export selected-frame byte inspection",
                FileName = $"ARIEC60870-ByteInspector-{row.Sequence}.txt",
                DefaultExt = ".txt",
                Filter = "Plain text (*.txt)|*.txt|All files (*.*)|*.*",
                AddExtension = true
            };
            if (dialog.ShowDialog(this) == true)
                File.WriteAllText(dialog.FileName, _source.ExportAllFields() + Environment.NewLine, Encoding.UTF8);
        });
        AddButton(actions, "Close", uiFont, Close);
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);

        Content = root;
        if (_source.Fields.Count > 0) _fieldList.SelectedIndex = 0;
        else SelectFieldInternal(null, -1);
    }

    /// <summary>Allows pointer and WPF acceptance to test the same selection path.</summary>
    public bool SelectField(string key)
    {
        var match = _source.Fields.FirstOrDefault(x => x.Key == key);
        if (match is null) return false;
        _fieldList.SelectedItem = _fieldItems[match];
        _fieldList.ScrollIntoView(_fieldItems[match]);
        return true;
    }

    private void BuildHexGrid(StackPanel panel, FontFamily mono)
    {
        const int columns = 16;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new TextBlock { Text = "OFFSET  ", Width = 63, FontFamily = mono, FontSize = 10, Foreground = Secondary });
        for (var c = 0; c < columns; c++)
            header.Children.Add(new TextBlock
            {
                Text = c.ToString("X2"), Width = 28, FontFamily = mono,
                FontSize = 10, Foreground = Secondary, TextAlignment = TextAlignment.Center
            });
        panel.Children.Add(header);

        for (var i = 0; i < _source.Bytes.Count; i += columns)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            line.Children.Add(new TextBlock
            {
                Text = i.ToString("X4") + "   ", Width = 63, FontFamily = mono, FontSize = 10.8,
                Foreground = Secondary, VerticalAlignment = VerticalAlignment.Center
            });
            for (var c = 0; c < columns && i + c < _source.Bytes.Count; c++)
            {
                var offset = i + c;
                var cell = new Border
                {
                    Width = 28, Height = 26, Background = White,
                    CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand,
                    Tag = offset, Child = new TextBlock
                    {
                        Text = _source.Bytes[offset].ToString("X2"),
                        FontFamily = mono, FontSize = 10.8, Foreground = Ink,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                cell.MouseLeftButtonDown += (_, args) =>
                {
                    _activeByte = offset;
                    var field = _source.BestFieldAt(offset);
                    if (field is null)
                    {
                        _fieldList.SelectedItem = null;
                        SelectFieldInternal(null, offset);
                    }
                    else _fieldList.SelectedItem = _fieldItems[field];
                    args.Handled = true;
                };
                _byteCells.Add(cell);
                line.Children.Add(cell);
            }
            panel.Children.Add(line);
        }
    }

    private void SelectFieldInternal(FrameByteInspectorProjection.Field? field, int offset)
    {
        _selected = field;
        if (offset >= 0) _activeByte = offset;
        for (var i = 0; i < _byteCells.Count; i++)
        {
            var selected = field?.Contains(i) == true || (field is null && i == _activeByte);
            _byteCells[i].Background = selected ? SelectionFill : White;
            _byteCells[i].BorderBrush = selected ? SolidColorBrushFrom(84, 144, 192) : Brushes.Transparent;
        }
        _selectedTitle.Text = field is null
            ? (_activeByte >= 0 ? "Byte offset " + _activeByte : "Original source bytes")
            : field.Name + "  ·  offset " + field.Offset + "–" + (field.EndExclusive - 1);
        _selectedDetail.Text = field is null
            ? (!string.IsNullOrWhiteSpace(_source.Warning) ? _source.Warning : "No verified field mapping for this byte.")
            : field.Meaning + Environment.NewLine + "Original HEX: " + field.Hex +
                (!string.IsNullOrWhiteSpace(_source.Warning) ? Environment.NewLine + _source.Warning : "");
    }

    private static Border Section(string heading, out DockPanel contents)
    {
        contents = new DockPanel { LastChildFill = true };
        var header = new TextBlock
        {
            Text = heading, Foreground = Secondary, FontSize = 10.5,
            FontWeight = FontWeights.Medium, Margin = new Thickness(10, 10, 8, 6)
        };
        DockPanel.SetDock(header, Dock.Top);
        contents.Children.Add(header);
        return new Border
        {
            Background = White, BorderBrush = BorderInk, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Child = contents
        };
    }

    private static void AddButton(Panel actions, string label, FontFamily font, Action callback)
    {
        var b = new Button
        {
            Content = label, FontFamily = font, FontSize = 10.8,
            MinHeight = 30, Margin = new Thickness(7, 0, 0, 0),
            Padding = new Thickness(11, 4, 11, 4),
            Background = White, Foreground = Ink, BorderBrush = BorderInk,
            BorderThickness = new Thickness(1), Cursor = Cursors.Hand
        };
        b.Click += (_, _) => callback();
        actions.Children.Add(b);
    }

    private static Brush SolidColorBrushFrom(byte r, byte g, byte b) =>
        new SolidColorBrush(Color.FromRgb(r, g, b));
}
