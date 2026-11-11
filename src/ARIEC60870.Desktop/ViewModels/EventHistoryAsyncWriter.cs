// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Threading.Channels;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// A single bounded writer off the WPF Dispatcher. TryEnqueue performs no
/// serialization, hashing, disk I/O or waits. Overflow and write failure
/// close admission rather than silently creating holes in the on-disk index.
/// The index remains a contiguous, verified prefix of UI-observed events.
/// </summary>
public sealed class EventHistoryAsyncWriter : IDisposable
{
    private sealed record Work(RelayEventRow? Row, TaskCompletionSource<bool>? Barrier);
    private readonly Channel<Work> _channel;
    private readonly EventHistoryJournal _journal;
    private readonly CancellationTokenSource _abort = new();
    private readonly Task _worker;
    private readonly int _batchSize;
    private long _accepted;
    private int _disposed;
    private int _completed;
    private string? _failure;

    public EventHistoryAsyncWriter(EventHistoryJournal journal, int capacity = 4096,
        int batchSize = 64)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (capacity is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (batchSize is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(batchSize));
        _journal = journal;
        _batchSize = batchSize;
        _channel = Channel.CreateBounded<Work>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _worker = Task.Run(ConsumeAsync);
    }

    public long AcceptedCount => Interlocked.Read(ref _accepted);
    public long IndexedCount => _journal.Count;
    public long PendingCount => Volatile.Read(ref _completed) == 1
        ? 0 : Math.Max(0, AcceptedCount - IndexedCount);
    public string? FailureReason => Volatile.Read(ref _failure);
    public bool IsCompleted => Volatile.Read(ref _completed) == 1;

    /// <summary>Non-blocking bounded enqueue; returns false on first overflow
    /// or after an I/O failure. A false return must be counted as unindexed.</summary>
    public bool TryEnqueue(RelayEventRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (Volatile.Read(ref _disposed) != 0 || FailureReason is not null ||
            IsCompleted) return false;
        if (_channel.Writer.TryWrite(new Work(row, null)))
        {
            Interlocked.Increment(ref _accepted);
            return true;
        }
        Fail("Event History writer queue exceeded its bounded capacity; " +
             "new rows are not indexed, and capture continues independently.");
        return false;
    }

    /// <summary>FIFO barrier for tests and explicit UI settling; never called
    /// synchronously on the Dispatcher. Existing accepted rows are durable
    /// to the session file buffers before this task completes.</summary>
    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        if (FailureReason is { } failure) throw new IOException(failure);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _channel.Writer.WriteAsync(new Work(null, done), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChannelClosedException ex)
        {
            throw new IOException(FailureReason ?? "Event History writer is closed.", ex);
        }
        await done.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (FailureReason is { } reason) throw new IOException(reason);
    }

    private void FlushBatch(List<RelayEventRow> batch)
    {
        if (batch.Count == 0) return;
        _journal.AppendBatch(batch);
        batch.Clear();
    }

    private async Task ConsumeAsync()
    {
        var batch = new List<RelayEventRow>(_batchSize);
        TaskCompletionSource<bool>? activeBarrier = null;
        try
        {
            while (await _channel.Reader.WaitToReadAsync(_abort.Token).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var item))
                {
                    if (item.Row is { } row) batch.Add(row);
                    if (item.Barrier is { } barrier)
                    {
                        activeBarrier = barrier;
                        FlushBatch(batch);
                        activeBarrier.TrySetResult(true);
                        activeBarrier = null;
                    }
                    else if (batch.Count >= _batchSize)
                    {
                        FlushBatch(batch);
                    }
                }
                FlushBatch(batch);
            }
        }
        catch (OperationCanceledException) when (_abort.IsCancellationRequested)
        {
            // A new session/closed window discards this temporary history.
        }
        catch (Exception ex)
        {
            Fail("Event History writer failed: " + ex.Message);
            activeBarrier?.TrySetException(new IOException(FailureReason, ex));
        }
        finally
        {
            Volatile.Write(ref _completed, 1);
            _channel.Writer.TryComplete();
            while (_channel.Reader.TryRead(out var pending))
                pending.Barrier?.TrySetException(new IOException(
                    FailureReason ?? "Event History writer stopped."));
            if (Volatile.Read(ref _disposed) != 0)
                _journal.Dispose();
        }
    }

    private void Fail(string reason)
    {
        Interlocked.CompareExchange(ref _failure, reason, null);
        _channel.Writer.TryComplete();
    }

    /// <summary>Non-blocking UI shutdown. Worker owns journal disposal after
    /// cancellation and releases any pending barrier. No file I/O on WPF.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _channel.Writer.TryComplete();
        _abort.Cancel();
        if (IsCompleted) _journal.Dispose();
        _ = _worker.ContinueWith(_ => _abort.Dispose(), TaskScheduler.Default);
    }
}
