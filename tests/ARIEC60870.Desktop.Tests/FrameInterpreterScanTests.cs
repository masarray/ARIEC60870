// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class FrameInterpreterScanTests
{
    private static readonly Iec10xCp56Time2aDetails GenuineTime = new()
    {
        Year = 2026, Month = 11, Day = 9, DayOfWeek = 1,
        Hour = 7, Minute = 29, MillisecondsInMinute = 16200
    };

    [Fact]
    public void GenuineDeviceTimeIsSeparateFromHostAndShowsAllSourceFlags()
    {
        var point = new Iec10xInformationObject
        {
            InformationObjectAddress = 16712704, ValueText = "DP=ON",
            Cp56TimeDetails = GenuineTime, TimestampText = GenuineTime.TimestampText
        };
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 31, IsHeaderValid = true, InformationObjectAddressWidth = 3,
            Objects = [point], ObjectCount = 1
        };
        var row = new EvidenceRow(new Iec103MasterEvidenceEvent
        {
            AsduDetails = asdu, TimestampUtc = new DateTime(2030, 1, 1)
        });
        var text = row.TimelineSignalDigest;
        Assert.Contains("IOA 16712704 [ IOA3: 255 · IOA2: 4 · IOA1: 0 ] · TI-31 · Value: ON", text);
        Assert.Contains("09-11-2026 07:29:16.200", text);
        Assert.Contains("Timestamp Valid", text);
        Assert.Contains("Genuine (GEN=0)", text);
        Assert.Contains("DST OFF", text);
        Assert.Contains("Day: Monday", text);
        Assert.DoesNotContain("2030", text);
        Assert.Empty(row.TimelineReadableAlerts);
    }

    [Fact]
    public void InvalidOrSubstitutedCp56NeverAppearsGenuineOrValid()
    {
        var time = GenuineTime with
        {
            DayOfWeek = 2, InvalidFlag = true, SubstitutedFlag = true,
            SummerTimeFlag = true, Reserved3 = 1
        };
        var obj = new Iec10xInformationObject
        {
            InformationObjectAddress = 12,
            TimestampText = time.TimestampText,
            Cp56TimeDetails = time
        };
        var output = Iec10xSignalTraceFormatter.ScanDeviceTime(obj);
        Assert.Contains("Valid: FALSE", output);
        Assert.Contains("Genuine: FALSE", output);
        Assert.Contains("Summer Time: TRUE", output);
        Assert.Contains("Time invalid (IV): TRUE", output);
        Assert.Contains("Day mismatch: TRUE", output);
        Assert.Contains("Reserved bits: SET", output);
    }

    [Fact]
    public void TimeWithoutDecodedCp56DoesNotInventValidityOrGenuineFlag()
    {
        var obj = new Iec10xInformationObject
        {
            TimestampText = "07:29:16.200"
        };
        var output = Iec10xSignalTraceFormatter.ScanDeviceTime(obj);
        Assert.Contains("RTU 07:29:16.200", output);
        Assert.Contains("CP56 flags: N/A", output);
        Assert.DoesNotContain("Valid: TRUE", output);
        Assert.DoesNotContain("Genuine: TRUE", output);
        Assert.Empty(Iec10xSignalTraceFormatter.ScanDeviceTime(new Iec10xInformationObject()));
        var typedOnly = new Iec10xInformationObject { Cp56TimeDetails = GenuineTime };
        Assert.Contains("RTU 2026-11-09 07:29:16.200",
            Iec10xSignalTraceFormatter.ScanDeviceTime(typedOnly));
        Assert.Contains("Genuine: TRUE",
            Iec10xSignalTraceFormatter.ScanDeviceTime(typedOnly));
    }

    [Fact]
    public void ApplicableActiveQualityFlagsAreReadableAndUnknownIsNeverFalse()
    {
        var set = new Iec10xQualityDetails(Iec10xQualityKind.Qds, 0xD1);
        var text = Iec10xSignalTraceFormatter.ScanActiveQuality(set);
        Assert.Contains("Overflow (OV): TRUE", text);
        Assert.Contains("Invalid (IV): TRUE", text);
        Assert.Contains("Blocked (BL): TRUE", text);
        Assert.Contains("Not topical (NT): TRUE", text);
        Assert.DoesNotContain("FALSE", text);
        Assert.Empty(Iec10xSignalTraceFormatter.ScanActiveQuality(null));
        Assert.DoesNotContain("Overflow (OV)",
            Iec10xSignalTraceFormatter.ScanActiveQuality(
                new Iec10xQualityDetails(Iec10xQualityKind.Siq, 0x80)));
    }

    [Fact]
    public void FlaggedObjectsHaveIoaContextAndOverflowCountIsBounded()
    {
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 13, ObjectCount = 9,
            Objects = Enumerable.Range(1, 9).Select(i => new Iec10xInformationObject
            {
                Index = i,
                InformationObjectAddress = 400 + i,
                QualityDetails = i <= 5
                    ? new Iec10xQualityDetails(Iec10xQualityKind.Qds, 0x81)
                    : null
            }).ToArray()
        };
        var source = new Iec103MasterEvidenceEvent { AsduDetails = asdu };
        var output = EngineerFramePresentation.ReadableAlerts(source);
        Assert.Contains("IOA 401 · Overflow (OV): TRUE · Invalid (IV): TRUE", output);
        Assert.Contains("IOA 403", output);
        Assert.DoesNotContain("IOA 404", output);
        Assert.Contains("+2 flagged IOAs · see Frame Details", output);
        Assert.DoesNotContain("IOA 406", output);
    }

    [Fact]
    public void UnverifiedRawIoaCannotShowPlausibleGoodSignal()
    {
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 31, InformationObjectAddressWidth = 3,
            RawBytes = new byte[] { 31, 1, 3, 0, 1, 0, 7, 0, 0, 1 }
        };
        var obj = new Iec10xInformationObject
        {
            InformationObjectAddress = 9, InformationObjectOffset = 6, ValueText = "DP=ON"
        };
        var text = Iec10xSignalTraceFormatter.ScanValue(asdu, obj);
        Assert.Contains("Decode warning", text);
        Assert.Contains("IOA bytes mismatch", text);
        Assert.DoesNotContain("Value: ON", text);
    }
}
