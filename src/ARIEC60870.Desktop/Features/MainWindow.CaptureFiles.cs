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
    private void OpenCapture_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open ARIEC capture",
            Filter = "ARIEC capture (*.ariec;*.zip)|*.ariec;*.zip|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var rows = ReadCaptureRows(dialog.FileName);
            if (rows.Count == 0)
            {
                MessageBox.Show(this,
                    "The capture file does not contain frame rows.",
                    "Open capture",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ClearSessionView(clearLog: false);
            _protocolTraceStore.Clear();
            _evidenceSummaryStore.Clear();
            foreach (var row in rows)
            {
                _protocolTraceStore.Add(row);
                _evidenceSummaryStore.Add(row);
            }

            FrameTraceRows.ReplaceRange(rows);
            EvidenceRows.ReplaceRange(rows);
            MainTabControl.SelectedIndex = 1;
            UpdateSegmentedNav(false);
            UpdateStableHeader("Offline Capture Review", $"{rows.Count} unified evidence rows loaded from {Path.GetFileName(dialog.FileName)}.");
            AddUiDiagnostic(
                "Info",
                "Capture",
                "ARIEC-CAPTURE-OPENED",
                "ARIEC capture opened for offline review",
                $"Loaded {rows.Count} unified evidence rows from {dialog.FileName}. Protocol Trace and Evidence Summary are rebuilt from the same frames.jsonl truth.",
                "Use Protocol Trace or Evidence Summary selection, frame interpreter, export data, or save another selected capture block.");
            AppendSessionLog($"Offline capture opened: {rows.Count} rows <- {dialog.FileName}");
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
            MessageBox.Show(this,
                "Failed to open capture: " + ex.Message,
                "Open capture",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private IReadOnlyList<EvidenceRow> ReadCaptureRows(string fileName)
    {
        using var archive = ZipFile.OpenRead(fileName);
        var framesText = ReadZipTextEntry(archive, "frames.jsonl");
        if (string.IsNullOrWhiteSpace(framesText))
        {
            throw new InvalidOperationException("Capture file does not contain frames.jsonl.");
        }

        var rows = new List<EvidenceRow>();
        foreach (var line in framesText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var snapshot = JsonSerializer.Deserialize<CaptureFrameSnapshot>(line);
            if (snapshot is not null)
            {
                rows.Add(new EvidenceRow(snapshot));
            }
        }

        return rows.OrderBy(row => row.Sequence).ToArray();
    }

    private static string ReadZipTextEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return string.Empty;
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }


    private void SaveSelectedCapture_Click(object sender, RoutedEventArgs e)
    {
        var rows = GetSelectedRowsForUnifiedEvidenceCapture(out var sourceWorkspace);
        if (rows.Count == 0)
        {
            MessageBox.Show(this,
                "Select one or more rows in Protocol Trace or Evidence Summary first, then export the selected rows as an ARIEC capture file.",
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
                "The capture file is a single source of truth. Opening it rebuilds Protocol Trace and Evidence Summary from the same frames.jsonl ledger.");
            AppendSessionLog($"Selected evidence capture saved: {sourceWorkspace}, {rows.Count} rows -> {dialog.FileName}");
            MessageBox.Show(this,
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
            MessageBox.Show(this,
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
            ProtocolMode = GetSelectedProtocolMode().ToString(),
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
            policy = "Selected capture is generated from unified evidence rows. frames.jsonl is the single source of truth and is used to rebuild Protocol Trace and Evidence Summary on open.",
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
        builder.AppendLine("> This capture uses `frames.jsonl` as the single evidence ledger. Opening the capture rebuilds Protocol Trace and Evidence Summary from the same data.");
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
                RawHex = row.RawHex,
                ProtocolTraceTitle = row.ProtocolTraceTitle,
                ProtocolTraceMeaning = row.ProtocolTraceMeaning,
                ProtocolTraceRaw = row.ProtocolTraceRaw,
                ProtocolTraceMeta = row.ProtocolTraceMeta
            };
    }

}
