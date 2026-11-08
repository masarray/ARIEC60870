// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class EngineerFramePresentationTests
{
    private static Iec103MasterEvidenceEvent Event(Iec10xAsduDecode? asdu = null, Ft12FrameDecode? frame = null) =>
        new()
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            AsduDetails = asdu,
            Frame = frame,
            RawHex = "10 29 69 92 16"
        };

    private static Iec10xAsduDecode MeasuredAsdu(byte quality)
    {
        // TI13 M_ME_NC_1, one 32-bit floating point measured object,
        // QDS is an actual information-element octet, not a presentation guess.
        byte[] bytes = [13, 1, 3, 0, 1, 0, 44, 0, 0, 0, 0, 32, 65, quality];
        var decoded = new Iec10xAsduDecoder(2, 2, 3).Decode(bytes);
        Assert.Empty(decoded.Issues);
        Assert.Single(decoded.Objects);
        Assert.NotNull(decoded.Objects[0].QualityDetails);
        return decoded;
    }

    private static Iec10xAsduDecode TimedMeasuredAsdu(byte minuteFlags)
    {
        // TI36 M_ME_TF_1 float + QDS + CP56Time2a, GEN and IV come
        // from the timestamp minute octet, not the process QDS.
        byte[] bytes = [36, 1, 3, 0, 1, 0, 44, 0, 0,
            0, 0, 32, 65, 0, 0x88, 0x13, minuteFlags, 15, 8, 11, 26];
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(bytes);
        Assert.Empty(asdu.Issues);
        Assert.NotNull(asdu.Objects[0].Cp56TimeDetails);
        return asdu;
    }

    [Fact]
    public void ACDOneIsExplicitlyClassOnePendingAndNotADataRejection()
    {
        var wire = Convert.FromHexString("1029699216");
        var frame = new Ft12Parser(1).Decode(wire);
        Assert.True(frame.IsChecksumValid);
        var alerts = EngineerFramePresentation.Alerts(Event(frame: frame));
        Assert.Contains("ACD=1 · Class 1 pending", alerts);
        Assert.DoesNotContain("rejected", alerts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IV=1", alerts);
    }

    [Fact]
    public void ActualQdsInvalidAndOverflowBitsBecomeVisibleFrameAlarms()
    {
        var asdu = MeasuredAsdu(0x81);
        Assert.Equal(Iec10xQualityKind.Qds, asdu.Objects[0].QualityDetails!.Kind);
        var e = Event(asdu);
        Assert.Contains("IV=1 · Invalid value", EngineerFramePresentation.Alerts(e));
        Assert.Contains("OV=1 · Overflow", EngineerFramePresentation.Alerts(e));
        var point = Assert.Single(DecodedPointRow.FromAsdu(asdu));
        Assert.Contains("IV=1", point.QualityValidity);
        Assert.Equal("No device timestamp", point.RelayTime);
        Assert.Equal("No device event timestamp encoded", point.TimeFlags);
        Assert.Contains("Process quality: IV=1 invalid", EngineerFramePresentation.TrustSummary(e));
        Assert.Contains("Device time: not encoded", EngineerFramePresentation.TrustSummary(e));
    }

    [Fact]
    public void GoodQdsReportsValidButDoesNotFabricateGenuineTime()
    {
        var asdu = MeasuredAsdu(0);
        var e = Event(asdu);
        Assert.Equal("", EngineerFramePresentation.Alerts(e));
        Assert.Contains("IV=0 (not marked invalid)", EngineerFramePresentation.TrustSummary(e));
        Assert.DoesNotContain("Genuine", EngineerFramePresentation.TrustSummary(e));
    }

    [Fact]
    public void DigitalSiqLowStateBitIsNotMisdiagnosedAsMeasurementOverflow()
    {
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 1, Objects = [new Iec10xInformationObject
            {
                InformationObjectAddress = 77,
                QualityDetails = new Iec10xQualityDetails(Iec10xQualityKind.Siq, 0x81),
                ValueText = "ON"
            }]
        };
        Assert.Contains("IV=1", EngineerFramePresentation.Alerts(Event(asdu)));
        Assert.DoesNotContain("OV=1", EngineerFramePresentation.Alerts(Event(asdu)));
    }

    [Fact]
    public void Cp56TimeKeepsIndependentValidityAndGenuineSemantics()
    {
        var genuine = TimedMeasuredAsdu(22);
        var genuinePoint = Assert.Single(DecodedPointRow.FromAsdu(genuine));
        Assert.Contains("Valid (time IV=0)", genuinePoint.TimeFlags);
        Assert.Contains("Genuine (GEN=0)", genuinePoint.TimeFlags);
        Assert.Contains("genuine (GEN=0)", EngineerFramePresentation.TrustSummary(Event(genuine)));
        Assert.DoesNotContain("GEN=1", EngineerFramePresentation.Alerts(Event(genuine)));

        var substitute = TimedMeasuredAsdu(0x40 | 22);
        var subPoint = Assert.Single(DecodedPointRow.FromAsdu(substitute));
        Assert.Contains("Substituted (GEN=1)", subPoint.TimeFlags);
        Assert.Contains("GEN=1", EngineerFramePresentation.Alerts(Event(substitute)));

        var invalid = TimedMeasuredAsdu(0x80 | 22);
        Assert.Contains("Invalid time", Assert.Single(DecodedPointRow.FromAsdu(invalid)).TimeFlags);
        Assert.Contains("Invalid device time", EngineerFramePresentation.Alerts(Event(invalid)));
        Assert.DoesNotContain("Process quality: IV=1", EngineerFramePresentation.TrustSummary(Event(invalid)));
    }

    [Fact]
    public void SignalNameAppearsOnlyFromExplicitResolver()
    {
        var asdu = MeasuredAsdu(0);
        var original = Assert.Single(DecodedPointRow.FromAsdu(asdu));
        Assert.Equal("IOA 44", original.DisplaySignal);
        Assert.Empty(original.SignalName);
        var named = Assert.Single(DecodedPointRow.FromAsdu(asdu, ioa =>
            ioa == 44 ? "52-BKR Position" : null));
        Assert.Equal("52-BKR Position", named.DisplaySignal);
        Assert.Equal("IOA 44", named.SignalAddress);
    }
}
