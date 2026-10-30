// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0
using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class QuickFindMatcherTests
{
    [Theory]
    [InlineData(" 68539 ", "IOA 68539", true)]
    [InlineData("ACTCON", "Negative ACTCON", true)]
    [InlineData("a1b2", "HEX A1B2E5", true)]
    [InlineData("68540", "IOA 68539", false)]
    [InlineData("", "IOA 68539", true)]
    public void SearchMatchesTextWithoutChangingSource(string query, string field, bool expected) =>
        Assert.Equal(expected, QuickFindMatcher.MatchesText(query, field));

    [Fact]
    public void TraceScopeRetainsExactTxRxAndDoesNotTreatAllFramesAsCommands()
    {
        var tx = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.MasterToSlave,
            PhysicalSource = "Link A", PhysicalStreamId = 0, RawStreamOffset = 3822,
            RawHex = "680A0A68", TypeId = 46, Summary = "SELECT"
        });
        var rx = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            PhysicalSource = "Link A", PhysicalStreamId = 1, RawStreamOffset = 3810,
            RawHex = "E5", Summary = "response"
        });
        Assert.True(QuickFindMatcher.MatchesTrace(tx, "3822", "All frames"));
        Assert.True(QuickFindMatcher.MatchesTrace(tx, "", "TX / SCADA"));
        Assert.False(QuickFindMatcher.MatchesTrace(tx, "", "RX / RTU"));
        Assert.True(QuickFindMatcher.MatchesTrace(tx, "", "Commands"));
        Assert.False(QuickFindMatcher.MatchesTrace(rx, "", "Commands"));
        Assert.True(QuickFindMatcher.MatchesTrace(rx, "", "RX / RTU"));
    }
}
