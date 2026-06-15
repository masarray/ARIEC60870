// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Analysis;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec104AndMasterPolicyRegressionTests
{
    [Fact]
    public void Iec104StartDtActivationRoundTripsAsUFormat()
    {
        var decoded = new Iec104ApduParser().Decode(Iec104FrameBuilder.StartDtActivation());

        Assert.True(decoded.IsValid);
        Assert.Equal("U", decoded.Format);
        Assert.Equal("STARTDT act", decoded.UFormatName);
        Assert.Contains("STARTDT", decoded.ShortMeaning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Iec104IFormatRoundTripsSequenceNumbersAndAsdu()
    {
        var settings = new Iec103MasterSettings
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104,
            CauseOfTransmissionSize = 2,
            CommonAddressSize = 2,
            InformationObjectAddressSize = 3,
            CommonAddress = 1
        };
        var asdu = Iec10xAsduBuilder.SinglePoint(settings, ioa: 101, value: true, cause: 3);
        var raw = Iec104FrameBuilder.I(sendSequence: 7, receiveSequence: 5, asdu);

        var decoded = new Iec104ApduParser().Decode(raw);

        Assert.True(decoded.IsValid);
        Assert.Equal("I", decoded.Format);
        Assert.Equal(7, decoded.SendSequence);
        Assert.Equal(5, decoded.ReceiveSequence);
        Assert.NotNull(decoded.Asdu);
        Assert.Equal(1, decoded.Asdu!.TypeId);
        Assert.Equal(101, decoded.Asdu.InformationObjectAddress);
        Assert.Equal("SP=ON", decoded.Asdu.ValueText);
    }

    [Fact]
    public void Iec104LengthMismatchDoesNotHideFrameContext()
    {
        var raw = Iec104FrameBuilder.TestFrActivation();
        raw[1] = 0x05;

        var decoded = new Iec104ApduParser().Decode(raw);

        Assert.False(decoded.IsValid);
        Assert.Equal("U", decoded.Format);
        Assert.Equal("TESTFR act", decoded.UFormatName);
        Assert.Contains(decoded.Issues, issue => issue.Contains("length mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReportSnapshotStripsLocalMappingPathByDefault()
    {
        var settings = new Iec103MasterSettings
        {
            MappingProfilePath = @"C:\Customer\Secret Project\profiles\bay-a.json",
            IncludeLocalPathsInReports = false
        };

        var snapshot = settings.CreateReportSnapshot();

        Assert.Equal("bay-a.json", snapshot.MappingProfilePath);
    }

    [Fact]
    public void AssessmentFailsInvalidFrameQualityAndWarnsMissingGiEnd()
    {
        var settings = new Iec103MasterSettings { SendGeneralInterrogationOnConnect = true };
        var counters = new Iec103MasterCounters
        {
            TxFrames = 5,
            RxFrames = 4,
            Class2Requests = 2,
            GiCommands = 1,
            GiEndResponses = 0,
            ChecksumErrors = 1,
            TimedResponses = 1,
            TotalResponseTimeMs = 25,
            MaxResponseTimeMs = 25
        };

        var assessment = Iec103MasterAssessmentBuilder.Build(
            settings,
            counters,
            events: Array.Empty<Iec103MasterEvidenceEvent>(),
            findings: Array.Empty<Iec103MasterFinding>(),
            valuePoints: Array.Empty<Iec103ValuePoint>(),
            eventLog: Array.Empty<Iec103RelayEventLogEntry>(),
            completedNormally: true);

        Assert.Equal(Iec103AssessmentStatus.Fail, assessment.OverallStatus);
        Assert.Contains(assessment.Items, item => item.Area == "Frame quality" && item.Status == Iec103AssessmentStatus.Fail);
        Assert.Contains(assessment.Items, item => item.Area == "GI" && item.Status == Iec103AssessmentStatus.Warning);
    }

    [Fact]
    public void ControlCommandSummaryUsesOperatorFriendlyLanguage()
    {
        var command = new Iec60870ControlCommandRequest
        {
            Kind = Iec60870ControlCommandKind.DoubleCommand,
            CommonAddress = 1,
            InformationObjectAddress = 2501,
            Value = 2,
            SelectBeforeOperate = true
        };

        Assert.Contains("CA 1", command.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IOA 2501", command.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CLOSE", command.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SELECT", command.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
