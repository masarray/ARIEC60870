// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Core.Model;

namespace ARIEC60870.Master.Model;

/// <summary>Protocol response phase, not an assertion about physical switchgear operation.</summary>
public enum Iec10xCommandResponsePhase
{
    NotApplicable,
    Unverified,
    ActivationConfirmed,
    ActivationTerminated,
    DeactivationConfirmed,
    NegativeConfirmation,
    ProtocolError
}

/// <summary>
/// Evidence-only application command matching rules. They never infer protocol truth
/// from human-readable messages, link ACKs, or an unrelated physical source.
/// </summary>
public static class Iec10xCommandProofRules
{
    public static string CorrelationKey(Iec103MasterEvidenceEvent item)
    {
        if (item.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104) ||
            !item.CommonAddressNumber.HasValue || !item.InformationObjectAddress.HasValue || !item.TypeId.HasValue)
            return string.Empty;

        var source = NormalizeSource(item.PhysicalSource);
        return $"{(int)item.ProtocolMode}|{source.Length}:{source}|CA{item.CommonAddressNumber.Value.ToString(CultureInfo.InvariantCulture)}|" +
               $"IOA{item.InformationObjectAddress.Value.ToString(CultureInfo.InvariantCulture)}|T{item.TypeId.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    public static Iec10xCommandResponsePhase ClassifyResponse(Iec103MasterEvidenceEvent reply)
    {
        if (reply.Direction != FrameDirection.SlaveToMaster ||
            reply.ProtocolMode is not (Iec60870ProtocolMode.Iec101 or Iec60870ProtocolMode.Iec104) ||
            reply.TypeId is not (45 or 46 or 47 or 48 or 49 or 50 or 51 or 58 or 59 or 60 or 61 or 62 or 63 or 64))
            return Iec10xCommandResponsePhase.NotApplicable;

        // A corrupt frame or decoder warning must never confirm an operation.
        if (reply.Frame?.IsChecksumValid == false ||
            string.Equals(reply.Category, "RX Warning", StringComparison.OrdinalIgnoreCase))
            return Iec10xCommandResponsePhase.Unverified;

        var cause = reply.CauseDetails;
        if (cause is null) return Iec10xCommandResponsePhase.Unverified;
        if (cause.Negative) return Iec10xCommandResponsePhase.NegativeConfirmation;
        return cause.Code switch
        {
            7 => Iec10xCommandResponsePhase.ActivationConfirmed,
            9 => Iec10xCommandResponsePhase.DeactivationConfirmed,
            10 => Iec10xCommandResponsePhase.ActivationTerminated,
            44 or 45 or 46 or 47 => Iec10xCommandResponsePhase.ProtocolError,
            _ => Iec10xCommandResponsePhase.Unverified
        };
    }

    /// <summary>ACTTERM without a prior, source-matched ACTCON is incomplete evidence.</summary>
    public static bool HasVerifiedApplicationSequence(bool actConSeen,
        Iec10xCommandResponsePhase phase)
        => phase != Iec10xCommandResponsePhase.ActivationTerminated || actConSeen;

    public static bool HasEchoPhaseMismatch(bool selectPhase, Iec103MasterEvidenceEvent reply)
        => reply.CommandDetails?.Select is bool echoedSelect && echoedSelect != selectPhase;

    public static bool MatchesFeedback(Iec103MasterEvidenceEvent feedback, Iec60870ProtocolMode protocol,
        string physicalSource, int? ca, int feedbackIoa)
    {
        return feedback.Direction == FrameDirection.SlaveToMaster &&
               feedback.ProtocolMode == protocol &&
               feedback.IsRelayValue &&
               feedback.Frame?.IsChecksumValid != false &&
               !string.Equals(feedback.Category, "RX Warning", StringComparison.OrdinalIgnoreCase) &&
               feedback.CommonAddressNumber == ca &&
               feedback.InformationObjectAddress == feedbackIoa &&
               feedback.QualityDetails is { Invalid: false } quality &&
               quality.Blocked != true && quality.Substituted != true && quality.NotTopical != true &&
               string.Equals(NormalizeSource(feedback.PhysicalSource), NormalizeSource(physicalSource),
                   StringComparison.Ordinal);
    }

    private static string NormalizeSource(string? source)
        => string.IsNullOrWhiteSpace(source) ? "SINGLE" : source.Trim().ToUpperInvariant();
}
