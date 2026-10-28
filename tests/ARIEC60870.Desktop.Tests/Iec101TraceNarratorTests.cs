// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec101TraceNarratorTests
{
    private static Iec103MasterEvidenceEvent LinkFrame(bool primary, int function, string dataClass = "Class 2",
        bool acd = false, bool dfc = false) => new()
    {
        ProtocolMode = Iec60870ProtocolMode.Iec101,
        Direction = primary ? FrameDirection.MasterToSlave : FrameDirection.SlaveToMaster,
        DataClass = dataClass,
        LinkAddress = 105,
        RawHex = "10 09 69 00 72 16",
        Frame = new Ft12FrameDecode
        {
            IsChecksumValid = true,
            LinkAddress = 105,
            LinkControl = new LinkControlInfo
            {
                Prm = primary, FunctionCode = function,
                Fcb = primary ? true : null, Fcv = primary ? true : null,
                Acd = primary ? null : acd, Dfc = primary ? null : dfc,
                FunctionName = "IEC-101 link function"
            }
        }
    };

    [Fact]
    public void EveryClass2RequestHasDirectionLinkAndRawProtocolFlags()
    {
        var presentation = Iec101TraceNarrator.Describe(LinkFrame(true, 11));
        Assert.Contains("➤ TX", presentation.Title);
        Assert.Contains("CLASS 2 REQUEST", presentation.Title);
        Assert.Contains("DL 105", presentation.Title);
        Assert.Contains("PRM=1", presentation.Meaning);
        Assert.Contains("FCV=1", presentation.Meaning);
    }

    [Fact]
    public void Class2NoDataWithAcdMeansClass1QueueIsIndicatedNotAnError()
    {
        var presentation = Iec101TraceNarrator.Describe(LinkFrame(false, 9, "Class 2", acd: true));
        Assert.Contains("◀ RX", presentation.Title);
        Assert.Contains("CLASS 2 DATA NOT AVAILABLE", presentation.Title);
        Assert.Contains("CLASS 1 AVAILABLE", presentation.Meaning);
        Assert.Contains("ACD=1", presentation.Meaning);
        Assert.Contains("DFC=0", presentation.Meaning);
        Assert.Contains("not a process failure", presentation.Meaning);
    }

    [Fact]
    public void UnknownPollClassMustNotBeClaimedClass2()
    {
        var presentation = Iec101TraceNarrator.Describe(LinkFrame(false, 9, "-"));
        Assert.Contains("REQUESTED DATA NOT AVAILABLE", presentation.Title);
        Assert.DoesNotContain("CLASS 2 DATA NOT AVAILABLE", presentation.Title);
    }

    [Fact]
    public void RTUAsduKeepsTypeCotCaIoaAndValueWithoutReparsingHex()
    {
        var asdu = new Iec10xAsduDecode
        {
            TypeId = 30, TypeName = "M_SP_TB_1", CauseOfTransmission = 20,
            CauseName = "interrogated by station", CommonAddress = 1,
            Objects = new[] { new Iec10xInformationObject
                { Index = 1, InformationObjectAddress = 8388715, EngineeringValue = "ON",
                  TimestampText = "2026-10-28 20:39:28.460" } }
        };
        var basic = LinkFrame(false, 8, "Class 1", acd: true);
        var item = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            DataClass = basic.DataClass,
            LinkAddress = basic.LinkAddress,
            Frame = basic.Frame,
            AsduDetails = asdu
        };
        var presentation = Iec101TraceNarrator.Describe(item);
        Assert.Contains("RTU USER DATA", presentation.Title);
        Assert.Contains("ASDU Type 30", presentation.Meaning);
        Assert.Contains("COT 20", presentation.Meaning);
        Assert.Contains("CA=1", presentation.Meaning);
        Assert.Contains("IOA 8388715=ON", presentation.Meaning);
        Assert.Contains("RTU time=2026-10-28", presentation.Meaning);
    }

    [Fact]
    public void LinkAckNeverClaimsSuccessfulPhysicalOperation()
    {
        var presentation = Iec101TraceNarrator.Describe(LinkFrame(false, 0));
        Assert.Contains("LINK ACK", presentation.Title);
        Assert.Contains("does not prove", presentation.Meaning);
    }

    [Fact]
    public void E5ResponseIsExplicitlyContextDependent()
    {
        var item = new Iec103MasterEvidenceEvent
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            Direction = FrameDirection.SlaveToMaster,
            Frame = new Ft12FrameDecode
            {
                Format = Ft12FrameFormat.SingleCharacter,
                RawBytes = new byte[] { 0xE5 },
                IsChecksumValid = true
            }
        };
        var presentation = Iec101TraceNarrator.Describe(item);
        Assert.Contains("E5 SHORT RESPONSE", presentation.Title);
        Assert.Contains("matched link request", presentation.Meaning);
    }
}
