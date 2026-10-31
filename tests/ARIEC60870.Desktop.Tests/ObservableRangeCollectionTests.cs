// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Specialized;
using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class ObservableRangeCollectionTests
{
    [Fact]
    public void LiveAppendAndTrimEmitNoResetAfterInitialLoad()
    {
        var rows = new ObservableRangeCollection<object>();
        var actions = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, e) => actions.Add(e.Action);
        var initial = Enumerable.Range(0, 1200).Select(_ => new object()).ToArray();
        rows.ReplaceRange(initial);
        for (int i = 0; i < 10; i++)
        {
            rows.AddRange(Enumerable.Range(0, 42).Select(_ => new object()));
            rows.TrimStart(1200);
        }
        Assert.Equal(1, actions.Count(x => x == NotifyCollectionChangedAction.Reset));
        Assert.Equal(420, actions.Count(x => x == NotifyCollectionChangedAction.Remove));
        Assert.Equal(420, actions.Count(x => x == NotifyCollectionChangedAction.Add));
        Assert.Equal(1200, rows.Count);
    }

    [Fact]
    public void FiftyThousandRollingEventFactsDoNotResetAnUnchangedWindow()
    {
        const int window = 420;
        var source = new List<object>();
        var rows = new ObservableRangeCollection<object>();
        var resets = 0;
        rows.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
        };
        for (var i = 0; i < 50_000; i += 125)
        {
            source.AddRange(Enumerable.Range(i, 125).Select(_ => new object()));
            if (source.Count > window) source.RemoveRange(0, source.Count - window);
            rows.ReconcileTail(source, allowResetForUnrelated: false);
            Assert.Equal(source, rows);
        }
        Assert.Equal(0, resets);
        Assert.Equal(window, rows.Count);
        // Intentional changes to filter scope are distinct from a live append.
        var filtered = source.Take(1).ToArray();
        rows.ReconcileTail(filtered, allowResetForUnrelated: true);
        Assert.Single(rows);
        rows.ReconcileTail(source, allowResetForUnrelated: true);
        Assert.Equal(source, rows);
    }

    [Fact]
    public void RollingSnapshotPreservesOriginalFrameReferences()
    {
        var initial = Enumerable.Range(0, 1200).Select(_ => new object()).ToArray();
        var rows = new ObservableRangeCollection<object>();
        rows.ReplaceRange(initial);
        int resets = 0;
        rows.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
        var next = initial.Skip(45).Concat(Enumerable.Range(0, 45).Select(_ => new object())).ToArray();
        rows.ReconcileTail(next);
        rows.ReconcileTail(next);
        Assert.Equal(0, resets);
        Assert.Same(initial[45], rows[0]);
        Assert.Equal(next, rows.ToArray());
    }

    [Fact]
    public void ValuesUpdateOneKeyWithoutRebuildingUnchangedRows()
    {
        var rows = new ObservableRangeCollection<(string Key, int Value)>();
        rows.ReplaceRange([("IOA101", 1), ("IOA102", 2), ("IOA103", 3)]);
        var actions = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, e) => actions.Add(e.Action);
        rows.ReconcileByKey([("IOA101", 1), ("IOA102", 55), ("IOA103", 3)], x => x.Key);
        Assert.Equal(55, rows[1].Value);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Replace }, actions);
        rows.ReconcileByKey([("IOA101", 1), ("IOA102", 55), ("IOA103", 3)], x => x.Key);
        Assert.Single(actions);
    }

    [Fact]
    public void NewestFirstCaseInsertionPreservesExistingSelectionIdentity()
    {
        var rows = new ObservableRangeCollection<(string Source, long Id, string Status)>();
        rows.ReplaceRange([("Link A", 3, "Pending"), ("Link A", 2, "Accepted"),
            ("Link B", 1, "Rejected")]);
        int resets = 0;
        rows.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
        };
        rows.ReconcileByKey([
            ("Link A", 4, "Requested"), ("Link A", 3, "Accepted"),
            ("Link A", 2, "Accepted"), ("Link B", 1, "Rejected")
        ], x => (x.Source, x.Id));
        Assert.Equal(0, resets);
        Assert.Equal(4, rows.Count);
        Assert.Equal(4, rows[0].Id);
        Assert.Equal("Accepted", rows[1].Status);
        Assert.Equal(("Link B", 1), (rows[3].Source, rows[3].Id));
    }

    [Fact]
    public void ContinuousCaseRevisionsDoNotResetFiveHundredRows()
    {
        var rows = new ObservableRangeCollection<(int Id, int Status)>();
        rows.ReplaceRange(Enumerable.Range(0, 500).Select(x => (x, 0)).ToArray());
        var resets = 0;
        var replaces = 0;
        rows.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
            if (e.Action == NotifyCollectionChangedAction.Replace) replaces++;
        };
        for (var pass = 1; pass <= 100; pass++)
        {
            var targetId = pass % 500;
            rows.ReconcileByKey(Enumerable.Range(0, 500)
                .Select(id => (id, id == targetId ? pass : 0)).ToArray(), x => x.Id);
        }
        Assert.Equal(0, resets);
        Assert.Equal(199, replaces); // each revision updates the current and restores the previous row
        Assert.Equal(500, rows.Count);
    }

    [Fact]
    public void RollingTraceAtFourteenHundredFramesHasNoSteadyStateResets()
    {
        var rows = new ObservableRangeCollection<object>();
        var ring = new List<object>();
        ring.AddRange(Enumerable.Range(0, 25).Select(_ => new object()));
        rows.ReconcileTail(ring);
        var resets = 0;
        rows.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
        };
        for (var i = 25; i < 1400; i += 25)
        {
            ring.AddRange(Enumerable.Range(0, 25).Select(_ => new object()));
            while (ring.Count > 1200) ring.RemoveAt(0);
            rows.ReconcileTail(ring);
        }
        Assert.Equal(0, resets);
        Assert.Equal(1200, rows.Count);
        Assert.Equal(ring, rows.ToArray());
    }

    [Fact]
    public void UnrelatedArchiveForcesDeliberateReset()
    {
        var rows = new ObservableRangeCollection<string>();
        rows.ReplaceRange(["session-A", "session-A-new"]);
        int resets = 0;
        rows.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
        rows.ReconcileTail(["unrelated-archive"]);
        Assert.Equal(1, resets);
        Assert.Equal(new[] { "unrelated-archive" }, rows.ToArray());
    }

    [Fact]
    public void RollingOverlapHandlesRepeatedObjectReferences()
    {
        var one = new object(); var two = new object(); var three = new object();
        var rows = new ObservableRangeCollection<object>();
        rows.ReplaceRange([one, two, one, two]);
        rows.ReconcileTail([one, two, three]);
        Assert.Equal(new[] { one, two, three }, rows.ToArray());
    }
}
