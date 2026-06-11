// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Model;

public enum FrameDirection
{
    Unknown = 0,
    MasterToSlave = 1,
    SlaveToMaster = 2
}

public enum Ft12FrameFormat
{
    Unknown = 0,
    SingleCharacter = 1,
    FixedLength = 2,
    VariableLength = 3,
    Malformed = 4
}

public enum FindingSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public enum DecodeStatus
{
    Ok = 0,
    Warning = 1,
    Error = 2,
    Unknown = 3
}
