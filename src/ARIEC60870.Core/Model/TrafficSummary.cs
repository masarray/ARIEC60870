// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Model;

public sealed class TrafficSummary
{
    public int TotalLines { get; init; }
    public int TotalFrames { get; init; }
    public int FixedFrames { get; init; }
    public int VariableFrames { get; init; }
    public int SingleCharacterFrames { get; init; }
    public int MalformedFrames { get; init; }
    public int ChecksumErrors { get; init; }
    public int Class1Requests { get; init; }
    public int Class2Requests { get; init; }
    public int NoDataResponses { get; init; }
    public int ResetFcbCommands { get; init; }
    public int AckResponses { get; init; }
    public int SecondaryUserDataFrames { get; init; }
    public double NoDataRatio => TotalFrames == 0 ? 0 : (double)NoDataResponses / TotalFrames;
    public double UsefulVariableFrameRatio => TotalFrames == 0 ? 0 : (double)VariableFrames / TotalFrames;
    public IReadOnlyDictionary<int, int> AsduTypeCounts { get; init; } = new Dictionary<int, int>();
}
