// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class PassiveSessionTraceHistoryTests
{
    [Fact]
    public void DiskIndexOpensExactControlResponseWithAdjacentScadaRequestAfterPreviewEvictsIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "ARIEC60870-history-test-" + Guid.NewGuid().ToString("N"));
        var now = new DateTime(2026, 10, 30, 5, 52, 22, DateTimeKind.Utc);
        string? sessionPath;
        try
        {
            using (var history = new PassiveSessionTraceHistory(2, root))
            {
                sessionPath = history.DirectoryPath;
                for (var n = 0; n < 1800; n++)
                {
                    // Many Class 2 background frames: a 1200-row WPF preview
                    // cannot retain the original Select and negative ACTCON.
                    history.Append(new PassiveObservedFrame(0, n * 6, now.AddMilliseconds(n * 20d),
                        "Fixed", true, new byte[] { 0x10, 0x5B, 0x69, 0xC4, 0x16 }));
                    history.Append(new PassiveObservedFrame(1, n * 6, now.AddMilliseconds(n * 20d + 9),
                        "Fixed", true, new byte[] { 0x10, 0x09, 0x69, 0x72, 0x16 }));
                }
                Assert.Equal(3600, history.FrameCount);
                var original = history.ReadWindow(1, streamOffset: 300 * 6, before: 60, after: 60);
                Assert.True(original.Count > 1);
                Assert.Equal(1, original.Count(x => x.StreamId == 1 && x.StreamOffset == 1800));
                Assert.Contains(original, x => x.StreamId == 0 && x.StreamOffset == 1800);
                Assert.All(original, x => Assert.True(x.RawBytes.Length > 0));
            }
            Assert.False(Directory.Exists(sessionPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadWindowSupportsFourIndependentPhysicalStreamsAndKeepsOriginalBytes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ARIEC60870-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var h = new PassiveSessionTraceHistory(4, folder);
            var now = new DateTime(2026, 10, 30, 6, 1, 0, DateTimeKind.Utc);
            for (byte sid = 0; sid < 4; sid++)
                for (var i = 0; i < 40; i++)
                    h.Append(new PassiveObservedFrame(sid, i * 17, now.AddMilliseconds(i * 10d + sid),
                        "Variable", true, new byte[] { 0x68, sid, (byte)i, 0x16 }, 1));
            var actual = h.ReadWindow(3, 17 * 20, before: 20, after: 20);
            var found = Assert.Single(actual, x => x.StreamId == 3 && x.StreamOffset == 340);
            Assert.Equal(new byte[] { 0x68, 3, 20, 0x16 }, found.RawBytes);
            Assert.Equal(1, found.DetectedLinkAddressWidth);
            Assert.Equal("Variable", found.Format);
            Assert.Contains(actual, x => x.StreamId == 0);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void UnknownSourceOrOffsetCannotSubstituteNeighboringFrame()
    {
        using var h = new PassiveSessionTraceHistory(2);
        h.Append(new PassiveObservedFrame(1, 3810, DateTime.UtcNow, "Single", true,
            new byte[] { 0xE5 }));
        Assert.Empty(h.ReadWindow(0, 3810));
        Assert.Empty(h.ReadWindow(1, 3809));
        Assert.Single(h.ReadWindow(1, 3810, before: 0, after: 0));
    }
}
