// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec10xSignalTraceFormatterTests
{
    [Fact]
    public void ScanUnstructuredIoaRestoresHighToLowStructuredOctetsWithIoa1Last()
    {
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 31, InformationObjectAddressWidth = 3
        };
        var obj = new Iec10xInformationObject
        {
            InformationObjectAddress = 0xFF0400, ValueText = "DP=ON"
        };
        var line = Iec10xSignalTraceFormatter.ScanValue(asdu, obj);
        Assert.Contains("IOA 16712704 (IOA3: 255 - IOA2: 4 - IOA1: 0)", line);
        Assert.Contains("Value: ON (Double Point)", line);
    }

    [Fact]
    public void TwoByteIoaContainsOnlyActualConfiguredComponents()
    {
        var asdu = new Iec10xAsduDecode { TypeId = 1, InformationObjectAddressWidth = 2 };
        var obj = new Iec10xInformationObject
        {
            InformationObjectAddress = 0x1234, ValueText = "SP=ON"
        };
        var line = Iec10xSignalTraceFormatter.ScanValue(asdu, obj);
        Assert.Contains("IOA 4660 (IOA2: 18 - IOA1: 52)", line);
        Assert.DoesNotContain("IOA3", line);
    }

    [Fact]
    public void SqOneDerivedAddressHasStructuredLogicalOctetsButNoClaimOfPhysicalBytes()
    {
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 3, InformationObjectAddressWidth = 3, IsSequence = true
        };
        var obj = new Iec10xInformationObject
        {
            Index = 2, InformationObjectAddress = 257,
            InformationObjectOffset = -1, ValueText = "DP=OFF"
        };
        var line = Iec10xSignalTraceFormatter.ScanValue(asdu, obj);
        Assert.Contains("IOA 257 (IOA3: 0 - IOA2: 1 - IOA1: 1)", line);
        Assert.Contains("SQ=1 implied IOA", line);
        Assert.DoesNotContain("bytes mismatch", line);
    }

    [Fact]
    public void MismatchedActualWireIoaDoesNotPrintPlausibleStructuredBytes()
    {
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 31, InformationObjectAddressWidth = 3,
            RawBytes = new byte[] { 31, 1, 3, 0, 1, 0, 0x00, 0x04, 0xFF }
        };
        var obj = new Iec10xInformationObject
        {
            InformationObjectAddress = 0xFF0401, InformationObjectOffset = 6,
            ValueText = "DP=ON"
        };
        var line = Iec10xSignalTraceFormatter.ScanValue(asdu, obj);
        Assert.Contains("IOA bytes mismatch", line);
        Assert.Contains("Decode warning", line);
        Assert.DoesNotContain("IOA3: 255", line);
        Assert.DoesNotContain("Value: ON", line);
    }

    [Fact]
    public void SinglePointShowsThreeLittleEndianIoaOctetsAndTypedState()
    {
        var asdu = new Iec10xAsduDecode { TypeId = 1, InformationObjectAddressWidth = 3 };
        var obj = new Iec10xInformationObject
        {
            InformationObjectAddress = 0x80006A, ValueText = "SP=ON"
        };
        var text = Iec10xSignalTraceFormatter.Format(asdu, obj, "52-BKR");
        Assert.Contains("52-BKR · IOA 8388714 (IOA3: 128 - IOA2: 0 - IOA1: 106)", text);
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
        Assert.Contains("IOA 342 (IOA3: 0 - IOA2: 1 - IOA1: 86)", text);
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
        Assert.Contains("IOA 257 (IOA3: 0 - IOA2: 1 - IOA1: 1)", text);
        Assert.Contains("SQ=1 implied (no IOA octets repeated)", text);
    }

    [Fact]
    public void ShortAddressAndMissingDecodeDoNotPretendToHaveThreeOriginalOctets()
    {
        var shortAsdu = new Iec10xAsduDecode { TypeId = 1, InformationObjectAddressWidth = 1 };
        var obj = new Iec10xInformationObject { InformationObjectAddress = 12, ValueText = "SP=OFF" };
        Assert.Contains("IOA1: 12",
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
        Assert.DoesNotContain("IOA1: 9", text);
    }
}
