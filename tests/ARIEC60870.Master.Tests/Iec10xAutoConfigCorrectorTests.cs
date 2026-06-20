// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Analysis;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec10xAutoConfigCorrectorTests
{
    [Fact]
    public void AnalyzeSuggestsIec101ProfileSizesFromDecodedEvidence()
    {
        var actual = Iec103MasterSettings.CreateDefault();
        actual.ProtocolMode = Iec60870ProtocolMode.Iec101;
        actual.LinkAddress = 105;
        actual.CommonAddress = 1;
        actual.LinkAddressSize = 2;
        actual.CauseOfTransmissionSize = 2;
        actual.CommonAddressSize = 2;
        actual.InformationObjectAddressSize = 3;

        var asdu = Iec10xAsduBuilder.GeneralInterrogation(actual, qualifier: 20, cause: 7);
        var frame = Ft12FrameBuilder.Variable(0x08, actual.LinkAddress, asdu, actual.LinkAddressSize);

        var configured = Iec103MasterSettings.CreateDefault();
        configured.ProtocolMode = Iec60870ProtocolMode.Iec101;
        configured.LinkAddress = actual.LinkAddress;
        configured.CommonAddress = actual.CommonAddress;
        configured.LinkAddressSize = 2;
        configured.CauseOfTransmissionSize = 1;
        configured.CommonAddressSize = 1;
        configured.InformationObjectAddressSize = 3;

        var samples = new[]
        {
            new Iec10xAutoConfigFrameSample(Iec60870ProtocolMode.Iec101, FrameDirection.SlaveToMaster, ToHex(frame), 1),
            new Iec10xAutoConfigFrameSample(Iec60870ProtocolMode.Iec101, FrameDirection.SlaveToMaster, ToHex(frame), 2)
        };
        var findings = new[]
        {
            new Iec103MasterFinding
            {
                Id = "IEC101-ASDU-DECODE",
                Title = "IEC-101 ASDU decode issue",
                Evidence = "COT/CA/IOA profile size may be shifted.",
                Impact = "Decoded fields may be wrong.",
                Recommendation = "Verify COT size, CA size, IOA size."
            }
        };

        var result = Iec10xAutoConfigCorrector.Analyze(configured, samples, findings);

        Assert.Contains(result.Suggestions, item => item.FieldName == nameof(Iec103MasterSettings.CauseOfTransmissionSize) && item.NewValue == "2");
        Assert.Contains(result.Suggestions, item => item.FieldName == nameof(Iec103MasterSettings.CommonAddressSize) && item.NewValue == "2");
    }

    [Fact]
    public void AnalyzeSuggestsCommonAddressAndLinkAddressFromRxFrameEvidence()
    {
        var actual = Iec103MasterSettings.CreateDefault();
        actual.ProtocolMode = Iec60870ProtocolMode.Iec101;
        actual.LinkAddress = 105;
        actual.CommonAddress = 1;
        actual.LinkAddressSize = 2;
        actual.CauseOfTransmissionSize = 2;
        actual.CommonAddressSize = 2;
        actual.InformationObjectAddressSize = 3;

        var asdu = Iec10xAsduBuilder.SinglePoint(actual, ioa: 100, value: true, cause: 20);
        var frame = Ft12FrameBuilder.Variable(0x08, actual.LinkAddress, asdu, actual.LinkAddressSize);

        var configured = Iec103MasterSettings.CreateDefault();
        configured.ProtocolMode = Iec60870ProtocolMode.Iec101;
        configured.LinkAddress = 1;
        configured.CommonAddress = 2;
        configured.LinkAddressSize = 2;
        configured.CauseOfTransmissionSize = 2;
        configured.CommonAddressSize = 2;
        configured.InformationObjectAddressSize = 3;

        var samples = new[]
        {
            new Iec10xAutoConfigFrameSample(Iec60870ProtocolMode.Iec101, FrameDirection.SlaveToMaster, ToHex(frame), 1),
            new Iec10xAutoConfigFrameSample(Iec60870ProtocolMode.Iec101, FrameDirection.SlaveToMaster, ToHex(frame), 2)
        };

        var result = Iec10xAutoConfigCorrector.Analyze(configured, samples);

        Assert.Contains(result.Suggestions, item => item.FieldName == nameof(Iec103MasterSettings.CommonAddress) && item.NewValue == "1");
        Assert.Contains(result.Suggestions, item => item.FieldName == nameof(Iec103MasterSettings.LinkAddress) && item.NewValue == "105");
    }

    private static string ToHex(IReadOnlyList<byte> bytes)
        => string.Join(" ", bytes.Select(item => item.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
}
