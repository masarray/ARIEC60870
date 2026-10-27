// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private PassiveMonitorWindow? _passiveMonitorWindow;
    private bool _closeAfterPassiveFinalization;
    private bool _passiveCloseHandlerAttached;

    private async void MainWindow_ClosingPassive(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeAfterPassiveFinalization || _passiveMonitorWindow is not { IsRecording: true } monitor)
            return;

        e.Cancel = true;
        _closeAfterPassiveFinalization = true;
        IsEnabled = false;
        try { await monitor.StopForOwnerCloseAsync(); }
        finally { Close(); }
    }

    private void OpenPassiveMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (_passiveMonitorWindow is not null)
        {
            _passiveMonitorWindow.Activate();
            return;
        }

        if (!_passiveCloseHandlerAttached)
        {
            Closing += MainWindow_ClosingPassive;
            _passiveCloseHandlerAttached = true;
        }
        var window = new PassiveMonitorWindow { Owner = this };
        _passiveMonitorWindow = window;
        window.Closed += (_, _) => _passiveMonitorWindow = null;
        window.Show();
        window.Activate();
    }
}
