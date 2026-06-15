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
    private void EvidenceGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isProtocolTraceSelectionBatching && ReferenceEquals(sender, FrameTraceGrid))
        {
            _pendingProtocolTraceSelectionInspectorRefresh = true;
            return;
        }

        var selectedItem = sender switch
        {
            DataGrid grid => grid.SelectedItem,
            ListBox listBox => listBox.SelectedItem ?? listBox.SelectedItems.OfType<EvidenceRow>().OrderBy(row => row.Sequence).LastOrDefault(),
            _ => null
        };

        ApplySelectedEvidenceRowToInspector(selectedItem);
    }

    private void ApplySelectedEvidenceRowToInspector(object? selectedItem)
    {
        if (selectedItem is not EvidenceRow row)
        {
            _selectedFrameRow = null;
            SelectedDetailText.Text = "Select evidence row to inspect decoded meaning.";
            SelectedRawText.Text = "-";
            _selectedFrameExplanation = "Select a frame. This panel translates raw bytes into commissioning meaning.";
            SelectedLineSummaryText.Text = _selectedFrameExplanation;
            SelectedLineDirectionText.Text = "Select a frame";
            SelectedLineSummaryText.Text = "The selected IEC 60870 frame will be decoded into transport/link layer, ASDU/APCI fields, address, value/time, and integrity groups.";
            SelectedProtocolMapLines.Clear();
            SelectedHexSegments.Clear();
            UpdateFrameInterpreterTone(null);
            if (ActiveProtocolMapText is not null)
            {
                ActiveProtocolMapText.Text = "linked highlight";
            }
            return;
        }

        _selectedFrameRow = row;
        _pinnedProtocolMapKey = null;
        if (PinProtocolMapCheckBox != null)
        {
            PinProtocolMapCheckBox.IsChecked = false;
        }

        var explanation = BuildCompactFrameExplanation(row);
        SelectedDetailText.Text = explanation + Environment.NewLine + Environment.NewLine + "Raw: " + row.RawHex;
        SelectedRawText.Text = row.RawHex;
        _selectedFrameExplanation = explanation;
        SelectedLineSummaryText.Text = "Hover or click a protocol group. The panel stays stable; linked raw/meaning groups are highlighted without rewriting the inspector.";
        SelectedLineDirectionText.Text = BuildLineMonitorTitle(row);
        SelectedLineSummaryText.Text = BuildLineMonitorSummary(row);
        UpdateFrameInterpreterTone(row);
        RebuildProtocolMap(row);
        ActivateDefaultProtocolMapGroup(row);
    }


    private void ActivateDefaultProtocolMapGroup(EvidenceRow row)
    {
        if (PinProtocolMapCheckBox?.IsChecked == true && !string.IsNullOrWhiteSpace(_pinnedProtocolMapKey))
        {
            SetActiveProtocolMap(_pinnedProtocolMapKey);
            return;
        }

        var key = row.ProtocolMode switch
        {
            "104" when row.ApciFormat == "I" && row.IoAddress != "-" => "object",
            "104" => "apci",
            "101" when row.IoAddress != "-" => "object",
            "101" when row.TypeId != "-" => "asdu",
            "103" when row.FunInf != "-" => "asdu",
            _ => "raw"
        };

        SetActiveProtocolMap(key);
    }

    private static string DescribeProtocolMapKey(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "apci" => "APCI selected",
            "ft12" => "FT1.2 selected",
            "control" => "link control selected",
            "asdu" => "ASDU header selected",
            "object" => "object address selected",
            "payload" => "payload selected",
            "value" => "value selected",
            "check" => "integrity selected",
            "raw" => "raw frame selected",
            _ => key + " selected"
        };
    }


    private void UpdateFrameInterpreterTone(EvidenceRow? row)
    {
        if (FrameInterpreterPanel is null)
        {
            return;
        }

        var tone = row?.TrafficTone ?? string.Empty;
        var background = tone switch
        {
            "Tx" => Color.FromRgb(245, 250, 255),
            "Rx" => Color.FromRgb(244, 255, 249),
            "Error" => Color.FromRgb(255, 245, 245),
            _ => Color.FromRgb(248, 251, 255)
        };
        var border = tone switch
        {
            "Tx" => Color.FromRgb(191, 219, 254),
            "Rx" => Color.FromRgb(187, 247, 208),
            "Error" => Color.FromRgb(254, 202, 202),
            _ => Color.FromRgb(226, 232, 240)
        };

        FrameInterpreterPanel.Background = new SolidColorBrush(background);
        FrameInterpreterPanel.BorderBrush = new SolidColorBrush(border);
    }

    private static string BuildCompactFrameExplanation(EvidenceRow row)
    {
        var parts = new List<string>();
        parts.Add(row.ReadableMeaning);

        if (!string.IsNullOrWhiteSpace(row.SignalOrAddress) && row.SignalOrAddress != "-")
        {
            parts.Add($"Address: {row.SignalOrAddress}.");
        }

        if (!string.IsNullOrWhiteSpace(row.SemanticState))
        {
            parts.Add($"Value: {row.SemanticState}.");
        }

        parts.Add(row.ProtocolMode switch
        {
            "104" => $"Protocol: IEC-104 {row.Direction}, APCI={row.ApciFormat}, NS={row.SendSequence}, NR={row.ReceiveSequence}, Type ID={row.TypeIdName}, COT={row.CotDisplay}, CA={row.CommonAddress}, IOA={row.IoAddress}.",
            "101" => $"Protocol: IEC-101 {row.Direction} {row.DataClass}, Link={row.LinkAddress}, Type ID={row.TypeIdName}, COT={row.CotDisplay}, CA={row.CommonAddress}, IOA={row.IoAddress}, ACD={row.Acd}, DFC={row.Dfc}.",
            _ => $"Protocol: IEC-103 {row.Direction} {row.DataClass}, ASDU={row.AsduType}, COT={row.Cot}, FUN/INF={row.FunInf}, ACD={row.Acd}, DFC={row.Dfc}."
        });

        if (!string.IsNullOrWhiteSpace(row.PollingReason) && row.PollingReason != "-")
        {
            parts.Add($"Why it happened: {row.PollingReason}.");
        }

        if (!string.IsNullOrWhiteSpace(row.OperatorAction))
        {
            parts.Add($"Recommended action: {row.OperatorAction}.");
        }

        if (!string.IsNullOrWhiteSpace(row.RelayTime) && row.RelayTime != "-")
        {
            parts.Add($"Relay time: {row.RelayTime}.");
        }

        return string.Join(Environment.NewLine, parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string BuildLineMonitorTitle(EvidenceRow row)
    {
        var arrow = row.Direction.Equals("TX", StringComparison.OrdinalIgnoreCase)
            ? "TX → Master to relay"
            : row.Direction.Equals("RX", StringComparison.OrdinalIgnoreCase)
                ? "RX ← Relay to master"
                : row.Direction;
        var cls = row.DataClass == "-" ? "Link" : row.DataClass;
        var service = row.ProtocolMode == "104"
            ? row.ProtocolService
            : row.AsduType == "-" ? row.Summary : row.AsduType;
        return $"{arrow} · {cls} · {service}";
    }

    private static string BuildLineMonitorSummary(EvidenceRow row)
    {
        var parts = new List<string> { row.ReadableMeaning };
        if (!string.IsNullOrWhiteSpace(row.ProtocolAddress) && row.ProtocolAddress != "-") parts.Add(row.ProtocolAddress);
        if (!string.IsNullOrWhiteSpace(row.RelayTime) && row.RelayTime != "-") parts.Add("relay time " + row.RelayTime);
        return string.Join(" · ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private void RebuildProtocolMap(EvidenceRow row)
    {
        SelectedProtocolMapLines.Clear();
        SelectedHexSegments.Clear();

        foreach (var line in BuildProtocolMapLines(row))
        {
            SelectedProtocolMapLines.Add(line);
        }

        foreach (var segment in BuildHexSegments(row))
        {
            SelectedHexSegments.Add(segment);
        }
    }

    private static IEnumerable<ProtocolMapLine> BuildProtocolMapLines(EvidenceRow row)
    {
        var bytes = SplitHexBytes(row.RawHex);
        var directionMeaning = row.Direction.Equals("TX", StringComparison.OrdinalIgnoreCase)
            ? "Master-to-relay frame. This is a tester action, not a relay event."
            : row.Direction.Equals("RX", StringComparison.OrdinalIgnoreCase)
                ? "Relay-to-master frame. This is relay evidence returned to the tester."
                : "Session note or diagnostic entry.";

        yield return new ProtocolMapLine("direction", "Direction", directionMeaning, row.Direction);

        if (bytes.Length == 0)
        {
            yield return new ProtocolMapLine("summary", "No raw frame", $"This row is a state/diagnostic note, not a physical {row.ProtocolName} frame.", "-");
            yield break;
        }

        if (row.ProtocolMode == "104")
        {
            yield return new ProtocolMapLine("envelope", "APDU envelope", "IEC-104 APDU starts with 0x68 and a length byte. It is TCP stream framing, not FT1.2 serial framing.", string.Join(" ", bytes.Take(Math.Min(2, bytes.Length))));
            if (bytes.Length >= 6)
            {
                yield return new ProtocolMapLine("control", "APCI control", $"Format={row.ApciFormat}, N(S)={row.SendSequence}, N(R)={row.ReceiveSequence}, U={row.UFormatName}. I/S/U format tells whether this is payload transfer, acknowledgement, or connection control.", string.Join(" ", bytes.Skip(2).Take(4)));
            }
            if (row.ApciFormat == "I" && bytes.Length > 6)
            {
                yield return new ProtocolMapLine("asdu", "ASDU header", BuildAsduHeaderMeaning(row), string.Join(" ", bytes.Skip(6).Take(Math.Min(6, Math.Max(0, bytes.Length - 6)))));
                yield return new ProtocolMapLine("object", "CA / IOA", BuildSignalAddressMeaning(row), row.ProtocolAddress);
                if (!string.IsNullOrWhiteSpace(row.SemanticState))
                {
                    yield return new ProtocolMapLine("payload", "Information element", BuildPayloadMeaning(row), row.SemanticState);
                }
            }
            yield break;
        }

        if (row.ProtocolMode == "101" && bytes[0].Equals("68", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 9)
        {
            yield return new ProtocolMapLine("envelope", "FT1.2 variable frame", "IEC-101 serial variable-length frame. The repeated length block and checksum protect the serial telegram.", string.Join(" ", bytes.Take(4)));
            yield return new ProtocolMapLine("control", "Link control", BuildControlMeaning(row), string.Join(" ", bytes.Skip(4).Take(2)));
            yield return new ProtocolMapLine("asdu", "ASDU header", BuildAsduHeaderMeaning(row), string.Join(" ", bytes.Skip(6).Take(Math.Min(6, Math.Max(0, bytes.Length - 8)))));
            yield return new ProtocolMapLine("object", "Information object address", BuildSignalAddressMeaning(row), row.ProtocolAddress);
            if (!string.IsNullOrWhiteSpace(row.SemanticState))
            {
                yield return new ProtocolMapLine("payload", "Information element", BuildPayloadMeaning(row), row.SemanticState);
            }
            yield return new ProtocolMapLine("check", "Integrity", "Checksum and end byte close the FT1.2 frame. Keep this as audit evidence when discussing serial quality.", string.Join(" ", bytes.Skip(Math.Max(0, bytes.Length - 2)).Take(2)));
            yield break;
        }

        if (bytes[0].Equals("E5", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ProtocolMapLine("envelope", "Single char ACK", "IEC FT1.2 single-character acknowledgement. The relay accepted the previous link/action frame.", "E5");
            yield break;
        }


        if (bytes[0].Equals("10", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 5)
        {
            yield return new ProtocolMapLine("envelope", "FT1.2 fixed frame", $"Short {row.ProtocolName} link frame. Used for reset, Class 1/Class 2 request, ACK, or NO DATA response.", string.Join(" ", bytes.Take(1)));
            yield return new ProtocolMapLine("control", "Control field", BuildControlMeaning(row), bytes.ElementAtOrDefault(1) ?? "-");
            yield return new ProtocolMapLine("address", "Link address", "Slave/link address on the serial IEC-60870 link.", bytes.ElementAtOrDefault(2) ?? "-");
            yield return new ProtocolMapLine("check", "Integrity", "Checksum and stop byte. This proves what was actually transmitted on the wire.", string.Join(" ", bytes.Skip(3).Take(2)));
            yield break;
        }

        if (bytes[0].Equals("68", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 9)
        {
            yield return new ProtocolMapLine("envelope", "FT1.2 variable frame", $"Variable {row.ProtocolName} frame carrying an ASDU. The length bytes define the payload size and must match.", string.Join(" ", bytes.Take(4)));
            yield return new ProtocolMapLine("control", "Link control", BuildControlMeaning(row), string.Join(" ", bytes.Skip(4).Take(2)));
            yield return new ProtocolMapLine("asdu", "ASDU header", BuildAsduHeaderMeaning(row), string.Join(" ", bytes.Skip(6).Take(Math.Min(4, Math.Max(0, bytes.Length - 8)))));

            if (bytes.Length > 11)
            {
                yield return new ProtocolMapLine("object", "FUN / INF", BuildSignalAddressMeaning(row), string.Join(" ", bytes.Skip(10).Take(2)));
            }

            var payloadEnd = Math.Max(12, bytes.Length - 2);
            if (payloadEnd > 12)
            {
                yield return new ProtocolMapLine("payload", "Information element", BuildPayloadMeaning(row), string.Join(" ", bytes.Skip(12).Take(payloadEnd - 12)));
            }

            yield return new ProtocolMapLine("check", "Integrity", "Checksum and end byte close the frame. Keep this as audit evidence when discussing interoperability.", string.Join(" ", bytes.Skip(Math.Max(0, bytes.Length - 2)).Take(2)));
            yield break;
        }

        yield return new ProtocolMapLine("raw", $"Raw {row.ProtocolName} bytes", "The analyzer preserved this frame as raw evidence, but it could not classify it into the expected protocol structure.", string.Join(" ", bytes));
    }

    private static string BuildControlMeaning(EvidenceRow row)
    {
        if (row.Direction == "TX")
        {
            return row.DataClass.Contains("Class 1", StringComparison.OrdinalIgnoreCase)
                ? "Master asks for pending Class 1 event data. This should be done only during ACD=1 event drain or bounded GI follow-up."
                : row.DataClass.Contains("Class 2", StringComparison.OrdinalIgnoreCase)
                    ? "Master performs normal Class 2 background polling."
                    : string.IsNullOrWhiteSpace(row.ReadableMeaning) ? "Master link/control action." : row.ReadableMeaning;
        }

        if (row.Direction == "RX")
        {
            if (row.Acd == "1")
            {
                return "Relay response indicates ACD=1, meaning Class 1 data is pending and the master may drain event data.";
            }

            if (row.ProtocolMeaning.Contains("FC=9", StringComparison.OrdinalIgnoreCase) || row.ReadableMeaning.Contains("ACK", StringComparison.OrdinalIgnoreCase))
            {
                return "Relay acknowledges the link/application command.";
            }

            return string.IsNullOrWhiteSpace(row.ProtocolMeaning) ? "Relay link response." : row.ProtocolMeaning;
        }

        return string.IsNullOrWhiteSpace(row.ReadableMeaning) ? "Link-layer control information." : row.ReadableMeaning;
    }

    private static string BuildAsduHeaderMeaning(EvidenceRow row)
    {
        if (row.ProtocolMode is "101" or "104")
        {
            if (row.TypeIdName == "-" && row.CotDisplay == "-")
            {
                return "No IEC-10x ASDU payload is present in this frame.";
            }

            return $"Type ID={row.TypeIdName}, VSQ={row.Vsq}, COT={row.CotDisplay}, CA={row.CommonAddress}. These fields define the telecontrol data class, cause, station/common address, and object addressing context.";
        }

        if (row.AsduType == "-" && row.Cot == "-")
        {
            return "No ASDU payload is present in this link frame.";
        }

        return $"ASDU={row.AsduType}, COT={row.Cot}. This tells the tester what kind of protection information is being transferred and why it was sent.";
    }

    private static string BuildSignalAddressMeaning(EvidenceRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.SemanticLabel))
        {
            return $"Mapped signal: {row.SemanticLabel}. Raw address remains {row.SignalOrAddress}.";
        }

        if (row.ProtocolMode is "101" or "104")
        {
            return row.IoAddress == "-"
                ? "This ASDU has no decoded IOA."
                : $"Information object address {row.IoAddress} inside common address {row.CommonAddress}. Add an IOA naming profile later to show a readable signal name.";
        }

        return row.FunInf == "-"
            ? "This ASDU has no decoded FUN/INF signal address."
            : $"Unmapped IEC-103 signal address {row.SignalOrAddress}. Add it to the user mapping profile to show a readable signal name.";
    }

    private static string BuildPayloadMeaning(EvidenceRow row)
    {
        var state = string.IsNullOrWhiteSpace(row.SemanticState) ? "state/value" : row.SemanticState;
        var time = string.IsNullOrWhiteSpace(row.RelayTime) || row.RelayTime == "-" ? "No field timestamp decoded." : $"Field timestamp: {row.RelayTime}.";
        var typeText = row.ProtocolMode is "101" or "104" ? row.TypeIdName : row.AsduType;

        if (typeText.Contains("Measur", StringComparison.OrdinalIgnoreCase))
        {
            return $"Measurement payload. Decoded value/state: {state}. Quality: {row.Quality}. {time}";
        }

        if (typeText.Contains("DPI", StringComparison.OrdinalIgnoreCase) ||
            typeText.Contains("SPI", StringComparison.OrdinalIgnoreCase) ||
            typeText.Contains("single", StringComparison.OrdinalIgnoreCase) ||
            typeText.Contains("double", StringComparison.OrdinalIgnoreCase) ||
            typeText.Contains("time-tagged", StringComparison.OrdinalIgnoreCase))
        {
            return $"Status/event payload. Decoded state: {state}. Quality: {row.Quality}. {time}";
        }

        return $"Information element payload. Decoded state/value: {state}. Quality: {row.Quality}. {time}";
    }

    private static string[] SplitHexBytes(string rawHex)
    {
        return rawHex
            .Split(new[] { ' ', '|', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x != "-")
            .ToArray();
    }

    private static IEnumerable<HexSegment> BuildHexSegments(EvidenceRow row)
    {
        var bytes = SplitHexBytes(row.RawHex);

        if (bytes.Length == 0)
        {
            yield break;
        }

        if (row.ProtocolMode == "104")
        {
            yield return new HexSegment("envelope", string.Join(" ", bytes.Take(Math.Min(2, bytes.Length))), "IEC-104 APDU", "0x68 start byte and APDU length for TCP stream framing.");
            if (bytes.Length >= 6)
            {
                yield return new HexSegment("control", string.Join(" ", bytes.Skip(2).Take(4)), "APCI control", $"Format={row.ApciFormat}, N(S)={row.SendSequence}, N(R)={row.ReceiveSequence}, U={row.UFormatName}.");
            }
            if (bytes.Length > 6)
            {
                yield return new HexSegment("asdu", string.Join(" ", bytes.Skip(6).Take(Math.Min(6, Math.Max(0, bytes.Length - 6)))), "ASDU header", BuildAsduHeaderMeaning(row));
                yield return new HexSegment("object", row.ProtocolAddress, "CA / IOA", BuildSignalAddressMeaning(row));
                if (!string.IsNullOrWhiteSpace(row.SemanticState))
                {
                    yield return new HexSegment("payload", row.SemanticState, "Value / quality", BuildPayloadMeaning(row));
                }
            }
            yield break;
        }

        if (row.ProtocolMode == "101" && bytes[0].Equals("68", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 9)
        {
            yield return new HexSegment("envelope", string.Join(" ", bytes.Take(4)), "FT1.2 variable frame", "IEC-101 variable-length serial frame envelope.");
            yield return new HexSegment("control", string.Join(" ", bytes.Skip(4).Take(2)), "Control + link", BuildControlMeaning(row));
            yield return new HexSegment("asdu", string.Join(" ", bytes.Skip(6).Take(Math.Min(6, Math.Max(0, bytes.Length - 8)))), "ASDU header", BuildAsduHeaderMeaning(row));
            yield return new HexSegment("object", row.ProtocolAddress, "Information object address", BuildSignalAddressMeaning(row));
            if (!string.IsNullOrWhiteSpace(row.SemanticState))
            {
                yield return new HexSegment("payload", row.SemanticState, "Value / quality", BuildPayloadMeaning(row));
            }
            yield return new HexSegment("check", string.Join(" ", bytes.Skip(Math.Max(0, bytes.Length - 2)).Take(2)), "Integrity", "Checksum and end byte.");
            yield break;
        }

        if (bytes[0].Equals("10", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 5)
        {
            yield return new HexSegment("envelope", bytes[0], "FT1.2 fixed frame", "Fixed-length link frame envelope.");
            yield return new HexSegment("control", bytes[1], "Control", BuildControlMeaning(row));
            yield return new HexSegment("address", bytes[2], "Link address", "Relay/slave link address.");
            yield return new HexSegment("check", string.Join(" ", bytes.Skip(3).Take(2)), "Integrity", "Checksum and end byte.");
            yield break;
        }

        if (bytes[0].Equals("68", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 9)
        {
            yield return new HexSegment("envelope", string.Join(" ", bytes.Take(4)), "FT1.2 variable frame", "Variable frame start and length block.");
            yield return new HexSegment("control", string.Join(" ", bytes.Skip(4).Take(2)), "Control + link", BuildControlMeaning(row));
            yield return new HexSegment("asdu", string.Join(" ", bytes.Skip(6).Take(Math.Min(4, Math.Max(0, bytes.Length - 8)))), "ASDU header", BuildAsduHeaderMeaning(row));

            if (bytes.Length > 11)
            {
                yield return new HexSegment("object", string.Join(" ", bytes.Skip(10).Take(2)), "Signal address", BuildSignalAddressMeaning(row));
            }

            var payloadEnd = Math.Max(12, bytes.Length - 2);
            if (payloadEnd > 12)
            {
                yield return new HexSegment("payload", string.Join(" ", bytes.Skip(12).Take(payloadEnd - 12)), "State / value / relay time", BuildPayloadMeaning(row));
            }

            yield return new HexSegment("check", string.Join(" ", bytes.Skip(Math.Max(0, bytes.Length - 2)).Take(2)), "Integrity", "Checksum and end byte.");
            yield break;
        }

        yield return new HexSegment("raw", string.Join(" ", bytes), "Raw frame", "Frame bytes are preserved as evidence. This frame is not recognized by the high-level mapper.");
    }

    private void HexSegment_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_pinnedProtocolMapKey != null && PinProtocolMapCheckBox?.IsChecked == true)
        {
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is HexSegment segment)
        {
            SetActiveProtocolMap(segment.Key);
        }
    }

    private void HexSegment_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_pinnedProtocolMapKey != null && PinProtocolMapCheckBox?.IsChecked == true)
        {
            return;
        }

        ClearActiveProtocolMap();
    }

    private void HexSegment_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HexSegment segment)
        {
            _pinnedProtocolMapKey = segment.Key;
            if (PinProtocolMapCheckBox != null)
            {
                PinProtocolMapCheckBox.IsChecked = true;
            }
            SetActiveProtocolMap(segment.Key);
        }
    }

    private void ProtocolMapLine_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_pinnedProtocolMapKey != null && PinProtocolMapCheckBox?.IsChecked == true)
        {
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is ProtocolMapLine line)
        {
            SetActiveProtocolMap(line.Key);
        }
    }

    private void ProtocolMapLine_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_pinnedProtocolMapKey != null && PinProtocolMapCheckBox?.IsChecked == true)
        {
            return;
        }

        ClearActiveProtocolMap();
    }

    private void ProtocolMapLine_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProtocolMapLine line)
        {
            _pinnedProtocolMapKey = line.Key;
            if (PinProtocolMapCheckBox != null)
            {
                PinProtocolMapCheckBox.IsChecked = true;
            }
            SetActiveProtocolMap(line.Key);
        }
    }

    private void ClearProtocolMapHighlight_Click(object sender, RoutedEventArgs e)
    {
        _pinnedProtocolMapKey = null;
        if (PinProtocolMapCheckBox != null)
        {
            PinProtocolMapCheckBox.IsChecked = false;
        }
        ClearActiveProtocolMap();
    }

    private void CopySelectedRawFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFrameRow is null || string.IsNullOrWhiteSpace(_selectedFrameRow.RawHex) || _selectedFrameRow.RawHex == "-")
        {
            return;
        }

        Clipboard.SetText(_selectedFrameRow.RawHex);
        AppendSessionLog($"Copied raw frame #{_selectedFrameRow.Sequence} to clipboard.");
    }

    private void CopySelectedFrameDecode_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFrameRow is null)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Frame #{_selectedFrameRow.Sequence} {BuildLineMonitorTitle(_selectedFrameRow)}");
        builder.AppendLine(BuildCompactFrameExplanation(_selectedFrameRow));
        builder.AppendLine();
        builder.AppendLine("Raw: " + _selectedFrameRow.RawHex);
        Clipboard.SetText(builder.ToString());
        AppendSessionLog($"Copied decoded frame #{_selectedFrameRow.Sequence} to clipboard.");
    }

    private void SetActiveProtocolMap(string key)
    {
        var matched = false;

        foreach (var line in SelectedProtocolMapLines)
        {
            line.IsActive = string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase);
            matched |= line.IsActive;
        }

        foreach (var segment in SelectedHexSegments)
        {
            segment.IsActive = string.Equals(segment.Key, key, StringComparison.OrdinalIgnoreCase);
            matched |= segment.IsActive;
        }

        if (ActiveProtocolMapText is not null)
        {
            ActiveProtocolMapText.Text = matched ? DescribeProtocolMapKey(key) : "linked highlight";
        }
    }

    private void ClearActiveProtocolMap()
    {
        foreach (var line in SelectedProtocolMapLines)
        {
            line.IsActive = false;
        }

        foreach (var segment in SelectedHexSegments)
        {
            segment.IsActive = false;
        }

        if (ActiveProtocolMapText is not null)
        {
            ActiveProtocolMapText.Text = "linked highlight";
        }
    }
}
