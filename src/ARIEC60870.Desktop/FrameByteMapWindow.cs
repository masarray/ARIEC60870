// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ARIEC60870.Desktop.ViewModels;

namespace ARIEC60870.Desktop;

/// <summary>
/// Native movable and resizable WPF byte-to-field inspection window.
/// Created only on explicit frame action; never touches capture callbacks.
/// </summary>
public sealed class FrameByteMapWindow : Window
{
    private readonly TextBox _diagram;

    public string DiagramText => _diagram.Text;

    public FrameByteMapWindow(EvidenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var font = Application.Current.TryFindResource("AppFont") as FontFamily ??
            new FontFamily("Inter, Segoe UI");
        Title = $"Frame #{row.Sequence} · Physical HEX byte detail";
        Width = 1020;
        Height = 650;
        MinWidth = 520;
        MinHeight = 345;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = font;
        FontSize = 12;
        Background = new SolidColorBrush(Color.FromRgb(246, 249, 252));

        var layout = new Grid { Margin = new Thickness(14, 11, 14, 11) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };
        var title = new TextBlock
        {
            Text = $"#{row.Sequence}  {row.Direction}  {row.Time}  ·  {row.ProtocolName}",
            FontSize = 12.7, FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromRgb(32, 56, 78)),
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(title);
        layout.Children.Add(header);

        _diagram = new TextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            Text = FrameByteTreeFormatter.Format(row),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11.1,
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            Background = Brushes.White,
            Foreground = new SolidColorBrush(Color.FromRgb(34, 66, 92)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(208, 219, 228)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(11, 8, 11, 8),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(_diagram, 1);
        layout.Children.Add(_diagram);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var copyDiagram = MakeButton("Copy byte diagram", font);
        copyDiagram.Click += (_, _) => Clipboard.SetText(DiagramText);
        var copyHex = MakeButton("Copy original HEX", font);
        copyHex.Margin = new Thickness(8, 0, 0, 0);
        copyHex.Click += (_, _) => Clipboard.SetText(row.RawHex);
        var close = MakeButton("Close", font);
        close.Margin = new Thickness(8, 0, 0, 0);
        close.Click += (_, _) => Close();
        controls.Children.Add(copyDiagram);
        controls.Children.Add(copyHex);
        controls.Children.Add(close);
        Grid.SetRow(controls, 2);
        layout.Children.Add(controls);
        Content = layout;
    }

    private static Button MakeButton(string text, FontFamily font) => new()
    {
        Content = text,
        FontFamily = font,
        FontSize = 11.4,
        MinHeight = 29,
        Padding = new Thickness(11, 4, 11, 4),
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(205, 218, 231)),
        BorderThickness = new Thickness(1),
        Foreground = new SolidColorBrush(Color.FromRgb(37, 58, 77)),
        Cursor = System.Windows.Input.Cursors.Hand
    };
}
