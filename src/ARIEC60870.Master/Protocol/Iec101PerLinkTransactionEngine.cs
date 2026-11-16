// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using ARIEC60870.Core.Model;

namespace ARIEC60870.Master.Protocol;

/// <summary>
/// Sole transaction and FCB owner for one physical IEC-101 unbalanced link.
/// Single-link and dual-link orchestrators share this exact implementation.
/// No I/O, scheduler, UI or alternate FT1.2 parsing belongs here.
/// </summary>
public sealed class Iec101PerLinkTransactionEngine
{
    private long _epoch;
    private long _lastExchangeId;
    private bool _fcb;
    private Iec101PendingExchange? _pending;

    // Exactly one optional retransmission of an unchanged *read* exchange.
    // No retry on an out-of-context/garbled candidate or mutating FC3.
    public const int MaxReadRetransmissions = 1;

    public long LinkEpoch => _epoch;
    public bool FrameCountBit => _fcb;
    public bool HasPendingExchange => _pending is not null;

    public void StartConnectionEpoch()
    {
        EndConnectionEpoch();
        _fcb = false;
    }

    public void EndConnectionEpoch()
    {
        if (_pending is { } pending)
        {
            pending.State = pending.State is Iec101ExchangeState.Transmitted or Iec101ExchangeState.AwaitingResponse
                ? Iec101ExchangeState.Unknown : Iec101ExchangeState.Failed;
            _pending = null;
        }
        _epoch++;
    }

    public void ResetFrameCount()
    {
        if (_pending is not null)
            throw new InvalidOperationException("Cannot reset FCB while an IEC-101 exchange is in flight.");
        _fcb = false;
    }

    public Iec101PendingExchange BeginFixed(int functionCode, bool fcv, int linkAddress,
        int linkAddressSize, string physicalSource)
    {
        var control = Ft12FrameBuilder.BuildPrimaryControl(functionCode, fcv, fcv && _fcb);
        var bytes = Ft12FrameBuilder.Fixed(control, linkAddress, linkAddressSize);
        return Admit(functionCode, fcv, linkAddress, physicalSource, bytes,
            Iec101TransactionSafety.ReadOnly);
    }

    public Iec101PendingExchange BeginVariable(byte[] asdu, int linkAddress, int linkAddressSize,
        string physicalSource)
    {
        ArgumentNullException.ThrowIfNull(asdu);
        var control = Ft12FrameBuilder.BuildPrimaryControl(3, fcv: true, fcb: _fcb);
        var bytes = Ft12FrameBuilder.Variable(control, linkAddress, asdu, linkAddressSize);
        // Conservative classification: application FC3 may be a physical command.
        // No implicit retransmission of an uncertain FC3 request is permitted.
        return Admit(3, true, linkAddress, physicalSource, bytes,
            Iec101TransactionSafety.PotentiallyMutating);
    }

    private Iec101PendingExchange Admit(int functionCode, bool fcv, int linkAddress,
        string physicalSource, byte[] originalBytes, Iec101TransactionSafety safety)
    {
        if (_pending is not null)
            throw new InvalidOperationException("IEC-101 per-link invariant: another confirmed exchange is in flight.");
        var exchange = new Iec101PendingExchange(this, ++_lastExchangeId, _epoch, physicalSource,
            linkAddress, functionCode, fcv, _fcb, originalBytes, safety);
        _pending = exchange;
        return exchange;
    }

    /// <summary>
    /// Prepare one bounded read retransmission after pure RX silence.
    /// The very same exchange owns the unchanged physical request/FCB:
    /// no second exchange can start and no control may be replayed.
    /// </summary>
    public bool TryAuthorizeUnchangedReadRetransmission(Iec101PendingExchange exchange)
    {
        EnsureActive(exchange);
        if (!exchange.CanRetryUnchangedReadFrame ||
            exchange.State != Iec101ExchangeState.AwaitingResponse ||
            exchange.AcceptedValidation is not null ||
            exchange.RejectedCandidates != 0 ||
            exchange.RetryAuthorized ||
            exchange.ReadRetransmissions >= MaxReadRetransmissions)
            return false;
        exchange.RetryAuthorized = true;
        return true;
    }

    public void MarkTransmitAttempt(Iec101PendingExchange exchange)
    {
        EnsureActive(exchange);
        if (exchange.State == Iec101ExchangeState.AwaitingResponse && exchange.RetryAuthorized)
        {
            exchange.RetryAuthorized = false;
            exchange.ReadRetransmissions++;
        }
        else if (exchange.State != Iec101ExchangeState.Admitted)
        {
            throw new InvalidOperationException(
                "IEC-101 transmit attempt is not new/admitted or a specifically authorized read retransmission.");
        }
        exchange.State = Iec101ExchangeState.Transmitted;
        exchange.TransmitStartedAt = Stopwatch.GetTimestamp();
    }

    public void MarkAwaitingResponse(Iec101PendingExchange exchange)
    {
        EnsureActive(exchange);
        if (exchange.State != Iec101ExchangeState.Transmitted)
            throw new InvalidOperationException("Exchange was not admitted for transmission.");
        exchange.State = Iec101ExchangeState.AwaitingResponse;
    }

    public Iec101SecondaryResponseValidation ValidateCandidate(Iec101PendingExchange exchange,
        Ft12FrameDecode candidate, bool matchesEarlierTransmit = false)
    {
        EnsureActive(exchange);
        if (exchange.State != Iec101ExchangeState.AwaitingResponse)
            throw new InvalidOperationException("IEC-101 response arrived outside a transmitted exchange.");
        var validation = Iec101SecondaryResponseValidator.Validate(candidate,
            exchange.LinkAddress, exchange.OriginalTransmit, matchesEarlierTransmit,
            exchange.PrimaryFunctionCode);
        if (!validation.IsValid) exchange.RejectedCandidates++;
        if (validation.IsValid && exchange.AcceptedValidation is null)
        {
            exchange.AcceptedValidation = validation;
            exchange.AcceptedFrame = candidate;
            exchange.State = Iec101ExchangeState.ValidatedOutcome;
        }
        return validation;
    }

    public Iec101ExchangeOutcome Complete(Iec101PendingExchange exchange)
    {
        EnsureActive(exchange);
        var accepted = exchange.AcceptedValidation;
        var frame = exchange.AcceptedFrame;
        var positive = accepted is { IsValid: true, AdvancesFcb: true };
        var advance = positive && exchange.UsesFrameCount;
        if (advance) _fcb = !_fcb;

        Iec101ExchangeVerdict verdict;
        if (frame?.LinkControl is { Dfc: true })
            verdict = Iec101ExchangeVerdict.Busy;
        else if (frame?.LinkControl is { FunctionCode: 1 })
            verdict = Iec101ExchangeVerdict.ConfirmedNegative;
        else if (positive && (frame?.LinkControl?.FunctionCode == 9 ||
                              (frame?.IsSingleCharacterAck == true && exchange.PrimaryFunctionCode is 10 or 11)))
            verdict = Iec101ExchangeVerdict.NoData;
        else if (positive)
            verdict = Iec101ExchangeVerdict.ConfirmedPositive;
        else if (exchange.Safety == Iec101TransactionSafety.PotentiallyMutating &&
                 exchange.TransmitStartedAt != 0)
            verdict = Iec101ExchangeVerdict.UnknownDelivery;
        else
            verdict = Iec101ExchangeVerdict.Unconfirmed;

        exchange.State = verdict is Iec101ExchangeVerdict.ConfirmedPositive or Iec101ExchangeVerdict.NoData
            ? Iec101ExchangeState.Completed
            : verdict == Iec101ExchangeVerdict.UnknownDelivery
                ? Iec101ExchangeState.Unknown : Iec101ExchangeState.Failed;
        _pending = null;
        return new Iec101ExchangeOutcome(exchange.ExchangeId, exchange.LinkEpoch, verdict, advance);
    }

    internal void Abort(Iec101PendingExchange exchange)
    {
        if (!ReferenceEquals(_pending, exchange)) return;
        exchange.State = exchange.TransmitStartedAt != 0
            ? Iec101ExchangeState.Unknown : Iec101ExchangeState.Failed;
        _pending = null;
    }

    private void EnsureActive(Iec101PendingExchange exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        if (!ReferenceEquals(_pending, exchange) || exchange.LinkEpoch != _epoch)
            throw new InvalidOperationException("Stale or foreign IEC-101 exchange/connection epoch.");
    }
}

public enum Iec101ExchangeState
{
    Created, Admitted, Transmitted, AwaitingResponse, ValidatedOutcome, Completed, Failed, Unknown
}

public enum Iec101TransactionSafety { ReadOnly, PotentiallyMutating }
public enum Iec101ExchangeVerdict { ConfirmedPositive, ConfirmedNegative, Busy, NoData, Unconfirmed, UnknownDelivery }

public readonly record struct Iec101ExchangeOutcome(
    long ExchangeId, long LinkEpoch, Iec101ExchangeVerdict Verdict, bool FrameCountAdvanced);

public sealed class Iec101PendingExchange : IDisposable
{
    private readonly Iec101PerLinkTransactionEngine _owner;

    internal Iec101PendingExchange(Iec101PerLinkTransactionEngine owner, long id, long epoch,
        string source, int address, int functionCode, bool fcv, bool fcb,
        byte[] originalTransmit, Iec101TransactionSafety safety)
    {
        _owner = owner;
        ExchangeId = id;
        LinkEpoch = epoch;
        PhysicalSource = source;
        LinkAddress = address;
        PrimaryFunctionCode = functionCode;
        UsesFrameCount = fcv;
        FrameCountBit = fcb;
        OriginalTransmit = originalTransmit;
        Safety = safety;
        State = Iec101ExchangeState.Admitted;
    }

    public long ExchangeId { get; }
    public long LinkEpoch { get; }
    public string PhysicalSource { get; }
    public int LinkAddress { get; }
    public int PrimaryFunctionCode { get; }
    public bool UsesFrameCount { get; }
    public bool FrameCountBit { get; }
    public byte[] OriginalTransmit { get; }
    public Iec101TransactionSafety Safety { get; }
    public bool CanRetryUnchangedReadFrame => Safety == Iec101TransactionSafety.ReadOnly
        && PrimaryFunctionCode is 9 or 10 or 11;
    public Iec101ExchangeState State { get; internal set; }
    public int ReadRetransmissions { get; internal set; }
    public int RejectedCandidates { get; internal set; }
    internal bool RetryAuthorized { get; set; }
    public long TransmitStartedAt { get; internal set; }
    internal Iec101SecondaryResponseValidation? AcceptedValidation { get; set; }
    internal Ft12FrameDecode? AcceptedFrame { get; set; }

    public void Dispose() => _owner.Abort(this);
}
