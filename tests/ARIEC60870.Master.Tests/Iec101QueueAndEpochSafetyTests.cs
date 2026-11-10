// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Protocol;
using Xunit;

namespace ARIEC60870.Master.Tests;

/// <summary>IEC101-R06/R09, T06/T10, S06: explicit bound and connection-epoch safety.</summary>
public sealed class Iec101QueueAndEpochSafetyTests
{
    [Fact]
    public void BoundedOperatorQueueRefusesNewCommandWithoutReplacingExistingWork()
    {
        var queue = new Iec101BoundedFifo<int>(2);
        Assert.True(queue.TryEnqueue(10));
        Assert.True(queue.TryEnqueue(20));
        Assert.False(queue.TryEnqueue(30));
        Assert.Equal(2, queue.Count);
        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal(10, first);
        Assert.True(queue.TryEnqueue(40));
        Assert.True(queue.TryDequeue(out var second));
        Assert.Equal(20, second);
        Assert.True(queue.TryDequeue(out var third));
        Assert.Equal(40, third);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void StopDiscardsAllPendingIntentsRatherThanReplayingOnNewEpoch()
    {
        var commands = new Iec101BoundedFifo<string>(2);
        commands.TryEnqueue("select");
        commands.TryEnqueue("execute");
        Assert.Equal(2, commands.Clear());
        Assert.Equal(0, commands.Count);
        Assert.False(commands.TryDequeue(out _));
    }

    [Fact]
    public void OpeningNewComEpochInvalidatesPreviousGoodStandbyProof()
    {
        var evidence = new Iec101LinkLayerState();
        var old = DateTime.UtcNow;
        evidence.MarkGoodResponse(old);
        evidence.Acd = true;
        evidence.Dfc = true;
        evidence.FrameCountBit = true;
        evidence.TxFrames = 99;
        evidence.ResetConnectionEvidence();
        Assert.Null(evidence.LastGoodResponseUtc);
        Assert.False(evidence.Acd);
        Assert.False(evidence.Dfc);
        Assert.False(evidence.FrameCountBit);
        Assert.Equal(0, evidence.ConsecutiveGoodResponses);
        Assert.Equal(99, evidence.TxFrames);
    }
}
