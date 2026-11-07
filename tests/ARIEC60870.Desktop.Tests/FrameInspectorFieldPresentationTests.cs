// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class FrameInspectorFieldPresentationTests
{
    [Theory]
    [InlineData(790446, 174, 15, 12)]
    [InlineData(16712686, 174, 2, 255)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0xFFFFFF, 255, 255, 255)]
    public void IoaBytesFollowActualIecLittleEndianOrder(
        int ioa, int low, int mid, int high)
    {
        var rows = FrameInspectorFieldPresentation.Ioa(ioa);
        Assert.Equal(3, rows.Count);
        Assert.Equal("IOA1 (low)", rows[0].Field);
        Assert.StartsWith(low.ToString(System.Globalization.CultureInfo.InvariantCulture), rows[0].Value);
        Assert.Equal("IOA2 (mid)", rows[1].Field);
        Assert.StartsWith(mid.ToString(System.Globalization.CultureInfo.InvariantCulture), rows[1].Value);
        Assert.Equal("IOA3 (high)", rows[2].Field);
        Assert.StartsWith(high.ToString(System.Globalization.CultureInfo.InvariantCulture), rows[2].Value);
    }

    [Fact]
    public void VsqHasReadableSequenceAndCountAlongsideOriginalHex()
    {
        var rows = FrameInspectorFieldPresentation.Vsq(0x82, true, 2);
        Assert.Equal("True", rows[0].Value);
        Assert.Equal("2", rows[1].Value);
        Assert.Equal("0x82", rows[2].Value);
    }

    [Fact]
    public void QualityUnknownOrInapplicableIsNotInventedAsFalse()
    {
        var qds = FrameInspectorFieldPresentation.Quality(new Iec10xQualityDetails(Iec10xQualityKind.Qds, 0x81));
        Assert.Contains(qds, x => x.Field == "Invalid (IV)" && x.Value == "True");
        Assert.Contains(qds, x => x.Field == "Overflow (OV)" && x.Value == "True");
        Assert.Contains(qds, x => x.Field == "Blocked (BL)" && x.Value == "False");
        var diq = FrameInspectorFieldPresentation.Quality(new Iec10xQualityDetails(Iec10xQualityKind.Diq, 0));
        Assert.DoesNotContain(diq, x => x.Field == "Overflow (OV)");
        Assert.Empty(FrameInspectorFieldPresentation.Quality(null));
        Assert.Empty(FrameInspectorFieldPresentation.Ioa(-1));
    }

    [Fact]
    public void AllClearDecodedQualityStillListsApplicableFalseFlags()
    {
        var flags = FrameInspectorFieldPresentation.Quality(
            new Iec10xQualityDetails(Iec10xQualityKind.Siq, 0));
        Assert.Contains(flags, x => x.Field == "Invalid (IV)" && x.Value == "False");
        Assert.DoesNotContain(flags, x => x.Field == "Carry (CY)");
        Assert.DoesNotContain(flags, x => x.Field == "Overflow (OV)");
    }
}
