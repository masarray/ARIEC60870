// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Desktop.Reporting;
using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class EvidencePdfReportRegressionTests
{
    [Fact]
    public void EvidencePdfReportServiceWritesValidPdfHeader()
    {
        var row = new EvidenceRow(new CaptureFrameSnapshot
        {
            Sequence = 1,
            Time = "10:15:30.125",
            Direction = "RX",
            ProtocolMode = "IEC-104",
            ProtocolName = "IEC-104",
            Service = "I-format APDU",
            CommonAddress = "1",
            Ioa = "100",
            TypeId = "1",
            CotCode = "3",
            Cot = "spontaneous",
            Quality = "Good",
            Meaning = "Single-point indication decoded successfully",
            ProtocolTraceMeaning = "Mapped single-point monitor value",
            RawHex = "68 0B 00 00 00 00 01 01 03 00 01 00 64 00 00 01"
        });

        var model = new EvidencePdfReportModel(
            "ARIEC-REPORT-TEST",
            new DateTime(2026, 6, 15, 10, 0, 0),
            "ProtocolTrace",
            "Iec104",
            "PASS",
            "GI and event evidence are present in the selected report scope.",
            "pass",
            new[] { new KeyValuePair<string, string>("Report ID", "ARIEC-REPORT-TEST") },
            new[] { new KeyValuePair<string, string>("TX / RX", "1 / 1") },
            new[] { new KeyValuePair<string, string>("Protocol", "Iec104") },
            Array.Empty<EvidenceRow>(),
            Array.Empty<EvidenceRow>(),
            new[] { row },
            new[] { row },
            1,
            1,
            1,
            "0123456789ABCDEF0123456789ABCDEF");

        var output = Path.Combine(Path.GetTempPath(), "ariec60870-report-test-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            EvidencePdfReportService.Save(output, model);
            var bytes = File.ReadAllBytes(output);
            var header = bytes.Take(4).ToArray();
            var text = System.Text.Encoding.ASCII.GetString(bytes);

            Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, header);
            Assert.Contains("xref", text, StringComparison.Ordinal);
            Assert.Contains("%%EOF", text, StringComparison.Ordinal);
            Assert.Contains("ARIEC60870", text, StringComparison.Ordinal);
            Assert.Contains("Native PDF Engine", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HTML", text, StringComparison.OrdinalIgnoreCase);
            Assert.True(new FileInfo(output).Length > 1024);
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }
}
