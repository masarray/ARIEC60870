// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ARIEC60870.Core.Mapping;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

public sealed class ValueRow : INotifyPropertyChanged
{
    private bool _isRecentlyChanged;

    public ValueRow(Iec103ValuePoint item, int? wireDl = null, int? configuredDl = null,
        int? configuredCa = null, int? mappedCa = null, bool readOnlyAlias = false,
        int? mappedTi = null)
    {
        Key = item.Key;
        PhysicalSource = item.PhysicalSource;
        EvidenceReference = item.EvidenceReference;
        Protocol = item.ProtocolMode switch
        {
            Iec60870ProtocolMode.Iec101 => "101",
            Iec60870ProtocolMode.Iec104 => "104",
            _ => "103"
        };
        Signal = item.SignalName;
        Group = item.SignalGroup;
        Value = item.DisplayValue;
        DiscreteBadge = DigitalStateBadge.FromDecoded(Protocol,
            item.TypeId?.ToString(CultureInfo.InvariantCulture), Value);
        TypeBadge = IecSignalTypeBadge.FromDecoded(Protocol, item.TypeId);
        RelayTime = string.IsNullOrWhiteSpace(item.RelayTimeText) ? "no timestamp" : item.RelayTimeText;
        Fun = item.FunctionType.HasValue ? item.FunctionType.Value.ToString(CultureInfo.InvariantCulture) : "-";
        Inf = item.InformationNumber.HasValue ? item.InformationNumber.Value.ToString(CultureInfo.InvariantCulture) : "-";
        FunInf = Fun == "-" && Inf == "-" ? "-" : $"{Fun}/{Inf}";
        TypeId = item.TypeId.HasValue ? item.TypeId.Value.ToString(CultureInfo.InvariantCulture) : "-";
        TiLabel = item.TypeId.HasValue ? $"TI{item.TypeId.Value}" : "-";
        IsTiMismatch = mappedTi.HasValue && item.TypeId.HasValue &&
            !Iec10xPointTypeFamily.SameObservationFamily(mappedTi.Value, item.TypeId.Value);
        TiExplanation = IsTiMismatch
            ? $"Signal List TI{mappedTi}, actual received TI{item.TypeId}. Name associated by unique IOA only; raw TI determines process value and quality. No profile state conversion."
            : $"Received {TiLabel}. Type ID is decoded from original ASDU.";
        Type = item.SignalType;
        Cot = item.CauseOfTransmission;
        CotLabel = item.CauseDetails is { } cause ? $"{cause.Code} · {cause.Phase}" : Cot;
        CotChips = ProtocolFlagPresentation.Cot(item.CauseDetails);
        CotDetail = item.CauseDetails?.Detail ?? Cot;
        CommonAddress = item.CommonAddress.HasValue ? item.CommonAddress.Value.ToString(CultureInfo.InvariantCulture) : "-";
        LinkAddress = wireDl?.ToString(CultureInfo.InvariantCulture) ?? "-";
        IsDlMismatch = wireDl.HasValue && configuredDl.HasValue && wireDl.Value != configuredDl.Value;
        IsCaMismatch = item.CommonAddress.HasValue &&
            ((configuredCa.HasValue && item.CommonAddress.Value != configuredCa.Value) ||
             (mappedCa.HasValue && item.CommonAddress.Value != mappedCa.Value));
        DlExplanation = $"Observed FT1.2 DL={LinkAddress}; configured DL={configuredDl?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}. DL and ASDU CA are independent.";
        CaExplanation = $"Observed ASDU CA={CommonAddress}; configured CA={configuredCa?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; Signal List CA={mappedCa?.ToString(CultureInfo.InvariantCulture) ?? "unmapped"}. " +
            (readOnlyAlias ? "Read-only name alignment: trusted wire CA and command target remain unchanged."
                           : "Displayed value comes from accepted RX; no CA modification.");
        IoAddress = item.InformationObjectAddress.HasValue ? item.InformationObjectAddress.Value.ToString(CultureInfo.InvariantCulture) : "-";
        IoaTooltip = Iec10xIoaPresentation.StructuredTooltip(item.InformationObjectAddress);
        IoaSortKey = item.InformationObjectAddress ?? int.MaxValue;
        TypeSortKey = item.TypeId ?? int.MaxValue;
        Quality = string.IsNullOrWhiteSpace(item.QualityText) ? "-" : item.QualityText;
        QualityFlags = item.QualityDetails;
        RawValue = item.RawValue;
        var quality = ProtocolFlagPresentation.QualityState(item.QualityDetails, Quality, Protocol is "101" or "104");
        QualityChips = quality.Chips;
        QualityDetail = quality.Tooltip;
        QualityRawLabel = quality.CleanRawLabel;
        IsQualityInvalid = quality.IsInvalid;
        Address = Protocol is "101" or "104"
            ? (IoAddress == "-" ? "-" : (string.IsNullOrWhiteSpace(PhysicalSource) ? "" : PhysicalSource + " · ") + "IOA " + IoAddress)
            : (FunInf == "-" ? "-" : "FUN/INF " + FunInf);
        Mapped = item.IsMapped ? "Yes" : "No";
        RawHex = item.RawHex;
    }

    public string Key { get; }
    public string PhysicalSource { get; }
    public string EvidenceReference { get; }
    public string Protocol { get; }
    public string Signal { get; }
    public string Group { get; }
    public string Value { get; }
    public string OperatorValue => IecOperatorValueFormatter.ForGrid(Protocol, TypeId, Value);
    public string FullOperatorValue => Value;
    public DigitalStateBadge? DiscreteBadge { get; }
    public IecSignalTypeBadge? TypeBadge { get; }
    public string RelayTime { get; }
    public string Fun { get; }
    public string Inf { get; }
    public string FunInf { get; }
    public string TypeId { get; }
    public string TiLabel { get; }
    public bool IsTiMismatch { get; }
    public string TiExplanation { get; }
    public int TypeSortKey { get; }
    public string Type { get; }
    public string Cot { get; }
    public string CotLabel { get; }
    public string CotDetail { get; }
    public IReadOnlyList<ProtocolFlagChip> CotChips { get; }
    public string CommonAddress { get; }
    public string LinkAddress { get; }
    public bool IsDlMismatch { get; }
    public bool IsCaMismatch { get; }
    public string DlExplanation { get; }
    public string CaExplanation { get; }
    public string IoAddress { get; }
    public string IoaTooltip { get; }
    public int IoaSortKey { get; }
    public string Address { get; }
    public string Quality { get; }
    public Iec10xQualityDetails? QualityFlags { get; }
    public string RawValue { get; }
    public string QualityDetail { get; }
    public string QualityRawLabel { get; }
    public bool IsQualityInvalid { get; }
    public IReadOnlyList<ProtocolFlagChip> QualityChips { get; }
    public string Mapped { get; }
    public string RawHex { get; }

    public bool IsRecentlyChanged
    {
        get => _isRecentlyChanged;
        set
        {
            if (_isRecentlyChanged == value)
            {
                return;
            }

            _isRecentlyChanged = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
