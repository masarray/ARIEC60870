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

    // Read only the decoder knobs. Do NOT validate or open COM ports here: an
    // offline capture remains usable on a PC that has no serial adapters.
    private PassiveReplayProfile ReadPassiveSetupReplayProfile()
    {
        var value = new PassiveReplayProfile(
            ReadComboInt(PassiveLinkWidthCombo, "Link address width"),
            ReadComboInt(PassiveCotWidthCombo, "COT width"),
            ReadComboInt(PassiveCaWidthCombo, "Common address width"),
            ReadComboInt(PassiveIoaWidthCombo, "IOA width"),
            ReadInt(PassiveFrameGapBox, "Partial-frame gap", 100, 10000),
            (PassiveLinkModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Auto");
        value.Validate();
        return value;
    }

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

        // Capture profile and current Setup can legitimately differ; present
        // an explicit choice rather than silently trusting either value.
        var originalManifest = JsonSerializer.Deserialize<PassiveCaptureManifest>(
            File.ReadAllText(manifestPath)) ?? throw new InvalidDataException("Invalid capture manifest.");
        var savedProfile = PassiveReplayProfile.FromManifest(originalManifest);
        var setupProfile = ReadPassiveSetupReplayProfile();
        var effectiveProfile = savedProfile;
        if (setupProfile != savedProfile)
        {
            var choice = ModernMessageBox.Show(this,
                "The saved IEC-101 field sizes differ from your current Setup.\n\n" +
                "Saved capture: " + savedProfile.Describe() + "\n\n" +
                "Current Setup: " + setupProfile.Describe() + "\n\n" +
                "YES: Use saved capture profile (default).\n" +
                "NO: Re-decode using current Setup (raw bytes and manifest remain untouched).\n" +
                "CANCEL: Leave this capture unopened.",
                "IEC-101 offline decode profile", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) return;
            if (choice == MessageBoxResult.No) effectiveProfile = setupProfile;
        }
        var isOverridden = effectiveProfile != savedProfile;

        using var cancel = new CancellationTokenSource();
        _passiveReplayCancellation = cancel;
        OpenCaptureButton.IsEnabled = false;
        SetupButton.IsEnabled = false;
        StartButton.IsEnabled = false;
        try
        {
            ClearSessionView(clearLog: true);
            _passiveFrameSequence = 0;
            _passiveTransactionNotes.Clear();
            _passiveFindingLastShown.Clear();
            _passiveFindingWindowEndUtc = DateTime.MinValue;
            _passiveFindingWindowCount = 0;
            _passiveReplayCorrelator = null;
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
                    _passiveLinkAddressWidth = effectiveProfile.LinkAddressWidth;
                    _passiveReplayCorrelator = new PassiveIec101TransactionCorrelator(
                        new PassiveTransactionOptions(
                            LinkAddressWidth: effectiveProfile.LinkAddressWidth,
                            LinkMode: effectiveProfile.LinkMode));
                    _passiveAsduDecoder = new ARIEC60870.Master.Protocol.Iec10x.Iec10xAsduDecoder(
                        effectiveProfile.CotWidth, effectiveProfile.CommonAddressWidth,
                        effectiveProfile.InformationAddressWidth);
                    PassiveStatusMessageText.Text = "Reconstructing both directions…";
                }, DispatcherPriority.Background);

                var batch = new List<PassiveObservedFrame>(96);
                foreach (var observed in PassiveJournalReplay.ReadVerifiedFrames(
                    directory, effectiveProfile, cancel.Token))
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
            _passiveReplayCorrelator?.AdvanceTime(finalManifest.EndUtc, ApplyPassiveTransactionEvent);
            _passiveReplayCorrelator?.Complete(ApplyPassiveTransactionEvent);
            FlushUiQueues();
            _passiveReplayCorrelator = null;
            _passiveCaptureResult = (directory, finalManifest);
            PassiveScadaCountText.Text = finalManifest.Streams
                .Where(x => x.StreamId == 0).Sum(x => x.Bytes).ToString("N0") + " bytes";
            PassiveRtuCountText.Text = finalManifest.Streams
                .Where(x => x.StreamId == 1).Sum(x => x.Bytes).ToString("N0") + " bytes";
            PassiveVerifyButton.IsEnabled = true;
            PassiveIntegrityText.Text = "VERIFIED";
            PassiveStatusMessageText.Text = totalFrames.ToString("N0") +
                " telegrams · " + (isOverridden ? "RE-DECODED (Setup profile)" : "original profile") +
                " · raw journal verified.";
            UpdateStableHeader("Offline capture", totalFrames.ToString("N0") +
                " telegrams from two RX directions · verified · " +
                (isOverridden ? "Decode override" : "Recorded profile"));
            SessionSubtitleText.Text = "Offline Passive Monitor · " + finalManifest.SessionId +
                (isOverridden ? " · RE-DECODE" : "");
            AppendSessionLog("Verified offline raw replay: " + totalFrames + " frames from " + directory);
            AppendSessionLog("IEC-101 recorded profile: " + savedProfile.Describe());
            AppendSessionLog("IEC-101 effective replay profile: " + effectiveProfile.Describe() +
                (isOverridden ? " [NON-DESTRUCTIVE OVERRIDE; raw/manifest unchanged]" : " [saved]"));
            AddUiDiagnostic("Info", "Capture", "IEC101-REPLAY-PROFILE",
                isOverridden ? "IEC-101 capture interpreted with Setup override" : "IEC-101 capture interpreted with recorded profile",
                "Original: " + savedProfile.Describe() + " | Effective: " + effectiveProfile.Describe() +
                " | Raw integrity verified. This is a decode interpretation, not a change to original evidence.",
                "Compare Frame Trace and Findings, adjust IEC-101 decoder sizes in Setup if necessary, then reopen the same manifest.");
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
            _passiveReplayCorrelator = null;
            _passiveReplayCancellation = null;
            OpenCaptureButton.IsEnabled = true;
            SetupButton.IsEnabled = true;
            StartButton.IsEnabled = true;
        }
    }
}
