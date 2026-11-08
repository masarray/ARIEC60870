// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class ObservationInterpretivePresentationTests
{
    private static EvidenceRow LinkFrame(bool primary, int fc, bool acd = false, bool dfc = false, bool valid = true) =>
        new(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = primary ? FrameDirection.MasterToSlave : FrameDirection.SlaveToMaster,
            RawHex = "10 5B 69 C4 16",
            Frame = new Ft12FrameDecode
            {
                Format = Ft12FrameFormat.FixedLength,
                IsChecksumValid = valid,
                IsLengthValid = true,
                LinkControl = new LinkControlInfo
                {
                    Prm = primary, FunctionCode = fc, Acd = acd, Dfc = dfc
                }
            }
        });

    [Fact]
    public void FocusPreservesEveryRealMasterTxAndMayHideOnlyUnimportantNoData()
    {
        Assert.True(QuickFindMatcher.MatchesTrace(LinkFrame(true, 11), "", "Activity focus"));
        Assert.True(QuickFindMatcher.MatchesTrace(LinkFrame(true, 10), "", "Activity focus"));
        Assert.True(QuickFindMatcher.MatchesTrace(LinkFrame(true, 9), "", "Activity focus"));
        Assert.False(QuickFindMatcher.MatchesTrace(LinkFrame(false, 9), "", "Activity focus"));
        Assert.True(QuickFindMatcher.MatchesTrace(LinkFrame(true, 11), "", "All frames"));
        Assert.True(QuickFindMatcher.MatchesTrace(LinkFrame(false, 9, acd: true), "", "Activity focus"));
        Assert.True(QuickFindMatcher.MatchesTrace(LinkFrame(false, 9, dfc: true), "", "Activity focus"));
        Assert.True(QuickFindMatcher.MatchesTrace(LinkFrame(false, 9, valid: false), "", "Activity focus"));
        Assert.True(QuickFindMatcher.MatchesTrace(LinkFrame(true, 0), "", "Activity focus"));
        var widthMismatch = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Category = "RX Warning",
            Frame = new Ft12FrameDecode
            {
                Format = Ft12FrameFormat.Malformed,
                IsChecksumValid = true,
                IsLengthValid = true,
                Issues = new[] { "LinkWidthMismatch" },
                LinkControl = new LinkControlInfo { Prm = true, FunctionCode = 9 }
            }
        });
        Assert.True(QuickFindMatcher.MatchesTrace(widthMismatch, "", "Activity focus"));
    }

    [Fact]
    public void FocusPreservesDecodedAsduAndUnknownUnverifiedEvidence()
    {
        var asdu = new Iec10xAsduDecode { TypeId = 1, ObjectCount = 1, CommonAddress = 1,
            Objects = new[] { new Iec10xInformationObject { Index = 1, InformationObjectAddress = 7, ValueText = "ON" } } };
        var decoded = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101, AsduDetails = asdu, TypeId = 1
        });
        var unknown = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec104, RawHex = "6804", Summary = "Undecoded APCI"
        });
        Assert.True(QuickFindMatcher.MatchesTrace(decoded, "", "Activity focus"));
        Assert.True(QuickFindMatcher.MatchesTrace(unknown, "", "Activity focus"));
        Assert.Contains("IOA 7", decoded.ProtocolTraceOperatorLine);
        Assert.Contains("1/1 points", decoded.ProtocolTraceOperatorLine);
    }

    [Fact]
    public void TwoColumnLineMonitorKeepsGiSignalValuesBesideOriginalHex()
    {
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 3, CommonAddress = 1, CauseOfTransmission = 20,
            InformationObjectAddressWidth = 3, ObjectCount = 3,
            Objects =
            [
                new Iec10xInformationObject { InformationObjectAddress = 9, ValueText = "DP=ON" },
                new Iec10xInformationObject { InformationObjectAddress = 10, ValueText = "DP=OFF" },
                new Iec10xInformationObject { InformationObjectAddress = 11, ValueText = "DP=INDETERMINATE" }
            ]
        };
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            RawHex = "680C0C686803",
            AsduDetails = asdu
        });
        Assert.Equal("68 0C 0C 68 68 03", row.TimelineHex);
        Assert.Equal("GI data · Double-point", row.TimelineTitle);
        Assert.Equal("CA 1 · TI 3 · COT 20", row.TimelineCompactContext);
        Assert.Contains("3 signals", row.TimelineSignalDigest);
        Assert.Contains("IOA 9 (IOA1:9 - IOA2:0 - IOA3:0)  Value: ON (Double Point)", row.TimelineSignalDigest);
        Assert.Contains("IOA 10 (IOA1:10 - IOA2:0 - IOA3:0)  Value: OFF (Double Point)", row.TimelineSignalDigest);
        Assert.Contains("+1 more in inspector", row.TimelineSignalDigest);
        Assert.DoesNotContain("IOA 11 (", row.TimelineSignalDigest);
        Assert.True(row.HasTimelineSignalDigest);
    }

    [Fact]
    public void SingleSignalHasMappedNameOnlyWhenSourceIsAuthoritative()
    {
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 1, CommonAddress = 2, CauseOfTransmission = 3,
            InformationObjectAddressWidth = 3, ObjectCount = 1,
            Objects = [new Iec10xInformationObject
            {
                InformationObjectAddress = 342, ValueText = "ON"
            }]
        };
        var mapped = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101, AsduDetails = asdu,
            SignalName = "Breaker 110 kV position", IsMappedSignal = true
        });
        var unmapped = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101, AsduDetails = asdu,
            SignalName = "Unverified label", IsMappedSignal = false
        });
        Assert.Contains("Breaker 110 kV position · IOA 342 (IOA1:86 - IOA2:1 - IOA3:0)  Value: ON (Single Point)", mapped.TimelineSignalDigest);
        Assert.DoesNotContain("Unverified label", unmapped.TimelineSignalDigest);
        Assert.Contains("IOA 342 (IOA1:86 - IOA2:1 - IOA3:0)  Value: ON (Single Point)", unmapped.TimelineSignalDigest);
        Assert.Equal("CA 2 · TI 1 · COT 3", mapped.TimelineCompactContext);
    }

    [Fact]
    public void UndecodedAndRoutineFramesNeverGetFabricatedSignalStates()
    {
        var routine = LinkFrame(true, 11);
        Assert.False(routine.HasTimelineSignalDigest);
        Assert.Empty(routine.TimelineSignalDigest);
        var badAsdu = new Iec10xAsduDecode
        {
            TypeId = 3, Issues = ["Incomplete ASDU"], ObjectCount = 1,
            Objects = [new Iec10xInformationObject { InformationObjectAddress = 9, ValueText = "ON" }]
        };
        var frame = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101, AsduDetails = badAsdu
        });
        Assert.Equal("ASDU decode warning · inspect selected frame", frame.TimelineSignalDigest);
        Assert.DoesNotContain("= ON", frame.TimelineSignalDigest);
    }

    [Fact]
    public void MultiObjectProjectionShowsAllObjectsAndNeverFabricatesSqAddressBytes()
    {
        var items = Enumerable.Range(0, 40).Select(i => new Iec10xInformationObject
        {
            Index = i + 1, InformationObjectAddress = 200 + i,
            ValueText = i % 2 == 0 ? "ON" : "OFF",
            InformationObjectOffset = i == 0 ? 6 : -1,
            InformationElementOffset = 9 + i,
            RawBytes = new byte[] { (byte)(i % 2) },
            QualityDetails = i == 0 ? new Iec10xQualityDetails(Iec10xQualityKind.Siq, 0x80) : null,
            TimestampText = i == 0 ? "2026-10-31 01:00:00.000" : ""
        }).ToArray();
        var asdu = new Iec10xAsduDecode { TypeId = 1, ObjectCount = 40, IsSequence = true, Objects = items };
        var rows = DecodedPointRow.FromAsdu(asdu);
        Assert.Equal(40, rows.Count);
        Assert.Equal("IOA 200", rows[0].Address);
        Assert.Equal("Invalid", rows[0].Quality);
        Assert.Equal("No device timestamp", rows[1].RelayTime);
        Assert.Equal("Not provided", rows[1].Quality);
        Assert.Contains("implied by sequence", rows[1].Provenance);
        Assert.Equal("01", rows[1].RawHex);
        Assert.Empty(DecodedPointRow.FromAsdu(null));
    }

    [Fact]
    public void NoGuessWhenDecoderReturnsNoObjectsOrIssues()
    {
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 13, ObjectCount = 2, CommonAddress = 3,
            Objects = new[]
            {
                new Iec10xInformationObject
                {
                    Index = 1, InformationObjectAddress = 19,
                    ValueText = "Not decoded",
                    Issues = new[] { "Truncated quality" },
                    RawBytes = new byte[] { 0xFF }
                }
            }
        };
        var row = DecodedPointRow.FromAsdu(asdu).Single();
        Assert.Contains("Decode warning", row.Quality);
        Assert.Contains("Not decoded", row.Value);
        var evidence = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101, AsduDetails = asdu, TypeId = 13
        });
        Assert.Contains("1/2 points", evidence.ProtocolTraceOperatorLine);
    }
}
