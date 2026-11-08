// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class ProtocolTruthFrameTraceTests
{
    [Fact]
    public void TruncatedHeaderNeverInventsTiCaOrCotZero()
    {
        var decoded = new Iec10xAsduDecoder().Decode([1, 1, 3, 0, 1]);
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101, AsduDetails = decoded
        });
        Assert.Contains("ASDU header not decoded", row.TimelineEngineeringIdentity);
        Assert.Contains("COT not decoded", row.TimelineEngineeringCause);
        Assert.Equal("ASDU header not decoded", row.TimelineCompactContext);
        Assert.DoesNotContain("TI 0", row.TimelineEngineeringIdentity);
        Assert.DoesNotContain("CA 0", row.TimelineEngineeringIdentity);
        Assert.Contains("ASDU decode warning", row.TimelineSignalDigest);
    }

    [Fact]
    public void OutOfRangeSyntheticIoaIsNeverPresentedAsWrappedPhysicalOctets()
    {
        var asdu = new Iec10xAsduDecode { TypeId = 1, InformationObjectAddressWidth = 3 };
        var objectWithInvalidAddress = new Iec10xInformationObject
        {
            Index = 2, InformationObjectAddress = 16777216, ValueText = "SP=ON"
        };
        var label = Iec10xSignalTraceFormatter.Format(asdu, objectWithInvalidAddress);
        Assert.Contains("out of configured range", label);
        Assert.Contains("Decode warning", label);
        Assert.DoesNotContain("IOA1:0 - IOA2:0 - IOA3:0", label);
    }

    [Fact]
    public void Ti21DisplaysSourceNormalizedValueWithoutInventedQuality()
    {
        var asdu = new Iec10xAsduDecoder().Decode(
            [21, 1, 3, 0, 1, 0, 1, 0, 0, 0, 0x40]);
        Assert.Empty(asdu.Issues);
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101, AsduDetails = asdu
        });
        Assert.Contains("Value: Normalized=0.5 (Normalized)", row.TimelineSignalDigest);
        Assert.Equal("N/A", Assert.Single(DecodedPointRow.FromAsdu(asdu)).Quality);
    }
}
