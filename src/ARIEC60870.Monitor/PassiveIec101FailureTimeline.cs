// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Monitor;

/// <summary>
/// A compact, forensic observation interval. Counts describe what two serial RX
/// adapters delivered to the host; they do not attest the electrical state or
/// the redundant line that was not captured.
/// </summary>
public sealed record PassiveTimelineWindow(
    DateTime StartUtc, DateTime EndUtc, long Source0Frames, long Source1Frames,
    long InvalidFrames, long PrimaryRequests, long Class1Polls,
    long Class2Polls, long LinkStatusPolls, long ShortE5Responses,
    long SecondaryLinkStatus, long UnknownPrimaryFunctions,
    long ServiceNotImplementedReplies, long Retries, long ResponseTimeouts,
    long UnmatchedReplies, long DfcBusy, long GiActivations, long GiCompleted,
    long GiExceptions, int? FirstAnomalyStream, long? FirstAnomalyOffset,
    bool HostTimestampOrderUncertain, long CompressedEmptyWindows)
{
    public bool HasAnomaly => InvalidFrames > 0 || UnknownPrimaryFunctions > 0 ||
        ServiceNotImplementedReplies > 0 || Retries > 0 || ResponseTimeouts > 0 ||
        UnmatchedReplies > 0 || DfcBusy > 0 || GiExceptions > 0;

    public long Frames => Source0Frames + Source1Frames;
}

/// <summary>Duration/retention bounds for time bucket computation.</summary>
public sealed record PassiveTimelineOptions(int WindowSeconds = 5, int MaxIndividualEmptyWindows = 12)
{
    public void Validate()
    {
        if (WindowSeconds is < 1 or > 60 || MaxIndividualEmptyWindows is < 0 or > 120)
            throw new ArgumentOutOfRangeException(nameof(WindowSeconds),
                "Timeline window must be 1-60s; gap expansion must remain bounded.");
    }
}

/// <summary>
/// Allocation-light, deterministic IEC-101 evidence timeline. Consumes the
/// complete framed writer stream or a verified offline replay, independent of
/// WPF presentation/retention. No COM, TX, timers or unbounded event history.
/// </summary>
public sealed class PassiveIec101FailureTimeline
{
    private readonly DateTime _origin;
    private readonly PassiveTimelineOptions _options;
    private readonly long _windowTicks;
    private long _currentIndex = -1;
    private Bucket _bucket = new();

    private sealed class Bucket
    {
        public long Source0Frames, Source1Frames, InvalidFrames, PrimaryRequests;
        public long Class1Polls, Class2Polls, LinkStatusPolls, ShortE5Responses;
        public long SecondaryLinkStatus, UnknownPrimaryFunctions, ServiceNotImplementedReplies;
        public long Retries, ResponseTimeouts, UnmatchedReplies, DfcBusy;
        public long GiActivations, GiCompleted, GiExceptions;
        public int? FirstAnomalyStream;
        public long? FirstAnomalyOffset;
        public bool TimeUncertain;
        public void Mark(byte stream, long offset)
        {
            if (FirstAnomalyOffset.HasValue) return;
            FirstAnomalyStream = stream;
            FirstAnomalyOffset = offset;
        }
    }

    public PassiveIec101FailureTimeline(DateTime captureStartUtc,
        PassiveTimelineOptions? options = null)
    {
        if (captureStartUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Capture anchor must be UTC.", nameof(captureStartUtc));
        _origin = captureStartUtc;
        _options = options ?? new PassiveTimelineOptions();
        _options.Validate();
        _windowTicks = TimeSpan.FromSeconds(_options.WindowSeconds).Ticks;
    }

    public long WindowCount { get; private set; }
    public long CompressedEmptyWindowCount { get; private set; }

    public void ObserveFrame(PassiveObservedFrame frame, Action<PassiveTimelineWindow> emit)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(emit);
        if (frame.StreamId > 1 || frame.EstimatedUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timeline requires stream ID 0/1 and UTC receive estimates.");
        AdvanceTime(frame.EstimatedUtc, emit);
        if (frame.StreamId == 0) _bucket.Source0Frames++;
        else _bucket.Source1Frames++;

        var raw = frame.RawBytes;
        if (!frame.ChecksumValid || frame.Format is "Partial" or not ("Fixed" or "Variable" or "Single") ||
            (frame.Format == "Single" && (raw.Length != 1 || raw[0] != 0xE5)))
        {
            _bucket.InvalidFrames++;
            _bucket.Mark(frame.StreamId, frame.StreamOffset);
            return;
        }

        if (frame.Format == "Single")
        {
            _bucket.ShortE5Responses++;
            return;
        }

        var controlAt = frame.Format == "Fixed" ? 1 : 4;
        if (raw.Length <= controlAt)
        {
            _bucket.InvalidFrames++;
            _bucket.Mark(frame.StreamId, frame.StreamOffset);
            return;
        }
        var control = raw[controlAt];
        var function = control & 0x0f;
        if ((control & 0x40) != 0)
        {
            _bucket.PrimaryRequests++;
            if (function == 9) _bucket.LinkStatusPolls++;
            else if (function == 10) _bucket.Class1Polls++;
            else if (function == 11) _bucket.Class2Polls++;
            if (function is not (0 or 1 or 3 or 4 or 7 or 9 or 10 or 11))
            {
                _bucket.UnknownPrimaryFunctions++;
                _bucket.Mark(frame.StreamId, frame.StreamOffset);
            }
        }
        else
        {
            if (function == 11) _bucket.SecondaryLinkStatus++;
            if (function == 15)
            {
                _bucket.ServiceNotImplementedReplies++;
                _bucket.Mark(frame.StreamId, frame.StreamOffset);
            }
        }
    }

    public void ObserveTransaction(PassiveTransactionEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        // Transaction events can reference old request timestamps. Attribute
        // them to the current *observation* window, not to the original request
        // time, so the first-anomaly source offset is never silently discarded.
        switch (item.Kind)
        {
            case PassiveTransactionKind.RetryObserved:
                _bucket.Retries++;
                _bucket.Mark(item.ObservedStream, item.ObservedOffset);
                break;
            case PassiveTransactionKind.ResponseTimeout:
                _bucket.ResponseTimeouts++;
                _bucket.Mark(item.ObservedStream, item.ObservedOffset);
                break;
            case PassiveTransactionKind.UnmatchedReply:
            case PassiveTransactionKind.IncompatibleReply:
            case PassiveTransactionKind.AmbiguousSingleReply:
                _bucket.UnmatchedReplies++;
                _bucket.Mark(item.ObservedStream, item.ObservedOffset);
                break;
            case PassiveTransactionKind.DfcBusy:
                _bucket.DfcBusy++;
                _bucket.Mark(item.ObservedStream, item.ObservedOffset);
                break;
        }
    }

    public void ObserveGi(PassiveGiEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        switch (item.Kind)
        {
            case PassiveGiKind.Activation:
                _bucket.GiActivations++;
                break;
            case PassiveGiKind.Completed:
                _bucket.GiCompleted++;
                break;
            case PassiveGiKind.NegativeConfirmation:
            case PassiveGiKind.Superseded:
            case PassiveGiKind.TimedOut:
            case PassiveGiKind.UnmatchedResponse:
            case PassiveGiKind.DataBeforeConfirmation:
            case PassiveGiKind.CapacityEviction:
                _bucket.GiExceptions++;
                _bucket.Mark(item.StreamId, item.StreamOffset);
                break;
        }
    }

    public void AdvanceTime(DateTime utc, Action<PassiveTimelineWindow> emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        if (utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timeline timestamps must be UTC.", nameof(utc));

        var elapsed = utc.Ticks - _origin.Ticks;
        var target = elapsed <= 0 ? 0 : elapsed / _windowTicks;
        if (_currentIndex == -1) { _currentIndex = target; return; }
        if (target < _currentIndex)
        {
            // Cross-adapter host receive times may arrive out of order. Keep
            // bounded streaming state and explicitly record uncertainty.
            _bucket.TimeUncertain = true;
            return;
        }
        if (target == _currentIndex) return;
        EmitCurrent(emit);
        var empty = target - _currentIndex - 1;
        if (empty > _options.MaxIndividualEmptyWindows)
        {
            var firstEmpty = _currentIndex + 1;
            var lastEmpty = target;
            emit(Create(firstEmpty, new Bucket(), empty, lastEmpty));
            WindowCount++;
            CompressedEmptyWindowCount += empty;
        }
        else
        {
            for (var i = _currentIndex + 1; i < target; i++)
            {
                emit(Create(i, new Bucket(), 0, i + 1));
                WindowCount++;
            }
        }
        _currentIndex = target;
        _bucket = new Bucket();
    }

    public void Complete(Action<PassiveTimelineWindow> emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        if (_currentIndex < 0) return;
        EmitCurrent(emit);
        _currentIndex = -1;
        _bucket = new Bucket();
    }

    private void EmitCurrent(Action<PassiveTimelineWindow> emit)
    {
        emit(Create(_currentIndex, _bucket, 0, _currentIndex + 1));
        WindowCount++;
    }

    private PassiveTimelineWindow Create(long index, Bucket b, long compressed, long endIndex)
    {
        var start = _origin.AddTicks(checked(index * _windowTicks));
        var end = _origin.AddTicks(checked(endIndex * _windowTicks));
        return new PassiveTimelineWindow(start, end, b.Source0Frames,
            b.Source1Frames, b.InvalidFrames, b.PrimaryRequests, b.Class1Polls,
            b.Class2Polls, b.LinkStatusPolls, b.ShortE5Responses,
            b.SecondaryLinkStatus, b.UnknownPrimaryFunctions,
            b.ServiceNotImplementedReplies, b.Retries, b.ResponseTimeouts,
            b.UnmatchedReplies, b.DfcBusy, b.GiActivations, b.GiCompleted,
            b.GiExceptions, b.FirstAnomalyStream, b.FirstAnomalyOffset,
            b.TimeUncertain, compressed);
    }
}
