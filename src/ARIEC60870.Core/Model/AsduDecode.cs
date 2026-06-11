// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Core.Model;

public sealed class AsduDecode
{
    public int TypeId { get; init; }
    public string TypeName { get; init; } = "Unknown ASDU";
    public byte Vsq { get; init; }
    public int ObjectCount { get; init; }
    public bool Sequence { get; init; }
    public int CauseOfTransmission { get; init; }
    public string CauseName { get; init; } = "Unknown COT";
    public int CommonAddress { get; init; }
    public int? FunctionType { get; init; }
    public int? InformationNumber { get; init; }
    public int? Dpi { get; init; }
    public string? DpiText { get; init; }
    public double? NumericValue { get; init; }
    public string? NumericValueText { get; init; }
    public Cp32Time2a? Time { get; init; }
    public string? IdentificationText { get; init; }
    public string? EngineeringMeaning { get; init; }
    public string? ProfileName { get; init; }
    public string? SemanticLabel { get; init; }
    public string? SemanticCategory { get; init; }
    public string? SemanticState { get; init; }
    public string? SemanticConfidence { get; init; }
    public string? SemanticNote { get; init; }
    public DecodeStatus Status { get; init; } = DecodeStatus.Ok;
    public IReadOnlyList<byte> DataBytes { get; init; } = Array.Empty<byte>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
