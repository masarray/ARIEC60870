// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Windows;
using ARIEC60870.Desktop.Services;

namespace ARIEC60870.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        // An independent copy of the self-contained app performs installation
        // only after the original WPF process has shut down normally.
        if (e.Args.Length == 2 &&
            string.Equals(e.Args[0], "--apply-release-update", StringComparison.OrdinalIgnoreCase))
        {
            base.OnStartup(e);
            var code = await ReleaseInstallWorker.RunAsync(e.Args[1]);
            Shutdown(code);
            return;
        }
        if (e.Args.Contains("--observation-ui-acceptance", StringComparer.OrdinalIgnoreCase))
        {
            base.OnStartup(e);
            try
            {
                var outputOption = Array.FindIndex(e.Args, arg =>
                    string.Equals(arg, "--observation-out", StringComparison.OrdinalIgnoreCase));
                var output = outputOption >= 0 && outputOption + 1 < e.Args.Length
                    ? e.Args[outputOption + 1] : Path.Join(Path.GetTempPath(), "ARIEC60870-OBS-C");
                var window = new MainWindow();
                window.Show();
                await window.RunPassiveGuiSmokeProbeAsync();
                await window.RunPassiveHistoryGuiSmokeProbeAsync();
                await window.RunObservationUiAcceptanceProbeAsync(output);
                window.Close();
                Shutdown(0);
            }
            // Deliberately broad at the executable acceptance boundary:
            // unexpected WPF errors must produce failure.txt and exit 1.
            catch (Exception ex)
            {
                var idx = Array.FindIndex(e.Args, arg => string.Equals(arg,
                    "--observation-out", StringComparison.OrdinalIgnoreCase));
                var dir = idx >= 0 && idx + 1 < e.Args.Length
                    ? e.Args[idx + 1] : Path.GetTempPath();
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Join(dir, "failure.txt"), ex.ToString());
                Console.Error.WriteLine("OBS-C acceptance failed: " + ex);
                Shutdown(1);
            }
            return;
        }
        if (e.Args.Contains("--installer-master-close-smoke", StringComparer.OrdinalIgnoreCase))
        {
            base.OnStartup(e);
            try
            {
                // Windows installer acceptance: real WPF window with active
                // simulated Master (no physical port), left running until
                // Restart Manager requests normal WM_CLOSE.
                var markerIndex = Array.FindIndex(e.Args, x =>
                    string.Equals(x, "--ready-file", StringComparison.OrdinalIgnoreCase));
                if (markerIndex < 0 || markerIndex + 1 >= e.Args.Length)
                    throw new ArgumentException("--ready-file is required for installer smoke.");
                var window = new MainWindow();
                window.Show();
                window.StartInstallerMasterCloseSmokeProbe();
                File.WriteAllText(e.Args[markerIndex + 1], "active-master-started");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Active Master installer close smoke failed: " + ex);
                Shutdown(1);
            }
            return;
        }
        if (e.Args.Contains("--passive-gui-smoke", StringComparer.OrdinalIgnoreCase))
        {
            base.OnStartup(e);
            try
            {
                // Real main application window, existing Setup dropdown and Trace workspace.
                var window = new MainWindow();
                window.Show();
                await window.RunPassiveGuiSmokeProbeAsync();
                await window.RunPassiveHistoryGuiSmokeProbeAsync();
                await window.RunEventHistoryGuiSmokeProbeAsync();
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
        // StartupUri was deliberately removed from App.xaml: normal and
        // test-mode launches now each create exactly one MainWindow.
        var main = new MainWindow();
        if (e.Args.Length == 3 &&
            string.Equals(e.Args[0], UpdateStartupHandshake.StartupArgument, StringComparison.Ordinal))
        {
            // Acknowledge only after WPF's real first frame is rendered.
            // Invalid paths/tokens silently do nothing; this is not an
            // application command or an auto-connect instruction.
            main.ContentRendered += (_, _) => UpdateStartupHandshake.WriteReadyAcknowledgement(e.Args);
        }
        main.Show();
    }
}
