// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Input;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private HelpWindow? _helpWindow;

    private void Help_Click(object sender, RoutedEventArgs e) => OpenHelpWindow(GetHelpTopicKeyForCurrentContext());

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // The Setup footer is the only Enter default action. Do not interpret
        // CheckBox/ToggleButton as explicit action buttons: both inherit
        // ButtonBase and the old guard silently discarded Enter on Auto-GI
        // and other focused switches (most visible in IEC-101 Single).
        if (e.Key == Key.Enter &&
            CanSubmitSetupOnEnter(ResolveSetupEnterSource(e.OriginalSource)))
        {
            e.Handled = true;
            // Route through the actual WPF button Click, exactly as the mouse
            // does: one validation/start lifecycle, no mode-specific shortcut.
            SetupConnectButton.RaiseEvent(new RoutedEventArgs(
                System.Windows.Controls.Primitives.ButtonBase.ClickEvent,
                SetupConnectButton));
            return;
        }

        if (e.Key == Key.F1)
        {
            e.Handled = true;
            OpenHelpWindow(GetHelpTopicKeyForCurrentContext());
        }
    }

    private static IInputElement? ResolveSetupEnterSource(object? originalSource)
    {
        // PreviewKeyDown tunnels through the actual setup control. Resolve
        // its nearest interactive ancestor, not a possibly stale global
        // Keyboard.FocusedElement (which differs across protocol templates).
        var element = originalSource as DependencyObject;
        while (element is not null)
        {
            if (element is System.Windows.Controls.TextBox or
                System.Windows.Controls.ComboBox or
                System.Windows.Controls.Primitives.ButtonBase)
                return (IInputElement)element;

            var visualParent = element is System.Windows.Media.Visual or
                System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : null;
            element = visualParent ?? LogicalTreeHelper.GetParent(element);
        }

        return Keyboard.FocusedElement;
    }

    // Also used by real-WPF acceptance without opening COM or invoking Click.
    private bool CanSubmitSetupOnEnter(IInputElement? focused)
    {
        if (SetupOverlay?.IsVisible != true ||
            _sessionCancellation is not null || SetupConnectButton?.IsEnabled != true)
            return false;

        // Preserve dropdown selection and multiline editing. An ordinary
        // TextBox, CheckBox, RadioButton or ToggleButton must not block Enter.
        if (focused is System.Windows.Controls.ComboBox { IsDropDownOpen: true } ||
            focused is System.Windows.Controls.TextBox { AcceptsReturn: true })
            return false;

        // Only a real action Button (Cancel/Close/Browse/etc) retains Enter.
        // CheckBox/ToggleButton derive ButtonBase but are not Button.
        return focused is not System.Windows.Controls.Button button ||
            ReferenceEquals(button, SetupConnectButton);
    }

    private void OpenHelpWindow(string topicKey)
    {
        if (_helpWindow is { IsVisible: true })
        {
            _helpWindow.SelectTopic(topicKey);
            if (_helpWindow.WindowState == WindowState.Minimized)
            {
                _helpWindow.WindowState = WindowState.Normal;
            }
            _helpWindow.Activate();
            return;
        }

        _helpWindow = new HelpWindow(topicKey)
        {
            Owner = this
        };
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Show();
    }

    private string GetHelpTopicKeyForCurrentContext()
    {
        if (IsIec101DualLinkModeSelected() && MainTabControl.SelectedIndex == 9)
        {
            return "dual-link";
        }

        return MainTabControl.SelectedIndex switch
        {
            0 => "overview",
            1 => "frame-trace",
            2 => "values",
            3 => "events",
            4 => "smart-findings",
            5 => "smart-findings",
            6 => "report",
            7 => "overview",
            8 => "frame-trace",
            9 => "dual-link",
            _ => "overview"
        };
    }
}
