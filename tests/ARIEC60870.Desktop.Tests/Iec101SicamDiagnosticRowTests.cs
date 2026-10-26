// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.ViewModels;
using ARIEC60870.Master.Model;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class Iec101SicamDiagnosticRowTests
{
    [Fact]
    public void RejectedFrameRetainsSpecificCodeAndCompleteForensicEvidence()
    {
        var record = new Iec103MasterEvidenceEvent
        {
            Category = "Warning",
            PollingReason = "IEC101-RX-TX-ECHO",
            Summary = "IEC-101 RX rejected",
            Detail = "COM=COM9; PRM=1; expected Link=105; last TX=10 49 69 00 B2 16; RX=10 49 69 00 B2 16",
            ProtocolMeaning = "Primary frame cannot be accepted as secondary",
            OperatorMessage = "Invalid secondary reply",
            OperatorAction = "Check converter and wiring",
            RawHex = "10 49 69 00 B2 16"
        };

        var row = new DiagnosticRow(record);

        Assert.Equal("IEC101-RX-TX-ECHO", row.Code);
        Assert.Contains("COM=COM9", row.Detail);
        Assert.Contains("last TX=", row.Detail);
        Assert.Contains("10 49", row.RawHex);
        Assert.Contains("expected Link=105", row.ToClipboardText());
    }

    [Fact]
    public void SerialOpenProfileExposesActionableCodeInsteadOfGenericDiagnostic()
    {
        var record = new Iec103MasterEvidenceEvent
        {
            Category = "Diagnostic",
            PollingReason = "IEC60870-SERIAL-OPEN-PROFILE",
            Detail = "Flow=None, RTS=ON, DTR=OFF; CTS=0, DSR=0, DCD=0",
            OperatorMessage = "Serial adapter settings and modem input snapshot"
        };

        var row = new DiagnosticRow(record);

        Assert.Equal("IEC60870-SERIAL-OPEN-PROFILE", row.Code);
        Assert.Contains("CTS=0", row.Detail);
        Assert.Contains("RTS=ON", row.ToClipboardText());
    }
}
