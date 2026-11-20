// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    // One bounded notification timer for all control stages. No per-frame
    // timers, modal prompts, passive-capture toasts or data-grid mutations.
    private readonly DispatcherTimer _commandToastTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(1750)
    };
    private bool _commandToastReady;
    private long _commandToastVersion;
    private long _lastCommandToastSequence = -1;
    private string _lastCommandToastReference = string.Empty;

    private void ShowObservedCommandFeedback(
        Iec103MasterEvidenceEvent evidence,
        Iec10xCommandCasebook.CommandCase commandCase,
        Iec10xCommandCasebook.CommandStage stage)
    {
        // Only the active command session may surface immediate confirmations.
        // Passive replay/sniffer observations remain in Events/Controls instead.
        if (_activeControlSession is null ||
            evidence.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104) ||
            evidence.PollingReason.StartsWith("PASSIVE-", StringComparison.Ordinal))
            return;

        if (_lastCommandToastSequence == stage.Sequence &&
            _lastCommandToastReference == stage.EvidenceReference)
            return;

        var phase = stage.Phase;
        var message = phase switch
        {
            "SELECT sent" => "SELECT sent · awaiting RTU",
            "SELECT ACTCON" => "SELECT accepted · RTU ACTCON",
            "EXECUTE sent" => "EXECUTE sent · awaiting RTU",
            "EXECUTE ACTCON" => "EXECUTE accepted · RTU ACTCON",
            "EXECUTE ACTTERM" => "EXECUTE ended · verify CB",
            "RTU rejection" => "Command rejected · review",
            "RTU DEACTCON" => "Cancellation confirmed",
            _ when phase.Contains("overlapping", StringComparison.OrdinalIgnoreCase)
                || phase.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)
                || phase.Contains("out of order", StringComparison.OrdinalIgnoreCase)
                => "Command unclear · review Controls",
            _ => null
        };
        if (message is null) return;

        _lastCommandToastSequence = stage.Sequence;
        _lastCommandToastReference = stage.EvidenceReference;
        ShowCommandToastMessage(message, commandCase.Status is "Ambiguous" or "RTU rejected");
    }

    private void ShowCommandToastMessage(string message, bool attention = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowCommandToastMessage(message, attention));
            return;
        }
        if (CommandToastBorder is null || CommandToastText is null ||
            !_commandDockExpanded) return;

        if (!_commandToastReady)
        {
            _commandToastTimer.Tick += (_, _) =>
            {
                _commandToastTimer.Stop();
                var version = _commandToastVersion;
                var fade = new DoubleAnimation(1, 0,
                    new Duration(TimeSpan.FromMilliseconds(250)))
                {
                    FillBehavior = FillBehavior.HoldEnd
                };
                fade.Completed += (_, _) =>
                {
                    if (version == _commandToastVersion)
                    {
                        CommandToastBorder.Visibility = Visibility.Collapsed;
                        CommandToastBorder.BeginAnimation(UIElement.OpacityProperty, null);
                    }
                };
                CommandToastBorder.BeginAnimation(UIElement.OpacityProperty, fade);
            };
            _commandToastReady = true;
        }

        _commandToastVersion++;
        _commandToastTimer.Stop();
        CommandToastBorder.BeginAnimation(UIElement.OpacityProperty, null);
        CommandToastBorder.Opacity = 1;
        CommandToastBorder.Background = attention
            ? new SolidColorBrush(Color.FromRgb(255, 243, 226))
            : new SolidColorBrush(Color.FromRgb(230, 244, 241));
        CommandToastText.Foreground = attention
            ? new SolidColorBrush(Color.FromRgb(142, 72, 13))
            : new SolidColorBrush(Color.FromRgb(21, 94, 75));
        CommandToastText.Text = message;
        CommandToastBorder.Visibility = Visibility.Visible;
        _commandToastTimer.Start();
    }
}
