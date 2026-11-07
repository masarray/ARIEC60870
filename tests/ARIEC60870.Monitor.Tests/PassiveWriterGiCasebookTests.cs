// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class PassiveWriterGiCasebookTests
{
    [Fact]
    public void CompleteWriterStreamKeepsGiDataAndActconDespitePreviewSizedBurst()
    {
        var source = new PassiveWriterGiCasebook(1, 2, 2, 3);
        var time = new DateTime(2026, 11, 8, 1, 0, 0, DateTimeKind.Utc);
        long txOffset = 0, rxOffset = 0;
        void Send(bool master, byte[] frame)
        {
            var offset = master ? txOffset++ : rxOffset++;
            source.Observe(new PassiveObservedFrame((byte)(master ? 0 : 1),
                offset * 64, time.AddMilliseconds(offset * 10),
                frame[0] == 0x10 ? "Fixed" : "Variable", true, frame, 1));
        }

        Send(true, Variable(true, 100, 6, [0, 0, 0, 20]));
        Send(false, Variable(false, 100, 7, [0, 0, 0, 20]));
        for (var ioa = 1; ioa <= 15; ioa++)
        {
            Send(true, Fixed(0x5A)); // Master requested Class 1
            Send(false, Variable(false, 3, 20,
                [(byte)ioa, (byte)(ioa >> 8), 0, 2])); // one valid double-point GI object
        }
        // This traffic would roll over the legacy 512-frame UI preview.
        for (var i = 0; i < 700; i++)
            Send(false, Variable(false, 3, 3, [1, 0, 0, 2]));
        Send(false, Variable(false, 100, 10, [0, 0, 0, 20]));
        source.Complete();

        var snapshot = source.Snapshot();
        var gi = Assert.Single(snapshot.Cases);
        Assert.Equal(15, gi.DataFrames);
        Assert.Equal(15, gi.DataObjects);
        Assert.Equal(15, gi.UniqueIoas);
        Assert.Equal(15, gi.Class1Requests);
        Assert.Equal(15, gi.Class1Responses);
        Assert.Equal(0, gi.InvalidQualityObjects);
        Assert.Equal("ACTTERM observed · coverage unknown", gi.Status);
        Assert.Contains(gi.Stages, x => x.Phase == "Positive GI ACTCON");
        Assert.Contains(gi.Stages, x => x.Phase == "GI ACTTERM");
        var phases = new List<string>();
        while (source.TryReadMilestone(out var milestone))
            phases.Add(milestone.Milestone.Phase);
        Assert.Contains("GI request", phases);
        Assert.Contains("Positive GI ACTCON", phases);
        Assert.Contains("GI ACTTERM", phases);
        Assert.Equal(0, snapshot.MilestonesNotPresented);
    }

    [Fact]
    public void MissingActconIsExplicitButNeverDiscardsFifteenObservedPoints()
    {
        var source = new PassiveWriterGiCasebook(1, 2, 2, 3);
        var time = new DateTime(2026, 11, 8, 2, 0, 0, DateTimeKind.Utc);
        long n = 0;
        void Send(bool master, byte[] bytes)
        {
            source.Observe(new PassiveObservedFrame((byte)(master ? 0 : 1),
                n++ * 64, time.AddMilliseconds(n * 10), "Variable", true, bytes, 1));
        }
        Send(true, Variable(true, 100, 6, [0, 0, 0, 20]));
        for (var ioa = 1; ioa <= 15; ioa++)
            Send(false, Variable(false, 3, 20, [(byte)ioa, 0, 0, 2]));
        Send(false, Variable(false, 100, 10, [0, 0, 0, 20]));
        var item = Assert.Single(source.Snapshot().Cases);
        Assert.Equal("ACTTERM without observed ACTCON · incomplete", item.Status);
        Assert.Equal(15, item.DataFrames);
        Assert.Equal(15, item.UniqueIoas);
    }

    [Fact]
    public void WrongCommonAddressCannotBorrowAnUnrelatedGi()
    {
        var source = new PassiveWriterGiCasebook(1, 2, 2, 3);
        var t = new DateTime(2026, 11, 8, 3, 0, 0, DateTimeKind.Utc);
        source.Observe(new PassiveObservedFrame(0, 0, t, "Variable", true,
            Variable(true, 100, 6, [0, 0, 0, 20]), 1));
        source.Observe(new PassiveObservedFrame(1, 0, t.AddMilliseconds(5), "Variable", true,
            Variable(false, 3, 20, [9, 0, 0, 2], ca: 1), 1));
        source.Observe(new PassiveObservedFrame(1, 64, t.AddMilliseconds(10), "Variable", true,
            Variable(false, 100, 10, [0, 0, 0, 20], ca: 1), 1));
        var history = source.Snapshot().Cases;
        Assert.Equal(2, history.Count);
        Assert.Equal(0, history[0].DataFrames);
        Assert.Equal("Unmatched response · request not observed", history[1].Status);
    }

    private static byte[] Variable(bool master, byte type, byte cot,
        byte[] information, byte ca = 105)
    {
        var body = new List<byte> {
            (byte)(master ? 0x43 : 0x08), 105, type, 1, cot, 0, ca, 0
        };
        body.AddRange(information);
        var size = (byte)body.Count;
        return new byte[] { 0x68, size, size, 0x68 }
            .Concat(body)
            .Concat(new byte[] { (byte)(body.Sum(x => x) & 0xff), 0x16 })
            .ToArray();
    }

    private static byte[] Fixed(byte control) =>
        [0x10, control, 105, (byte)((control + 105) & 0xff), 0x16];
}
