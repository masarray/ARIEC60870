// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Channels;
using ARIEC60870.Core.Model;
using ARIEC60870.Core.Parsing;
using ARIEC60870.Master.Iec101.Redundancy;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Protocol.Iec10x;
using ARIEC60870.Master.Transport;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101DualLinkRedundancySessionTests
{
    [Fact]
    public async Task ManualFailoverPromotesStandbyAndProducesEvidence()
    {
        var settings = CreateFastSimulatedIec101Settings();
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "Link A", PortName = "SIM-A", LinkAddress = 1 },
            LinkB = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "SIM-B", LinkAddress = 1 },
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(100),
            RecoveryBackoff = TimeSpan.FromMilliseconds(100),
            AntiPingPongWindow = TimeSpan.FromSeconds(30),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Required
        };

        var session = new Iec101DualLinkRedundancySession(
            options,
            new SimulatedIec101Transport(options.LinkA.ApplyTo(settings)),
            new SimulatedIec101Transport(options.LinkB.ApplyTo(settings)));

        var runTask = session.RunForAsync(TimeSpan.FromMilliseconds(1500), CancellationToken.None);
        await Task.Delay(250);
        session.QueueManualFailover("unit test manual switchover proof");
        var result = await runTask;
        var snapshot = session.CreateSnapshot();

        Assert.Contains(session.FailoverJournal, x => x.Completed && x.FromLink == "Link A" && x.ToLink == "Link B");
        Assert.Contains(result.Events, x => x.Category == Iec101RedundancyEventKind.ManualFailoverRequested.ToString());
        Assert.Contains(result.Events, x => x.Category == Iec101RedundancyEventKind.FailoverCompleted.ToString() || x.Summary.Contains("failover completed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Link B", snapshot.ActiveLinkName);
        Assert.True(snapshot.ApplicationImageObjectCount > 0);
    }

    [Fact]
    public async Task StartupGiSequenceAsduPublishesEveryInformationObject()
    {
        var settings = CreateFastSimulatedIec101Settings();
        settings.SendGeneralInterrogationOnConnect = true;
        settings.MaxClass1DrainFrames = 4;
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "Link A", PortName = "SIM-A", LinkAddress = 1 },
            LinkB = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "SIM-B", LinkAddress = 1 },
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(500),
            RecoveryBackoff = TimeSpan.FromMilliseconds(100),
            AntiPingPongWindow = TimeSpan.FromSeconds(30),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled
        };

        var session = new Iec101DualLinkRedundancySession(
            options,
            new SequenceGiIec101Transport(options.LinkA.ApplyTo(settings), baseIoa: 100),
            new SimulatedIec101Transport(options.LinkB.ApplyTo(settings)));

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(650), CancellationToken.None);
        var stationInterrogationValues = result.Events
            .Where(x => x.Direction == FrameDirection.SlaveToMaster && x.TypeId == 1 && x.CauseOfTransmission == 20)
            .ToArray();

        Assert.Contains(stationInterrogationValues, x => x.InformationObjectAddress == 100 && x.SignalDisplayValue.Contains("ON", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(stationInterrogationValues, x => x.InformationObjectAddress == 101 && x.SignalDisplayValue.Contains("OFF", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Events, x => x.Category == "RX Object" && x.InformationObjectAddress == 101);
        Assert.Contains(result.Events, x => string.Equals(x.PollingReason, "Dual-link startup post-GI Class 2 verification sweep", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, session.CreateSnapshot().ApplicationImageObjectCount);
    }

    [Fact]
    public async Task NegativeStationGiUsesGroupInterrogationFallbackOnActiveLink()
    {
        var settings = CreateFastSimulatedIec101Settings();
        settings.SendGeneralInterrogationOnConnect = true;
        settings.MaxClass1DrainFrames = 8;
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "Link A", PortName = "SIM-A", LinkAddress = 1 },
            LinkB = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "SIM-B", LinkAddress = 1 },
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(500),
            RecoveryBackoff = TimeSpan.FromMilliseconds(100),
            AntiPingPongWindow = TimeSpan.FromSeconds(30),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled
        };

        var session = new Iec101DualLinkRedundancySession(
            options,
            new NegativeStationGiThenGroupIec101Transport(options.LinkA.ApplyTo(settings), baseIoa: 120),
            new SimulatedIec101Transport(options.LinkB.ApplyTo(settings)));

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(900), CancellationToken.None);
        var valueEvents = result.Events
            .Where(x => x.Direction == FrameDirection.SlaveToMaster && x.TypeId == 1)
            .ToArray();

        Assert.Contains(result.Findings, x => x.Id == "IEC101-DUAL-GI-NEGATIVE-CONFIRMATION");
        Assert.Contains(result.Events, x => x.Summary == "IEC-101 group interrogation fallback started");
        Assert.Contains(result.Events, x => x.PollingReason == "Group interrogation fallback QOI=21");
        Assert.Contains(valueEvents, x => x.InformationObjectAddress == 120 && x.SignalDisplayValue.Contains("ON", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(valueEvents, x => x.InformationObjectAddress == 121 && x.SignalDisplayValue.Contains("OFF", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, session.CreateSnapshot().ApplicationImageObjectCount);
    }

    [Fact]
    public async Task ObservedProcessCommonAddressRetriesGiWithLearnedCa()
    {
        var settings = CreateFastSimulatedIec101Settings();
        settings.CommonAddress = 105;
        settings.SendGeneralInterrogationOnConnect = true;
        settings.MaxClass1DrainFrames = 8;
        settings.Class2PollIntervalMs = 40;
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "Link A", PortName = "SIM-A", LinkAddress = 1 },
            LinkB = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "SIM-B", LinkAddress = 1 },
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(500),
            RecoveryBackoff = TimeSpan.FromMilliseconds(100),
            AntiPingPongWindow = TimeSpan.FromSeconds(30),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled
        };

        var session = new Iec101DualLinkRedundancySession(
            options,
            new WrongConfiguredCaIec101Transport(options.LinkA.ApplyTo(settings), configuredCa: 105, observedCa: 1, baseIoa: 140),
            new SimulatedIec101Transport(options.LinkB.ApplyTo(settings)));

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(1200), CancellationToken.None);
        var valueEvents = result.Events
            .Where(x => x.Direction == FrameDirection.SlaveToMaster && x.TypeId == 1)
            .ToArray();

        Assert.Equal(1, result.Settings.CommonAddress);
        Assert.Contains(result.Findings, x => x.Id == "IEC101-DUAL-CA-AUTO-LEARNED");
        Assert.Contains(result.Events, x => x.Direction == FrameDirection.MasterToSlave && x.TypeId == 100 && x.CommonAddressNumber == 1 && x.PollingReason == "Dual-link GI retry using observed CA 1");
        Assert.Contains(valueEvents, x => x.CommonAddressNumber == 1 && x.InformationObjectAddress == 140 && x.SignalDisplayValue.Contains("ON", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(valueEvents, x => x.CommonAddressNumber == 1 && x.InformationObjectAddress == 141 && x.SignalDisplayValue.Contains("OFF", StringComparison.OrdinalIgnoreCase));
        Assert.True(session.CreateSnapshot().ApplicationImageObjectCount >= 2);
    }

    [Fact]
    public async Task ManualFailoverBypassesStabilizationGuardButStillRequiresPromotableStandby()
    {
        var settings = CreateFastSimulatedIec101Settings();
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "Link A", PortName = "SIM-A", LinkAddress = 1 },
            LinkB = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "SIM-B", LinkAddress = 1 },
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(100),
            AntiPingPongWindow = TimeSpan.FromMinutes(5),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled
        };

        var session = new Iec101DualLinkRedundancySession(
            options,
            new SimulatedIec101Transport(options.LinkA.ApplyTo(settings)),
            new SimulatedIec101Transport(options.LinkB.ApplyTo(settings)));

        var runTask = session.RunForAsync(TimeSpan.FromMilliseconds(1800), CancellationToken.None);
        await Task.Delay(250);
        session.QueueManualFailover("first manual switchover");
        await Task.Delay(300);
        session.QueueManualFailover("second manual switchover inside stabilization window");
        await runTask;

        Assert.True(session.FailoverJournal.Count(x => x.Completed) >= 2);
        Assert.DoesNotContain(session.FailoverJournal, x => !x.Completed && x.Detail.Contains("anti-ping-pong", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ActiveTimeoutPromotesStandbyAndKeepsOldActiveAsRecoveringStandby()
    {
        var settings = CreateFastSimulatedIec101Settings();
        settings.ResponseTimeoutMs = 80;
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "Link A", PortName = "SIM-A", LinkAddress = 1 },
            LinkB = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "SIM-B", LinkAddress = 1 },
            ActiveFailureThreshold = 1,
            StandbyFailureThreshold = 1,
            StandbyRecoveryGoodResponseThreshold = 2,
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(100),
            RecoveryBackoff = TimeSpan.FromMilliseconds(100),
            AntiPingPongWindow = TimeSpan.FromSeconds(30),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled,
            FailbackPolicy = Iec101DualLinkFailbackPolicy.ManualOnly
        };

        var linkA = new DroppingReadTransport(new SimulatedIec101Transport(options.LinkA.ApplyTo(settings)), droppedReadCount: 3);
        var session = new Iec101DualLinkRedundancySession(
            options,
            linkA,
            new SimulatedIec101Transport(options.LinkB.ApplyTo(settings)));

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(2200), CancellationToken.None);
        var snapshot = session.CreateSnapshot();

        Assert.Contains(session.FailoverJournal, x => x.Completed && x.FromLink == "Link A" && x.ToLink == "Link B");
        Assert.Contains(result.Events, x => x.Category == Iec101RedundancyEventKind.FailoverStarted.ToString());
        Assert.Contains(result.Events, x => x.Category == Iec101RedundancyEventKind.FailoverCompleted.ToString());
        Assert.Contains(result.Events, x => x.Category == Iec101RedundancyEventKind.RecoveryStarted.ToString());
        Assert.Contains(result.Events, x => x.Category == Iec101RedundancyEventKind.RecoveryCompleted.ToString());
        Assert.Equal("Link B", snapshot.ActiveLinkName);
        Assert.Equal(Iec101DualLinkFailbackPolicy.ManualOnly, snapshot.FailbackPolicy);
    }

    [Fact]
    public async Task PreferredLinkAutoFailbackIsOptInAndProducesEvidenceWhenEnabled()
    {
        var settings = CreateFastSimulatedIec101Settings();
        settings.ResponseTimeoutMs = 80;
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "Link A", PortName = "SIM-A", LinkAddress = 1 },
            LinkB = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "SIM-B", LinkAddress = 1 },
            PreferredActiveLink = "A",
            ActiveFailureThreshold = 1,
            StandbyFailureThreshold = 1,
            StandbyRecoveryGoodResponseThreshold = 1,
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(100),
            RecoveryBackoff = TimeSpan.FromMilliseconds(100),
            AntiPingPongWindow = TimeSpan.FromMilliseconds(500),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled,
            FailbackPolicy = Iec101DualLinkFailbackPolicy.PreferredLinkAfterStableRecovery
        };

        var linkA = new DroppingReadTransport(new SimulatedIec101Transport(options.LinkA.ApplyTo(settings)), droppedReadCount: 2);
        var session = new Iec101DualLinkRedundancySession(
            options,
            linkA,
            new SimulatedIec101Transport(options.LinkB.ApplyTo(settings)));

        var result = await session.RunForAsync(TimeSpan.FromMilliseconds(2600), CancellationToken.None);

        Assert.Contains(result.Events, x => x.Category == Iec101RedundancyEventKind.AutoFailbackRequested.ToString());
        Assert.Contains(result.Findings, x => x.Id == "IEC101-DUAL-AUTO-FAILBACK");
        Assert.True(session.FailoverJournal.Count(x => x.Completed) >= 1);
    }

    [Fact]
    public async Task CascadedActiveTimeoutCanFailBackToRecoveredStandbyInsideAntiPingPongWindow()
    {
        var settings = CreateFastSimulatedIec101Settings();
        settings.ResponseTimeoutMs = 60;
        settings.Class2PollIntervalMs = 40;
        var options = new Iec101DualLinkRedundancyOptions
        {
            BaseSettings = settings,
            LinkA = new Iec101DualLinkEndpoint { Name = "Link A", PortName = "SIM-A", LinkAddress = 1 },
            LinkB = new Iec101DualLinkEndpoint { Name = "Link B", PortName = "SIM-B", LinkAddress = 1 },
            ActiveFailureThreshold = 1,
            StandbyFailureThreshold = 1,
            StandbyRecoveryGoodResponseThreshold = 2,
            StandbySupervisionInterval = TimeSpan.FromMilliseconds(80),
            RecoveryBackoff = TimeSpan.FromMilliseconds(80),
            AntiPingPongWindow = TimeSpan.FromSeconds(30),
            PostSwitchGiPolicy = Iec101PostSwitchGiPolicy.Disabled,
            FailbackPolicy = Iec101DualLinkFailbackPolicy.ManualOnly
        };

        var linkA = new DroppingReadTransport(new SimulatedIec101Transport(options.LinkA.ApplyTo(settings)), droppedReadCount: 1);
        var linkB = new DropReadsAfterSuccessfulReadsTransport(new SimulatedIec101Transport(options.LinkB.ApplyTo(settings)), successfulReadsBeforeDrop: 1, droppedReadCount: 3);
        var session = new Iec101DualLinkRedundancySession(options, linkA, linkB);

        await session.RunForAsync(TimeSpan.FromMilliseconds(2400), CancellationToken.None);
        var completed = session.FailoverJournal.Where(x => x.Completed).ToArray();
        var snapshot = session.CreateSnapshot();

        Assert.Contains(completed, x => x.FromLink == "Link A" && x.ToLink == "Link B");
        Assert.Contains(completed, x => x.FromLink == "Link B" && x.ToLink == "Link A");
        Assert.Equal("Link A", snapshot.ActiveLinkName);
    }

    private static Iec103MasterSettings CreateFastSimulatedIec101Settings()        => new()
        {
            ProtocolMode = Iec60870ProtocolMode.Iec101,
            UseSimulatedSlave = true,
            TargetProfile = "IEC-101 dual-link simulated outstation",
            LinkAddress = 1,
            LinkAddressSize = 1,
            CommonAddress = 1,
            CommonAddressSize = 2,
            CauseOfTransmissionSize = 2,
            InformationObjectAddressSize = 3,
            ResponseTimeoutMs = 250,
            Class2PollIntervalMs = 60,
            Class1DrainDelayMs = 0,
            MaxClass1DrainFrames = 16,
            ResetRemoteLinkOnConnect = false,
            ResetFcbOnConnect = false,
            SendGeneralInterrogationOnConnect = false,
            SendClockSyncOnConnect = false,
            RequestClass2ImmediatelyAfterStartup = true,
            MaxRetainedEvidenceEvents = 2000
        };

    private sealed class DroppingReadTransport : IByteTransport
    {
        private readonly IByteTransport _inner;
        private int _remainingDroppedReads;

        public DroppingReadTransport(IByteTransport inner, int droppedReadCount)
        {
            _inner = inner;
            _remainingDroppedReads = droppedReadCount;
        }

        public bool IsOpen => _inner.IsOpen;
        public ValueTask OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);
        public ValueTask CloseAsync(CancellationToken cancellationToken) => _inner.CloseAsync(cancellationToken);
        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) => _inner.WriteAsync(buffer, cancellationToken);

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_remainingDroppedReads > 0)
            {
                _remainingDroppedReads--;
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                return 0;
            }

            return await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() => _inner.Dispose();
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class DropReadsAfterSuccessfulReadsTransport : IByteTransport
    {
        private readonly IByteTransport _inner;
        private int _remainingSuccessfulReadsBeforeDrop;
        private int _remainingDroppedReads;

        public DropReadsAfterSuccessfulReadsTransport(IByteTransport inner, int successfulReadsBeforeDrop, int droppedReadCount)
        {
            _inner = inner;
            _remainingSuccessfulReadsBeforeDrop = Math.Max(0, successfulReadsBeforeDrop);
            _remainingDroppedReads = Math.Max(0, droppedReadCount);
        }

        public bool IsOpen => _inner.IsOpen;
        public ValueTask OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);
        public ValueTask CloseAsync(CancellationToken cancellationToken) => _inner.CloseAsync(cancellationToken);
        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) => _inner.WriteAsync(buffer, cancellationToken);

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_remainingSuccessfulReadsBeforeDrop > 0)
            {
                _remainingSuccessfulReadsBeforeDrop--;
                return await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }

            if (_remainingDroppedReads > 0)
            {
                _remainingDroppedReads--;
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                return 0;
            }

            return await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() => _inner.Dispose();
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class NegativeStationGiThenGroupIec101Transport : IByteTransport
    {
        private readonly Iec103MasterSettings _settings;
        private readonly Ft12Parser _ft12;
        private readonly Channel<byte> _rxBytes = Channel.CreateUnbounded<byte>();
        private readonly int _baseIoa;
        private bool _isOpen;
        private bool _groupAccepted;
        private bool _sentSequence;
        private bool _sentGiEnd;

        public NegativeStationGiThenGroupIec101Transport(Iec103MasterSettings settings, int baseIoa)
        {
            _settings = settings;
            _baseIoa = baseIoa;
            _ft12 = new Ft12Parser(settings.LinkAddressSize);
        }

        public bool IsOpen => _isOpen;

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            _isOpen = true;
            _groupAccepted = false;
            _sentSequence = false;
            _sentGiEnd = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            _isOpen = false;
            return ValueTask.CompletedTask;
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            EnsureOpen();
            var decoded = _ft12.Decode(buffer.ToArray());
            if (decoded.AsduBytes.Count > 0 && decoded.AsduBytes[0] == 100)
            {
                var qoi = decoded.AsduBytes[^1];
                if (qoi == 20)
                {
                    await EnqueueAsync(UserData(NegativeInterrogationConfirmation(qoi), acd: false), cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (qoi == 21)
                {
                    _groupAccepted = true;
                    await EnqueueAsync(FixedSecondary(functionCode: 0, acd: true), cancellationToken).ConfigureAwait(false);
                    return;
                }

                await EnqueueAsync(UserData(NegativeInterrogationConfirmation(qoi), acd: false), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (decoded.LinkControl?.FunctionCode == 10 && _groupAccepted)
            {
                if (!_sentSequence)
                {
                    _sentSequence = true;
                    await EnqueueAsync(UserData(SinglePointGroupSequenceAsdu(), acd: true), cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!_sentGiEnd)
                {
                    _sentGiEnd = true;
                    await EnqueueAsync(UserData(Iec10xAsduBuilder.ActivationTermination(_settings), acd: false), cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            await EnqueueAsync(FixedSecondary(functionCode: 9, acd: false), cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            EnsureOpen();
            if (buffer.Length == 0)
            {
                return 0;
            }

            var first = await _rxBytes.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            buffer.Span[0] = first;
            var count = 1;
            while (count < buffer.Length && _rxBytes.Reader.TryRead(out var next))
            {
                buffer.Span[count++] = next;
            }

            return count;
        }

        public void Dispose() => _isOpen = false;

        public ValueTask DisposeAsync()
        {
            _isOpen = false;
            return ValueTask.CompletedTask;
        }

        private byte[] NegativeInterrogationConfirmation(byte qoi)
        {
            var bytes = Iec10xAsduBuilder.Header(typeId: 100, vsq: 1, cause: 7, settings: _settings, ioa: 0);
            bytes[2] |= 0x40;
            bytes.Add(qoi);
            return bytes.ToArray();
        }

        private byte[] SinglePointGroupSequenceAsdu()
        {
            var bytes = Iec10xAsduBuilder.Header(typeId: 1, vsq: 0x82, cause: 21, settings: _settings, ioa: _baseIoa);
            bytes.Add(0x01);
            bytes.Add(0x00);
            return bytes.ToArray();
        }

        private byte[] FixedSecondary(int functionCode, bool acd)
        {
            var control = (byte)(functionCode & 0x0F);
            if (acd)
            {
                control |= 0x20;
            }

            return Ft12FrameBuilder.Fixed(control, _settings.LinkAddress, _settings.LinkAddressSize);
        }

        private byte[] UserData(byte[] asdu, bool acd)
        {
            var control = (byte)0x08;
            if (acd)
            {
                control |= 0x20;
            }

            return Ft12FrameBuilder.Variable(control, _settings.LinkAddress, asdu, _settings.LinkAddressSize);
        }

        private async Task EnqueueAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            foreach (var b in bytes)
            {
                await _rxBytes.Writer.WriteAsync(b, cancellationToken).ConfigureAwait(false);
            }
        }

        private void EnsureOpen()
        {
            if (!_isOpen)
            {
                throw new InvalidOperationException("Scripted IEC-101 negative GI test transport is not open.");
            }
        }
    }

    private sealed class WrongConfiguredCaIec101Transport : IByteTransport
    {
        private readonly Iec103MasterSettings _settings;
        private readonly Iec10xAsduDecoder _asduDecoder;
        private readonly Ft12Parser _ft12;
        private readonly Channel<byte> _rxBytes = Channel.CreateUnbounded<byte>();
        private readonly int _observedCa;
        private readonly int _baseIoa;
        private bool _isOpen;
        private bool _learnedGiAccepted;
        private bool _sentLearnedSequence;
        private bool _sentLearnedGiEnd;
        private int _class2ValueResponses;

        public WrongConfiguredCaIec101Transport(Iec103MasterSettings settings, int configuredCa, int observedCa, int baseIoa)
        {
            if (configuredCa == observedCa)
            {
                throw new ArgumentException("The scripted CA-learning test requires a different configured and observed CA.", nameof(observedCa));
            }

            _settings = settings;
            _observedCa = observedCa;
            _baseIoa = baseIoa;
            _asduDecoder = new Iec10xAsduDecoder(settings.CauseOfTransmissionSize, settings.CommonAddressSize, settings.InformationObjectAddressSize);
            _ft12 = new Ft12Parser(settings.LinkAddressSize);
        }

        public bool IsOpen => _isOpen;

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            _isOpen = true;
            _learnedGiAccepted = false;
            _sentLearnedSequence = false;
            _sentLearnedGiEnd = false;
            _class2ValueResponses = 0;
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            _isOpen = false;
            return ValueTask.CompletedTask;
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            EnsureOpen();
            var decoded = _ft12.Decode(buffer.ToArray());
            if (decoded.AsduBytes.Count > 0)
            {
                var asdu = _asduDecoder.Decode(decoded.AsduBytes);
                if (asdu.TypeId == 100)
                {
                    var qoi = decoded.AsduBytes[^1];
                    if (asdu.CommonAddress == _observedCa && qoi == 20)
                    {
                        _learnedGiAccepted = true;
                        await EnqueueAsync(FixedSecondary(functionCode: 0, acd: true), cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    await EnqueueAsync(UserData(NegativeInterrogationConfirmation(asdu.CommonAddress, qoi), acd: false), cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            if (decoded.LinkControl?.FunctionCode == 10 && _learnedGiAccepted)
            {
                if (!_sentLearnedSequence)
                {
                    _sentLearnedSequence = true;
                    await EnqueueAsync(UserData(SinglePointSequenceAsdu(), acd: true), cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!_sentLearnedGiEnd)
                {
                    _sentLearnedGiEnd = true;
                    await EnqueueAsync(UserData(Iec10xAsduBuilder.ActivationTermination(SettingsForCa(_observedCa)), acd: false), cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            if (decoded.LinkControl?.FunctionCode == 11 && _class2ValueResponses < 2)
            {
                var ioa = _baseIoa + 20 + _class2ValueResponses;
                _class2ValueResponses++;
                await EnqueueAsync(UserData(Iec10xAsduBuilder.FloatMeasurement(SettingsForCa(_observedCa), ioa, 12.5f + _class2ValueResponses, cause: 3), acd: false), cancellationToken).ConfigureAwait(false);
                return;
            }

            await EnqueueAsync(FixedSecondary(functionCode: 9, acd: false), cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            EnsureOpen();
            if (buffer.Length == 0)
            {
                return 0;
            }

            var first = await _rxBytes.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            buffer.Span[0] = first;
            var count = 1;
            while (count < buffer.Length && _rxBytes.Reader.TryRead(out var next))
            {
                buffer.Span[count++] = next;
            }

            return count;
        }

        public void Dispose() => _isOpen = false;

        public ValueTask DisposeAsync()
        {
            _isOpen = false;
            return ValueTask.CompletedTask;
        }

        private byte[] NegativeInterrogationConfirmation(int requestCa, byte qoi)
        {
            var bytes = Iec10xAsduBuilder.Header(typeId: 100, vsq: 1, cause: 7, settings: SettingsForCa(requestCa), ioa: 0);
            bytes[2] |= 0x40;
            bytes.Add(qoi);
            return bytes.ToArray();
        }

        private byte[] SinglePointSequenceAsdu()
        {
            var bytes = Iec10xAsduBuilder.Header(typeId: 1, vsq: 0x82, cause: 20, settings: SettingsForCa(_observedCa), ioa: _baseIoa);
            bytes.Add(0x01);
            bytes.Add(0x00);
            return bytes.ToArray();
        }

        private Iec103MasterSettings SettingsForCa(int ca)
        {
            var copy = _settings.CreateReportSnapshot();
            copy.CommonAddress = ca;
            return copy;
        }

        private byte[] FixedSecondary(int functionCode, bool acd)
        {
            var control = (byte)(functionCode & 0x0F);
            if (acd)
            {
                control |= 0x20;
            }

            return Ft12FrameBuilder.Fixed(control, _settings.LinkAddress, _settings.LinkAddressSize);
        }

        private byte[] UserData(byte[] asdu, bool acd)
        {
            var control = (byte)0x08;
            if (acd)
            {
                control |= 0x20;
            }

            return Ft12FrameBuilder.Variable(control, _settings.LinkAddress, asdu, _settings.LinkAddressSize);
        }

        private async Task EnqueueAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            foreach (var b in bytes)
            {
                await _rxBytes.Writer.WriteAsync(b, cancellationToken).ConfigureAwait(false);
            }
        }

        private void EnsureOpen()
        {
            if (!_isOpen)
            {
                throw new InvalidOperationException("Scripted IEC-101 CA learning test transport is not open.");
            }
        }
    }

    private sealed class SequenceGiIec101Transport : IByteTransport
    {
        private readonly Iec103MasterSettings _settings;
        private readonly Ft12Parser _ft12;
        private readonly Channel<byte> _rxBytes = Channel.CreateUnbounded<byte>();
        private readonly int _baseIoa;
        private bool _isOpen;
        private bool _sentSequence;
        private bool _sentGiEnd;

        public SequenceGiIec101Transport(Iec103MasterSettings settings, int baseIoa)
        {
            _settings = settings;
            _baseIoa = baseIoa;
            _ft12 = new Ft12Parser(settings.LinkAddressSize);
        }

        public bool IsOpen => _isOpen;

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            _isOpen = true;
            _sentSequence = false;
            _sentGiEnd = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            _isOpen = false;
            return ValueTask.CompletedTask;
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            EnsureOpen();
            var decoded = _ft12.Decode(buffer.ToArray());
            if (decoded.AsduBytes.Count > 0 && decoded.AsduBytes[0] == 100)
            {
                await EnqueueAsync(FixedSecondary(functionCode: 0, acd: true), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (decoded.LinkControl?.FunctionCode == 10)
            {
                if (!_sentSequence)
                {
                    _sentSequence = true;
                    await EnqueueAsync(UserData(SinglePointSequenceAsdu(), acd: true), cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!_sentGiEnd)
                {
                    _sentGiEnd = true;
                    await EnqueueAsync(UserData(Iec10xAsduBuilder.ActivationTermination(_settings), acd: false), cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            await EnqueueAsync(FixedSecondary(functionCode: 9, acd: false), cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            EnsureOpen();
            if (buffer.Length == 0)
            {
                return 0;
            }

            var first = await _rxBytes.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            buffer.Span[0] = first;
            var count = 1;
            while (count < buffer.Length && _rxBytes.Reader.TryRead(out var next))
            {
                buffer.Span[count++] = next;
            }

            return count;
        }

        public void Dispose() => _isOpen = false;

        public ValueTask DisposeAsync()
        {
            _isOpen = false;
            return ValueTask.CompletedTask;
        }

        private byte[] SinglePointSequenceAsdu()
        {
            var bytes = Iec10xAsduBuilder.Header(typeId: 1, vsq: 0x82, cause: 20, settings: _settings, ioa: _baseIoa);
            bytes.Add(0x01);
            bytes.Add(0x00);
            return bytes.ToArray();
        }

        private byte[] FixedSecondary(int functionCode, bool acd)
        {
            var control = (byte)(functionCode & 0x0F);
            if (acd)
            {
                control |= 0x20;
            }

            return Ft12FrameBuilder.Fixed(control, _settings.LinkAddress, _settings.LinkAddressSize);
        }

        private byte[] UserData(byte[] asdu, bool acd)
        {
            var control = (byte)0x08;
            if (acd)
            {
                control |= 0x20;
            }

            return Ft12FrameBuilder.Variable(control, _settings.LinkAddress, asdu, _settings.LinkAddressSize);
        }

        private async Task EnqueueAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            foreach (var b in bytes)
            {
                await _rxBytes.Writer.WriteAsync(b, cancellationToken).ConfigureAwait(false);
            }
        }

        private void EnsureOpen()
        {
            if (!_isOpen)
            {
                throw new InvalidOperationException("Scripted IEC-101 test transport is not open.");
            }
        }
    }
}
