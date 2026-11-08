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
    public sealed record FrameInspectorProperty(
        string Field, string Value, string Detail,
        IReadOnlyList<FrameInspectorFieldPresentation.DetailRow>? Children = null)
    {
        public bool HasChildren => Children is { Count: > 0 };
        public string ExpansionMark => HasChildren ? "▸" : "";
    }

    private void PopulateFrameProperties(EvidenceRow row)
    {
        SelectedFrameProperties.Clear();
        SelectedDecodedPoints.Clear();
        var asdu = row.Source.AsduDetails;
        var frame = row.Source.Frame;
        var link = frame?.LinkControl;

        // Only the selected row pays for the full point and field projections.
        // Strict, read-only signal-list lookup for every decoded object.
        // Do not apply source-CA alias learning or invent names in the inspector.
        Func<int, string?>? signalName = asdu is { Issues.Count: 0 } &&
            row.Direction == "RX" && row.ProtocolMode is "101" or "104"
            ? ioa => _ioaProfile.ResolveObservedSignal(
                asdu.CommonAddress, ioa, asdu.TypeId)?.Name
            : null;
        var decodedPoints = DecodedPointRow.FromAsdu(asdu, signalName);
        foreach (var point in decodedPoints)
            SelectedDecodedPoints.Add(point);
        // A single point must not waste half the inspector with an empty fixed
        // table. Two-point and burst ASDUs grow only within a bounded viewport.
        // No resize work occurs on receive; selected-frame UI projection only.
        DecodedPointGrid.Height = decodedPoints.Count switch
        {
            0 => 0,
            1 => 108,
            2 => 164,
            _ => 224
        };
        DecodedPointSection.Visibility = decodedPoints.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DecodedPointHeaderText.Text = decodedPoints.Count > 0
            ? $"Information objects · {decodedPoints.Count} / {asdu!.ObjectCount}"
            : "Information objects";

        void Add(string field, string? value, string? detail = null,
            IReadOnlyList<FrameInspectorFieldPresentation.DetailRow>? children = null)
        {
            if (!string.IsNullOrWhiteSpace(value) && value != "-")
                SelectedFrameProperties.Add(new FrameInspectorProperty(
                    field, value, detail ?? string.Empty, children));
        }
        static string? Bit(bool? value) => value.HasValue ? (value.Value ? "1 · Set" : "0 · Clear") : null;

        // Communication provenance: host clock is never substituted for RTU time.
        Add("Observed frame number", row.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Stable sequence identity from the captured session; search and filters do not renumber physical frames.");
        Add("Host observation time", row.Time,
            "Time recorded by this computer; it is not a timestamp supplied by the device.");
        Add("Communication direction", row.Direction,
            row.Direction == "TX" ? "From master toward remote device." :
            row.Direction == "RX" ? "From remote device toward master." : "Direction is not established.");
        Add("Protocol", row.ProtocolName);
        Add("Physical connection", row.Source.PhysicalSource);
        Add("Receive stream", row.Source.PhysicalStreamId?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add("Physical stream byte offset", row.Source.RawStreamOffset?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add("Evidence reference", row.Source.EvidenceReference);
        Add("Link address", row.LinkAddress,
            "Data-link station address; independent from the ASDU common address.");

        // Link layer: primary and secondary control bits have different meanings.
        if (link is not null)
        {
            Add("Link function", link.FunctionCode + " · " + link.FunctionName,
                !link.Prm && link.FunctionCode == 9
                    ? "The requested data is unavailable; this is not a link negative acknowledgement."
                    : "The meaning of the function code depends on the primary-message bit.");
            Add("Primary message bit (PRM)", link.Prm ? "1 · Master" : "0 · Remote",
                FrameInspectorFieldPresentation.PrimaryMessage(link.Prm));
            if (link.Prm)
            {
                Add("Frame count bit (FCB)", Bit(link.Fcb),
                    "Alternating bit used by the primary link procedure; interpret with frame-count-valid.");
                Add("Frame count valid (FCV)", Bit(link.Fcv),
                    "Indicates whether frame-count-bit processing is meaningful for this primary frame.");
            }
            else
            {
                Add("Access demand (ACD)", Bit(link.Acd),
                    FrameInspectorFieldPresentation.AccessDemand(link.Acd));
                Add("Data flow control (DFC)", Bit(link.Dfc),
                    FrameInspectorFieldPresentation.DataFlowControl(link.Dfc));
            }
        }

        // The original decoded fields and structured children are retained for
        // existing tooling, keyboard navigation and regression coverage.
        Add("Type", row.TypeId == "-" ? row.AsduType :
            Iec10xTypeIdPresentation.Tooltip(
                int.TryParse(row.TypeId, out var typeId) ? typeId : null, row.ProtocolMode),
            "Application data type and its full standard description.");
        Add("Value structure (VSQ)", asdu is null ? row.Vsq :
            $"{asdu.ObjectCount} object{(asdu.ObjectCount == 1 ? "" : "s")}",
            asdu is null ? row.Vsq :
            $"SQ={(asdu.IsSequence ? 1 : 0)}; {asdu.Objects.Count} objects decoded. A sequential address may be implied, not encoded for each object.",
            asdu is null ? null : FrameInspectorFieldPresentation.Vsq(
                asdu.VariableStructureQualifier, asdu.IsSequence, asdu.ObjectCount));
        Add("COT", row.CotDisplay, FrameInspectorFieldPresentation.CauseExplanation(asdu));
        if (asdu is not null)
        {
            Add("Negative confirmation", asdu.IsNegativeConfirm ? "Yes" : "No",
                "A negative application confirmation is distinct from a link NACK.");
            Add("Test indication", asdu.IsTest ? "Yes" : "No",
                "The application message is marked as test traffic only when this bit is set.");
            if (asdu.CotWidth == 2)
                Add("Originator address", asdu.OriginatorAddress.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "Application originator identifier from the second cause-of-transmission byte.");
        }
        Add("Common address", row.CommonAddress,
            "ASDU common address, not the data-link address.");
        Add("IOA", row.IoAddress, "Information object address of the selected source object.",
            int.TryParse(row.IoAddress, out var decodedIoa)
                ? FrameInspectorFieldPresentation.Ioa(decodedIoa)
                : null);
        if (row.ProtocolMode == "103") Add("FUN / INF", row.FunInf);
        Add("Object count", row.ObjectCount);
        Add("Signal name", row.SemanticLabel,
            "A name is displayed only when supplied by the captured signal mapping.");
        Add("Value", row.SemanticState);
        var decodedQuality = row.Source.QualityDetails ?? asdu?.FirstObject?.QualityDetails;
        Add("Quality", row.Quality, decodedQuality?.BitDetails,
            FrameInspectorFieldPresentation.Quality(decodedQuality));
        if (asdu is not null)
            Add("Device event timestamp", asdu.Objects.Any(x => !string.IsNullOrWhiteSpace(x.TimestampText))
                ? "Encoded in one or more information objects" : "Not encoded",
                "See each object above for its own device timestamp and validity. The host observation time is separate.");
        else
            Add("Device event timestamp", "Not encoded in this link-only frame",
                "Do not substitute the host clock for a missing remote timestamp.");
        Add("IED / RTU time", row.RelayTime);
        Add("Command", row.Source.CommandDetails?.Detail);
        Add("APCI", row.ApciFormat == "-" ? null :
            row.ApciFormat + " · NS=" + row.SendSequence + " · NR=" + row.ReceiveSequence);
        if (frame is not null)
        {
            Add("Frame checksum", frame.IsChecksumValid ? "Valid" : "Invalid",
                "This checks FT1.2 link-byte integrity only, not process quality or command execution.");
            Add("Frame length", frame.IsLengthValid ? "Valid" : "Invalid",
                "Link frame length validation is independent of the application-data decode.");
            Add("Original frame size", frame.RawBytes.Count + " bytes",
                "The complete captured byte sequence is available in Original HEX below.");
            if (frame.Issues.Count > 0)
                Add("Link decode warnings", string.Join("; ", frame.Issues));
        }
        else Add("Frame checksum", "Not verified",
            "No decoded FT1.2 wire frame was attached to this evidence.");
        if (asdu?.Issues.Count > 0)
            Add("ASDU decode warnings", string.Join("; ", asdu.Issues),
                "A valid link checksum does not guarantee a valid application payload.");

        // Every decoded point stays in the virtualized table; no hidden 16-item
        // limit or repeated giant per-frame object tree is introduced.
    }

    private void EvidenceGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Covers keyboard, context menu and programmatic selection as well as
        // mouse gestures. A selected frame must never be chased by AUTO.
        if (ReferenceEquals(sender, FrameTraceGrid) &&
            FrameTraceGrid?.SelectedItems.Count > 0 && IsFollowLiveEnabled())
            EnterLineMonitorHoldForUserSelection();

        // An engineer selecting a frame always updates the persistent inspector.
        // Live follow remains stopped while the evidence is being read.

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
            TraceInspectorRawText.Text = "Select a frame to see complete original bytes.";
            _selectedFrameExplanation = "Select a frame. This panel translates raw bytes into commissioning meaning.";
            SelectedLineSummaryText.Text = _selectedFrameExplanation;
            SelectedLineDirectionText.Text = "Select a frame";
            SelectedLineSummaryText.Text = "Select a frame on the left to inspect communication, signals, quality and device time.";
            SelectedLineTrustText.Text = "";
            SelectedLineAlertText.Text = "";
            SelectedLineAlertText.Visibility = Visibility.Collapsed;
            SelectedProtocolMapLines.Clear();
            SelectedHexSegments.Clear();
            SelectedFrameProperties.Clear();
            SelectedDecodedPoints.Clear();
            DecodedPointSection.Visibility = Visibility.Collapsed;
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
        TraceInspectorRawText.Text = row.TimelineHex;
        _selectedFrameExplanation = explanation;
        SelectedLineDirectionText.Text = "#" + row.Sequence + " · " + row.Time +
            " · " + row.Direction + " · " + row.ProtocolName;
        SelectedLineSummaryText.Text = FrameInspectorFieldPresentation.HumanSummary(row);
        SelectedLineSummaryText.Visibility = Visibility.Visible;
        SelectedLineTrustText.Text = EngineerFramePresentation.TrustSummary(row.Source);
        SelectedLineAlertText.Text = row.TimelineAlerts;
        SelectedLineAlertText.Visibility = row.HasTimelineAlerts
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateFrameInterpreterTone(row);
        PopulateFrameProperties(row);
        RebuildProtocolMap(row);
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
            "104" when row.Source.AsduDetails?.Objects.Count > 0 => "value-1",
            "104" when row.ApciFormat == "I" => "asdu-type",
            "104" => "apci",
            "101" when row.Source.AsduDetails?.Objects.Count > 0 => "value-1",
            "101" when row.Source.AsduDetails is not null => "asdu-type",
            "101" => "control",
            "103" when row.FunInf != "-" => "fun",
            "103" => "control",
            _ => "raw"
        };

        SetActiveProtocolMap(key);
    }

    private static string DescribeProtocolMapKey(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "apci" => "APCI selected",
            "fun" => "IEC-103 FUN selected",
            "inf" => "IEC-103 INF selected",
            _ when key.StartsWith("implicit-ioa-", StringComparison.OrdinalIgnoreCase)
                => "SQ=1 derived IOA; no address bytes on wire",
            "ft12" => "FT1.2 selected",
            "control" => "link control selected",
            "asdu" => "ASDU header selected",
            "cot" => "COT and flags selected",
            "object" => "object address selected",
            _ when key.StartsWith("object-", StringComparison.OrdinalIgnoreCase) => "IOA bytes selected",
            _ when key.StartsWith("value-", StringComparison.OrdinalIgnoreCase) => "value bytes selected",
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
        // Neutral inspector surface: meaning is conveyed by exact byte groups,
        // not a full-height green/blue panel whose color competes with the text.
        var background = tone == "Error" ? Color.FromRgb(251, 238, 238) : Color.FromRgb(242, 246, 249);
        var border = tone == "Error" ? Color.FromRgb(242, 185, 185) : Color.FromRgb(215, 226, 234);

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

        if (row.Source.CauseDetails is { } cot)
            parts.Add("COT / RTU response: " + cot.Detail);
        if (row.Source.CommandDetails is { } cmd)
            parts.Add("Command / response: " + cmd.Detail);
        if (row.Source.QualityDetails is { } quality)
            parts.Add("Quality bits: " + quality.BitDetails);
        if (row.Source.InformationObjectDetails is not null &&
            !string.IsNullOrWhiteSpace(row.Source.EvidenceReference))
            parts.Add("Evidence reference (session-local): " + row.Source.EvidenceReference +
                ". For passive captures, verify against the original journal before claiming raw integrity.");
        if (row.Source.InformationObjectDetails is { } selectedObject)
            parts.Add($"Object #{selectedObject.Index} IOA={selectedObject.InformationObjectAddress}; " +
                $"ASDU IOA offset={(selectedObject.InformationObjectOffset < 0 ? "implicit SQ" : selectedObject.InformationObjectOffset.ToString())}; " +
                $"element byte offset={selectedObject.InformationElementOffset}; length={selectedObject.RawBytes.Count}.");
        if (row.Source.AsduDetails is { Objects.Count: > 1 } parsed &&
            row.Source.InformationObjectDetails?.Index == 1)
        {
            // Lazy, bounded selected-frame inspection. The complete decoded object array
            // stays available as one shared source, without adding UI grid columns.
            foreach (var obj in parsed.Objects.Take(16))
                parts.Add($"#{obj.Index} IOA={obj.InformationObjectAddress}: {obj.ShortValue}; " +
                    $"quality={obj.QualityDetails?.DisplayText ?? "N/A"}; element byte={obj.InformationElementOffset}.");
            if (parsed.Objects.Count > 16)
                parts.Add($"+{parsed.Objects.Count - 16} more objects retained in the decoded ASDU.");
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

        // One source-byte authority across 101/103/104. No synthesized address
        // or value text is allowed to masquerade as a physical HEX segment.
        if (row.ProtocolMode is "101" or "103" or "104")
        {
            var fields = IecForensicByteInspector.Explain(row.Source);
            if (fields.Count > 0)
            {
                foreach (var field in fields)
                {
                    SelectedProtocolMapLines.Add(new ProtocolMapLine(field.Key,
                        field.Title, field.Meaning, field.Hex));
                    if (field.Count > 0)
                        SelectedHexSegments.Add(new HexSegment(field.Key,
                            field.Hex, field.Title, field.Meaning));
                }
                return;
            }
        }

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
            if (row.Source.CommandDetails is { } cmd104)
                yield return new ProtocolMapLine("command", "Command / RTU response", cmd104.Detail, CommandEvidence(row, bytes, 6));
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
            var linkSize = Math.Clamp(row.Source.Frame?.LinkAddressSize ?? 1, 0, 2);
            var asduStart = 5 + linkSize;
            yield return new ProtocolMapLine("control", "Link control and address", BuildControlMeaning(row), BuildControlEvidence(row, string.Join(" ", bytes.Skip(4).Take(1 + linkSize))));
            yield return new ProtocolMapLine("asdu", "ASDU header", BuildAsduHeaderMeaning(row), string.Join(" ", bytes.Skip(asduStart).Take(Math.Min(6, Math.Max(0, bytes.Length - asduStart - 2)))));
            if (row.Source.CauseDetails is { } cause)
                yield return new ProtocolMapLine("cot", "COT flags", cause.Detail, string.Join(" ", bytes.Skip(asduStart + 2).Take(cause.OriginatorAddress.HasValue ? 2 : 1)));
            yield return new ProtocolMapLine("object", "Information object address", BuildSignalAddressMeaning(row), row.ProtocolAddress);
            if (row.Source.CommandDetails is { } cmd101)
                yield return new ProtocolMapLine("command", "Command / RTU response", cmd101.Detail, CommandEvidence(row, bytes, 5 + (row.Source.Frame?.LinkAddressSize ?? 1)));
            if (!string.IsNullOrWhiteSpace(row.SemanticState))
            {
                yield return new ProtocolMapLine("payload", "Information element", BuildPayloadMeaning(row), row.SemanticState);
            }
            yield return new ProtocolMapLine("check", "Integrity", "Checksum and end byte close the FT1.2 frame. Keep this as audit evidence when discussing serial quality.", string.Join(" ", bytes.Skip(Math.Max(0, bytes.Length - 2)).Take(2)));
            yield break;
        }

        if (bytes[0].Equals("E5", StringComparison.OrdinalIgnoreCase))
        {
            var meaning = row.ProtocolMode == "101"
                ? "IEC-101 E5 is contextual: link acknowledgement or NO DATA after a pending Class 1/2 poll. " +
                  "Without an unambiguous matched request, the reply outcome remains unknown."
                : "IEC-103 E5 single-character link acknowledgement; it does not prove an application command or physical operation succeeded.";
            yield return new ProtocolMapLine("envelope", "E5 short response", meaning, "E5");
            yield break;
        }


        if (bytes[0].Equals("10", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 5)
        {
            yield return new ProtocolMapLine("envelope", "FT1.2 fixed frame", $"Short {row.ProtocolName} link frame. Used for reset, Class 1/Class 2 request, ACK, or NO DATA response.", string.Join(" ", bytes.Take(1)));
            var linkSize = Math.Clamp(row.Source.Frame?.LinkAddressSize ?? 1, 0, 2);
            yield return new ProtocolMapLine("control", "Control field", BuildControlMeaning(row), BuildControlEvidence(row, bytes.ElementAtOrDefault(1) ?? "-"));
            yield return new ProtocolMapLine("address", "Link address", "Slave/link address on the serial IEC-60870 link.", linkSize == 0 ? "N/A (0 byte)" : string.Join(" ", bytes.Skip(2).Take(linkSize)));
            yield return new ProtocolMapLine("check", "Integrity", "Checksum and stop byte. This proves what was actually transmitted on the wire.", string.Join(" ", bytes.Skip(2 + linkSize).Take(2)));
            yield break;
        }

        if (bytes[0].Equals("68", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 9)
        {
            yield return new ProtocolMapLine("envelope", "FT1.2 variable frame", $"Variable {row.ProtocolName} frame carrying an ASDU. The length bytes define the payload size and must match.", string.Join(" ", bytes.Take(4)));
            yield return new ProtocolMapLine("control", "Link control", BuildControlMeaning(row), BuildControlEvidence(row, string.Join(" ", bytes.Skip(4).Take(2))));
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

    private static string CommandEvidence(EvidenceRow row, string[] bytes, int asduStart)
    {
        var cmd = row.Source.CommandDetails;
        if (cmd?.QualifierAsduOffset is int offset && asduStart + offset < bytes.Length)
            return bytes[asduStart + offset];
        return cmd?.QualifierRaw is byte raw ? $"0x{raw:X2}" : "No qualifier byte";
    }

    private static string BuildControlEvidence(EvidenceRow row, string rawControl)
    {
        var flags = row.LinkFlags == "-" ? string.Empty : " · " + row.LinkFlags;
        return string.IsNullOrWhiteSpace(rawControl) ? flags.TrimStart(' ', '·') : rawControl + flags;
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
            if (row.Source.Frame?.LinkControl is { Prm: false, FunctionCode: 9 })
                return "Secondary FC=9: NO REQUESTED DATA; not an application command confirmation.";
            if (row.Source.Frame?.LinkControl is { Prm: false, FunctionCode: 0 })
                return "Secondary FC=0: link-layer ACK, not proof that the physical command operated.";
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

            var detail = row.Source.CauseDetails?.Detail ?? $"COT={row.CotDisplay}";
            return $"Type ID={row.TypeIdName}, VSQ={row.Vsq}, {detail} CA={row.CommonAddress}.";
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

        if (row.Source.CommandDetails is { } command) return command.Detail;

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
        // Master uses spaced HEX; Passive Monitor/archives may use compact HEX.
        // This executes only upon selection, not in the receive loop.
        var tokens = rawHex.Split(new[] { ' ', '|', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            if (token == "-") continue;
            if (token.Length > 2 && (token.Length & 1) == 0 && token.All(Uri.IsHexDigit))
                for (var i = 0; i < token.Length; i += 2) bytes.Add(token.Substring(i, 2));
            else bytes.Add(token);
        }
        return bytes.ToArray();
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
            var linkSize = Math.Clamp(row.Source.Frame?.LinkAddressSize ?? 1, 0, 2);
            var asduStart = 5 + linkSize;
            yield return new HexSegment("control", string.Join(" ", bytes.Skip(4).Take(1 + linkSize)), "Control + link", BuildControlMeaning(row));
            yield return new HexSegment("asdu", string.Join(" ", bytes.Skip(asduStart).Take(Math.Min(6, Math.Max(0, bytes.Length - asduStart - 2)))), "ASDU header", BuildAsduHeaderMeaning(row));
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
            var linkSize = Math.Clamp(row.Source.Frame?.LinkAddressSize ?? 1, 0, 2);
            yield return new HexSegment("address", linkSize == 0 ? "N/A" : string.Join(" ", bytes.Skip(2).Take(linkSize)), "Link address", "Relay/slave link address.");
            yield return new HexSegment("check", string.Join(" ", bytes.Skip(2 + linkSize).Take(2)), "Integrity", "Checksum and end byte.");
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

    private void CopySelectedSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFrameRow is not { } row) return;
        Clipboard.SetText($"Frame #{row.Sequence} · {row.Direction} · {row.Time}" +
            Environment.NewLine + row.TimelineTitle + Environment.NewLine +
            FrameInspectorFieldPresentation.HumanSummary(row) + Environment.NewLine +
            row.TimelineContext);
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
        if (_selectedFrameRow.ProtocolMode is "101" or "103" or "104")
        {
            var fields = IecForensicByteInspector.Explain(_selectedFrameRow.Source);
            foreach (var field in fields)
                builder.AppendLine($"{field.Hex}  @ byte {field.Offset} [{field.Count}] → {field.Title}: {field.Meaning}");
            if (fields.Count == 0) builder.AppendLine("No verified byte map. Retain raw evidence.");
        }
        else builder.AppendLine(BuildCompactFrameExplanation(_selectedFrameRow));
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
