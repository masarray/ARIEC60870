// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;

namespace ARIEC60870.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Internal Windows-CI GUI load probe: exercises real XAML/resource loading
        // with no COM connection and no master transaction.
        if (e.Args.Contains("--passive-gui-smoke", StringComparer.OrdinalIgnoreCase))
        {
            StartupUri = null; // do not open main commissioning session in a smoke test
            base.OnStartup(e);
            try
            {
                var window = new PassiveMonitorWindow();
                window.Show();
                window.Close();
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Passive GUI startup probe failed: " + ex);
                Shutdown(1);
            }
            return;
        }
        base.OnStartup(e);
    }
}
