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
    private void BrowseMapping_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = GetSelectedProtocolMode() == Iec60870ProtocolMode.Iec103 ? "Open IEC-103 FUN/INF Mapping Profile" : "Open IEC-101/104 IOA Point Profile",
            Filter = "ARIEC60870 mapping profile (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        TryLoadMappingProfile(dialog.FileName, showMessage: true);
        SaveSetupPreferencesFromUi(silent: true);
    }

    private void TryLoadMappingProfile(string fileName, bool showMessage)
    {
        try
        {
            if (GetSelectedProtocolMode() == Iec60870ProtocolMode.Iec103)
            {
                _mappingProfile = Iec103SignalMappingProfile.LoadFromFile(fileName);
                MappingProfilePathBox.Text = SanitizeSavedMappingProfilePath(fileName);
                MappingProfileStatusText.Text = $"Loaded: {_mappingProfile.ProfileName} ({_mappingProfile.Signals.Count} signals)";
                AppendSessionLog("IEC-103 mapping profile loaded: " + _mappingProfile.ProfileName);
            }
            else
            {
                _ioaProfile = Iec10xPointMappingProfile.LoadFromFile(fileName);
                MappingProfilePathBox.Text = SanitizeSavedMappingProfilePath(fileName);
                ApplyIoaProfileDefaultsToUi(_ioaProfile, onlyWhenUiLooksDefault: false);
                var scenarioText = _ioaProfile.TestScenarios.Count > 0 ? $", {_ioaProfile.TestScenarios.Count} test scenarios" : string.Empty;
                MappingProfileStatusText.Text = $"Loaded: {_ioaProfile.ProfileName} ({_ioaProfile.Points.Count} IOA points{scenarioText})";
                RefreshIoaProfileRows();
                AppendSessionLog("IEC-101/104 IOA profile loaded: " + _ioaProfile.ProfileName);
            }
        }
        catch (Exception ex)
        {
            AddUiDiagnostic("Warning", "Mapping", "IEC60870-MAPPING-LOAD", "Mapping profile could not be loaded", ex.Message, "Check JSON syntax and schema. IEC-103 uses FUN/INF schema; IEC-101/104 uses ariec10x-ioa-profile-v1.", ex);
            if (showMessage)
            {
                MessageBox.Show(this, ex.Message, "Mapping profile error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void ClearMapping_Click(object sender, RoutedEventArgs e)
    {
        _mappingProfile = Iec103SignalMappingProfile.Empty;
        _ioaProfile = Iec10xPointMappingProfile.Empty;
        MappingProfilePathBox.Text = string.Empty;
        RefreshIoaProfileRows();
        MappingProfileStatusText.Text = GetSelectedProtocolMode() == Iec60870ProtocolMode.Iec103
            ? "No mapping profile loaded. Raw FUN/INF will be shown."
            : "No IOA profile loaded. Raw IOA labels will be shown.";
        AppendSessionLog("Mapping profile cleared.");
        SaveSetupPreferencesFromUi(silent: true);
    }

    private void EditSignalList_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedProtocolMode() == Iec60870ProtocolMode.Iec103)
        {
            MessageBox.Show(this,
                "Signal List Editor is for IEC-101/104 IOA mapping profiles. IEC-103 uses FUN/INF mapping and will get a dedicated editor later.",
                "Signal List Editor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var editor = new SignalListEditorWindow(_ioaProfile.HasPoints ? _ioaProfile : Iec10xPointMappingProfile.Empty, SanitizeSavedMappingProfilePath(MappingProfilePathBox.Text))
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            _ioaProfile = editor.Profile;
            MappingProfilePathBox.Text = SanitizeSavedMappingProfilePath(editor.SavedProfilePath);
            ApplyIoaProfileDefaultsToUi(_ioaProfile, onlyWhenUiLooksDefault: false);
            var scenarioText = _ioaProfile.TestScenarios.Count > 0 ? $", {_ioaProfile.TestScenarios.Count} test scenarios" : string.Empty;
            MappingProfileStatusText.Text = $"Loaded: {_ioaProfile.ProfileName} ({_ioaProfile.Points.Count} IOA points{scenarioText})";
            RefreshIoaProfileRows();
            AppendSessionLog("IEC-101/104 IOA profile edited and applied: " + _ioaProfile.ProfileName);
            SaveSetupPreferencesFromUi(silent: true);
        }
    }


    private Iec10xPointMappingEntry? ResolveIoaPoint(Iec103MasterEvidenceEvent item)
    {
        return item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104
            ? _ioaProfile.Resolve(item.CommonAddressNumber, item.InformationObjectAddress, item.TypeId)
            : null;
    }

    private static string ExtractSimpleStateToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var equals = trimmed.IndexOf('=');
        if (equals >= 0 && equals < trimmed.Length - 1)
        {
            var right = trimmed[(equals + 1)..].Trim();
            var comma = right.IndexOf(',');
            return comma > 0 ? right[..comma].Trim() : right;
        }

        var comma2 = trimmed.IndexOf(',');
        return comma2 > 0 ? trimmed[..comma2].Trim() : trimmed;
    }

    private void UpdateValueAndEventViews(Iec103MasterEvidenceEvent item)
    {
        var shouldShowValue = item.IsRelayValue || IsIec10xProcessValue(item);
        var shouldShowEdgeEvent = item.IsRelayEdgeEvent || IsIec10xDigitalEdgeEvent(item);
        var ioaPoint = ResolveIoaPoint(item);
        var key = BuildValueKey(item);
        _lastDisplayedValueByKey.TryGetValue(key, out var previousValueBeforeUpdate);
        if (shouldShowValue)
        {
            MarkGiValueReceived(key);
        }
        ReportGiCompletenessIfReady(item);

        var fallbackSignal = BuildFallbackSignalName(item);
        var displayValue = !string.IsNullOrWhiteSpace(item.SignalDisplayValue)
            ? item.SignalDisplayValue
            : !string.IsNullOrWhiteSpace(item.ObjectSummary)
                ? item.ObjectSummary
                : item.SignalRawValue;
        if (ioaPoint is not null)
        {
            displayValue = ioaPoint.ResolveDisplayValue(ExtractSimpleStateToken(displayValue));
        }

        var previousValue = previousValueBeforeUpdate;
        if (string.IsNullOrWhiteSpace(previousValue) && !string.IsNullOrWhiteSpace(item.PreviousSignalValue))
        {
            previousValue = item.PreviousSignalValue;
        }
        if (ioaPoint is not null && !string.IsNullOrWhiteSpace(previousValue))
        {
            previousValue = ioaPoint.ResolveDisplayValue(ExtractSimpleStateToken(previousValue));
        }

        var hasMeaningfulChange = HasKnownStateTransition(previousValue, displayValue);
        var keepValueHighlight = _valueHighlightExpiryByKey.TryGetValue(key, out var highlightUntil) && highlightUntil > DateTime.UtcNow;

        if (shouldShowValue)
        {
            var valueRow = new ValueRow(new Iec103ValuePoint
            {
                Key = key,
                IsMapped = item.IsMappedSignal || ioaPoint is not null,
                SignalName = ioaPoint?.Name ?? (string.IsNullOrWhiteSpace(item.SignalName) ? fallbackSignal : item.SignalName),
                SignalGroup = ioaPoint?.Group ?? (string.IsNullOrWhiteSpace(item.SignalGroup) ? BuildFallbackSignalGroup(item) : item.SignalGroup),
                SignalType = !string.IsNullOrWhiteSpace(ioaPoint?.SignalType) ? ioaPoint!.SignalType : (!string.IsNullOrWhiteSpace(item.SignalType) ? item.SignalType : (item.AsduType ?? string.Empty)),
                FunctionType = item.FunctionType,
                InformationNumber = item.InformationNumber,
                RawValue = string.IsNullOrWhiteSpace(item.SignalRawValue) ? item.ObjectSummary : item.SignalRawValue,
                DisplayValue = displayValue,
                Source = item.Cot ?? string.Empty,
                CauseOfTransmission = item.Cot ?? string.Empty,
                AsduType = item.AsduType ?? string.Empty,
                RelayTimeText = string.IsNullOrWhiteSpace(item.RelayTimestampText) ? "no timestamp" : item.RelayTimestampText,
                RelayTimeInvalid = item.RelayTimestampInvalid,
                ArrivalTimeUtc = item.TimestampUtc,
                RawHex = item.RawHex,
                ProtocolMode = item.ProtocolMode,
                CommonAddress = item.CommonAddressNumber,
                InformationObjectAddress = item.InformationObjectAddress,
                TypeId = item.TypeId,
                QualityText = ExtractQualityTextFromEvidence(item)
            })
            {
                IsRecentlyChanged = hasMeaningfulChange || keepValueHighlight
            };

            UpsertValueRowStable(valueRow);
            if (hasMeaningfulChange)
            {
                MarkValueRowRecentlyChanged(key);
            }

            if (!string.IsNullOrWhiteSpace(displayValue))
            {
                _lastDisplayedValueByKey[key] = displayValue;
            }
        }

        if (shouldShowEdgeEvent)
        {
            var isIec10xDigital = item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104
                                  && IsIec10xDigitalType(item.TypeId);
            if (isIec10xDigital && !hasMeaningfulChange)
            {
                // Event Log is a change journal, not a value mirror. Ignore OFF->OFF,
                // ON->ON, and first-observed states with no trustworthy before value.
                return;
            }

            var relayEventRow = new RelayEventRow(new Iec103RelayEventLogEntry
            {
                EvidenceSequenceNumber = item.SequenceNumber,
                RelayTimeText = string.IsNullOrWhiteSpace(item.RelayTimestampText) ? "no timestamp" : item.RelayTimestampText,
                RelayTimeInvalid = item.RelayTimestampInvalid,
                ArrivalTimeUtc = item.TimestampUtc,
                IsMapped = item.IsMappedSignal || ioaPoint is not null,
                SignalName = ioaPoint?.Name ?? (string.IsNullOrWhiteSpace(item.SignalName) ? fallbackSignal : item.SignalName),
                SignalGroup = ioaPoint?.Group ?? (string.IsNullOrWhiteSpace(item.SignalGroup) ? BuildFallbackSignalGroup(item) : item.SignalGroup),
                SignalType = !string.IsNullOrWhiteSpace(ioaPoint?.SignalType) ? ioaPoint!.SignalType : (!string.IsNullOrWhiteSpace(item.SignalType) ? item.SignalType : (item.AsduType ?? string.Empty)),
                FunctionType = item.FunctionType,
                InformationNumber = item.InformationNumber,
                PreviousValue = string.IsNullOrWhiteSpace(previousValue) ? string.Empty : previousValue,
                NewValue = displayValue,
                EdgeReason = string.IsNullOrWhiteSpace(item.EdgeReason) ? (item.Cot ?? string.Empty) : item.EdgeReason,
                CauseOfTransmission = item.Cot ?? string.Empty,
                AsduType = item.AsduType ?? string.Empty,
                RawHex = item.RawHex,
                ProtocolMode = item.ProtocolMode,
                CommonAddress = item.CommonAddressNumber,
                InformationObjectAddress = item.InformationObjectAddress,
                TypeId = item.TypeId,
                QualityText = ExtractQualityTextFromEvidence(item)
            });

            _relayEventStore.Add(relayEventRow);
            _relayEventRowsDirty = true;
        }
    }

    private static bool IsIec10xDigitalType(int? typeId)
        => typeId is 1 or 2 or 3 or 4 or 30 or 31;

    private static bool HasKnownStateTransition(string? previousValue, string? newValue)
    {
        var before = NormalizeStateForComparison(previousValue);
        var after = NormalizeStateForComparison(newValue);
        if (string.IsNullOrWhiteSpace(before) || string.IsNullOrWhiteSpace(after))
        {
            return false;
        }

        return !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeStateForComparison(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var token = ExtractSimpleStateToken(value).Trim();
        if (token.Length == 0 || token == "-" || token.Equals("no timestamp", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var upper = token.ToUpperInvariant();
        if (upper.StartsWith("SP=", StringComparison.Ordinal) || upper.StartsWith("DP=", StringComparison.Ordinal))
        {
            upper = upper[3..].Trim();
        }

        if (upper is "0" or "FALSE") return "0";
        if (upper is "1" or "TRUE") return "1";
        if (upper is "2") return "2";
        if (upper is "3") return "3";
        if (upper.Contains("INVALID OPEN", StringComparison.Ordinal)) return "INVALID_OPEN";
        if (upper.Contains("INVALID CLOSE", StringComparison.Ordinal)) return "INVALID_CLOSE";
        if (upper.Contains("OPEN", StringComparison.Ordinal)) return "OPEN";
        if (upper.Contains("CLOSE", StringComparison.Ordinal) || upper.Contains("CLOSED", StringComparison.Ordinal)) return "CLOSED";
        if (upper.Contains("OFF", StringComparison.Ordinal) || upper.Contains("NORMAL", StringComparison.Ordinal)) return "OFF";
        if (upper.Contains("ON", StringComparison.Ordinal) || upper.Contains("ACTIVE", StringComparison.Ordinal)) return "ON";
        return upper;
    }

    private void MarkValueRowRecentlyChanged(string key)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        _valueHighlightExpiryByKey[key] = until;
        if (_valueRowsByKey.TryGetValue(key, out var storedRow))
        {
            storedRow.IsRecentlyChanged = true;
            _valueRowsDirty = true;
        }

        foreach (var row in ValueRows)
        {
            if (string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                row.IsRecentlyChanged = true;
                break;
            }
        }
    }

    private void ResetExpiredValueHighlights()
    {
        if (_valueHighlightExpiryByKey.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var expired = _valueHighlightExpiryByKey
            .Where(x => x.Value <= now)
            .Select(x => x.Key)
            .ToArray();
        if (expired.Length == 0)
        {
            return;
        }

        foreach (var key in expired)
        {
            _valueHighlightExpiryByKey.Remove(key);
            if (_valueRowsByKey.TryGetValue(key, out var storedRow))
            {
                storedRow.IsRecentlyChanged = false;
                _valueRowsDirty = true;
            }

            foreach (var row in ValueRows)
            {
                if (string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    row.IsRecentlyChanged = false;
                    break;
                }
            }
        }
    }

    private static bool IsIec10xProcessValue(Iec103MasterEvidenceEvent item)
    {
        if (item.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104))
        {
            return false;
        }

        if (!item.TypeId.HasValue || !item.InformationObjectAddress.HasValue)
        {
            return false;
        }

        return item.TypeId.Value is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37;
    }

    private static bool IsIec10xDigitalEdgeEvent(Iec103MasterEvidenceEvent item)
    {
        if (item.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104))
        {
            return false;
        }

        if (!item.TypeId.HasValue || !item.CauseOfTransmission.HasValue || !item.InformationObjectAddress.HasValue)
        {
            return false;
        }

        var isEventCause = item.CauseOfTransmission.Value is 3 or 11 or 12;
        return IsIec10xDigitalType(item.TypeId) && isEventCause;
    }

    private static string BuildValueKey(Iec103MasterEvidenceEvent item)
    {
        if (!string.IsNullOrWhiteSpace(item.SignalKey))
        {
            return item.SignalKey;
        }

        if (item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104)
        {
            return item.InformationObjectAddress.HasValue
                ? BuildIoaValueKey(item.InformationObjectAddress.Value)
                : $"{item.ProtocolMode}:IOA-";
        }

        return $"FUN{(item.FunctionType ?? 0):000}:INF{(item.InformationNumber ?? 0):000}";
    }

    private static string BuildFallbackSignalName(Iec103MasterEvidenceEvent item)
    {
        if (item.ProtocolMode is Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104)
        {
            return item.InformationObjectAddress.HasValue
                ? $"IOA {item.InformationObjectAddress.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                : "Unaddressed IEC-10x object";
        }

        return $"FUN {item.FunctionType} / INF {item.InformationNumber}";
    }

    private static string BuildFallbackSignalGroup(Iec103MasterEvidenceEvent item)
    {
        return item.ProtocolMode switch
        {
            Iec60870ProtocolMode.Iec101 => "IEC-101",
            Iec60870ProtocolMode.Iec104 => "IEC-104",
            _ => "Unmapped"
        };
    }


    private void EventLogFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyRelayEventFilter();
    }

    private void ApplyRelayEventFilter()
    {
        if (RelayEventRows is null)
        {
            return;
        }

        var filter = (EventLogFilterComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
        var rows = _relayEventStore
            .Snapshot()
            .Reverse()
            .Where(row => ShouldIncludeRelayEvent(row, filter))
            .Take(MaxVisibleRelayEventRows)
            .ToArray();

        RelayEventRows.ReplaceRange(rows);
    }

    private static bool ShouldIncludeRelayEvent(RelayEventRow row, string filter)
    {
        if (filter.Equals("Digital status", StringComparison.OrdinalIgnoreCase))
        {
            return IsDigitalEvent(row);
        }

        if (filter.Equals("Analog", StringComparison.OrdinalIgnoreCase))
        {
            return IsAnalogEvent(row);
        }

        return true;
    }

    private static bool IsDigitalEvent(RelayEventRow row)
    {
        var text = string.Join(" ", row.Type, row.Cot, row.Signal, row.NewValue, row.Reason);
        return text.Contains("DPI", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("SPI", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("status", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("trip", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("pickup", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ON", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("OFF", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAnalogEvent(RelayEventRow row)
    {
        var text = string.Join(" ", row.Type, row.Cot, row.Signal, row.NewValue, row.Reason);
        return text.Contains("Measur", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Analog", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("current", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("voltage", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Measurands", StringComparison.OrdinalIgnoreCase);
    }


    private void UpsertValueRowStable(ValueRow row)
    {
        _valueRowsByKey[row.Key] = row;
        _valueRowsDirty = true;

        if (_valueRowsByKey.Count > MaxVisibleValueRows + 200)
        {
            foreach (var stale in _valueRowsByKey.Values
                         .OrderBy(GetValueRowSortRank)
                         .ThenBy(x => x.IoaSortKey)
                         .ThenBy(x => x.TypeSortKey)
                         .ThenBy(x => x.Signal, StringComparer.OrdinalIgnoreCase)
                         .Skip(MaxVisibleValueRows)
                         .Select(x => x.Key)
                         .ToArray())
            {
                _valueRowsByKey.Remove(stale);
                _valueHighlightExpiryByKey.Remove(stale);
                _lastDisplayedValueByKey.Remove(stale);
            }
        }
    }

    private IReadOnlyList<ValueRow> GetSortedValueRowsSnapshot()
        => _valueRowsByKey.Values
            .OrderBy(GetValueRowSortRank)
            .ThenBy(x => x.IoaSortKey)
            .ThenBy(x => x.TypeSortKey)
            .ThenBy(x => x.Signal, StringComparer.OrdinalIgnoreCase)
            .Take(MaxVisibleValueRows)
            .ToArray();

    private static int CompareValueRowsForOperatorGrouping(ValueRow left, ValueRow right)
    {
        var rank = GetValueRowSortRank(left).CompareTo(GetValueRowSortRank(right));
        if (rank != 0) return rank;

        var ioa = left.IoaSortKey.CompareTo(right.IoaSortKey);
        if (ioa != 0) return ioa;

        var type = left.TypeSortKey.CompareTo(right.TypeSortKey);
        if (type != 0) return type;

        return string.Compare(left.Signal, right.Signal, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetValueRowSortRank(ValueRow row)
    {
        var text = string.Join(" ", row.Type, row.Cot, row.Signal, row.Group, row.TypeId);
        if (row.TypeId is "1" or "2" or "3" or "4" or "30" or "31" ||
            text.Contains("DPI", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("SPI", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("single-point", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("double-point", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("status", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("trip", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("fault", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("local remote", StringComparison.OrdinalIgnoreCase))
        {
            return 0; // digital/protection status first
        }

        if (row.TypeId is "9" or "10" or "11" or "12" or "13" or "14" or "21" or "34" or "35" or "36" ||
            text.Contains("measur", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("analog", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("current", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("voltage", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("power", StringComparison.OrdinalIgnoreCase))
        {
            return 1; // analog/measurand after digital
        }

        return 2;
    }


    private static string ExtractQualityTextFromEvidence(Iec103MasterEvidenceEvent item)
    {
        if (!string.IsNullOrWhiteSpace(item.QualityText))
        {
            return item.QualityText;
        }

        var text = string.Join(" ", item.SignalDisplayValue, item.SignalRawValue, item.ObjectSummary);
        var marker = "QDS=0x";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || text.Length < index + marker.Length + 2)
        {
            return string.Empty;
        }

        return text.Substring(index, marker.Length + 2);
    }

    private static bool IsDiagnosticEvidence(Iec103MasterEvidenceEvent item)
    {
        return !string.IsNullOrWhiteSpace(item.ExceptionType)
               || item.Category.Contains("Error", StringComparison.OrdinalIgnoreCase)
               || item.Category.Contains("Warning", StringComparison.OrdinalIgnoreCase)
               || item.Category.Contains("Fault", StringComparison.OrdinalIgnoreCase)
               || item.Category.Contains("Diagnostic", StringComparison.OrdinalIgnoreCase)
               || item.Summary.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || item.Detail.Contains("exception", StringComparison.OrdinalIgnoreCase);
    }

    private void AddUiDiagnostic(string severity, string source, string code, string message, string detail, string recommendation, Exception? exception = null)
    {
        AddDiagnosticRow(new DiagnosticRow(severity, source, code, message, detail, recommendation, exception));
        UpdateBufferStatus();
    }

    private void AddDiagnosticRow(DiagnosticRow row)
    {
        _diagnosticStore.Add(row);
        _pendingDiagnosticUiRows.Add(row);
    }

    private void DiagnosticsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is not DiagnosticRow row)
        {
            DiagnosticDetailBox.Text = "Select a diagnostic row to view complete detail.";
            return;
        }

        DiagnosticDetailBox.Text = row.ToClipboardText();
    }

    private void CopySelectedDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        if (DiagnosticsGrid.SelectedItem is DiagnosticRow row)
        {
            Clipboard.SetText(row.ToClipboardText());
            AppendSessionLog("Diagnostic row copied to clipboard.");
        }
    }

    private void CopyDiagnosticDetail_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(DiagnosticDetailBox.Text))
        {
            Clipboard.SetText(DiagnosticDetailBox.Text);
            AppendSessionLog("Diagnostic detail copied to clipboard.");
        }
    }

    private void UpdateStableHeader(string state, string detail)
    {
        StateText.Text = state;
        CompletionText.Text = "History below";
        StatusHistorySummaryText.Text = CompactSessionDetail(detail);
        StatusHistorySummaryText.ToolTip = string.IsNullOrWhiteSpace(detail) ? "-" : detail;
    }

    private static string CompactSessionDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return "-";
        }

        var text = detail.Replace("Assessment:", "Assess:", StringComparison.OrdinalIgnoreCase)
            .Replace("Stopped by cancellation or requested duration.", "Stopped/duration reached.", StringComparison.OrdinalIgnoreCase)
            .Replace("Stopped by cancellation.", "Stopped by user.", StringComparison.OrdinalIgnoreCase);

        const int max = 74;
        return text.Length <= max ? text : text[..max] + "…";
    }

    private void SetRunUiState(bool isRunning)
    {
        StartButton.IsEnabled = true;
        if (StopButton is not null)
        {
            StopButton.IsEnabled = false;
            StopButton.Visibility = Visibility.Collapsed;
        }
        UpdateConnectToggleVisual(isRunning);
        SetupButton.IsEnabled = !isRunning;
        SetupOverlay.Visibility = isRunning ? Visibility.Collapsed : SetupOverlay.Visibility;
        ExportReportButton.IsEnabled = true;
        ProtocolModeComboBox.IsEnabled = !isRunning;
        TransportModeComboBox.IsEnabled = !isRunning;
        TcpHostBox.IsEnabled = !isRunning;
        TcpPortBox.IsEnabled = !isRunning;
        PortComboBox.IsEnabled = !isRunning;
        BaudComboBox.IsEnabled = !isRunning;
        SerialModeComboBox.IsEnabled = !isRunning;
        LinkAddressBox.IsEnabled = !isRunning;
        CommonAddressBox.IsEnabled = !isRunning;
        LinkAddressSizeComboBox.IsEnabled = !isRunning;
        TransmissionModeComboBox.IsEnabled = !isRunning;
        CotSizeComboBox.IsEnabled = !isRunning;
        CaSizeComboBox.IsEnabled = !isRunning;
        IoaSizeComboBox.IsEnabled = !isRunning;
        Iec104T0Box.IsEnabled = !isRunning;
        Iec104T1Box.IsEnabled = !isRunning;
        Iec104T2Box.IsEnabled = !isRunning;
        Iec104T3Box.IsEnabled = !isRunning;
        Iec104KBox.IsEnabled = !isRunning;
        Iec104WBox.IsEnabled = !isRunning;
        DurationBox.IsEnabled = !isRunning;
        TimeoutBox.IsEnabled = !isRunning;
        Class2IntervalBox.IsEnabled = !isRunning;
        MaxDrainBox.IsEnabled = !isRunning;
        ResetRemoteLinkCheckBox.IsEnabled = !isRunning;
        ResetFcbCheckBox.IsEnabled = !isRunning;
        ClockSyncCheckBox.IsEnabled = !isRunning;
        GiCheckBox.IsEnabled = !isRunning;
        Class2StartupCheckBox.IsEnabled = !isRunning;
        MappingProfilePathBox.IsEnabled = !isRunning;
        BrowseMappingButton.IsEnabled = !isRunning;
        ClearMappingButton.IsEnabled = !isRunning;
    }

    private void ClearSessionView(bool clearLog)
    {
        EvidenceRows.Clear();
        FrameTraceRows.Clear();
        _evidenceSummaryStore.Clear();
        _protocolTraceStore.Clear();
        _pendingEvidenceSummaryUiRows.Clear();
        _pendingProtocolTraceUiRows.Clear();
        _pendingFindingUiRows.Clear();
        _pendingDiagnosticUiRows.Clear();
        _findingStore.Clear();
        _diagnosticStore.Clear();
        _relayEventStore.Clear();
        _valueRowsByKey.Clear();
        _valueRowsDirty = false;
        _relayEventRowsDirty = false;
        _backpressureDroppedEvents = 0;
        _backpressureDroppedAckNoData = 0;
        _backpressureDroppedBackgroundPoll = 0;
        _backpressureDroppedTestFrames = 0;
        _backpressureDroppedOtherLowValue = 0;
        _backpressureNoticePending = 0;
        _lastDropSummaryMarkerTotal = 0;
        _traceVerbositySuppressedRows = 0;
        _traceVerbositySuppressedRoutine = 0;
        _traceVerbositySuppressedSupervisory = 0;
        _maxPendingEvidenceDepth = 0;
        _uiFlushTicks = 0;
        _lastUiFlushMs = 0;
        _maxUiFlushMs = 0;
        _lastEvidenceProcessed = 0;
        _lastFindingProcessed = 0;
        _lastVisibleBatchRows = 0;
        _lastFlushBudget = MaxUiFlushPerTick;
        _lastBackpressureLogUtc = DateTime.MinValue;
        _lastDispatcherPressureDiagnosticUtc = DateTime.MinValue;
        _lastDispatcherSlowDiagnosticUtc = DateTime.MinValue;
        _triggerPreCaptureBuffer.Clear();
        _activeProtocolTriggerCaptures.Clear();
        _lastProtocolTriggerUtcByKey.Clear();
        _protocolTriggerStartedCount = 0;
        _protocolTriggerCompletedCount = 0;
        FindingRows.Clear();
        ValueRows.Clear();
        RelayEventRows.Clear();
        _lastDisplayedValueByKey.Clear();
        _valueHighlightExpiryByKey.Clear();
        _evidenceSummarySignatureByKey.Clear();
        _evidenceSummaryLastUtcByKey.Clear();
        _evidenceSummaryLastAnalogValueByKey.Clear();
        _evidenceSummaryLastAnalogUtcByKey.Clear();
        _giExpectedValueKeys.Clear();
        _giReceivedValueKeys.Clear();
        _giCompletenessWatchActive = false;
        _giCompletenessReported = false;
        _firstObservedRuntimeCa = null;
        _runtimeCaMismatchReported = false;
        ResetRuntimeHealthStores();
        AssessmentRows.Clear();
        DiagnosticRows.Clear();
        TriggerCaptureRows.Clear();
        DualLinkTimelineRows.Clear();
        if (TriggerCaptureDetailBox is not null)
        {
            TriggerCaptureDetailBox.Text = "Automatic IEC trigger captures will appear here. Select a capture row to view detail.";
        }
        while (_pendingEvidence.TryDequeue(out _)) { }
        while (_pendingFindings.TryDequeue(out _)) { }
        _visibleEvidenceDropped = 0;
        _visibleRelayEventsDropped = 0;
        _visibleLogLinesDropped = 0;
        _visibleDiagnosticsDropped = 0;
        _txCount = 0;
        _rxCount = 0;
        _giCount = 0;
        _class1Count = 0;
        _class2Count = 0;
        _noDataCount = 0;
        _dpiCount = 0;
        TxLed.Opacity = 0.28;
        RxLed.Opacity = 0.28;
        GiLed.Opacity = 0.28;
        Class1Led.Opacity = 0.28;
        Class2Led.Opacity = 0.28;
        EventLed.Opacity = 0.28;
        DiagLed.Opacity = 0.28;
        TxRxText.Text = "0 / 0";
        ClassPollText.Text = "0 / 0 / 0";
        NoDataText.Text = "0";
        DpiText.Text = "0";
        FindingCountText.Text = "0";
        SelectedDetailText.Text = "Select evidence row to inspect decoded meaning.";
        SelectedRawText.Text = "-";
        _selectedFrameExplanation = "Select a frame. This panel translates raw bytes into commissioning meaning.";
        SelectedLineSummaryText.Text = _selectedFrameExplanation;
        SelectedProtocolMapLines.Clear();
        SelectedHexSegments.Clear();
        StatusHistorySummaryText.Text = "Visible session rows cleared.";
        UpdateBufferStatus();
        if (clearLog)
        {
            _sessionLogLines.Clear();
            SessionLogBox?.Clear();
            StatusHistoryRows.Clear();
            AppendSessionLog("Session view cleared.");
        }
    }

    private void AppendSessionLog(string message)
    {
        _sessionLogLines.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}");
        while (_sessionLogLines.Count > MaxSessionLogLines)
        {
            _sessionLogLines.Dequeue();
            _visibleLogLinesDropped++;
        }

        if (SessionLogBox is not null)
        {
            SessionLogBox.Text = string.Join(Environment.NewLine, _sessionLogLines);
            if (SessionLogBox.Text.Length > 0)
            {
                SessionLogBox.AppendText(Environment.NewLine);
            }
            SessionLogBox.ScrollToEnd();
        }

        if (StatusHistoryRows is not null && StatusHistorySummaryText is not null)
        {
            AddStatusHistoryRow(message);
        }

        if (BufferStatusText is not null)
        {
            UpdateBufferStatus();
        }
    }

    private void AddStatusHistoryRow(string message)
    {
        if (StatusHistoryRows is null)
        {
            return;
        }

        StatusHistoryRows.Insert(0, new StatusHistoryRow(DateTime.Now.ToString("HH:mm:ss"), ClassifyStatusMessage(message), message));
        while (StatusHistoryRows.Count > 160)
        {
            StatusHistoryRows.RemoveAt(StatusHistoryRows.Count - 1);
        }

        if (StatusHistorySummaryText is not null)
        {
            StatusHistorySummaryText.Text = CompactSessionDetail(message);
            StatusHistorySummaryText.ToolTip = message;
        }
    }

    private static string ClassifyStatusMessage(string message)
    {
        if (message.Contains("fault", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("warning", StringComparison.OrdinalIgnoreCase))
        {
            return "Attention";
        }

        if (message.Contains("stopped", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("disconnect", StringComparison.OrdinalIgnoreCase))
        {
            return "Stopped";
        }

        if (message.Contains("starting", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("monitor", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("transport", StringComparison.OrdinalIgnoreCase))
        {
            return "Runtime";
        }

        return "Info";
    }

    private void ToggleStatusHistory_Click(object sender, RoutedEventArgs e)
        => ToggleStatusHistoryPanel();

    private void StatusHistoryHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ToggleStatusHistoryPanel();
        e.Handled = true;
    }

    private void ToggleStatusHistoryPanel()
        => SetStatusHistoryPanelExpanded(!_statusHistoryExpanded);

    private void SetStatusHistoryPanelExpanded(bool expanded)
    {
        _statusHistoryExpanded = expanded;
        StatusHistoryPanel.Height = expanded ? double.NaN : 52;
        StatusHistoryGapRow.Height = expanded ? new GridLength(8) : new GridLength(0);
        StatusHistoryContentRow.Height = expanded ? new GridLength(118) : new GridLength(0);
        StatusHistoryGrid.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        StatusHistoryToggleIcon.Data = (Geometry)FindResource(expanded ? "LucideCircleChevronDown" : "LucideCircleChevronUp");
        StatusHistoryToggleIcon.Stroke = (Brush)FindResource("Ink500Brush");
    }

    private void PanelHeader_MouseEnter(object sender, MouseEventArgs e)
    {
        SetPanelHeaderIconHover(sender, isHovering: true);
    }

    private void PanelHeader_MouseLeave(object sender, MouseEventArgs e)
    {
        SetPanelHeaderIconHover(sender, isHovering: false);
    }

    private void SetPanelHeaderIconHover(object sender, bool isHovering)
    {
        var brush = (Brush)FindResource(isHovering ? "AccentBrush" : "Ink500Brush");

        if (ReferenceEquals(sender, CommandDockHeader) && CommandDockToggleIcon is not null)
        {
            CommandDockToggleIcon.Stroke = brush;
        }
        else if (ReferenceEquals(sender, CommandDockMiniButton) && CommandDockMiniIcon is not null)
        {
            CommandDockMiniIcon.Stroke = brush;
        }
        else if (ReferenceEquals(sender, StatusHistoryHeader) && StatusHistoryToggleIcon is not null)
        {
            StatusHistoryToggleIcon.Stroke = brush;
        }
    }

    private void UpdateBufferStatus()
    {
        if (BufferStatusText == null)
        {
            return;
        }

        var traceHold = IsProtocolTraceViewFrozen() ? $", traceHold {_protocolTraceRowsDeferredWhileFrozen}" : string.Empty;
        var evidenceHold = IsEvidenceSummaryViewFrozen() ? $", evidenceHold {_evidenceSummaryRowsDeferredWhileFrozen}" : string.Empty;
        BufferStatusText.Text =
            $"Buffer: trace {GetTraceVerbosityMode()}{traceHold}{evidenceHold}, operator {EvidenceRows.Count}/{MaxVisibleEvidenceRows}, frames {FrameTraceRows.Count}/{MaxVisibleFrameTraceRows}, values {ValueRows.Count}/{MaxVisibleValueRows}, events {RelayEventRows.Count}/{MaxVisibleRelayEventRows}, diagnostics {DiagnosticRows.Count}/{MaxVisibleDiagnosticRows}, queued {_pendingEvidence.Count}, qMax {_maxPendingEvidenceDepth}, budget {_lastFlushBudget}, dropped {_backpressureDroppedEvents} [ack {_backpressureDroppedAckNoData}, poll {_backpressureDroppedBackgroundPoll}, test {_backpressureDroppedTestFrames}, other {_backpressureDroppedOtherLowValue}], traceSkip {_traceVerbositySuppressedRows} [routine {_traceVerbositySuppressedRoutine}, sup {_traceVerbositySuppressedSupervisory}], flush {_lastUiFlushMs}/{_maxUiFlushMs} ms, ticks {_uiFlushTicks}, rows {_lastEvidenceProcessed}+{_lastFindingProcessed}/{_lastVisibleBatchRows}, relayDrop {_visibleRelayEventsDropped}, diagDrop {_visibleDiagnosticsDropped}";
    }
}
