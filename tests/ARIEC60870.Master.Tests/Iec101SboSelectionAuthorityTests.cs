// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Master.Protocol;
using ARIEC60870.Master.Protocol.Iec10x;
using Xunit;

namespace ARIEC60870.Master.Tests;

public sealed class Iec101SboSelectionAuthorityTests
{
    private static readonly Iec10xAsduDecoder Decoder = new(2, 2, 3);

    private static Iec10xAsduDecode Command(int cot, bool select, int ca = 1,
        int ioa = 101, int value = 2, int qualifier = 0, int oa = 0,
        bool negative = false, bool test = false, byte type = 46)
    {
        var qoc = (byte)((value & 3) | ((qualifier & 31) << 2) | (select ? 0x80 : 0));
        return Decoder.Decode(new byte[]
        {
            type, 1, (byte)(cot | (negative ? 0x40 : 0) | (test ? 0x80 : 0)),
            (byte)oa, (byte)ca, (byte)(ca >> 8),
            (byte)ioa, (byte)(ioa >> 8), (byte)(ioa >> 16), qoc
        });
    }

    [Fact]
    public void SelectRequiresRealTxAndMatchingPositiveActcon()
    {
        var clock = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
        var authority = new Iec101SboSelectionAuthority();
        var operate = Command(6, select: false);
        Assert.False(authority.CanExecuteSelected(operate, clock));
        authority.ObserveReceived(Command(7, select: true));
        Assert.False(authority.CanExecuteSelected(operate, clock));
        authority.ObserveTransmitted(Command(6, select: true), clock);
        Assert.False(authority.CanExecuteSelected(operate, clock));
        authority.ObserveReceived(Command(7, select: true));
        Assert.True(authority.CanExecuteSelected(operate, clock.AddSeconds(1)));
        authority.ObserveTransmitted(operate, clock.AddSeconds(1));
        Assert.False(authority.CanExecuteSelected(operate, clock.AddSeconds(2)));
    }

    [Fact]
    public void OtherAddressValueQualifierOriginatorAndTestCannotAuthorizeExecute()
    {
        var now = DateTime.UtcNow;
        var authority = new Iec101SboSelectionAuthority();
        authority.ObserveTransmitted(Command(6, true), now);
        authority.ObserveReceived(Command(7, true, ca: 2));
        authority.ObserveReceived(Command(7, true, ioa: 102));
        authority.ObserveReceived(Command(7, true, value: 1));
        authority.ObserveReceived(Command(7, true, qualifier: 3));
        authority.ObserveReceived(Command(7, true, oa: 1));
        authority.ObserveReceived(Command(7, true, test: true));
        Assert.False(authority.CanExecuteSelected(Command(6, false), now.AddSeconds(1)));
        authority.ObserveReceived(Command(7, true));
        foreach (var wrong in new[] {
            Command(6, false, ca: 2), Command(6, false, ioa: 102),
            Command(6, false, value: 1), Command(6, false, qualifier: 3),
            Command(6, false, oa: 1), Command(6, false, test: true)
        }) Assert.False(authority.CanExecuteSelected(wrong, now.AddSeconds(1)));
        Assert.True(authority.CanExecuteSelected(Command(6, false), now.AddSeconds(1)));
    }

    [Fact]
    public void NegativeOverlapExpiredAndEpochResetFailClosed()
    {
        var now = DateTime.UtcNow;
        var authority = new Iec101SboSelectionAuthority();
        authority.ObserveTransmitted(Command(6, true), now);
        authority.ObserveReceived(Command(7, true, negative: true));
        Assert.False(authority.CanExecuteSelected(Command(6, false), now));
        authority.ObserveTransmitted(Command(6, true), now);
        authority.ObserveTransmitted(Command(6, true), now.AddMilliseconds(1));
        authority.ObserveReceived(Command(7, true));
        Assert.False(authority.CanExecuteSelected(Command(6, false), now.AddSeconds(1)));
        authority.Reset();
        authority.ObserveTransmitted(Command(6, true), now);
        authority.ObserveReceived(Command(7, true));
        Assert.False(authority.CanExecuteSelected(Command(6, false), now.AddSeconds(31)));
        authority.Reset();
        Assert.False(authority.CanExecuteSelected(Command(6, false), now.AddSeconds(1)));
    }

    [Fact]
    public void CancellationTxRevokesPriorPositiveSelectWithoutWaitingForDeactcon()
    {
        var now = DateTime.UtcNow;
        var authority = new Iec101SboSelectionAuthority();
        authority.ObserveTransmitted(Command(6, select: true), now);
        authority.ObserveReceived(Command(7, select: true));
        Assert.True(authority.CanExecuteSelected(Command(6, select: false), now.AddSeconds(1)));

        authority.ObserveTransmitted(Command(8, select: true), now.AddSeconds(1));
        Assert.False(authority.CanExecuteSelected(Command(6, select: false), now.AddSeconds(2)));
        authority.ObserveReceived(Command(9, select: true));
        Assert.False(authority.CanExecuteSelected(Command(6, select: false), now.AddSeconds(2)));

        authority.ObserveTransmitted(Command(6, select: true), now.AddSeconds(3));
        authority.ObserveReceived(Command(7, select: true));
        Assert.True(authority.CanExecuteSelected(Command(6, select: false), now.AddSeconds(4)));
    }

    [Fact]
    public void AAndBHaveIndependentAuthoritativeSelection()
    {
        var now = DateTime.UtcNow;
        var a = new Iec101SboSelectionAuthority();
        var b = new Iec101SboSelectionAuthority();
        a.ObserveTransmitted(Command(6, true), now);
        b.ObserveReceived(Command(7, true));
        a.ObserveReceived(Command(7, true));
        Assert.True(a.CanExecuteSelected(Command(6, false), now.AddSeconds(1)));
        Assert.False(b.CanExecuteSelected(Command(6, false), now.AddSeconds(1)));
    }
}
