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
            throw new InvalidOperationException("Stop the current monitor/replay before opening a capture.");
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
        _passiveLastReplayProfile = effectiveProfile;
        _passiveLastReplayDirectory = directory;

        using var cancel = new CancellationTokenSource();
        _passiveReplayCancellation = cancel;
        OpenCaptureButton.IsEnabled = false;
        SetupButton.IsEnabled = false;
        StartButton.IsEnabled = false;
        try
        {
            ClearSessionView(clearLog: true);
            // Raw passive journals carry their own saved decode widths. Never allow a
            // previous active session or selected-frame ZIP profile to contaminate
            // the identity of evidence imported through this separate path.
            _evidenceProtocolMode = GetSelectedProtocolMode().ToString();
            _evidenceProfileId = string.Empty;
            _evidenceProfileEdition = string.Empty;
            _evidenceProfileFingerprint = string.Empty;
            _evidenceProfileSource = isOverridden
                ? "Passive raw journal · explicit read-only Setup decode override; no project profile declared"
                : "Passive raw journal · original manifest decode widths; no project profile declared";
            PrepareLiveTraceForConnect();
            _passiveFrameSequence = 0;
            _passiveControlTimeline.Reset();
            _passiveControlNotes.Clear();
            _passiveTransactionNotes.Clear();
            _passiveFindingLastShown.Clear();
            _passiveFindingWindowEndUtc = DateTime.MinValue;
            _passiveFindingWindowCount = 0;
            _passiveReplayCorrelator = null;
            _passiveReplayGiTracker = null;
            _passiveReplayTimeline = null;
            _passiveReplayTimelines = null;
            _passiveGiNotes.Clear();
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
            long validatedFrames = 0;
            long malformedFrames = 0;
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
                    _passiveReplayTimeline = manifest.Streams.Length == 2
                        ? new PassiveIec101FailureTimeline(manifest.StartUtc) : null;
                    _passiveReplayTimelines = manifest.Streams.Length == 4
                        ? new[] { new PassiveIec101FailureTimeline(manifest.StartUtc),
                            new PassiveIec101FailureTimeline(manifest.StartUtc) } : null;
                    _passiveReplayGiTracker = new PassiveIec101GiTracker(
                        new PassiveGiOptions(LinkAddressWidth: effectiveProfile.LinkAddressWidth,
                            CotWidth: effectiveProfile.CotWidth,
                            CommonAddressWidth: effectiveProfile.CommonAddressWidth,
                            InformationAddressWidth: effectiveProfile.InformationAddressWidth));
                    _passiveAsduDecoder = new ARIEC60870.Master.Protocol.Iec10x.Iec10xAsduDecoder(
                        effectiveProfile.CotWidth, effectiveProfile.CommonAddressWidth,
                        effectiveProfile.InformationAddressWidth);
                    PassiveStatusMessageText.Text = "Reconstructing both directions…";
                }, DispatcherPriority.Background);

                var batch = new List<PassiveObservedFrame>(96);
                foreach (var observed in PassiveJournalReplay.ReadVerifiedFrames(
                    directory, effectiveProfile, cancel.Token))
                {
                    // Raw integrity and FT1.2 interpretation are distinct.
                    // Incomplete/invalid frame candidates are not valid telegrams.
                    if (observed.ChecksumValid && observed.Format != "Partial" &&
                        !(observed.Format == "Single" && observed.RawBytes.Length == 1 &&
                          observed.RawBytes[0] == 0xA2))
                        validatedFrames++;
                    else
                        malformedFrames++;
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
            _passiveReplayCorrelator?.AdvanceTime(finalManifest.EndUtc, x =>
            {
                if (_passiveReplayTimelines is { } timelines)
                    timelines[x.ObservedStream / 2].ObserveTransaction(x with
                    {
                        ObservedStream = (byte)(x.ObservedStream % 2)
                    });
                else _passiveReplayTimeline?.ObserveTransaction(x);
                ApplyPassiveTransactionEvent(x);
            });
            _passiveReplayCorrelator?.Complete(x =>
            {
                if (_passiveReplayTimelines is { } timelines)
                    timelines[x.ObservedStream / 2].ObserveTransaction(x with
                    {
                        ObservedStream = (byte)(x.ObservedStream % 2)
                    });
                else _passiveReplayTimeline?.ObserveTransaction(x);
                ApplyPassiveTransactionEvent(x);
            });
            _passiveReplayGiTracker?.AdvanceTime(finalManifest.EndUtc, x =>
            {
                if (_passiveReplayTimelines is { } timelines)
                    timelines[x.StreamId / 2].ObserveGi(x with
                    {
                        StreamId = (byte)(x.StreamId % 2)
                    });
                else _passiveReplayTimeline?.ObserveGi(x);
                ApplyPassiveGiEvent(x);
            });
            _passiveReplayGiTracker?.Complete(x =>
            {
                if (_passiveReplayTimelines is { } timelines)
                    timelines[x.StreamId / 2].ObserveGi(x with
                    {
                        StreamId = (byte)(x.StreamId % 2)
                    });
                else _passiveReplayTimeline?.ObserveGi(x);
                ApplyPassiveGiEvent(x);
            });
            _passiveReplayTimeline?.AdvanceTime(finalManifest.EndUtc, ApplyPassiveTimelineWindow);
            _passiveReplayTimeline?.Complete(ApplyPassiveTimelineWindow);
            if (_passiveReplayTimelines is { } restored)
                for (var i = 0; i < restored.Length; i++)
                {
                    var circuit = i;
                    restored[i].AdvanceTime(finalManifest.EndUtc,
                        x => ApplyPassiveTimelineWindow(x with { CircuitId = circuit }));
                    restored[i].Complete(
                        x => ApplyPassiveTimelineWindow(x with { CircuitId = circuit }));
                }
            FlushUiQueues();
            _passiveReplayCorrelator = null;
            _passiveReplayGiTracker = null;
            _passiveReplayTimeline = null;
            _passiveReplayTimelines = null;
            _passiveCaptureResult = (directory, finalManifest);
            PassiveScadaCountText.Text = finalManifest.Streams
                .Where(x => x.StreamId == 0).Sum(x => x.Bytes).ToString("N0") + " bytes" +
                (finalManifest.Streams.Length == 4
                    ? $" · B {finalManifest.Streams.Single(x => x.StreamId == 2).Bytes:N0}" : "");
            PassiveRtuCountText.Text = finalManifest.Streams
                .Where(x => x.StreamId == 1).Sum(x => x.Bytes).ToString("N0") + " bytes" +
                (finalManifest.Streams.Length == 4
                    ? $" · B {finalManifest.Streams.Single(x => x.StreamId == 3).Bytes:N0}" : "");
            PassiveVerifyButton.IsEnabled = true;
            PassiveIntegrityText.Text = "VERIFIED";
            PassiveStatusMessageText.Text = validatedFrames.ToString("N0") +
                " recognized IEC-101 frames, " + malformedFrames.ToString("N0") +
                " invalid/incomplete candidates · " +
                (isOverridden ? "RE-DECODED (Setup profile)" : "original profile") +
                " · raw journal verified.";
            UpdateStableHeader("Offline capture", validatedFrames.ToString("N0") +
                " IEC-101 frames from " + finalManifest.Streams.Length +
                " RX directions · raw verified · " +
                (isOverridden ? "Decode override" : "Recorded profile"));
            SessionSubtitleText.Text = (finalManifest.Streams.Length == 4
                ? "Offline Passive Monitor A/B · " : "Offline Passive Monitor · ") + finalManifest.SessionId +
                (isOverridden ? " · RE-DECODE" : "");
            AppendSessionLog("Verified offline raw replay: " + totalFrames + " frames from " + directory);
            AppendSessionLog("IEC-101 recorded profile: " + savedProfile.Describe());
            AppendSessionLog("IEC-101 effective replay profile: " + effectiveProfile.Describe() +
                (isOverridden ? " [NON-DESTRUCTIVE OVERRIDE; raw/manifest unchanged]" : " [saved]"));
            if (validatedFrames == 0 && finalManifest.Streams.Sum(x => x.Bytes) > 0)
                AddUiDiagnostic("Warning", "Capture", "IEC101-REPLAY-NO-VALID-FRAMES",
                    "Verified raw capture but no valid IEC-101 frames under this profile",
                    "The original serial data passed integrity verification, but the selected " +
                    "IEC-101 interpretation found no complete valid frames. Recorded profile: " +
                    savedProfile.Describe() + " | Effective profile: " + effectiveProfile.Describe(),
                    "Verify Link Address width (try 1 versus 2 octets), baud/parity of original RX acquisition " +
                    "and the real device interoperability list; reopen manifest to re-decode. The raw bytes remain unchanged.");
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
            _passiveReplayGiTracker = null;
            _passiveReplayTimeline = null;
            _passiveReplayTimelines = null;
            _passiveReplayCancellation = null;
            OpenCaptureButton.IsEnabled = true;
            SetupButton.IsEnabled = true;
            StartButton.IsEnabled = true;
        }
    }
}
