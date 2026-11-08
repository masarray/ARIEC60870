// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Specialized;
using ARIEC60870.Desktop.ViewModels;
using Xunit;

namespace ARIEC60870.Desktop.Tests;

public sealed class TraceRetentionPolicyTests
{
    private sealed record Frame(int Sequence);

    [Fact]
    public void SelectedOlderFrameSurvivesFiftyThousandRollingFramesWithoutWpfReset()
    {
        const int limit = 1200;
        var rows = new ObservableRangeCollection<Frame>();
        var original = Enumerable.Range(0, limit).Select(i => new Frame(i)).ToArray();
        rows.AddRange(original);
        var pinned = original[300];
        IReadOnlySet<Frame> selection = new HashSet<Frame> { pinned };
        var resets = 0;
        rows.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
        };

        var newlyReceived = 0;
        for (var start = limit; start < 50_000; start += 100)
        {
            var batch = Enumerable.Range(start, Math.Min(100, 50_000 - start))
                .Select(i => new Frame(i)).ToArray();
            rows.AddRange(batch);
            newlyReceived += batch.Length;
            var remove = TraceRetentionPolicy.PlanRemovalIndices(rows, limit, selection);
            Assert.Equal(batch.Length, remove.Count);
            Assert.Equal(remove.OrderByDescending(i => i), remove);
            foreach (var index in remove) rows.RemoveAt(index);

            Assert.Equal(limit, rows.Count);
            Assert.Contains(pinned, rows);
            Assert.Equal(batch[^1].Sequence, rows[^1].Sequence);
            Assert.Equal(rows.Count, rows.Select(x => x.Sequence).Distinct().Count());
        }

        Assert.Equal(50_000 - limit, newlyReceived);
        Assert.Equal(0, resets);
        Assert.Same(pinned, rows.Single(f => f.Sequence == pinned.Sequence));
        Assert.Contains(rows, f => f.Sequence == 49_999);
    }

    [Fact]
    public void AllSelectedOldFramesMustNotCauseNewRxToBeDiscarded()
    {
        const int limit = 1200;
        var old = Enumerable.Range(0, limit).Select(i => new Frame(i)).ToArray();
        var fresh = Enumerable.Range(limit, 100).Select(i => new Frame(i)).ToArray();
        var rows = old.Concat(fresh).ToArray();
        var selected = new HashSet<Frame>(old);

        var remove = TraceRetentionPolicy.PlanRemovalIndices(rows, limit, selected);
        Assert.Equal(100, remove.Count);
        Assert.Equal(Enumerable.Range(0, 100).Reverse(), remove);
        var kept = rows.Where((_, i) => !remove.Contains(i)).ToArray();
        Assert.Equal(limit, kept.Length);
        Assert.Equal(fresh, kept[^100..]);
        Assert.Equal(old[100], kept[0]);
    }

    [Fact]
    public void SingleHeldFrameKeepsOriginalIdentityAndNewestTail()
    {
        var old = Enumerable.Range(0, 1200).Select(i => new Frame(i)).ToArray();
        var fresh = Enumerable.Range(1200, 50).Select(i => new Frame(i)).ToArray();
        var rows = old.Concat(fresh).ToArray();
        var selected = new HashSet<Frame> { old[2] };
        var indices = TraceRetentionPolicy.PlanRemovalIndices(rows, 1200, selected);
        var kept = rows.Where((_, i) => !indices.Contains(i)).ToArray();
        Assert.Same(old[2], kept.Single(x => x.Sequence == 2));
        Assert.Equal(fresh, kept[^50..]);
        Assert.Equal(1200, kept.Length);
        Assert.DoesNotContain(2, indices);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 100)]
    [InlineData(5, 100)]
    [InlineData(64, 100)]
    public void SmallWindowsStayStrictlyBounded(int maximumRows, int incomingCount)
    {
        var rows = Enumerable.Range(0, incomingCount).ToArray();
        var selected = new HashSet<int>(rows);
        var remove = TraceRetentionPolicy.PlanRemovalIndices(rows, maximumRows, selected);
        Assert.Equal(incomingCount - maximumRows, remove.Count);
        var kept = rows.Where((_, i) => !remove.Contains(i)).ToArray();
        Assert.Equal(maximumRows, kept.Length);
        Assert.Equal(rows.TakeLast(maximumRows), kept);
    }

    [Fact]
    public void RejectsNegativeLimitsAndNeverRewritesOriginalRows()
    {
        var items = new[] { new Frame(1), new Frame(2) };
        var pinned = new HashSet<Frame>();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TraceRetentionPolicy.PlanRemovalIndices(items, -1, pinned));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TraceRetentionPolicy.PlanRemovalIndices(items, 1, pinned, -1));
        Assert.Equal([0], TraceRetentionPolicy.PlanRemovalIndices(items, 1, pinned));
        Assert.Equal(1, items[0].Sequence);
    }
}
