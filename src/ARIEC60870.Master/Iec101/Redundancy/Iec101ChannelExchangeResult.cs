// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Master.Iec101.Redundancy;

internal sealed class Iec101ChannelExchangeResult
{
    public bool Succeeded { get; init; }
    public bool TimedOut { get; init; }
    public bool IsNoData { get; init; }
    public bool IsUserData { get; init; }
    public bool Acd { get; init; }
    public bool Dfc { get; init; }
    public Ft12FrameDecode? Frame { get; init; }
    public Iec10xAsduDecode? Asdu { get; init; }
    public int ResponseTimeMs { get; init; }
}
