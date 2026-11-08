// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Monitor;

/// <summary>Immutable, source-qualified GI projection published by the complete Monitor
/// writer. Unlike the bounded WPF frame preview, a dropped preview cannot remove
/// ACTCON, ACTTERM or GI data from this transaction ledger.</summary>
public sealed record PassiveWriterGiSnapshot(long Revision,
    IReadOnlyList<Iec10xGiCasebook.GiCase> Cases, long MilestonesNotPresented);
public sealed record PassiveWriterGiMilestone(Iec103MasterEvidenceEvent Evidence,
    Iec10xGiCasebook.GiMilestone Milestone);

/// <summary>
/// Writer-owned adapter to the EXISTING Master GI casebook and Core codecs.
/// The passive-specific tracker still owns cross-port anomaly diagnostics;
/// it is not used as a second authority for the GI workspace.
/// </summary>
public sealed class PassiveWriterGiCasebook
{
    private const int MaxPendingMilestones = 512;
    private readonly object _gate = new();
    private readonly int _linkWidth;
    private readonly int _cotWidth;
    private readonly int _caWidth;
    private readonly Iec10xAsduDecoder _decoder;
    private readonly Iec10xGiCasebook _casebook = new();
    private readonly Queue<PassiveWriterGiMilestone> _milestones = new();
    // IEC101 unbalanced: at most one outstanding request per physical circuit.
    // Fixed two-slot state cannot grow under malformed or multi-DL traffic.
    private readonly (int Dl, bool Class1, DateTime Utc)?[] _pendingPolls = new (int, bool, DateTime)?[2];
    private long _sequence;
    private long _revision;
    private long _milestonesNotPresented;

    public PassiveWriterGiCasebook(int linkWidth = 1, int cotWidth = 2,
        int caWidth = 2, int ioaWidth = 3)
    {
        if (linkWidth is < 0 or > 2 || cotWidth is < 1 or > 2 ||
            caWidth is < 1 or > 2 || ioaWidth is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(linkWidth));
        _linkWidth = linkWidth;
        _cotWidth = cotWidth;
        _caWidth = caWidth;
        _decoder = new Iec10xAsduDecoder(cotWidth, caWidth, ioaWidth);
    }

    public PassiveWriterGiSnapshot Snapshot()
    {
        lock (_gate)
            return new PassiveWriterGiSnapshot(_revision, _casebook.Snapshot(),
                _milestonesNotPresented);
    }

    public bool TryReadMilestone(out PassiveWriterGiMilestone item)
    {
        lock (_gate)
        {
            if (_milestones.Count > 0)
            {
                item = _milestones.Dequeue();
                return true;
            }
            item = null!;
            return false;
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            Array.Clear(_pendingPolls);
            if (_casebook.ActiveCount == 0) return;
            _casebook.EndObservation();
            _revision++;
        }
    }

    public void Observe(PassiveObservedFrame seen)
    {
        ArgumentNullException.ThrowIfNull(seen);
        if (seen.Format is not ("Variable" or "Fixed") ||
            !seen.ChecksumValid || seen.StreamId > 3) return;
        var role = PassiveIec101FrameRoles.Classify(seen, _linkWidth);
        if (role == PassiveWireRole.Unknown) return;
        var width = seen.DetectedLinkAddressWidth ?? _linkWidth;
        if (width is < 0 or > 2) return;

        // Most passive traffic is not GI. Parse just the canonical wire/header
        // first and skip ordinary traffic when no GI is active.
        var isGiCommand = false;
        if (seen.Format == "Variable")
        {
            if (!Ft12WireLayout.TryParse(seen.RawBytes, width, out var wire) ||
                wire.Kind != Ft12WireKind.Variable ||
                !Iec10xAsduHeader.TryParse(
                    seen.RawBytes.AsSpan(wire.AsduOffset, wire.AsduLength),
                    _cotWidth, _caWidth, out var header)) return;
            isGiCommand = header.TypeId == 100;
        }

        lock (_gate)
        {
            if (seen.Format == "Variable" && _casebook.ActiveCount == 0 &&
                !isGiCommand)
                return;
            if (seen.Format == "Fixed" && _casebook.ActiveCount == 0)
                return;

            var frame = Ft12Parser.ForIec101(width).Decode(seen.RawBytes);
            if (!frame.IsChecksumValid || !frame.IsLengthValid ||
                frame.LinkControl is not { } control) return;
            var direction = role == PassiveWireRole.Primary
                ? FrameDirection.MasterToSlave : FrameDirection.SlaveToMaster;
            Iec10xAsduDecode? asdu = null;
            if (seen.Format == "Variable" && frame.AsduBytes.Count > 0)
            {
                asdu = _decoder.Decode(frame.AsduBytes);
                if (asdu.Issues.Count > 0) return;
            }

            var circuit = seen.StreamId / 2;
            var dl = frame.LinkAddress ?? -1;
            var dataClass = "-";
            if (direction == FrameDirection.MasterToSlave &&
                control.Prm && control.FunctionCode is 10 or 11)
            {
                _pendingPolls[circuit] = (dl, control.FunctionCode == 10, seen.EstimatedUtc);
                dataClass = control.FunctionCode == 10 ? "Class 1" : "Class 2";
            }
            else if (direction == FrameDirection.SlaveToMaster)
            {
                if (control.IsSecondaryUserData)
                {
                    var poll = _pendingPolls[circuit];
                    _pendingPolls[circuit] = null;
                    if (poll is { } matched && matched.Dl == dl &&
                        (seen.EstimatedUtc - matched.Utc).Duration() <= TimeSpan.FromSeconds(30))
                        dataClass = matched.Class1 ? "Class 1" : "Class 2";
                }
                else if (control.FunctionCode == 9)
                    _pendingPolls[circuit] = null;
            }

            var evidence = new Iec103MasterEvidenceEvent
            {
                SequenceNumber = ++_sequence,
                TimestampUtc = seen.EstimatedUtc,
                Direction = direction,
                ProtocolMode = Iec60870ProtocolMode.Iec101,
                PhysicalSource = seen.StreamId < 2 ? "Link A" : "Link B",
                PhysicalStreamId = seen.StreamId,
                RawStreamOffset = seen.StreamOffset,
                RawHex = Convert.ToHexString(seen.RawBytes),
                Frame = frame,
                LinkAddress = frame.LinkAddress,
                DataClass = dataClass,
                PollingReason = "PASSIVE-GI-WRITER",
                TypeId = asdu?.TypeId,
                CommonAddressNumber = asdu?.CommonAddress,
                CauseOfTransmission = asdu?.CauseOfTransmission,
                CauseDetails = asdu?.CauseDetails,
                OriginatorAddress = asdu?.OriginatorAddress,
                CommandDetails = asdu?.CommandDetails,
                AsduDetails = asdu
            };
            var milestone = _casebook.Observe(evidence);
            // The Master GI casebook records Class 1 activity and data even
            // when Observe returns no UI milestone.
            _revision++;
            if (milestone is null) return;
            if (_milestones.Count == MaxPendingMilestones)
            {
                _milestones.Dequeue();
                _milestonesNotPresented++;
            }
            _milestones.Enqueue(new PassiveWriterGiMilestone(evidence, milestone));
        }
    }
}
