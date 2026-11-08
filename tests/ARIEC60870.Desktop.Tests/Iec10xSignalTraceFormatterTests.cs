// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec10xSignalTraceFormatterTests
{
    [Fact]
    public void SinglePointShowsThreeLittleEndianIoaOctetsAndTypedState()
    {
        var asdu = new Iec10xAsduDecode { TypeId = 1, InformationObjectAddressWidth = 3 };
        var obj = new Iec10xInformationObject
        {
            InformationObjectAddress = 0x80006A, ValueText = "SP=ON"
        };
        var text = Iec10xSignalTraceFormatter.Format(asdu, obj, "52-BKR");
        Assert.Contains("52-BKR · IOA 8388714 (IOA1:106 - IOA2:0 - IOA3:128)", text);
        Assert.Contains("Value: ON (Single Point)", text);
        Assert.DoesNotContain("OV=1", text);
    }

    [Fact]
    public void DoublePointPreservesIntermediateOrIndeterminateNotForcedOnOff()
    {
        var asdu = new Iec10xAsduDecode { TypeId = 31, InformationObjectAddressWidth = 3 };
        var obj = new Iec10xInformationObject
        {
            InformationObjectAddress = 97, ValueText = "DP=INDETERMINATE",
            QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Diq, 0x80)
        };
        var text = Iec10xSignalTraceFormatter.Format(asdu, obj);
        Assert.Contains("Value: INDETERMINATE (Double Point)", text);
        Assert.Contains("IV=1 Invalid", text);
        Assert.DoesNotContain("OV=1", text);
    }

    [Fact]
    public void FloatStaysNumericAndOverflowHasExplicitApplicableFlag()
    {
        var asdu = new Iec10xAsduDecode { TypeId = 13, InformationObjectAddressWidth = 3 };
        var obj = new Iec10xInformationObject
        {
            InformationObjectAddress = 342, ValueText = "Float=12.7",
            QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Qds, 0x81)
        };
        var text = Iec10xSignalTraceFormatter.Format(asdu, obj);
        Assert.Contains("IOA 342 (IOA1:86 - IOA2:1 - IOA3:0)", text);
        Assert.Contains("Value: 12.7 (Float)", text);
        Assert.Contains("OV=1 Overflow", text);
        Assert.Contains("IV=1 Invalid", text);
        Assert.DoesNotContain("Value: ON", text);
    }

    [Fact]
    public void SequenceUsesDerivedLogicalIoaButDoesNotInventWireOctets()
    {
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 3, InformationObjectAddressWidth = 3, IsSequence = true
        };
        var obj = new Iec10xInformationObject
        {
            Index = 2, InformationObjectAddress = 257, ValueText = "DP=OFF",
            InformationObjectOffset = -1
        };
        var text = Iec10xSignalTraceFormatter.Format(asdu, obj);
        Assert.Contains("IOA 257 (IOA1:1 - IOA2:1 - IOA3:0)", text);
        Assert.Contains("SQ=1 implied (no IOA octets repeated)", text);
    }

    [Fact]
    public void ShortAddressAndMissingDecodeDoNotPretendToHaveThreeOriginalOctets()
    {
        var shortAsdu = new Iec10xAsduDecode { TypeId = 1, InformationObjectAddressWidth = 1 };
        var obj = new Iec10xInformationObject { InformationObjectAddress = 12, ValueText = "SP=OFF" };
        Assert.Contains("IOA1:12 - IOA2:N/A - IOA3:N/A",
            Iec10xSignalTraceFormatter.Format(shortAsdu, obj));
        var unknown = new Iec10xAsduDecode { TypeId = 1, InformationObjectAddressWidth = 0 };
        Assert.Contains("width unknown", Iec10xSignalTraceFormatter.Format(unknown, obj));
    }

    [Fact]
    public void MalformedAddressBytesAreNotPresentedAsMatchingDecodedOctets()
    {
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 1, InformationObjectAddressWidth = 3,
            RawBytes = new byte[] { 1, 1, 3, 0, 1, 0, 7, 0, 0, 0x80 }
        };
        var obj = new Iec10xInformationObject
        {
            InformationObjectAddress = 9, InformationObjectOffset = 6,
            ValueText = "SP=ON"
        };
        var text = Iec10xSignalTraceFormatter.Format(asdu, obj);
        Assert.Contains("IOA byte mismatch · inspect original HEX", text);
        Assert.DoesNotContain("IOA1:9", text);
    }
}
