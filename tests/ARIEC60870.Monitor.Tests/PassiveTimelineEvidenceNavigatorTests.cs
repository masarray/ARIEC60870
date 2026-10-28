// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using ARIEC60870.Monitor;
using Xunit;

namespace ARIEC60870.Monitor.Tests;

public sealed class PassiveTimelineEvidenceNavigatorTests
{
    private static readonly DateTime Origin =
        new(2026, 10, 28, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SparseNavigationPagesThousandsOfWindowsWithoutLoadingThemAll()
    {
        using var fixture = new TimelineFixture();
        for (var i = 0; i < 1200; i++)
        {
            var circuit = i % 2;
            fixture.Append(Window(i, circuit, anomaly: i % 7 == 0));
        }
        var index = PassiveTimelineEvidenceNavigator.OpenDerivedIndexOnly(fixture.Path);
        Assert.Equal(10, index.SparseBlockCount); // ceil(1200 / 128)
        var first = index.Search(new PassiveTimelineSearch(CircuitId: 1, Limit: 9));
        Assert.Equal(9, first.Count);
        Assert.All(first, x => Assert.Equal(1, x.Window.CircuitId));
        Assert.All(first, x => Assert.True(x.Window.HasAnomaly));
        Assert.True(first.Zip(first.Skip(1)).All(pair =>
            pair.First.FileOffset > pair.Second.FileOffset));
        var second = index.Search(new PassiveTimelineSearch(CircuitId: 1, Limit: 9,
            BeforeOffset: first[^1].FileOffset));
        Assert.Equal(9, second.Count);
        Assert.True(second[0].FileOffset < first[^1].FileOffset);
        Assert.Empty(first.Select(x => x.FileOffset).Intersect(second.Select(x => x.FileOffset)));
    }

    [Fact]
    public void QueriesFilterUtcAndLinkAndPreservePhysicalFourSourceOffset()
    {
        using var fixture = new TimelineFixture();
        fixture.Append(Window(0, 0, true));
        fixture.Append(Window(1, 1, true));
        fixture.Append(Window(2, 0, true));
        var index = PassiveTimelineEvidenceNavigator.OpenDerivedIndexOnly(fixture.Path);
        var from = Origin.AddSeconds(5);
        var result = index.Search(new PassiveTimelineSearch(FromUtc: from,
            UntilUtc: Origin.AddSeconds(9), CircuitId: 1));
        var hit = Assert.Single(result);
        Assert.Equal(1, hit.Window.CircuitId);
        Assert.Equal(3, hit.PhysicalAnomalyStream); // local RTU RX1 on Link B = RX3
        Assert.Equal(100L, hit.Window.FirstAnomalyOffset);
    }

    [Fact]
    public void CorruptOrModifiedDerivedIndexIsRejected()
    {
        using var fixture = new TimelineFixture();
        fixture.Append(Window(0, 0, true));
        var index = PassiveTimelineEvidenceNavigator.OpenDerivedIndexOnly(fixture.Path);
        File.AppendAllText(fixture.Path, "BROKEN\n");
        Assert.Throws<InvalidDataException>(() =>
            index.Search(new PassiveTimelineSearch()));
        Assert.Throws<InvalidDataException>(() =>
            PassiveTimelineEvidenceNavigator.OpenDerivedIndexOnly(fixture.Path));
    }

    [Fact]
    public void UnverifiedCaptureCannotBePresentedAsAuthenticatedRaw()
    {
        using var fixture = new TimelineFixture();
        fixture.Append(Window(0, 0, true));
        var index = PassiveTimelineEvidenceNavigator.OpenDerivedIndexOnly(fixture.Path);
        var hit = Assert.Single(index.Search(new PassiveTimelineSearch()));
        Assert.Throws<InvalidOperationException>(() => index.ReadVerifiedFrame(hit));
        Assert.Throws<InvalidDataException>(() =>
            PassiveTimelineEvidenceNavigator.OpenVerified(fixture.Directory));
    }

    [Fact]
    public void SearchRejectsInvalidBoundsAndReturnsEmptyWhenNothingMatches()
    {
        using var fixture = new TimelineFixture();
        fixture.Append(Window(0, 0, false));
        var index = PassiveTimelineEvidenceNavigator.OpenDerivedIndexOnly(fixture.Path);
        Assert.Empty(index.Search(new PassiveTimelineSearch()));
        Assert.Single(index.Search(new PassiveTimelineSearch(AnomaliesOnly: false)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            index.Search(new PassiveTimelineSearch(CircuitId: 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            index.Search(new PassiveTimelineSearch(Limit: 1000)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            index.Search(new PassiveTimelineSearch(FromUtc: DateTime.Now)));
    }

    private static PassiveTimelineWindow Window(int n, int circuit, bool anomaly)
    {
        var start = Origin.AddSeconds(5 * n);
        return new PassiveTimelineWindow(
            StartUtc: start, EndUtc: start.AddSeconds(5),
            Source0Frames: 1, Source1Frames: 1, InvalidFrames: 0,
            PrimaryRequests: 1, Class1Polls: 0, Class2Polls: 1,
            LinkStatusPolls: 0, ShortE5Responses: 1,
            SecondaryLinkStatus: 0, UnknownPrimaryFunctions: 0,
            ServiceNotImplementedReplies: 0, Retries: anomaly ? 1 : 0,
            ResponseTimeouts: 0, UnmatchedReplies: 0, DfcBusy: 0,
            GiActivations: 0, GiCompleted: 0, GiExceptions: 0,
            FirstAnomalyStream: anomaly ? 1 : null,
            FirstAnomalyOffset: anomaly ? n * 100L : null,
            HostTimestampOrderUncertain: false, CompressedEmptyWindows: 0,
            CircuitId: circuit);
    }

    private sealed class TimelineFixture : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ariec-timeline-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "timeline-windows.jsonl");

        public TimelineFixture() => System.IO.Directory.CreateDirectory(Directory);

        public void Append(PassiveTimelineWindow window) =>
            File.AppendAllText(Path, JsonSerializer.Serialize(window) + "\n");

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
