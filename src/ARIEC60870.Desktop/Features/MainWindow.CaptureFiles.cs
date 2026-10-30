// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ARIEC60870.Core.Mapping;
using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Reporting;
using ARIEC60870.Master.Transport;
using Microsoft.Win32;

namespace ARIEC60870.Desktop;

public partial class MainWindow
{
    private async void OpenCapture_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open ARIEC evidence",
            Filter = "All ARIEC evidence (*.ariec;*.zip;manifest.json)|*.ariec;*.zip;manifest.json|Selected frame capture (*.ariec;*.zip)|*.ariec;*.zip|Full passive raw capture (manifest.json)|manifest.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            if (Path.GetFileName(dialog.FileName).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                await OpenPassiveRawJournalAsync(dialog.FileName);
                return;
            }

            // No UI mutation until the entire source has passed bounded streaming
            // validation, including the manifest SHA-256 when present.
            OpenCaptureButton.IsEnabled = false;
            UpdateStableHeader("Opening capture", "Verifying raw evidence…");
            var imported = await Task.Run(() => CaptureArchiveReader.Read(dialog.FileName));
            var rows = imported.Rows.Select(snapshot => new EvidenceRow(snapshot)).ToArray();
            if (rows.Length == 0)
            {
                ModernMessageBox.Show(this,
                    "The capture file does not contain frame rows.",
                    "Open capture",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ClearSessionView(clearLog: false);
            _lastOpenedAriecFile = dialog.FileName;
            _traceArchivePage = 0;
            _evidenceProtocolMode = imported.CapturedProtocolMode;
            _evidenceProfileId = imported.CapturedProfileId;
            _evidenceProfileEdition = imported.CapturedProfileEdition;
            _evidenceProfileFingerprint = imported.CapturedProfileFingerprint;
            _evidenceProfileSource = string.IsNullOrWhiteSpace(imported.CapturedProfileId)
                ? "Not recorded in source capture" : "Reopened capture: original declaration";
            _protocolTraceStore.Clear();
            _evidenceSummaryStore.Clear();
            foreach (var row in rows)
            {
                _protocolTraceStore.Add(row);
                _evidenceSummaryStore.Add(row);
                // Offline analysis is strictly read-only. Consume the same typed
                // evidence recovered from verified raw capture; never issue TX.
                _commandCasebook.Observe(row.Source,
                    IsIec10xCommandType(row.Source.TypeId) ? ResolveFeedbackIoaForCommand(row.Source) : null);
                var commandCase = _commandCasebook.FindByEvidence(row.Source);
                var commandStage = Iec10xCommandEventProjection.Project(row.Source, commandCase?.CaseId);
                if (commandStage is not null)
                    AppendProtocolEvent(commandStage, row.Source);
                var giStage = _giCasebook.Observe(row.Source);
                if (giStage is not null)
                    AppendProtocolEvent(Iec10xGiEventProjection.Project(row.Source, giStage), row.Source);
            }
            _commandCasebook.EndObservation();
            _giCasebook.EndObservation();
            _commandCasesDirty = true;
            _giCasesDirty = true;

            FrameTraceRows.ReplaceRange(rows);
            EvidenceRows.ReplaceRange(rows);
            MainTabControl.SelectedIndex = 1;
            UpdateSegmentedNav(false);
            UpdateStableHeader("Offline Capture Review",
                $"{rows.Length:N0}/{imported.TotalRows:N0} frames · {imported.TrustDescription}");
            AddUiDiagnostic(
                "Info",
                "Capture",
                "ARIEC-CAPTURE-OPENED",
                "ARIEC capture opened for offline review",
                $"Stream-checked {imported.TotalRows:N0} raw JSONL rows; latest {rows.Length:N0} in Trace · " +
                imported.TrustDescription + ". Original ZIP remains unchanged.",
                "Use Trace or Evidence Ledger selection, frame interpreter, export data, or save another selected capture block.");
            AppendSessionLog($"Offline capture: {rows.Length:N0}/{imported.TotalRows:N0} preview · " +
                imported.TrustDescription + " · " + dialog.FileName);
            if (!string.IsNullOrWhiteSpace(imported.CapturedProfileId))
                AddUiDiagnostic("Info", "Capture", "ARIEC-CAPTURE-PROFILE-DECLARED",
                    "Original profile declaration retained",
                    $"{imported.CapturedProfileId}@{imported.CapturedProfileEdition} · {imported.CapturedProfileFingerprint}. " +
                    "Capture metadata is declared, not digitally signed; current Setup is unchanged.",
                    "Review original capture widths before interpreting historic bytes.");
            if (!imported.IntegrityVerified)
                AddUiDiagnostic("Warning", "Capture", "ARIEC-CAPTURE-LEGACY-NO-HASH",
                    "Older capture has no verifiable frame hash",
                    "The ZIP was safely parsed but its original bytes have no trusted integrity reference.",
                    "Use as historical evidence only; do not claim that frame integrity has been proven.");
        }
        catch (Exception ex)
        {
            AddUiDiagnostic(
                "Error",
                "Capture",
                "ARIEC-CAPTURE-OPEN-FAILED",
                "Failed to open ARIEC capture",
                ex.Message,
                "Verify the file is a valid .ariec ZIP capture containing frames.jsonl.",
                ex);
            ModernMessageBox.Show(this,
                "Failed to open capture: " + ex.Message,
                "Open capture",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally { OpenCaptureButton.IsEnabled = true; }
    }

    private void CopySelectedCaptureForAi_Click(object sender, RoutedEventArgs e)
    {
        var rows = GetSelectedRowsForUnifiedEvidenceCapture(out _);
        if (rows.Count == 0)
        {
            ModernMessageBox.Show(this,
                "Select one or more Trace frames using Shift/Ctrl, then copy them for analysis.",
                "Copy selected frames", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(SelectedFrameShareFormatter.FormatForClipboard(rows));
        AppendSessionLog($"Copied {rows.Count} selected protocol frames as AI-readable text. No data was uploaded.");
    }

    private void SaveSelectedCapture_Click(object sender, RoutedEventArgs e)
    {
        var rows = GetSelectedRowsForUnifiedEvidenceCapture(out var sourceWorkspace);
        if (rows.Count == 0)
        {
            ModernMessageBox.Show(this,
                "Select one or more rows in Trace or Evidence Ledger first, then export the selected rows as an ARIEC capture file.",
                "Export selected capture",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = $"Export selected {sourceWorkspace} rows as ARIEC capture",
            Filter = "ARIEC capture (*.ariec)|*.ariec|Zip container (*.zip)|*.zip|All files (*.*)|*.*",
            FileName = $"ARIEC60870-{sourceWorkspace}-evidence-{DateTime.Now:yyyyMMdd-HHmmss}.ariec",
            AddExtension = true,
            DefaultExt = ".ariec"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            WriteSelectedEvidenceCapture(dialog.FileName, rows, sourceWorkspace);
            AddUiDiagnostic(
                "Info",
                "Capture",
                "ARIEC-CAPTURE-SELECTION-SAVED",
                "Selected evidence rows saved as capture",
                $"Saved {rows.Count} selected {sourceWorkspace} rows to {dialog.FileName}.",
                "The capture file is a single source of truth. Opening it rebuilds Trace and Evidence Ledger from the same frames.jsonl ledger.");
            AppendSessionLog($"Selected evidence capture saved: {sourceWorkspace}, {rows.Count} rows -> {dialog.FileName}");
            ModernMessageBox.Show(this,
                $"Selected evidence capture saved successfully.\n\nSource: {sourceWorkspace}\nRows: {rows.Count}\nFile: {dialog.FileName}",
                "Export selected capture",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AddUiDiagnostic(
                "Error",
                "Capture",
                "ARIEC-CAPTURE-SELECTION-FAILED",
                "Failed to save selected evidence capture",
                ex.Message,
                "Check destination write permission and available disk space.",
                ex);
            ModernMessageBox.Show(this,
                "Failed to save capture: " + ex.Message,
                "Export selected capture",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private IReadOnlyList<EvidenceRow> GetSelectedRowsForUnifiedEvidenceCapture(out string sourceWorkspace)
    {
        sourceWorkspace = IsEvidenceSummaryTabActive() ? "EvidenceSummary" : "ProtocolTrace";

        if (IsEvidenceSummaryTabActive())
        {
            var evidenceRows = GetSelectedEvidenceSummaryRowsForCapture();
            if (evidenceRows.Count > 0)
            {
                sourceWorkspace = "EvidenceSummary";
                return evidenceRows;
            }
        }

        var traceRows = GetSelectedProtocolTraceRowsForCapture();
        if (traceRows.Count > 0)
        {
            sourceWorkspace = "ProtocolTrace";
            return traceRows;
        }

        var fallbackEvidenceRows = GetSelectedEvidenceSummaryRowsForCapture();
        if (fallbackEvidenceRows.Count > 0)
        {
            sourceWorkspace = "EvidenceSummary";
            return fallbackEvidenceRows;
        }

        return Array.Empty<EvidenceRow>();
    }

    private IReadOnlyList<EvidenceRow> GetSelectedProtocolTraceRowsForCapture()
    {
        var selected = FrameTraceGrid?.SelectedItems
            ?.OfType<EvidenceRow>()
            .OrderBy(row => row.Sequence)
            .ToArray();

        if (selected is { Length: > 0 })
        {
            return selected;
        }

        if (FrameTraceGrid?.SelectedItem is EvidenceRow single)
        {
            return new[] { single };
        }

        return Array.Empty<EvidenceRow>();
    }

    private IReadOnlyList<EvidenceRow> GetSelectedEvidenceSummaryRowsForCapture()
    {
        var selected = EvidenceSummaryList?.SelectedItems
            ?.OfType<EvidenceRow>()
            .OrderBy(row => row.Sequence)
            .ToArray();

        if (selected is { Length: > 0 })
        {
            return selected;
        }

        if (EvidenceSummaryList?.SelectedItem is EvidenceRow single)
        {
            return new[] { single };
        }

        return Array.Empty<EvidenceRow>();
    }

    private void WriteSelectedProtocolTraceCapture(string fileName, IReadOnlyList<EvidenceRow> rows)
        => WriteSelectedEvidenceCapture(fileName, rows, "ProtocolTrace");

    private void WriteSelectedEvidenceCapture(string fileName, IReadOnlyList<EvidenceRow> rows, string sourceWorkspace)
    {
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("No evidence rows selected.");
        }

        var createdUtc = DateTime.UtcNow;
        var captureId = "ARIEC-" + createdUtc.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var framesJsonl = BuildCaptureFramesJsonl(rows);
        var framesSha256 = ComputeSha256(framesJsonl);
        var manifest = BuildSelectedCaptureManifest(captureId, createdUtc, rows, framesSha256, sourceWorkspace);
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        var retentionJson = JsonSerializer.Serialize(BuildCaptureRetentionSnapshot(sourceWorkspace), new JsonSerializerOptions { WriteIndented = true });
        var reportMarkdown = BuildSelectedCaptureMarkdownReport(manifest, rows, framesSha256);
        var analysisMarkdown = SelectedFrameShareFormatter.Format(rows);

        var target = Path.GetFullPath(fileName);
        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (File.Exists(target))
        {
            File.Delete(target);
        }

        using var archive = ZipFile.Open(target, ZipArchiveMode.Create);
        WriteZipTextEntry(archive, "manifest.json", manifestJson);
        WriteZipTextEntry(archive, "frames.jsonl", framesJsonl);
        WriteZipTextEntry(archive, "retention.json", retentionJson);
        WriteZipTextEntry(archive, "report.md", reportMarkdown);
        WriteZipTextEntry(archive, "analysis.md", analysisMarkdown);
        WriteZipTextEntry(archive, "README.md",
            "# ARIEC60870 selected frame evidence\n\n" +
            "Open this .ariec file with ARIEC60870 > Open Capture.\n" +
            "For AI or human review, read analysis.md first; report.md is the compact summary.\n" +
            "frames.jsonl holds each selected row including original raw bytes and decoder profile;\n" +
            "manifest.json records its SHA-256 integrity value.\n" +
            "The selection is NOT a complete continuous serial recording.\n" +
            "Do not infer missing frames, frame timing precision, or physical command results.\n");
        WriteZipTextEntry(archive, "hash.txt", $"frames.jsonl sha256 {framesSha256}{Environment.NewLine}");
    }

    private CaptureManifest BuildSelectedCaptureManifest(string captureId, DateTime createdUtc, IReadOnlyList<EvidenceRow> rows, string framesSha256, string sourceWorkspace)
    {
        var first = rows.First();
        var last = rows.Last();
        return new CaptureManifest
        {
            Format = "ARIEC_CAPTURE_V1",
            CaptureId = captureId,
            CaptureKind = $"Selected{sourceWorkspace}Rows",
            SourceWorkspace = sourceWorkspace,
            CreatedUtc = createdUtc,
            Application = "ARIEC60870 Evidence Analyzer",
            ProtocolMode = string.IsNullOrWhiteSpace(_evidenceProtocolMode)
                ? "Unknown (not recorded)" : _evidenceProtocolMode,
            InteroperabilityProfileId = _evidenceProfileId,
            InteroperabilityProfileEdition = _evidenceProfileEdition,
            InteroperabilityProfileFingerprint = _evidenceProfileFingerprint,
            InteroperabilityProfileSource = _evidenceProfileSource,
            TraceVerbosityMode = GetTraceVerbosityMode().ToString(),
            RowCount = rows.Count,
            FirstSequence = first.Sequence,
            LastSequence = last.Sequence,
            FirstTimestampText = first.Time,
            LastTimestampText = last.Time,
            FramesSha256 = framesSha256,
            SourceSession = new CaptureSessionSnapshot
            {
                TxCount = _txCount,
                RxCount = _rxCount,
                GiCount = _giCount,
                Class1Count = _class1Count,
                Class2Count = _class2Count,
                NoDataCount = _noDataCount,
                DpiCount = _dpiCount,
                ValueRows = ValueRows.Count,
                EventRows = RelayEventRows.Count,
                DiagnosticRows = DiagnosticRows.Count,
                TraceRowsVisible = FrameTraceRows.Count,
                TraceRowsLimit = MaxVisibleFrameTraceRows,
                TraceSuppressedRows = _traceVerbositySuppressedRows,
                BackpressureDroppedRows = _backpressureDroppedEvents,
                QueueMaxObserved = _maxPendingEvidenceDepth,
                MaxUiFlushMs = _maxUiFlushMs
            }
        };
    }

    private object BuildCaptureRetentionSnapshot(string sourceWorkspace)
    {
        return new
        {
            sourceWorkspace,
            policy = "Selected capture is generated from unified evidence rows. frames.jsonl is the single source of truth and is used to rebuild Trace and Evidence Ledger on open.",
            retention = BuildEvidenceRetentionPolicyLines().ToArray(),
            trace = new
            {
                mode = GetTraceVerbosityMode().ToString(),
                visible = FrameTraceRows.Count,
                limit = MaxVisibleFrameTraceRows,
                suppressed = _traceVerbositySuppressedRows,
                routineSuppressed = _traceVerbositySuppressedRoutine,
                supervisorySuppressed = _traceVerbositySuppressedSupervisory
            },
            evidenceSummary = new
            {
                visible = EvidenceRows.Count,
                limit = MaxVisibleEvidenceRows,
                held = IsEvidenceSummaryViewFrozen(),
                deferred = _evidenceSummaryRowsDeferredWhileFrozen
            },
            triggers = new
            {
                started = _protocolTriggerStartedCount,
                completed = _protocolTriggerCompletedCount,
                active = _activeProtocolTriggerCaptures.Count,
                preRows = TriggerPreCaptureRows,
                postRows = TriggerPostCaptureRows
            },
            backpressure = new
            {
                dropped = _backpressureDroppedEvents,
                ackNoData = _backpressureDroppedAckNoData,
                backgroundPoll = _backpressureDroppedBackgroundPoll,
                testSupervisory = _backpressureDroppedTestFrames,
                other = _backpressureDroppedOtherLowValue
            },
            proof = new
            {
                caObserved = _proofObservedCa,
                giObserved = _proofGiObserved,
                giCompleted = _proofGiCompleted,
                giNegative = _proofGiNegative,
                digitalObserved = _proofDigitalObserved,
                analogObserved = _proofAnalogObserved,
                commandObserved = _proofCommandObserved,
                commandFeedbackObserved = _proofCommandFeedbackObserved,
                monitorCoverage = $"{_lastMonitorReceivedCount}/{_lastMonitorExpectedCount}",
                digitalCoverage = $"{_lastDigitalReceivedCount}/{_lastDigitalExpectedCount}",
                analogCoverage = $"{_lastAnalogReceivedCount}/{_lastAnalogExpectedCount}"
            }
        };
    }

    private static string BuildCaptureFramesJsonl(IReadOnlyList<EvidenceRow> rows)
    {
        var builder = new StringBuilder();
        var options = new JsonSerializerOptions { WriteIndented = false };

        foreach (var row in rows)
        {
            var record = CaptureFrameRecord.FromEvidenceRow(row);
            builder.AppendLine(JsonSerializer.Serialize(record, options));
        }

        return builder.ToString();
    }

    private string BuildSelectedCaptureMarkdownReport(CaptureManifest manifest, IReadOnlyList<EvidenceRow> rows, string framesSha256)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# ARIEC60870 Selected Evidence Capture");
        builder.AppendLine();
        builder.AppendLine($"- Capture ID: `{manifest.CaptureId}`");
        builder.AppendLine($"- Format: `{manifest.Format}`");
        builder.AppendLine($"- Kind: `{manifest.CaptureKind}`");
        builder.AppendLine($"- Source workspace: `{manifest.SourceWorkspace}`");
        builder.AppendLine($"- Created UTC: `{manifest.CreatedUtc:O}`");
        builder.AppendLine($"- Protocol mode: `{manifest.ProtocolMode}`");
        builder.AppendLine($"- Declared profile: `{manifest.InteroperabilityProfileId}@{manifest.InteroperabilityProfileEdition}`");
        builder.AppendLine($"- Profile fingerprint: `{manifest.InteroperabilityProfileFingerprint}`");
        builder.AppendLine($"- Profile source: `{manifest.InteroperabilityProfileSource}` (declaration, not digital signature)");
        builder.AppendLine($"- Trace mode: `{manifest.TraceVerbosityMode}`");
        builder.AppendLine($"- Rows: `{manifest.RowCount}`");
        builder.AppendLine($"- Sequence range: `{manifest.FirstSequence}` → `{manifest.LastSequence}`");
        builder.AppendLine($"- frames.jsonl SHA256: `{framesSha256}`");
        builder.AppendLine();
        builder.AppendLine("## Evidence Retention / Capture Integrity");
        builder.AppendLine();
        foreach (var line in BuildEvidenceRetentionPolicyLines())
        {
            builder.AppendLine("- " + line);
        }
        builder.AppendLine();
        builder.AppendLine("## Selected Evidence Rows");
        builder.AppendLine();
        builder.AppendLine("| # | Time | Dir | Service | Address | Meaning | Raw |");
        builder.AppendLine("|---:|---|---|---|---|---|---|");

        foreach (var row in rows)
        {
            builder.Append("| ")
                .Append(row.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" | ")
                .Append(EscapeMarkdownTable(row.Time)).Append(" | ")
                .Append(EscapeMarkdownTable(row.Direction)).Append(" | ")
                .Append(EscapeMarkdownTable(row.ProtocolService)).Append(" | ")
                .Append(EscapeMarkdownTable(row.ProtocolAddress)).Append(" | ")
                .Append(EscapeMarkdownTable(row.ProtocolTraceMeaning)).Append(" | ")
                .Append(EscapeMarkdownTable(row.RawHex)).AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("> This capture uses `frames.jsonl` as the single evidence ledger. Opening the capture rebuilds Trace and Evidence Ledger from the same data.");
        return builder.ToString();
    }

    private static string EscapeMarkdownTable(string value)
        => (value ?? string.Empty)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

    private static void WriteZipTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content ?? string.Empty);
    }

    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }



    private sealed class CaptureManifest
    {
        public string Format { get; set; } = string.Empty;
        public string CaptureId { get; set; } = string.Empty;
        public string CaptureKind { get; set; } = string.Empty;
        public string SourceWorkspace { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public string Application { get; set; } = string.Empty;
        public string ProtocolMode { get; set; } = string.Empty;
        public string InteroperabilityProfileId { get; set; } = string.Empty;
        public string InteroperabilityProfileEdition { get; set; } = string.Empty;
        public string InteroperabilityProfileFingerprint { get; set; } = string.Empty;
        public string InteroperabilityProfileSource { get; set; } = "Not recorded";
        public string TraceVerbosityMode { get; set; } = string.Empty;
        public int RowCount { get; set; }
        public long FirstSequence { get; set; }
        public long LastSequence { get; set; }
        public string FirstTimestampText { get; set; } = string.Empty;
        public string LastTimestampText { get; set; } = string.Empty;
        public string FramesSha256 { get; set; } = string.Empty;
        public CaptureSessionSnapshot SourceSession { get; set; } = new();
    }

    private sealed class CaptureSessionSnapshot
    {
        public int TxCount { get; set; }
        public int RxCount { get; set; }
        public int GiCount { get; set; }
        public int Class1Count { get; set; }
        public int Class2Count { get; set; }
        public int NoDataCount { get; set; }
        public int DpiCount { get; set; }
        public int ValueRows { get; set; }
        public int EventRows { get; set; }
        public int DiagnosticRows { get; set; }
        public int TraceRowsVisible { get; set; }
        public int TraceRowsLimit { get; set; }
        public long TraceSuppressedRows { get; set; }
        public long BackpressureDroppedRows { get; set; }
        public long QueueMaxObserved { get; set; }
        public long MaxUiFlushMs { get; set; }
    }

    private sealed class CaptureFrameRecord
    {
        public long Sequence { get; set; }
        public string Time { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public string ProtocolName { get; set; } = string.Empty;
        public string ProtocolMode { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string DataClass { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string SignalOrAddress { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Quality { get; set; } = string.Empty;
        public string AsduType { get; set; } = string.Empty;
        public string TypeId { get; set; } = string.Empty;
        public string Cot { get; set; } = string.Empty;
        public string CotCode { get; set; } = string.Empty;
        public string LinkAddress { get; set; } = string.Empty;
        public string CommonAddress { get; set; } = string.Empty;
        public string Ioa { get; set; } = string.Empty;
        public string Acd { get; set; } = string.Empty;
        public string Dfc { get; set; } = string.Empty;
        public string RelayTime { get; set; } = string.Empty;
        public string ResponseTime { get; set; } = string.Empty;
        public string Meaning { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public int? LinkWidth { get; set; }
        public int? CotWidth { get; set; }
        public int? CaWidth { get; set; }
        public int? IoaWidth { get; set; }
        public string DecoderSchema { get; set; } = string.Empty;
        public DateTime? CaptureArrivalUtc { get; set; }
        public string PhysicalSource { get; set; } = string.Empty;
        public int? PhysicalStreamId { get; set; }
        public long? RawStreamOffset { get; set; }
        public int? SelectedObjectIndex { get; set; }

        public string RawHex { get; set; } = string.Empty;
        public string ProtocolTraceTitle { get; set; } = string.Empty;
        public string ProtocolTraceMeaning { get; set; } = string.Empty;
        public string ProtocolTraceRaw { get; set; } = string.Empty;
        public string ProtocolTraceMeta { get; set; } = string.Empty;

        public static CaptureFrameRecord FromEvidenceRow(EvidenceRow row)
            => new()
            {
                Sequence = row.Sequence,
                Time = row.Time,
                Direction = row.Direction,
                ProtocolName = row.ProtocolName,
                ProtocolMode = row.ProtocolMode,
                State = row.State,
                Category = row.Category,
                DataClass = row.DataClass,
                Service = row.ProtocolService,
                Address = row.ProtocolAddress,
                SignalOrAddress = row.SignalOrAddress,
                Value = row.SemanticState,
                Quality = row.Quality,
                AsduType = row.AsduType,
                TypeId = row.TypeId,
                Cot = row.Cot,
                CotCode = row.CotCode,
                LinkAddress = row.LinkAddress,
                CommonAddress = row.CommonAddress,
                Ioa = row.IoAddress,
                Acd = row.Acd,
                Dfc = row.Dfc,
                RelayTime = row.RelayTime,
                ResponseTime = row.ResponseTime,
                Meaning = row.ReadableMeaning,
                Detail = row.Detail,
                LinkWidth = row.Source.Frame?.LinkAddressSize,
                CotWidth = row.Source.AsduDetails?.CauseDetails?.OriginatorAddress.HasValue == true ? 2 :
                    row.Source.AsduDetails is not null ? 1 : null,
                CaWidth = GetCaptureCaWidth(row.Source),
                IoaWidth = GetCaptureIoaWidth(row.Source),
                DecoderSchema = row.Source.AsduDetails is null ? string.Empty : "IEC10X-OBJECT-V1",
                CaptureArrivalUtc = row.Source.PollingReason == "offline-capture" ? null : row.Source.TimestampUtc,
                PhysicalSource = row.Source.PhysicalSource,
                PhysicalStreamId = row.Source.PhysicalStreamId,
                RawStreamOffset = row.Source.RawStreamOffset,
                SelectedObjectIndex = row.Source.InformationObjectDetails?.Index,
                RawHex = row.RawHex,
                ProtocolTraceTitle = row.ProtocolTraceTitle,
                ProtocolTraceMeaning = row.ProtocolTraceMeaning,
                ProtocolTraceRaw = row.ProtocolTraceRaw,
                ProtocolTraceMeta = row.ProtocolTraceMeta
            };

        private static int? GetCaptureCaWidth(Iec103MasterEvidenceEvent e)
        {
            var o = e.AsduDetails?.FirstObject;
            var cotSize = e.AsduDetails?.CauseDetails?.OriginatorAddress.HasValue == true ? 2 : 1;
            if (o is null || o.InformationObjectOffset < 0) return null;
            var width = o.InformationObjectOffset - 2 - cotSize;
            return width is 1 or 2 ? width : null;
        }

        private static int? GetCaptureIoaWidth(Iec103MasterEvidenceEvent e)
        {
            var o = e.AsduDetails?.FirstObject;
            if (o is null || o.InformationObjectOffset < 0) return null;
            var width = o.InformationElementOffset - o.InformationObjectOffset;
            return width is >= 1 and <= 3 ? width : null;
        }
    }

}
