// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class PointInvestigationTests
{
    private static ValueRow Point(int ioa, string source, string reference, byte? quality = null,
        bool mapped = false, string relayTime = "no timestamp") =>
        new(new Iec103ValuePoint
        {
            Key = "101|" + source + "|CA:1|IOA:" + ioa,
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            PhysicalSource = source,
            EvidenceReference = reference,
            SignalName = "IOA " + ioa,
            DisplayValue = "ON",
            CommonAddress = 1,
            InformationObjectAddress = ioa,
            RelayTimeText = relayTime,
            IsMapped = mapped,
            QualityText = quality.HasValue ? new Iec10xQualityDetails(Iec10xQualityKind.Siq, quality.Value).DisplayText : "",
            QualityDetails = quality.HasValue ? new Iec10xQualityDetails(Iec10xQualityKind.Siq, quality.Value) : null
        });

    [Fact]
    public void ScopeKeepsMissingQualityAndTimestampExplicitlyUnknown()
    {
        var good = Point(7, "Link A", "101|stream:1|byte:170|object:1", 0);
        var invalid = Point(8, "Link A", "101|stream:1|byte:170|object:2", 0x80);
        var unknown = Point(9, "Link A", "", null);
        Assert.False(PointInvestigation.MatchesScope(good, "Quality flags"));
        Assert.True(PointInvestigation.MatchesScope(invalid, "Quality flags"));
        Assert.False(PointInvestigation.MatchesScope(unknown, "Quality flags"));
        Assert.True(PointInvestigation.MatchesScope(good, "No device time"));
        Assert.False(PointInvestigation.MatchesScope(Point(10, "Link A", "101|stream:1|byte:5|object:1",
            relayTime: "2026-10-31 01:02:03"), "No device time"));
        Assert.True(PointInvestigation.MatchesScope(good, "Unmapped"));
        Assert.False(PointInvestigation.MatchesScope(Point(7, "Link A", "", mapped: true), "Unmapped"));
        Assert.False(PointInvestigation.MatchesScope(good, "Recently changed"));
        good.IsRecentlyChanged = true;
        Assert.True(PointInvestigation.MatchesScope(good, "Recently changed"));
        Assert.Contains("Relay time: not transmitted/unknown", PointInvestigation.Describe(unknown));
        Assert.False(PointInvestigation.HasOriginalReference(unknown));
    }

    [Fact]
    public void MultiObjectPointReferenceNeverSelectsWrongSourceStreamOrOffset()
    {
        var target = Point(8, "Link A", "101|stream:1|byte:170|object:2");
        static EvidenceRow Frame(long offset, string source, int stream = 1) =>
            new(new Iec103MasterEvidenceEvent
            {
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                Direction = FrameDirection.SlaveToMaster,
                PhysicalSource = source, PhysicalStreamId = stream, RawStreamOffset = offset,
                RawHex = "68 00"
            });
        var wrongOffset = Frame(171, "Link A");
        var wrongStream = Frame(170, "Link B", 3);
        var exact = Frame(170, "Link A");
        Assert.Same(exact, PointInvestigation.FindOriginal(
            new[] { wrongOffset, wrongStream, exact }, target));
        Assert.Null(PointInvestigation.FindOriginal(
            new[] { wrongOffset, wrongStream }, target));
        Assert.Null(PointInvestigation.FindOriginal(new[] { exact },
            Point(8, "Link A", "")));
        Assert.True(QuickFindMatcher.MatchesValue(target, "IOA 8", "Unmapped"));
        Assert.False(QuickFindMatcher.MatchesValue(target, "IOA 8", "Quality flags"));
    }

    [Fact]
    public void SnapshotFilterDoesNotChangeEvidenceOrMistakeNumberForQualityFlag()
    {
        var rawHigh = new ValueRow(new Iec103ValuePoint
        {
            Key = "101|SINGLE|CA:1|IOA:101",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            CommonAddress = 1, InformationObjectAddress = 101,
            DisplayValue = "32767", SignalName = "Analog",
            QualityText = "Good",
            QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Qds, 0)
        });
        var senderOv = new ValueRow(new Iec103ValuePoint
        {
            Key = "101|SINGLE|CA:1|IOA:102",
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            CommonAddress = 1, InformationObjectAddress = 102,
            DisplayValue = "32755", SignalName = "Analog",
            QualityText = "Overflow",
            QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Qds, 1)
        });
        Assert.False(PointInvestigation.MatchesScope(rawHigh, "Quality flags"));
        Assert.True(PointInvestigation.MatchesScope(senderOv, "Quality flags"));
        Assert.Equal("32755", senderOv.Value);
        Assert.Equal("32767", rawHigh.Value);
    }
}
