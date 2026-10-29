// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.Reporting;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class EvidenceSmartFindingAnalyzerRegressionTests
{
    [Fact]
    public void GeneralInterrogationBurstAfterRedundancySwitchIsNotCommandCongestion()
    {
        var rows = new List<EvidenceRow>
        {
            Row(10, "TX", "101", "Link status", "-", "-", "-", "Request link status", "TX Link B/Standby request link status", "10 49 01 00 4A 16"),
            Row(11, "RX", "101", "Link status", "-", "-", "-", "Link status / access demand", "RX Link B/Standby ACD=0 DFC=0", "10 0B 69 00 74 16"),
            Row(14, "TX", "101", "C_IC_NA_1 interrogation command", "100", "6", "0", "IEC-101 general interrogation", "TX Class 2 · Link A/Active · 100 · C_IC_NA_1 interrogation command", "68 0D 0D 68 53 69 00 64 01 06 00 01 00 00 00 14 3C 16"),
            Row(17, "RX", "101", "C_IC_NA_1 interrogation command", "100", "7", "0", "activation confirmation", "RX Class 1 · Link A/Active · 100 · C_IC_NA_1 interrogation command", "68 0D 0D 68 28 69 00 64 01 07 00 01 00 00 00 14 12 16")
        };

        for (var i = 0; i < 14; i++)
        {
            rows.Add(Row(
                19 + i,
                "RX",
                "101",
                "M_SP_TB_1 single-point with CP56Time2a",
                "30",
                "20",
                (8388754 + i).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "IEC-101 value received by station interrogation",
                "RX Class 1 · Link A/Active · 30 · M_SP_TB_1 single-point with CP56Time2a",
                "68 15 15 68 28 69 00 1E 01 14 00 01 22 00 80 01 00 00 00 00 00 00 00 00 16",
                responseTime: "48 ms"));
        }

        var findings = EvidenceSmartFindingAnalyzer.Analyze(rows, Array.Empty<FindingRow>(), Setup()).ToArray();

        Assert.DoesNotContain(findings, finding => finding.Code.Equals("ARIEC-SMART-CLASS1-CONGESTION", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(findings, finding => finding.Code.Equals("ARIEC-SMART-COMMAND-FEEDBACK", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RealControlCommandWithDelayedNoisyClassOneTrafficStillCreatesCongestionFinding()
    {
        var rows = new List<EvidenceRow>
        {
            Row(1, "TX", "101", "C_SC_NA_1 single command", "45", "6", "16712686", "Single command operate", "TX Class 1 · Link A/Active · 45 · C_SC_NA_1 single command", "68 0E 0E 68 53 69 00 2D 01 06 00 01 6E 16 FF 00 01 16")
        };

        for (var i = 0; i < 10; i++)
        {
            rows.Add(Row(
                2 + i,
                "RX",
                "101",
                "M_ME_NA_1 measured value",
                "9",
                "3",
                (9000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "spontaneous analog value",
                "RX Class 1 · Link A/Active · 9 · M_ME_NA_1 measured value spontaneous",
                "68 11 11 68 28 69 00 09 01 03 00 01 00 00 23 00 00 00 16",
                responseTime: "1800 ms"));
        }

        var findings = EvidenceSmartFindingAnalyzer.Analyze(rows, Array.Empty<FindingRow>(), Setup()).ToArray();

        Assert.Contains(findings, finding => finding.Code.Equals("ARIEC-SMART-CLASS1-CONGESTION", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void OperatorStopNoFailoverNoteIsNotImportedAsSmartFinding()
    {
        var findings = new[]
        {
            new FindingRow(new Iec103MasterFinding
            {
                Severity = FindingSeverity.Info,
                Id = "IEC101-DUAL-NO-FAILOVER",
                Title = "IEC-101 dual-link session completed without failover",
                Evidence = "No failover journal entries were recorded.",
                Impact = "This is expected when both links remain healthy during the run.",
                Recommendation = "Inject active-link failure for FAT/SAT proof."
            })
        };

        var rows = new List<EvidenceRow>
        {
            Row(1, "RX", "101", "M_ME_NC_1 short floating measured value", "13", "20", "790447", "IEC-101 value received", "RX Class 2 · Link A/Active · value", "68 11 11 68 08 69 00 0D 01 14 00 01 AF 0F 0C DC C9 40 00 B9 16")
        };

        var smartFindings = EvidenceSmartFindingAnalyzer.Analyze(rows, findings, Setup()).ToArray();

        Assert.DoesNotContain(smartFindings, finding => finding.Code.Contains("NO-FAILOVER", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TcpConnectionResetCreatesRemoteClosedContextFinding()
    {
        var findings = new[]
        {
            new FindingRow(new Iec103MasterFinding
            {
                Severity = FindingSeverity.Error,
                Id = "IEC104-SESSION-FAULT",
                Title = "IEC-104 session faulted",
                Evidence = "An existing connection was forcibly closed by the remote host.",
                Impact = "The IEC-104 client session could not continue.",
                Recommendation = "Check IP address, TCP port 2404, firewall, server active connection limit, CA/COT/IOA profile, and STARTDT handling."
            })
        };

        var rows = new List<EvidenceRow>
        {
            Row(1, "TX", "104", "TESTFR activation", "-", "-", "-", "IEC-104 TESTFR activation", "TX IEC-104 TESTFR activation", "68 04 43 00 00 00"),
            Row(2, "RX", "104", "M_SP_NA_1 single-point", "1", "3", "100", "spontaneous value before reset", "RX IEC-104 I-format value", "68 0E 00 00 00 00 01 01 03 00 01 00 64 00 00 01 00")
        };

        var smartFindings = EvidenceSmartFindingAnalyzer.Analyze(rows, findings, Setup()).ToArray();

        Assert.Contains(smartFindings, finding => finding.Code.Equals("ARIEC-SMART-REMOTE-CLOSED", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LinkNackIsNotNegativeCommandConfirmationInSmartPdfFinding()
    {
        var rows = new[]
        {
            TypedControl(1, FrameDirection.MasterToSlave, 46, 6),
            TypedControl(2, FrameDirection.SlaveToMaster, null, null,
                message: "RTU link NACK / no data; not an application rejection")
        };
        var finding = Assert.Single(EvidenceSmartFindingAnalyzer.Analyze(rows,
            Array.Empty<FindingRow>(), Setup()), x => x.Code == "ARIEC-SMART-COMMAND-FEEDBACK");
        Assert.Equal(EvidenceSmartFindingSeverity.Warning, finding.Severity);
        Assert.DoesNotContain("rejected or negatively confirmed", finding.Problem);
    }

    [Fact]
    public void TypedPnnNegativeActconIsAnErrorOnlyForMatchedPhysicalSource()
    {
        var tx = TypedControl(1, FrameDirection.MasterToSlave, 46, 6);
        var otherLinkNegative = TypedControl(2, FrameDirection.SlaveToMaster, 46, 7,
            negative: true, source: "Link B");
        var notOurReply = Assert.Single(EvidenceSmartFindingAnalyzer.Analyze(
            new[] { tx, otherLinkNegative }, Array.Empty<FindingRow>(), Setup())
            , x => x.Code == "ARIEC-SMART-COMMAND-FEEDBACK");
        Assert.Equal(EvidenceSmartFindingSeverity.Warning, notOurReply.Severity);

        var matchingNegative = TypedControl(3, FrameDirection.SlaveToMaster, 46, 7,
            negative: true);
        var actual = Assert.Single(EvidenceSmartFindingAnalyzer.Analyze(
            new[] { tx, otherLinkNegative, matchingNegative }, Array.Empty<FindingRow>(), Setup())
            , x => x.Code == "ARIEC-SMART-COMMAND-FEEDBACK");
        Assert.Equal(EvidenceSmartFindingSeverity.Error, actual.Severity);
        Assert.Contains("rejected or negatively confirmed", actual.Problem);
    }

    private static EvidenceRow TypedControl(long sequence, FrameDirection direction,
        int? type, int? cot, bool negative = false, string source = "Link A",
        string message = "Control observation")
        => new(new Iec103MasterEvidenceEvent
        {
            SequenceNumber = sequence,
            TimestampUtc = new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc),
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = direction,
            PhysicalSource = source,
            TypeId = type,
            CommonAddressNumber = type.HasValue ? 1 : null,
            InformationObjectAddress = type.HasValue ? 123 : null,
            CauseOfTransmission = cot,
            CauseDetails = cot.HasValue
                ? new ARIEC60870.Master.Protocol.Iec10x.Iec10xCauseDetails(
                    (byte)(cot.Value | (negative ? 0x40 : 0)), cot.Value, negative, false, 0)
                : null,
            Summary = message,
            ProtocolMeaning = message,
            RawHex = "68 00 00 68 16",
            Category = "Passive Traffic"
        });

    private static IReadOnlyList<KeyValuePair<string, string>> Setup()
        => new[]
        {
            new KeyValuePair<string, string>("Protocol", "Iec101"),
            new KeyValuePair<string, string>("Link address", "105"),
            new KeyValuePair<string, string>("Common address", "1"),
            new KeyValuePair<string, string>("Link address size", "2"),
            new KeyValuePair<string, string>("COT / CA / IOA size", "2 / 1 / 3"),
            new KeyValuePair<string, string>("Class 2 interval", "500")
        };

    private static EvidenceRow Row(
        long sequence,
        string direction,
        string protocolMode,
        string asduType,
        string typeId,
        string cotCode,
        string ioa,
        string meaning,
        string title,
        string raw,
        string responseTime = "-")
        => new(new CaptureFrameSnapshot
        {
            Sequence = sequence,
            Time = "12:00:00.000",
            Direction = direction,
            ProtocolName = "IEC-" + protocolMode,
            ProtocolMode = protocolMode,
            State = "Connected",
            Category = direction,
            DataClass = title.Contains("Class 1", StringComparison.OrdinalIgnoreCase) ? "Class 1" : "Class 2",
            Service = asduType,
            Address = string.IsNullOrWhiteSpace(ioa) || ioa == "-" ? "CA=1" : "CA=1, IOA=" + ioa,
            SignalOrAddress = string.IsNullOrWhiteSpace(ioa) || ioa == "-" ? "-" : "IOA " + ioa,
            Value = title.Contains("M_SP", StringComparison.OrdinalIgnoreCase) ? "ON" : "-",
            Quality = "Good",
            AsduType = asduType,
            TypeId = typeId,
            Cot = cotCode == "7" ? "activation confirmation" : cotCode == "6" ? "activation" : cotCode == "20" ? "interrogated by station interrogation" : "spontaneous",
            CotCode = cotCode,
            LinkAddress = "105",
            CommonAddress = "1",
            Ioa = ioa,
            ResponseTime = responseTime,
            Meaning = meaning,
            Detail = meaning,
            RawHex = raw,
            ProtocolTraceTitle = title,
            ProtocolTraceMeaning = meaning,
            ProtocolTraceRaw = "RAW " + raw,
            ProtocolTraceMeta = "#" + sequence + "  12:00:00.000  IEC-" + protocolMode
        });
}
