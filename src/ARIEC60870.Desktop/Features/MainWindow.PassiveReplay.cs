// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ARIEC60870.Monitor;

namespace ARIEC60870.Desktop;

/// <summary>Offline forensic review of full two-COM raw journals, inside existing workspaces.</summary>
public partial class MainWindow
{
    private CancellationTokenSource? _passiveReplayCancellation;
    private Task? _passiveReplayWork;

    private async Task OpenPassiveRawJournalAsync(string manifestPath)
    {
        if (_sessionCancellation is not null || _passiveReplayCancellation is not null)
            throw new InvalidOperationException("Stop the current recording/replay before opening a capture.");
        if (!Path.GetFileName(manifestPath).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select the manifest.json inside an ARIEC passive capture folder.");
        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
            ?? throw new InvalidDataException("Capture directory is missing.");

        // Use the *existing* Setup profile and ordinary Trace/Events/Values,
        // not a new offline-only window with a different decoder.
        if (!IsIec101PassiveMonitorSelected())
        {
            var match = ProtocolModeComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(x =>
                x.Content?.ToString()?.Contains("Passive Monitor", StringComparison.OrdinalIgnoreCase) == true);
            if (match is null) throw new InvalidOperationException("IEC-101 Passive Monitor profile unavailable.");
            ProtocolModeComboBox.SelectedItem = match;
        }

        using var cancel = new CancellationTokenSource();
        _passiveReplayCancellation = cancel;
        OpenCaptureButton.IsEnabled = false;
        SetupButton.IsEnabled = false;
        StartButton.IsEnabled = false;
        try
        {
            ClearSessionView(clearLog: true);
            _passiveFrameSequence = 0;
            _passiveCaptureResult = null;
            MainTabControl.SelectedIndex = PassiveTraceTabIndex;
            UpdateSegmentedNav(false);
            UpdateStableHeader("Loading capture", "Checking raw journal integrity and rebuilding frames.");
            PassiveCapturePathText.Text = directory;
            PassiveStatusMessageText.Text = "Verifying capture…";
            PassiveIntegrityText.Text = "Checking…";

            // One background producer, one UI consumer. At most 96 decoded frames
            // per dispatcher batch; no large List of the entire field capture and
            // no Dispatcher.Invoke per byte/frame.
            long totalFrames = 0;
            _passiveReplayWork = Task.Run(() =>
            {
                // Integrity verification happens before the first frame is displayed.
                var manifest = JsonSerializer.Deserialize<PassiveCaptureManifest>(
                    File.ReadAllText(manifestPath))
                    ?? throw new InvalidDataException("Invalid passive manifest.");
                if (manifest.CotWidth is < 1 or > 2 ||
                    manifest.CommonAddressWidth is < 1 or > 2 ||
                    manifest.InformationAddressWidth is < 1 or > 3)
                    throw new InvalidDataException("Invalid IEC-101 field widths in capture.");

                Dispatcher.Invoke(() =>
                {
                    _passiveLinkAddressWidth = manifest.LinkAddressWidth;
                    _passiveAsduDecoder = new ARIEC60870.Master.Protocol.Iec10x.Iec10xAsduDecoder(
                        manifest.CotWidth, manifest.CommonAddressWidth, manifest.InformationAddressWidth);
                    PassiveStatusMessageText.Text = "Reconstructing both directions…";
                }, DispatcherPriority.Background);

                var batch = new List<PassiveObservedFrame>(96);
                foreach (var observed in PassiveJournalReplay.ReadVerifiedFrames(directory, cancel.Token))
                {
                    batch.Add(observed);
                    if (batch.Count < 96) continue;
                    PublishBatch(batch);
                    totalFrames += batch.Count;
                    batch.Clear();
                }
                if (batch.Count > 0)
                {
                    PublishBatch(batch);
                    totalFrames += batch.Count;
                }

                void PublishBatch(List<PassiveObservedFrame> frames)
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    var snapshot = frames.ToArray();
                    Dispatcher.Invoke(() =>
                    {
                        foreach (var frame in snapshot)
                            FeedPassiveFrameToExistingViews(frame);
                        FlushUiQueues();
                    }, DispatcherPriority.Background, cancel.Token);
                }
            }, cancel.Token);
            await _passiveReplayWork;

            var finalManifest = JsonSerializer.Deserialize<PassiveCaptureManifest>(
                File.ReadAllText(manifestPath))!;
            _passiveCaptureResult = (directory, finalManifest);
            PassiveScadaCountText.Text = finalManifest.Streams
                .Where(x => x.StreamId == 0).Sum(x => x.Bytes).ToString("N0") + " bytes";
            PassiveRtuCountText.Text = finalManifest.Streams
                .Where(x => x.StreamId == 1).Sum(x => x.Bytes).ToString("N0") + " bytes";
            PassiveVerifyButton.IsEnabled = true;
            PassiveIntegrityText.Text = "VERIFIED";
            PassiveStatusMessageText.Text = totalFrames.ToString("N0") +
                " telegrams reconstructed from complete raw journals.";
            UpdateStableHeader("Offline capture", totalFrames.ToString("N0") +
                " telegrams from two receive-only directions · verified");
            SessionSubtitleText.Text = "Offline Passive Monitor · " + finalManifest.SessionId;
            AppendSessionLog("Verified offline raw replay: " + totalFrames + " frames from " + directory);
        }
        catch (OperationCanceledException)
        {
            UpdateStableHeader("Replay cancelled", "Raw source files were not modified.");
            PassiveStatusMessageText.Text = "Replay cancelled; source journal unchanged.";
        }
        catch (Exception ex)
        {
            UpdateStableHeader("Replay failed", "Capture could not be verified or decoded.");
            PassiveIntegrityText.Text = "FAILED";
            PassiveStatusMessageText.Text = ex.Message;
            throw;
        }
        finally
        {
            _passiveReplayWork = null;
            _passiveReplayCancellation = null;
            OpenCaptureButton.IsEnabled = true;
            SetupButton.IsEnabled = true;
            StartButton.IsEnabled = true;
        }
    }
}
