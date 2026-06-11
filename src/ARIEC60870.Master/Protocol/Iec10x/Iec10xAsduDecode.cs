// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Master.Protocol.Iec10x;

public sealed class Iec10xAsduDecode
{
    public byte TypeId { get; init; }
    public string TypeName { get; init; } = string.Empty;
    public byte VariableStructureQualifier { get; init; }
    public bool IsSequence { get; init; }
    public int ObjectCount { get; init; }
    public int CauseOfTransmission { get; init; }
    public string CauseName { get; init; } = string.Empty;
    public int OriginatorAddress { get; init; }
    public bool IsTest { get; init; }
    public bool IsNegativeConfirm { get; init; }
    public int CommonAddress { get; init; }
    public int? InformationObjectAddress { get; init; }
    public string ObjectSummary { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    public string QualityText { get; init; } = string.Empty;
    public string TimestampText { get; init; } = string.Empty;
    public bool IsControlCommand { get; init; }
    public IReadOnlyList<Iec10xInformationObject> Objects { get; init; } = Array.Empty<Iec10xInformationObject>();
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
    public IReadOnlyList<byte> RawBytes { get; init; } = Array.Empty<byte>();

    public Iec10xInformationObject? FirstObject => Objects.Count > 0 ? Objects[0] : null;

    public string CotNameWithFlags
    {
        get
        {
            var prefix = IsNegativeConfirm ? "NEG " : string.Empty;
            var test = IsTest ? ", TEST" : string.Empty;
            var oa = OriginatorAddress > 0 ? $", OA={OriginatorAddress}" : string.Empty;
            return $"{prefix}{CauseName}{test}{oa}";
        }
    }

    public string CotDisplay => $"{CauseOfTransmission} · {CotNameWithFlags}";

    public string ShortMeaning
    {
        get
        {
            var address = InformationObjectAddress.HasValue ? $", IOA={InformationObjectAddress.Value}" : string.Empty;
            var value = string.IsNullOrWhiteSpace(ValueText) ? string.Empty : $", {ValueText}";
            var quality = string.IsNullOrWhiteSpace(QualityText) || QualityText == "Good" ? string.Empty : $", Quality={QualityText}";
            return $"{TypeName}, COT={CotDisplay}, CA={CommonAddress}{address}{value}{quality}";
        }
    }
}

public sealed class Iec10xInformationObject
{
    public int Index { get; init; }
    public int InformationObjectAddress { get; init; }
    public string ValueText { get; init; } = string.Empty;
    public string EngineeringValue { get; init; } = string.Empty;
    public string QualityText { get; init; } = string.Empty;
    public string TimestampText { get; init; } = string.Empty;
    public string ElementSummary { get; init; } = string.Empty;
    public IReadOnlyList<byte> RawBytes { get; init; } = Array.Empty<byte>();
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    public string ShortValue => string.IsNullOrWhiteSpace(EngineeringValue) ? ValueText : EngineeringValue;

    public string ReadableSummary
    {
        get
        {
            var quality = string.IsNullOrWhiteSpace(QualityText) ? "Quality=Good" : $"Quality={QualityText}";
            var time = string.IsNullOrWhiteSpace(TimestampText) ? string.Empty : $", Time={TimestampText}";
            return $"IOA={InformationObjectAddress}, Value={ShortValue}, {quality}{time}";
        }
    }
}
