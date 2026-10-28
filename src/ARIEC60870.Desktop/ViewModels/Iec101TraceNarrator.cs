// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// One cheap, read-only interpretation of the already decoded FT1.2/ASDU evidence.
/// The line monitor must never infer an ACTTERM, process operation or queue content
/// from a link ACK, ACD, or an unrelated command.
/// </summary>
public static class Iec101TraceNarrator
{
    public static (string Title, string Meaning) Describe(Iec103MasterEvidenceEvent item)
    {
        var direction = item.Direction == FrameDirection.MasterToSlave ? "➤ TX" :
            item.Direction == FrameDirection.SlaveToMaster ? "◀ RX" : "• STATE";
        var link = item.Frame?.LinkControl;
        var prefix = string.IsNullOrWhiteSpace(item.PhysicalSource)
            ? direction : $"{direction} [{item.PhysicalSource}]";
        var dl = item.LinkAddress ?? item.Frame?.LinkAddress;
        var dlText = dl.HasValue ? $"DL {dl.Value}" : "DL ?";
        var kind = string.IsNullOrWhiteSpace(item.DataClass) ? string.Empty : item.DataClass;
        var className = kind.Contains("Class 1", StringComparison.OrdinalIgnoreCase) ? "CLASS 1" :
            kind.Contains("Class 2", StringComparison.OrdinalIgnoreCase) ? "CLASS 2" : "REQUESTED CLASS";
        if (link is not null)
        {
            var flags = link.Prm
                ? $"PRM=1, FCB={Bit(link.Fcb)}, FCV={Bit(link.Fcv)}, FC={link.FunctionCode}"
                : $"PRM=0, ACD={Bit(link.Acd)}, DFC={Bit(link.Dfc)}, FC={link.FunctionCode}";

            if (link.Prm && link.FunctionCode is 10 or 11)
            {
                var requested = link.FunctionCode == 10 ? "CLASS 1" : "CLASS 2";
                return ($"{prefix}  {requested} REQUEST  ·  {dlText}",
                    $"Master requests {requested} queued data from RTU · [{flags}].");
            }

            if (!link.Prm && link.FunctionCode == 9)
            {
                var classText = className == "REQUESTED CLASS" ? "REQUESTED DATA" : $"{className} DATA";
                var queued = link.Acd == true
                    ? "CLASS 1 AVAILABLE (ACD=1); master should request Class 1"
                    : "No Class 1 availability signaled by ACD";
                var busy = link.Dfc == true ? " · RTU flow control busy (DFC=1)" : string.Empty;
                return ($"{prefix}  {classText} NOT AVAILABLE  ·  {dlText}",
                    $"No requested data returned · {queued}{busy} · [{flags}]. This is a link response, not a process failure.");
            }

            if (!link.Prm && link.FunctionCode == 8)
            {
                var asdu = item.AsduDetails;
                var type = asdu?.TypeName ?? item.TypeName;
                if (string.IsNullOrWhiteSpace(type)) type = "ASDU";
                var cause = asdu?.CotDisplay ?? (item.CauseOfTransmission.HasValue
                    ? $"{item.CauseOfTransmission.Value} · {item.CauseName}" : "COT unknown");
                var ca = asdu?.CommonAddress ?? item.CommonAddressNumber;
                var first = item.InformationObjectDetails ?? asdu?.FirstObject;
                var objectCount = asdu?.Objects.Count ?? item.ObjectCount ?? 0;
                var details = $"ASDU Type {asdu?.TypeId ?? item.TypeId}: {type}; COT {cause}; CA={ca?.ToString() ?? "?"}; " +
                    $"objects={objectCount}; [{flags}]";
                if (first is not null)
                {
                    details += $"; IOA {first.InformationObjectAddress}={first.ShortValue}";
                    if (first.QualityDetails is not null) details += $"; Quality={first.QualityDetails.DisplayText}";
                    if (!string.IsNullOrWhiteSpace(first.TimestampText)) details += $"; RTU time={first.TimestampText}";
                }
                if (link.Acd == true) details += "; additional Class 1 data indicated";
                return ($"{prefix}  RTU USER DATA · {type}  ·  {dlText}", details + ".");
            }

            if (!link.Prm && link.FunctionCode == 0)
                return ($"{prefix}  LINK ACK  ·  {dlText}",
                    $"RTU acknowledged the link-layer request · [{flags}]. This does not prove application or physical command success.");

            if (!link.Prm && link.FunctionCode == 1)
                return ($"{prefix}  LINK NACK  ·  {dlText}",
                    $"RTU reported negative link confirmation · [{flags}]. Check the preceding request and link state.");

            if (link.Prm && link.FunctionCode == 0)
                return ($"{prefix}  RESET REMOTE LINK  ·  {dlText}",
                    $"Master requests link reset · [{flags}]. Await RTU link response.");
            if (link.Prm && link.FunctionCode == 9)
                return ($"{prefix}  LINK STATUS REQUEST  ·  {dlText}",
                    $"Master requests link status · [{flags}].");
            if (!link.Prm && link.FunctionCode == 11)
                return ($"{prefix}  LINK STATUS RESPONSE  ·  {dlText}",
                    $"RTU reports link status · [{flags}].");
            if (item.AsduDetails is not null)
            {
                var asdu = item.AsduDetails;
                return ($"{prefix}  {asdu.TypeName}  ·  {dlText}",
                    $"ASDU Type {asdu.TypeId}, COT {asdu.CotDisplay}, CA={asdu.CommonAddress}, " +
                    $"objects={asdu.Objects.Count} · [{flags}]. {asdu.ShortMeaning}");
            }
            return ($"{prefix}  {link.FunctionName.ToUpperInvariant()}  ·  {dlText}",
                $"Link function FC={link.FunctionCode} · [{flags}].");
        }

        if (item.Frame?.IsSingleCharacterAck == true)
            return ($"{prefix}  E5 SHORT RESPONSE",
                "IEC-101 E5 short response: meaning depends on the matched link request. " +
                "It must not be presented as proof that an application command succeeded.");

        if (item.Frame?.IsSingleCharacterNack == true)
            return ($"{prefix}  A2 INVALID IEC-101 RESPONSE",
                "A2 single-character response is not permitted in IEC-101. Keep original bytes for investigation.");

        if (item.Frame is { IsChecksumValid: false })
            return ($"{prefix}  INVALID FT1.2 FRAME", "Checksum/format invalid or incomplete; consult exact raw bytes.");

        return ($"{prefix}  IEC-101 FRAME",
            string.IsNullOrWhiteSpace(item.ProtocolMeaning) ? item.Summary : item.ProtocolMeaning);
    }

    private static string Bit(bool? value) => value is null ? "?" : value.Value ? "1" : "0";
}
