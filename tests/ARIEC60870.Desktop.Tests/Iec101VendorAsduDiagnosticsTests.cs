// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec101VendorAsduDiagnosticsTests
{
    // TI135 / COT / CA shape observed from a SICAM IEC101 link.
    // The private payload below is a deterministic opaque fixture, not a
    // claimed Siemens payload interpretation.
    private static byte[] PrivateAsdu(ushort ca, byte cot = 20) =>
    [
        135, 1, cot, 0, (byte)ca, (byte)(ca >> 8),
        0x0D, 0x94, 0x01, 0x5B, 0x14, 0xFE, 0xFC
    ];

    private static byte[] Frame(byte[] asdu)
    {
        int length = 3 + asdu.Length; // control + 2-byte link address + ASDU
        var bytes = new byte[length + 6];
        bytes[0] = 0x68; bytes[1] = (byte)length;
        bytes[2] = (byte)length; bytes[3] = 0x68;
        bytes[4] = 0x28; bytes[5] = 105; bytes[6] = 0;
        asdu.CopyTo(bytes, 7);
        var sum = 0;
        for (int i = 4; i < 4 + length; i++) sum += bytes[i];
        bytes[4 + length] = (byte)sum;
        bytes[5 + length] = 0x16;
        return bytes;
    }

    private static Iec103MasterEvidenceEvent Event(byte[] asduBytes)
    {
        var wire = Frame(asduBytes);
        var frame = new Ft12Parser(2).Decode(wire);
        Assert.True(frame.IsChecksumValid);
        Assert.True(frame.IsLengthValid);
        return new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            Frame = frame,
            AsduDetails = new Iec10xAsduDecoder(2, 2, 3).Decode(asduBytes),
            LinkAddress = 105,
            RawHex = Convert.ToHexString(wire)
        };
    }

    [Theory]
    [InlineData((ushort)0xFEFC, (byte)3)]
    [InlineData((ushort)0xFFFC, (byte)20)]
    public void Ti135KeepsExactRawCaAndNeverInventsProcessPoint(ushort ca, byte cot)
    {
        var source = Event(PrivateAsdu(ca, cot));
        var asdu = Assert.IsType<Iec10xAsduDecode>(source.AsduDetails);
        Assert.True(asdu.IsHeaderValid);
        Assert.True(asdu.IsOpaqueVendorSpecific);
        Assert.Empty(asdu.Issues);
        Assert.Empty(asdu.Objects);
        Assert.Equal(ca, asdu.CommonAddress);
        Assert.Equal(cot, asdu.CauseOfTransmission);

        var row = new EvidenceRow(source);
        Assert.Contains("Vendor-specific ASDU", row.TimelineTitle);
        Assert.Contains("Raw ASDU CA", row.TimelineEngineeringIdentity);
        Assert.Contains("0x" + ca.ToString("X4"), row.TimelineEngineeringIdentity);
        Assert.DoesNotContain("ASDU decode: WARNING", row.TimelineReadableAlerts);
        Assert.Contains("no standard process IOA decoded", row.TimelineSignalDigest);
        Assert.DoesNotContain(" · CA " + ca, row.TimelineTitle);

        var export = SelectedFrameProtocolText.Format(row);
        Assert.Contains("Raw CA", export);
        Assert.Contains("0x" + ca.ToString("X4"), export);
        Assert.Contains("TI=135", export);
        Assert.DoesNotContain("IOA ", export);
        Assert.Contains("opaque", export);
        Assert.DoesNotContain("ASDU issues", export);

        var fields = Iec101FrameByteInspector.Explain(source);
        Assert.Contains(fields, x => x.Key == "ca" &&
            x.Hex == ((byte)ca).ToString("X2") + " " + ((byte)(ca >> 8)).ToString("X2") &&
            x.Meaning.Contains("Raw ASDU CA"));
        Assert.Contains(fields, x => x.Key == "vendor-payload" && x.Hex.StartsWith("0D 94"));
        Assert.DoesNotContain(fields, x => x.Key == "issue");
    }

    [Fact]
    public void OrdinaryTi30StillDecodesProcessCa105AndPoint()
    {
        byte[] bytes = [30, 1, 3, 0, 105, 0, 12, 0, 0,
            1, 0x2C, 0x6F, 0x27, 0x14, 0x1C, 0x0A, 0x1A];
        var source = Event(bytes);
        var asdu = source.AsduDetails!;
        Assert.False(asdu.IsOpaqueVendorSpecific);
        Assert.Empty(asdu.Issues);
        Assert.Equal(105, asdu.CommonAddress);
        Assert.Single(asdu.Objects);
        Assert.Contains("CA 105", new EvidenceRow(source).TimelineEngineeringIdentity);
        Assert.Contains("CA  105", SelectedFrameProtocolText.Format(new EvidenceRow(source)));
    }

    [Fact]
    public void MalformedStandardAsduShowsActualReasonNotGenericWarning()
    {
        byte[] bytes = [30, 1, 3, 0, 105, 0, 12, 0, 0, 1];
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(bytes);
        Assert.NotEmpty(asdu.Issues);
        var alerts = EngineerFramePresentation.ReadableAlerts(new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            AsduDetails = asdu
        });
        Assert.Contains("Information element too short", alerts);
        Assert.DoesNotContain("ASDU decode: WARNING", alerts);
    }

    [Fact]
    public void TruncatedVendorHeaderStillProducesRealMalformedDiagnostic()
    {
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode([135, 1, 20, 0, 0xFC]);
        Assert.False(asdu.IsHeaderValid);
        Assert.NotEmpty(asdu.Issues);
        Assert.False(asdu.IsOpaqueVendorSpecific);
    }

    [Fact]
    public void OtherVendorPrivateTypeDoesNotGetSiemensIdentity()
    {
        var asdu = new Iec10xAsduDecoder(2, 2, 3).Decode(
            [200, 1, 3, 0, 1, 0, 0x01, 0x02, 0x03]);
        Assert.True(asdu.IsOpaqueVendorSpecific);
        Assert.Empty(asdu.Objects);
        Assert.Empty(asdu.Issues);
        Assert.DoesNotContain("SICAM", Iec10xVendorAsduPresentation.Explanation(asdu));
    }
}
