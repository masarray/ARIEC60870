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
    private void ExportData_Click(object sender, RoutedEventArgs e)
    {
        var tabName = (MainTabControl.SelectedItem as TabItem)?.Header?.ToString() ?? "data";
        if (tabName.Equals("Trace", StringComparison.OrdinalIgnoreCase))
        {
            ExportProtocolTraceRows(tabName);
            return;
        }

        var grid = GetCurrentTabDataGrid();
        if (grid is null)
        {
            MessageBox.Show(this, "The selected tab does not contain exportable grid data.", "Export Data", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var safeName = string.Concat(tabName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');
        var dialog = new SaveFileDialog
        {
            Title = "Export selected tab data",
            Filter = "Tab-separated text (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"ARIEC60870-{safeName}.txt",
            AddExtension = true,
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var exportText = BuildTextEvidenceRetentionHeader(tabName) + BuildTabSeparatedText(grid);
        File.WriteAllText(dialog.FileName, exportText, Encoding.UTF8);
        AddEvidenceRetentionExportMarker($"Tab export: {tabName}");
        AppendSessionLog($"Data exported from {tabName} with retention policy marker: {dialog.FileName}");
    }

    private void ExportSelectedTrace_Click(object sender, RoutedEventArgs e)
        => ExportProtocolTraceRows("Trace");

    private void ExportProtocolTraceRows(string tabName)
    {
        var selected = GetSelectedProtocolTraceRowsForCapture();
        var rows = selected.Count > 0
            ? selected
            : FrameTraceRows.ToArray();

        if (rows.Count == 0)
        {
            MessageBox.Show(this, "No Trace / Messages rows are available to export.", "Export Trace", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var mode = selected.Count > 0 ? "selected" : "visible";
        var dialog = new SaveFileDialog
        {
            Title = selected.Count > 0 ? "Export selected Trace rows" : "Export visible Trace rows",
            Filter = "Tab-separated text (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"ARIEC60870-Protocol-Trace-{mode}-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            AddExtension = true,
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var exportText = BuildTextEvidenceRetentionHeader($"{tabName} / {mode}") + BuildProtocolTraceTabSeparatedText(rows);
        File.WriteAllText(dialog.FileName, exportText, Encoding.UTF8);
        AddUiDiagnostic(
            "Info",
            "Capture",
            "ARIEC-TRACE-TXT-EXPORTED",
            "Trace rows exported",
            $"Exported {rows.Count} {mode} Trace rows to {dialog.FileName}.",
            "Use .ariec capture for re-openable evidence and .txt export for lightweight report appendix.");
        AppendSessionLog($"Trace exported: {rows.Count} {mode} rows -> {dialog.FileName}");
    }

    private static string BuildProtocolTraceTabSeparatedText(IReadOnlyList<EvidenceRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Sequence\tTime\tDirection\tProtocol\tService\tAddress\tTypeID\tCOT\tCA\tIOA\tQuality\tMeaning\tRawHex");

        foreach (var row in rows.OrderBy(x => x.Sequence))
        {
            builder
                .Append(EscapeTabValue(row.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append('\t')
                .Append(EscapeTabValue(row.Time)).Append('\t')
                .Append(EscapeTabValue(row.Direction)).Append('\t')
                .Append(EscapeTabValue(row.ProtocolName)).Append('\t')
                .Append(EscapeTabValue(row.ProtocolService)).Append('\t')
                .Append(EscapeTabValue(row.ProtocolAddress)).Append('\t')
                .Append(EscapeTabValue(row.TypeId)).Append('\t')
                .Append(EscapeTabValue(row.CotDisplay)).Append('\t')
                .Append(EscapeTabValue(row.CommonAddress)).Append('\t')
                .Append(EscapeTabValue(row.IoAddress)).Append('\t')
                .Append(EscapeTabValue(row.Quality)).Append('\t')
                .Append(EscapeTabValue(row.ProtocolTraceMeaning)).Append('\t')
                .Append(EscapeTabValue(row.RawHex))
                .AppendLine();
        }

        return builder.ToString();
    }

    private DataGrid? GetCurrentTabDataGrid()
    {
        var header = (MainTabControl.SelectedItem as TabItem)?.Header?.ToString() ?? string.Empty;
        return header switch
        {
            "Evidence Ledger" or "Evidence Summary" => EvidenceGrid,
            "Values" or "Value Viewer" => ValueGrid,
            "Events" or "Event Log" => RelayEventGrid,
            "Issues" or "Findings" => FindingsGrid,
            "Diagnostics" => DiagnosticsGrid,
            _ => null
        };
    }

    private static string BuildTabSeparatedText(DataGrid grid)
    {
        var visibleColumns = grid.Columns
            .Where(c => c.Visibility == Visibility.Visible)
            .OrderBy(c => c.DisplayIndex)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine(string.Join("\t", visibleColumns.Select(c => EscapeTabValue(c.Header?.ToString() ?? string.Empty))));

        foreach (var item in grid.ItemsSource?.Cast<object>() ?? Enumerable.Empty<object>())
        {
            var values = visibleColumns.Select(column => EscapeTabValue(ReadGridColumnValue(column, item)));
            builder.AppendLine(string.Join("\t", values));
        }

        return builder.ToString();
    }

    private static string ReadGridColumnValue(DataGridColumn column, object item)
    {
        if (column is DataGridTextColumn textColumn && textColumn.Binding is Binding binding && binding.Path is not null)
        {
            var path = binding.Path.Path;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var value = item.GetType().GetProperty(path)?.GetValue(item);
                return value?.ToString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string EscapeTabValue(string value)
    {
        return (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
    }

    private void DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            return;
        }

        if (!row.IsSelected)
        {
            grid.SelectedItems.Clear();
            row.IsSelected = true;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T target)
            {
                return target;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

}
