// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class RecordedControlSemanticTests
{
    private static Iec10xAsduDecode Decode(byte cot)
        => new Iec10xAsduDecoder(2, 2, 3).Decode(new byte[]
        {
            46, 1, cot, 0, 105, 0, 190, 11, 1, 0x81
        });

    [Fact]
    public void SecondaryActivationIsNotAnActconOrAnOrdinaryCommandReply()
    {
        var asdu = Decode(6);
        var title = Iec101TraceNarrator.ApplicationTitle(asdu, FrameDirection.SlaveToMaster);
        Assert.Contains("Unexpected secondary", title);
        Assert.Contains("not ACTCON", title);
    }

    [Fact]
    public void TestBitNeverDisplaysAnUnqualifiedAcceptedCommand()
    {
        var asdu = Decode(0x87);
        var evidence = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            TimestampUtc = DateTime.UtcNow,
            TypeId = 46, AsduDetails = asdu, CauseDetails = asdu.CauseDetails,
            RawHex = "synthetic"
        };
        var entry = Iec10xCommandEventProjection.Project(evidence);
        Assert.NotNull(entry);
        Assert.Contains("TEST", entry!.NewValue);
        Assert.Contains("non-operational", entry.NewValue);
        Assert.DoesNotContain("Command accepted", entry.NewValue);
    }
}
