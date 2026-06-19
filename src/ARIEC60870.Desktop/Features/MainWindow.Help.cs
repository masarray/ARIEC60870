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
        if (e.Key != Key.F1)
        {
            return;
        }

        e.Handled = true;
        OpenHelpWindow(GetHelpTopicKeyForCurrentContext());
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
