// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Media.Effects;

namespace ARIEC60870.Desktop;

/// <summary>
/// Application-wide replacement for native WPF MessageBox.
/// Keeps dialogs visually aligned with the main ARIEC60870 shell while retaining
/// the familiar MessageBoxResult / MessageBoxButton call pattern.
/// </summary>
public static class ModernMessageBox
{
    public static MessageBoxResult Show(Window? owner, string messageBoxText)
        => Show(owner, messageBoxText, "ARIEC60870", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption)
        => Show(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button)
        => Show(owner, messageBoxText, caption, button, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        var dialog = new ModernMessageDialog(caption, messageBoxText, button, icon)
        {
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
        };
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
        return dialog.Result;
    }
}

internal sealed class ModernMessageDialog : Window
{
    private readonly MessageBoxButton _buttons;
    private readonly Brush _accentBrush;
    private readonly Brush _accentSoftBrush;
    private readonly Brush _lineBrush;
    private readonly Brush _ink900Brush;
    private readonly Brush _ink700Brush;
    private readonly Brush _ink500Brush;
    private Button? _defaultButton;

    public ModernMessageDialog(string title, string message, MessageBoxButton buttons, MessageBoxImage icon)
    {
        _buttons = buttons;
        _accentBrush = GetBrush("AccentBrush", "#2563EB");
        _accentSoftBrush = GetBrush("AccentSoftBrush", "#EFF6FF");
        _lineBrush = GetBrush("LineBrush", "#DDE7F2");
        _ink900Brush = GetBrush("Ink900Brush", "#111827");
        _ink700Brush = GetBrush("Ink700Brush", "#334155");
        _ink500Brush = GetBrush("Ink500Brush", "#64748B");

        Title = string.IsNullOrWhiteSpace(title) ? "ARIEC60870" : title;
        Width = 520;
        MinWidth = 420;
        MaxWidth = 640;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 520;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = GetFontFamily();
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        KeyDown += OnKeyDown;

        Content = BuildContent(title, message, buttons, icon);
    }

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _defaultButton?.Focus();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (Result == MessageBoxResult.None)
        {
            Result = _buttons switch
            {
                MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
                MessageBoxButton.YesNo => MessageBoxResult.No,
                MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
                _ => MessageBoxResult.OK
            };
        }

        base.OnClosing(e);
    }

    private UIElement BuildContent(string title, string message, MessageBoxButton buttons, MessageBoxImage icon)
    {
        var shell = new Border
        {
            CornerRadius = new CornerRadius(22),
            BorderBrush = _lineBrush,
            BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(
                ColorFrom("#FFFFFFFF"),
                ColorFrom("#F7FBFFFF"),
                new Point(0, 0),
                new Point(1, 1)),
            Padding = new Thickness(18),
            Effect = new DropShadowEffect
            {
                Color = ColorFrom("#172033"),
                BlurRadius = 28,
                ShadowDepth = 8,
                Opacity = 0.14
            }
        };

        shell.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch (InvalidOperationException) { }
            }
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconBadge = CreateIconBadge(icon);
        Grid.SetColumn(iconBadge, 0);
        header.Children.Add(iconBadge);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(title) ? ResolveTitle(icon) : title,
            FontSize = 16.2,
            FontWeight = FontWeights.SemiBold,
            Foreground = _ink900Brush,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var captionText = new TextBlock
        {
            Text = ResolveCaption(icon),
            FontSize = 10.8,
            Foreground = _ink500Brush,
            Margin = new Thickness(0, 3, 0, 0)
        };
        titleStack.Children.Add(titleText);
        titleStack.Children.Add(captionText);
        Grid.SetColumn(titleStack, 2);
        header.Children.Add(titleStack);

        var closeButton = CreateChromeCloseButton();
        Grid.SetColumn(closeButton, 3);
        header.Children.Add(closeButton);

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var messageBox = new Border
        {
            Background = Brushes.White,
            BorderBrush = _lineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14, 12, 14, 12)
        };
        var scroll = new ScrollViewer
        {
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        scroll.Content = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(message) ? "-" : message,
            FontSize = 12.8,
            LineHeight = 18,
            Foreground = _ink700Brush,
            TextWrapping = TextWrapping.Wrap
        };
        messageBox.Child = scroll;
        Grid.SetRow(messageBox, 2);
        root.Children.Add(messageBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        foreach (var button in CreateDialogButtons(buttons))
        {
            buttonPanel.Children.Add(button);
        }

        Grid.SetRow(buttonPanel, 4);
        root.Children.Add(buttonPanel);

        shell.Child = root;
        return new Grid { Margin = new Thickness(18), Children = { shell } };
    }

    private Border CreateIconBadge(MessageBoxImage icon)
    {
        var (text, foreground, background, border) = icon switch
        {
            MessageBoxImage.Warning => ("!", "#B45309", "#FFF7ED", "#FED7AA"),
            MessageBoxImage.Error => ("×", "#DC2626", "#FEF2F2", "#FECACA"),
            MessageBoxImage.Question => ("?", "#2563EB", "#EFF6FF", "#BFDBFE"),
            MessageBoxImage.Information => ("i", "#2563EB", "#EFF6FF", "#BFDBFE"),
            _ => ("i", "#2563EB", "#EFF6FF", "#BFDBFE")
        };

        return new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(14),
            Background = BrushFrom(background),
            BorderBrush = BrushFrom(border),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                Foreground = BrushFrom(foreground),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }

    private Button CreateChromeCloseButton()
    {
        var button = new Button
        {
            Content = "×",
            Width = 32,
            Height = 32,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Background = BrushFrom("#F8FAFC"),
            BorderBrush = _lineBrush,
            Foreground = _ink500Brush,
            Cursor = Cursors.Hand,
            ToolTip = "Close"
        };
        button.Style = CreateRoundedButtonStyle(BrushFrom("#F1F5F9"), BrushFrom("#E2E8F0"));
        button.Click += (_, _) => Close();
        return button;
    }

    private Button[] CreateDialogButtons(MessageBoxButton buttons)
        => buttons switch
        {
            MessageBoxButton.OKCancel => new[]
            {
                CreateDialogButton("Cancel", MessageBoxResult.Cancel, isPrimary: false, isDefault: false),
                CreateDialogButton("OK", MessageBoxResult.OK, isPrimary: true, isDefault: true)
            },
            MessageBoxButton.YesNo => new[]
            {
                CreateDialogButton("No", MessageBoxResult.No, isPrimary: false, isDefault: false),
                CreateDialogButton("Yes", MessageBoxResult.Yes, isPrimary: true, isDefault: true)
            },
            MessageBoxButton.YesNoCancel => new[]
            {
                CreateDialogButton("Cancel", MessageBoxResult.Cancel, isPrimary: false, isDefault: false),
                CreateDialogButton("No", MessageBoxResult.No, isPrimary: false, isDefault: false),
                CreateDialogButton("Yes", MessageBoxResult.Yes, isPrimary: true, isDefault: true)
            },
            _ => new[] { CreateDialogButton("OK", MessageBoxResult.OK, isPrimary: true, isDefault: true) }
        };

    private Button CreateDialogButton(string text, MessageBoxResult result, bool isPrimary, bool isDefault)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 92,
            Height = 36,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Cursor = Cursors.Hand,
            IsDefault = isDefault,
            IsCancel = result is MessageBoxResult.Cancel or MessageBoxResult.No,
            Background = isPrimary ? _accentBrush : BrushFrom("#F8FAFC"),
            BorderBrush = isPrimary ? _accentBrush : _lineBrush,
            Foreground = isPrimary ? Brushes.White : _ink700Brush
        };

        if (isDefault)
        {
            _defaultButton = button;
        }

        button.Style = CreateRoundedButtonStyle(
            isPrimary ? BrushFrom("#1D4ED8") : BrushFrom("#F1F5F9"),
            isPrimary ? BrushFrom("#1D4ED8") : BrushFrom("#E2E8F0"));
        button.Click += (_, _) =>
        {
            Result = result;
            try
            {
                DialogResult = true;
            }
            catch (InvalidOperationException)
            {
                Close();
            }
        };
        return button;
    }

    private Style CreateRoundedButtonStyle(Brush hoverBackground, Brush hoverBorder)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        border.SetValue(Border.PaddingProperty, new Thickness(12, 0, 12, 0));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Control.BorderThickness)) { RelativeSource = RelativeSource.TemplatedParent });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty, hoverBackground),
                new Setter(Control.BorderBrushProperty, hoverBorder)
            }
        });
        template.Triggers.Add(new Trigger
        {
            Property = Button.IsPressedProperty,
            Value = true,
            Setters = { new Setter(UIElement.OpacityProperty, 0.88) }
        });
        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false,
            Setters = { new Setter(UIElement.OpacityProperty, 0.45) }
        });

        return new Style(typeof(Button))
        {
            Setters =
            {
                new Setter(Control.TemplateProperty, template),
                new Setter(Control.BorderThicknessProperty, new Thickness(1)),
                new Setter(Control.FontFamilyProperty, GetFontFamily())
            }
        };
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        Result = _buttons switch
        {
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.OK
        };
        Close();
    }

    private static string ResolveTitle(MessageBoxImage icon)
        => icon switch
        {
            MessageBoxImage.Warning => "Attention required",
            MessageBoxImage.Error => "Action failed",
            MessageBoxImage.Question => "Confirm action",
            MessageBoxImage.Information => "Information",
            _ => "ARIEC60870"
        };

    private static string ResolveCaption(MessageBoxImage icon)
        => icon switch
        {
            MessageBoxImage.Warning => "Review before continuing",
            MessageBoxImage.Error => "The operation could not be completed",
            MessageBoxImage.Question => "Choose how to continue",
            MessageBoxImage.Information => "System message",
            _ => "System message"
        };

    private static Brush GetBrush(string key, string fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? BrushFrom(fallback);

    private static FontFamily GetFontFamily()
        => Application.Current?.TryFindResource("AppFont") as FontFamily ?? new FontFamily("Segoe UI");

    private static SolidColorBrush BrushFrom(string hex)
    {
        var brush = new SolidColorBrush(ColorFrom(hex));
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    private static Color ColorFrom(string hex)
        => (Color)ColorConverter.ConvertFromString(hex)!;
}
