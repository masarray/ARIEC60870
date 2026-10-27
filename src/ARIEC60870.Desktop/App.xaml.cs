// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;

namespace ARIEC60870.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--passive-gui-smoke", StringComparer.OrdinalIgnoreCase))
        {
            StartupUri = null;
            base.OnStartup(e);
            try
            {
                // Real main application window, existing Setup dropdown and Trace workspace.
                var window = new MainWindow();
                window.Show();
                window.RunPassiveGuiSmokeProbe();
                window.Close();
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Integrated passive GUI smoke failed: " + ex);
                Shutdown(1);
            }
            return;
        }
        base.OnStartup(e);
    }
}
