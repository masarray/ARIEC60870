// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec10xCommandProofRulesTests
{
    private static Iec103MasterEvidenceEvent Reply(int code, bool negative = false, string source = "Link A")
        => new()
        {
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = source, TypeId = 46, CommonAddressNumber = 5,
            InformationObjectAddress = 123,
            CauseDetails = new Iec10xCauseDetails((byte)(code | (negative ? 0x40 : 0)), code, negative, false, 0)
        };

    [Fact]
    public void ActconIsNotActtermOrPhysicalProof()
    {
        Assert.Equal(Iec10xCommandResponsePhase.ActivationConfirmed, Iec10xCommandProofRules.ClassifyResponse(Reply(7)));
        Assert.Equal(Iec10xCommandResponsePhase.ActivationTerminated, Iec10xCommandProofRules.ClassifyResponse(Reply(10)));
        Assert.Equal(Iec10xCommandResponsePhase.NegativeConfirmation, Iec10xCommandProofRules.ClassifyResponse(Reply(7, true)));
        Assert.Equal(Iec10xCommandResponsePhase.ProtocolError, Iec10xCommandProofRules.ClassifyResponse(Reply(47)));
    }

    [Fact]
    public void NoTypedCotMustNotBeGuessedFromEnglishMessage()
    {
        var textOnly = new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.SlaveToMaster, ProtocolMode = Iec60870ProtocolMode.Iec101,
            TypeId = 46, CauseName = "positive, not negative; ACTCON"
        };
        Assert.Equal(Iec10xCommandResponsePhase.Unverified, Iec10xCommandProofRules.ClassifyResponse(textOnly));
    }

    [Fact]
    public void RequestAndReplyKeysIncludePhysicalChannelAndProtocol()
    {
        var a = Reply(7, source: "Link A");
        var b = Reply(7, source: "Link B");
        Assert.NotEqual(Iec10xCommandProofRules.CorrelationKey(a), Iec10xCommandProofRules.CorrelationKey(b));
        Assert.Equal(Iec10xCommandProofRules.CorrelationKey(a),
            Iec10xCommandProofRules.CorrelationKey(Reply(7, source: " link a ")));
        Assert.NotEqual(Iec10xCommandProofRules.CorrelationKey(a),
            Iec10xCommandProofRules.CorrelationKey(new Iec103MasterEvidenceEvent
            {
                ProtocolMode = Iec60870ProtocolMode.Iec104, PhysicalSource = "Link A",
                CommonAddressNumber = 5, InformationObjectAddress = 123, TypeId = 46
            }));
    }

    [Fact]
    public void FeedbackRequiresCorrectSourceCaAndValidQuality()
    {
        var feedback = new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = "Link A", CommonAddressNumber = 5,
            InformationObjectAddress = 211, TypeId = 31, IsRelayValue = true,
            QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Diq, 1)
        };
        Assert.True(Iec10xCommandProofRules.MatchesFeedback(feedback, Iec60870ProtocolMode.Iec101, "link a", 5, 211));
        Assert.False(Iec10xCommandProofRules.MatchesFeedback(feedback, Iec60870ProtocolMode.Iec101, "Link B", 5, 211));
        Assert.False(Iec10xCommandProofRules.MatchesFeedback(feedback, Iec60870ProtocolMode.Iec101, "Link A", 6, 211));
        Assert.False(Iec10xCommandProofRules.MatchesFeedback(feedback, Iec60870ProtocolMode.Iec101, "Link A", 5, 212));
    }

    [Fact]
    public void SelectEchoMismatchIsEvidenceNotSuccess()
    {
        var cmd = new Iec10xCommandDetails(46, 123, "Double command", "DCS=2", "DCO",
            0x82, 9, true, 0, new Iec10xCauseDetails(7, 7, false, false, 0));
        var reply = new Iec103MasterEvidenceEvent { CommandDetails = cmd };
        Assert.True(Iec10xCommandProofRules.HasEchoPhaseMismatch(false, reply));
        Assert.False(Iec10xCommandProofRules.HasEchoPhaseMismatch(true, reply));
    }

    [Fact]
    public void LinkOnlyAckCannotBeMistakenForCommandConfirmation()
    {
        var ack = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            CauseName = "ACK"
        };
        Assert.Equal(Iec10xCommandResponsePhase.NotApplicable, Iec10xCommandProofRules.ClassifyResponse(ack));
    }
    [Fact]
    public void RxWarningsAreNeverPositiveApplicationConfirmations()
    {
        var suspicious = new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Category = "RX Warning",
            TypeId = 46,
            CommonAddressNumber = 5,
            InformationObjectAddress = 123,
            CauseDetails = new Iec10xCauseDetails(7, 7, false, false, 0)
        };
        Assert.Equal(Iec10xCommandResponsePhase.Unverified,
            Iec10xCommandProofRules.ClassifyResponse(suspicious));
    }

    [Theory]
    [InlineData(0x00, true)]  // Healthy SIQ
    [InlineData(0x10, false)] // BL
    [InlineData(0x20, false)] // SB
    [InlineData(0x40, false)] // NT
    [InlineData(0x80, false)] // IV
    public void FeedbackEvidenceRequiresHealthyDecodedQuality(byte bits, bool expected)
    {
        var response = new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = "Link A",
            CommonAddressNumber = 5,
            InformationObjectAddress = 211,
            TypeId = 1,
            IsRelayValue = true,
            QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Siq, bits)
        };
        Assert.Equal(expected, Iec10xCommandProofRules.MatchesFeedback(response,
            Iec60870ProtocolMode.Iec101, "Link A", 5, 211));
    }

    [Fact]
    public void MissingQualityAndBadFrameCannotBecomeCommandFeedback()
    {
        var noQuality = new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = "Link A", CommonAddressNumber = 5,
            InformationObjectAddress = 211, TypeId = 1, IsRelayValue = true
        };
        Assert.False(Iec10xCommandProofRules.MatchesFeedback(noQuality,
            Iec60870ProtocolMode.Iec101, "Link A", 5, 211));
        var rxWarning = new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Category = "RX Warning",
            PhysicalSource = "Link A", CommonAddressNumber = 5,
            InformationObjectAddress = 211, TypeId = 1, IsRelayValue = true,
            QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Siq, 0)
        };
        Assert.False(Iec10xCommandProofRules.MatchesFeedback(rxWarning,
            Iec60870ProtocolMode.Iec101, "Link A", 5, 211));
    }

    [Fact]
    public void OverflowCounterCarryAndAdjustedAreNotCleanFeedback()
    {
        var bcr = new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            CommonAddressNumber = 1, InformationObjectAddress = 10,
            TypeId = 15, IsRelayValue = true,
            QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Bcr, 0x20)
        };
        Assert.False(Iec10xCommandProofRules.MatchesFeedback(bcr,
            Iec60870ProtocolMode.Iec104, "SINGLE", 1, 10));
        Assert.False(Iec10xCommandProofRules.MatchesFeedback(new Iec103MasterEvidenceEvent
        {
            Direction = FrameDirection.SlaveToMaster,
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            CommonAddressNumber = 1, InformationObjectAddress = 10,
            TypeId = 13, IsRelayValue = true,
            QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Qds, 0x01)
        }, Iec60870ProtocolMode.Iec104, "SINGLE", 1, 10));
    }

    [Fact]
    public void ActtermWithoutPriorActconIsNotCompleteTransaction()
    {
        Assert.False(Iec10xCommandProofRules.HasVerifiedApplicationSequence(false,
            Iec10xCommandResponsePhase.ActivationTerminated));
        Assert.True(Iec10xCommandProofRules.HasVerifiedApplicationSequence(true,
            Iec10xCommandResponsePhase.ActivationTerminated));
    }

}
