// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>Selection-only children from decoded protocol facts; no inferred wire quality.</summary>
public static class FrameInspectorFieldPresentation
{
    public sealed record DetailRow(string Field, string Value, string Explanation = "");

    /// <summary>
    /// Selected-frame narrative only. Every statement is derived from the
    /// canonical link/ASDU decode, never from HEX string or text keywords.
    /// No GI completion or command execution is inferred from ACTCON/ACK.
    /// </summary>
    public static string HumanSummary(EvidenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var source = row.Source;
        var frame = source.Frame;
        if (frame is { RawBytes.Count: > 0 } &&
            (!frame.IsChecksumValid || !frame.IsLengthValid))
            return "This frame failed the link-layer integrity check. Its decoded content must not be trusted without reviewing the original bytes.";

        var link = frame?.LinkControl;
        var asdu = source.AsduDetails;
        if (asdu is { TypeId: 100, CauseOfTransmission: 7 } &&
            source.Direction == FrameDirection.SlaveToMaster)
            return asdu.IsNegativeConfirm
                ? "The remote device rejected the station interrogation activation. This is not a successful interrogation."
                : "The remote device confirms that it accepted the station interrogation request. This does not mean that all interrogation values have arrived.";
        if (asdu is { TypeId: 100, CauseOfTransmission: 10 } &&
            source.Direction == FrameDirection.SlaveToMaster)
            return "The remote device reports termination of the station interrogation. Check the associated received values separately to assess coverage.";

        if (link is { Prm: true, FunctionCode: 11 })
            return "The master asks the remote station for Class 2 data. This request contains no process values.";
        if (link is { Prm: true, FunctionCode: 10 })
            return "The master asks the remote station for Class 1 data, normally used for event traffic.";
        if (link is { Prm: false, FunctionCode: 9 })
            return link.Acd == true
                ? "The remote station has no data for this specific request, but indicates that Class 1 data is waiting. This is not a link rejection."
                : "The remote station has no data for this specific request. This does not mean that every data class is empty.";
        if (link is { Prm: false, FunctionCode: 0 } && asdu is null)
            return "The remote station acknowledges a link-level request. This acknowledgement does not prove that a physical command operated.";
        if (frame?.IsSingleCharacterAck == true)
            return "A single-character link response was received. Its meaning depends on the preceding request and does not prove physical operation.";
        if (asdu is not null)
            return (source.Direction == FrameDirection.SlaveToMaster
                ? "The remote station transmitted " : "The master transmitted ") +
                asdu.Objects.Count + " decoded information object(s) of type " +
                asdu.TypeId + " (" + asdu.TypeName + ") with cause " +
                asdu.CauseOfTransmission + " (" + asdu.CauseName + ").";
        return "This link message contains no decoded application values. Review its function, status bits and original frame bytes below.";
    }

    public static string PrimaryMessage(bool primary) => primary
        ? "The message is from the primary station, normally the master."
        : "The message is from the secondary station, normally the remote device.";

    public static string AccessDemand(bool? active) => active switch
    {
        true => "The remote device indicates that Class 1 data is pending.",
        false => "The remote device does not signal pending Class 1 data in this frame.",
        _ => "The access-demand status is not encoded or could not be decoded."
    };

    public static string DataFlowControl(bool? active) => active switch
    {
        true => "The remote device indicates that data flow is restricted.",
        false => "The remote device does not indicate a data-flow restriction.",
        _ => "The data-flow-control status is not encoded or could not be decoded."
    };

    public static string CauseExplanation(Iec10xAsduDecode? asdu)
    {
        if (asdu is null) return "No application-service cause is decoded.";
        var cause = asdu.CauseOfTransmission switch
        {
            3 => "Spontaneous process information reported by the sender.",
            6 => "Activation request, not evidence that an operation completed.",
            7 => "Activation confirmation, not proof that interrogation coverage or physical command operation is complete.",
            10 => "Activation termination; review associated data for coverage.",
            _ => asdu.CauseName
        };
        if (asdu.IsNegativeConfirm) cause += " Negative confirmation bit is set.";
        if (asdu.IsTest) cause += " Test bit is set.";
        return cause;
    }


    public static IReadOnlyList<DetailRow> Vsq(byte raw, bool sequence, int objects)
        => [new("Sequence (SQ)", sequence ? "True" : "False"),
            new("Number of objects", objects.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Raw VSQ", $"0x{raw:X2}")];

    public static IReadOnlyList<DetailRow> Ioa(int address)
    {
        if (address is < 0 or > 0xFFFFFF) return [];
        return [
            Byte("IOA1 (low)", address & 0xFF),
            Byte("IOA2 (mid)", (address >> 8) & 0xFF),
            Byte("IOA3 (high)", (address >> 16) & 0xFF)
        ];
    }

    private static DetailRow Byte(string name, int value) => new(name, $"{value} (0x{value:X2})");

    public static IReadOnlyList<DetailRow> Quality(Iec10xQualityDetails? quality)
    {
        if (quality is null) return []; // Unknown must NOT be displayed as False.
        var children = new List<DetailRow>(9)
        {
            new("Invalid (IV)", quality.Invalid.ToString(),
                "When set, the device marks this process value as invalid.")
        };
        static void Add(List<DetailRow> target, string name, bool? bit, string meaning)
        {
            if (bit.HasValue) target.Add(new(name, bit.Value.ToString(), meaning));
        }
        Add(children, "Overflow (OV)", quality.Overflow,
            "The value exceeds its representable measurement range.");
        Add(children, "Not topical (NT)", quality.NotTopical,
            "The process value may not represent the latest source state.");
        Add(children, "Blocked (BL)", quality.Blocked,
            "The source marks the value as blocked.");
        Add(children, "Substituted (SB)", quality.Substituted,
            "The value is marked as substituted rather than the ordinary process reading.");
        Add(children, "Carry (CY)", quality.Carry,
            "The integrated counter indicates a carry or rollover condition.");
        Add(children, "Adjusted (CA)", quality.Adjusted,
            "The integrated counter is marked as adjusted.");
        if (quality.CounterSequence.HasValue)
            children.Add(new("Counter sequence", quality.CounterSequence.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
                "Sequence counter encoded for this integrated total."));
        children.Add(new("Raw quality", $"0x{quality.Raw:X2}",
            "Original decoded quality octet; checksum validity is a separate field."));
        return children;
    }

}
