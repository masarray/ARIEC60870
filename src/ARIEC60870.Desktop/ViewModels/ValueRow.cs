// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ARIEC60870.Master.Model;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

public sealed class ValueRow : INotifyPropertyChanged
{
    private bool _isRecentlyChanged;

    public ValueRow(Iec103ValuePoint item)
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
        RelayTime = string.IsNullOrWhiteSpace(item.RelayTimeText) ? "no timestamp" : item.RelayTimeText;
        Fun = item.FunctionType.HasValue ? item.FunctionType.Value.ToString(CultureInfo.InvariantCulture) : "-";
        Inf = item.InformationNumber.HasValue ? item.InformationNumber.Value.ToString(CultureInfo.InvariantCulture) : "-";
        FunInf = Fun == "-" && Inf == "-" ? "-" : $"{Fun}/{Inf}";
        TypeId = item.TypeId.HasValue ? item.TypeId.Value.ToString(CultureInfo.InvariantCulture) : "-";
        Type = item.SignalType;
        Cot = item.CauseOfTransmission;
        CotLabel = item.CauseDetails is { } cause ? $"{cause.Code} · {cause.Phase}" : Cot;
        CotChips = ProtocolFlagPresentation.Cot(item.CauseDetails);
        CotDetail = item.CauseDetails?.Detail ?? Cot;
        CommonAddress = item.CommonAddress.HasValue ? item.CommonAddress.Value.ToString(CultureInfo.InvariantCulture) : "-";
        IoAddress = item.InformationObjectAddress.HasValue ? item.InformationObjectAddress.Value.ToString(CultureInfo.InvariantCulture) : "-";
        IoaSortKey = item.InformationObjectAddress ?? int.MaxValue;
        TypeSortKey = item.TypeId ?? int.MaxValue;
        Quality = string.IsNullOrWhiteSpace(item.QualityText) ? "-" : item.QualityText;
        QualityFlags = item.QualityDetails;
        RawValue = item.RawValue;
        QualityChips = ProtocolFlagPresentation.Quality(item.QualityDetails, Quality, Protocol is "101" or "104");
        QualityDetail = item.QualityDetails?.BitDetails ?? Quality;
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
    public string RelayTime { get; }
    public string Fun { get; }
    public string Inf { get; }
    public string FunInf { get; }
    public string TypeId { get; }
    public int TypeSortKey { get; }
    public string Type { get; }
    public string Cot { get; }
    public string CotLabel { get; }
    public string CotDetail { get; }
    public IReadOnlyList<ProtocolFlagChip> CotChips { get; }
    public string CommonAddress { get; }
    public string IoAddress { get; }
    public int IoaSortKey { get; }
    public string Address { get; }
    public string Quality { get; }
    public Iec10xQualityDetails? QualityFlags { get; }
    public string RawValue { get; }
    public string QualityDetail { get; }
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
